using System;
using System.Text;
using System.Threading.Tasks;
using Godot;
using SphServer;
using SphServer.DataModels;
using SphServer.Db;
using SphServer.Helpers;
using SphServer.Packets;
using static SphServer.Helpers.BitHelper;
using Thread = System.Threading.Thread;

public enum ClientState
{
    I_AM_BREAD,
    INIT_READY_FOR_INITIAL_DATA,
    INIT_WAITING_FOR_LOGIN_DATA,
    INIT_WAITING_FOR_CHARACTER_SELECT,
    INIT_WAITING_FOR_CLIENT_INGAME_ACK,
    INIT_NEW_DUNGEON_TELEPORT_DELAY,
    INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT,
    INIT_NEW_DUNGEON_TELEPORT_INITIATED,
    INGAME_DEFAULT
}

public class Client : Node
{
    public ushort currentPlayerIndex = 0x4F6F;
    public StreamPeerTCP streamPeer = null!;
    private const bool reconnect = false;
    public const int BUFSIZE = 1024;
    private string playerIndexStr = null!;
    private readonly byte[] rcvBuffer = new byte[BUFSIZE];
    public const bool LiveServerCoords = false;
    private ushort pingCounter;
    private bool pingShouldXorTopBit;
    public static readonly string PingCoordsFilePath = LiveServerCoords
        ? "C:\\_sphereDumps\\currentWorldCoords"
        : "C:\\source\\clientCoordsSaved";
    private string? pingPreviousClientPingString;
    public CharacterData CurrentCharacter = null!;
    private int newPlayerDungeonMobHp = 64;
    private float timeSinceLastSixSecondPing = 1000;
    private float timeSinceLastFifteenSecondPing = 1000;
    private float timeSinceLastTransmissionEndPing = 1000;
    private ClientState currentState = ClientState.I_AM_BREAD;
    private ClientInitialData? charListData;
    private int selectedCharacterIndex = -1;
    private StaticBody? clientModel;
    private double oldY = Double.MaxValue;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        playerIndexStr = ConvertHelper.ToHexString(new[]
        {
            MajorByte(currentPlayerIndex),
            MinorByte(currentPlayerIndex)
        });
    }


    public override async void _Process(float delta)
    {
        if (streamPeer.GetStatus() != StreamPeerTCP.Status.Connected)
        {
            CloseConnection();
        }

        clientModel ??= GetNode<StaticBody>("ClientModel");

        switch (currentState)
        {
            case ClientState.I_AM_BREAD:
                Console.WriteLine($"CLI {playerIndexStr}: Ready to load initial data");
                streamPeer.PutPartialData(reconnect
                    ? CommonPackets.ReadyToLoadInitialDataReconnect
                    : CommonPackets.ReadyToLoadInitialData);
                currentState = ClientState.INIT_READY_FOR_INITIAL_DATA;

                break;
            case ClientState.INIT_READY_FOR_INITIAL_DATA:
                if (streamPeer.GetBytes(rcvBuffer) == 0)
                {
                    return;
                }

                Console.WriteLine($"CLI {playerIndexStr}: Connection initialized");
                streamPeer.PutPartialData(CommonPackets.ServerCredentials(currentPlayerIndex));
                Console.WriteLine($"SRV {playerIndexStr}: Credentials sent");
                currentState = ClientState.INIT_WAITING_FOR_LOGIN_DATA;

                break;
            case ClientState.INIT_WAITING_FOR_LOGIN_DATA:
                if (streamPeer.GetBytes(rcvBuffer) <= 12)
                {
                    return;
                }

                Console.WriteLine($"CLI {playerIndexStr}: Login data sent");
                (var login, var password) = LoginHelper.GetLoginAndPassword(rcvBuffer);

                charListData =
                    Login.CheckLoginAndGetPlayerCharacters(login, password, currentPlayerIndex);
                Console.WriteLine("Fetched char list data");
                await (ToSignal(GetTree().CreateTimer(0.05f), "timeout"));

                if (charListData == null)
                {
                    // TODO: actual incorrect pwd packet
                    Console.WriteLine($"SRV {playerIndexStr}: Incorrect password!");
                    streamPeer.PutPartialData(CommonPackets.AccountAlreadyInUse(currentPlayerIndex));
                    CloseConnection();

                    return;
                }

                streamPeer.PutPartialData(CommonPackets.CharacterSelectStartData(currentPlayerIndex));
                Console.WriteLine("SRV: Character select screen data - initial");
                Thread.Sleep(100);

                streamPeer.PutPartialData(charListData.ToByteArray(currentPlayerIndex));
                Console.WriteLine("SRV: Character select screen data - player characters");
                Thread.Sleep(100);
                currentState = ClientState.INIT_WAITING_FOR_CHARACTER_SELECT;

                break;
            case ClientState.INIT_WAITING_FOR_CHARACTER_SELECT:
                if (selectedCharacterIndex == -1)
                {
                    if (streamPeer.GetBytes(rcvBuffer) == 0x15)
                    {
                        selectedCharacterIndex = rcvBuffer[17] / 4 - 1;

                        return;
                    }

                    if (rcvBuffer[0] < 0x1b ||
                        (rcvBuffer[13] != 0x08 || rcvBuffer[14] != 0x40 || rcvBuffer[15] != 0x80 ||
                         rcvBuffer[16] != 0x05))
                    {
                        return;
                    }

                    selectedCharacterIndex = CharacterScreenCreateDeleteSelect();
                }

                CurrentCharacter = charListData![selectedCharacterIndex]!;

                Console.WriteLine("CLI: Enter game");
                streamPeer.PutPartialData(CurrentCharacter.ToGameDataByteArray());
                currentState = ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK;
                break;
            case ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK:
                if (streamPeer.GetBytes(rcvBuffer) == 0x13)
                {
                    return;
                }
                // Interlocked.Increment(ref playerCount);

                WorldDataTest.SendNewCharacterWorldData(streamPeer, playerIndexStr);
                currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY;
                await ToSignal(GetTree().CreateTimer(2), "timeout");
                currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT;
                
                break;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY:
                return;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT:
                await MoveToNewPlayerDungeonAsync(CurrentCharacter);
                break;
            case ClientState.INGAME_DEFAULT:
                break;
            default: return;
            // only in dungeon?
            // while (await ns.ReadAsync(rcvBuffer) == 0 || rcvBuffer[0] != 0x12)
            // {
            // }
        }

        if (currentState != ClientState.INGAME_DEFAULT)
        {
            return;
        }

        // Echo and keepalive
        timeSinceLastFifteenSecondPing += delta;
        timeSinceLastSixSecondPing += delta;
        timeSinceLastTransmissionEndPing += delta;

        if (timeSinceLastFifteenSecondPing >= 15)
        {
            streamPeer.PutPartialData(CommonPackets.FifteenSecondPing(currentPlayerIndex));
            timeSinceLastFifteenSecondPing = 0;
        }

        if (timeSinceLastSixSecondPing >= 6)
        {
            streamPeer.PutPartialData(CommonPackets.SixSecondPing(currentPlayerIndex));
            timeSinceLastSixSecondPing = 0;
        }

        if (timeSinceLastTransmissionEndPing >= 3)
        {
            streamPeer.PutPartialData(CommonPackets.TransmissionEndPacket);
            timeSinceLastTransmissionEndPing = 0;
        }

        var length = streamPeer.GetBytes(rcvBuffer);

        if (length == 0)
        {
            return;
        }

        if (Math.Abs(oldY - double.MaxValue) < double.Epsilon)
        {
            oldY = CurrentCharacter.Y;
        }
        
        switch (rcvBuffer[0])
        {
            // ping
            case 0x26:
                SendPingResponse();

                break;
            // interact (move item, open loot container)
            case 0x1A:
                if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0x0C)
                {
                    // item pickup
                    PickupItemToInventory();
                }
                else if (rcvBuffer[13] == 0x5c && rcvBuffer[14] == 0x46 && rcvBuffer[15] == 0xe1)
                {
                    var containerId = rcvBuffer[11] + rcvBuffer[12] * 0x100;
                    Console.WriteLine(containerId);
                    // open loot container
                    streamPeer.PutPartialData(ConvertHelper.FromHexString("27002C01000004FE475C466102000A1300501004803424004B000080822004A821015802000000"));
                }

                break;
            // echo
            case 0x08:
                // streamPeer.PutPartialData(CommonPackets.Echo(currentPlayerIndex));

                break;
            // damage
            // case 0x19:
            case 0x20:
                var damage = (byte)(10 + RNGHelper.GetUniform() * 8);
                var destId = (ushort)GetDestinationIdFromDamagePacket(rcvBuffer);
                var playerIndexByteSwap = ByteSwap(currentPlayerIndex);
                var selfDamage = destId == playerIndexByteSwap;

                if (selfDamage)
                {
                    var selfDamagePacket = new byte[]
                    {
                        0x10, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(currentPlayerIndex), 
                        MinorByte(currentPlayerIndex), 0x08, 0x40, (byte)(rcvBuffer[25] + 2), rcvBuffer[26], 
                        (byte)(rcvBuffer[27] + 0x60), damage, 0x00
                    };

                    streamPeer.PutPartialData(selfDamagePacket);
                }
                else
                {
                    newPlayerDungeonMobHp = Math.Max(0, newPlayerDungeonMobHp - damage);

                    if (newPlayerDungeonMobHp > 0)
                    {
                        var src_1 = (byte)((playerIndexByteSwap & 0b1111111) << 1);
                        var src_2 = (byte)((playerIndexByteSwap & 0b111111110000000) >> 7);
                        var src_3 = (byte)((playerIndexByteSwap & 0b1000000000000000) >> 15);
                        var dmg_1 = (byte)(0x60 - damage * 2);
                        var hp_1 = (byte)((newPlayerDungeonMobHp & 0b1111) << 4);
                        var hp_2 = (byte)((newPlayerDungeonMobHp & 0b11110000) >> 4);
                        var damagePacket = new byte[]
                        {
                            0x1B, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MinorByte(destId), MajorByte(destId), 0x48, 
                            0x43, 0xA1, 0x0B, src_1, src_2, src_3, dmg_1, 0xEA, 0x0A, 0x6D, hp_1, hp_2, 0x00, 
                            0x04, 0x50, 0x07, 0x00
                        };
                        streamPeer.PutPartialData(damagePacket);
                    }
                    else
                    {
                        var mob = GetNode<Mob>("/root/MainServer/NewPlayerDungeon/Navigation/NavigationMeshInstance/Mob");
                        mob.SetInactive();
                        
                        var moneyReward = (byte)(10 + RNGHelper.GetUniform() * 8);
                        var totalMoney = 10 + moneyReward;
                        var totalMoney_1 = (byte)(((totalMoney & 0b11111) << 3) + 0b100);
                        var totalMoney_2 = (byte)((totalMoney & 0b11100000) >> 5);
                        var karma = 1;
                        var karma_1 = (byte)(((karma & 0b1111111) << 1) + 1);
                        var src_1 = (byte)((playerIndexByteSwap & 0b1000000000000000) >> 15);
                        var src_2 = (byte)((playerIndexByteSwap & 0b111111110000000) >> 7);
                        var src_3 = (byte)((playerIndexByteSwap & 0b1111111) << 1);
                        var src_4 = (byte)(((playerIndexByteSwap & 0b111) << 5) + 0b01111);
                        var src_5 = (byte)((playerIndexByteSwap & 0b11111111000) >> 3);
                        var src_6 = (byte)(((playerIndexByteSwap & 0b1111100000000000) >> 11));

                        var moneyReward_1 = (byte)(((moneyReward & 0b11) << 6) + 1);
                        var moneyReward_2 = (byte)((moneyReward & 0b1111111100) >> 2);

                        // this packet can technically contain any stat, xp, level, hp/mp, etc
                        // for the new player dungeon we only care about giving karma and some money after a kill
                        // chat message should be bright green, idk how to get it to work though
                        var deathPacket = new byte[]
                        {
                            0x04, MinorByte(destId), MajorByte(destId), 0x48, 0x43, 0xA1, 0x09, src_3, src_2, src_1,
                            0x00, 0x7E, MinorByte(playerIndexByteSwap), MajorByte(playerIndexByteSwap), 0x08, 0x40, 
                            0x41, 0x0A, 0x34, 0x3A, 0x93, 0x00, 0x00, 0x7E, 0x14, 0xCE, 0x14, 0x47, 0x81, 0x05, 0x3A,
                            0x93, 0x7E, MinorByte(destId), MajorByte(destId), 0x00, 0xC0, src_4, src_5, src_6, 0x01, 
                            0x58, 0xE4, totalMoney_1, totalMoney_2, 0x16, 0x28, karma_1, 0x80, 0x46, 0x40, 
                            moneyReward_1, moneyReward_2
                        };
                        streamPeer.PutPartialData(Packet.ToByteArray(deathPacket));
                    }
                }

                break;
            // buy from npc
            case 0x35:
            case 0x30:
                // var vendorIdBytes = rcvBuffer[44..47];
                // var vendorId = ((vendorIdBytes[2] & 0b1111) << 12) + (vendorIdBytes[1] << 4) +
                //                ((vendorIdBytes[0] & 0b11110000) >> 4);
                // Console.WriteLine(vendorId);
                var vendorId = 0x8169;

                // streamPeer.PutPartialData(TestHelper.GetEntityData(
                //     new WorldCoords(669.1638793945312, 4501.63134765625, 931.0355224609375, -1), 4816,
                //     7654, 4816));

                var i = 0;

                // while (ns.CanWrite)
                // {
                //     var vendorList =
                //         $"27002C010000044f6f0840A362202D10E097164832142600400108E0DF08000000004000000000";
                //     streamPeer.PutPartialData(ConvertHelper.FromHexString(vendorList));
                //
                //     if (i < 80)
                //     {
                //         var ent = i % 4 == 0 ? 5688 : i % 4 == 1 ? 5616 : i % 4 == 2 ? 5712 : 5696;
                //         var entTypeId = 0b1000000000000000 + (ent >> 1);
                //         var deg = ((double)i * 24) * Math.PI / 180;
                //         var x0 = 1;
                //         var y0 = 1;
                //         var x = x0 * Math.Cos(deg) - y0 * Math.Sin(deg);
                //         var y = x0 * Math.Sin(deg) + y0 * Math.Cos(deg);
                //         streamPeer.PutPartialData(TestHelper.GetEntityData(
                //             new WorldCoords(671.1638793945312 + x, 4501.63134765625, 932.0355224609375 + y,
                //                 -1), 971, 7654 + i, entTypeId));
                //
                //         i++;
                //     }
                //
                //     System.Threading.Thread.Sleep(1350);
                // }

                // var vendorListLoaded = $"30002C01000004FE8D14870F80842E0900000000000000004091456696101560202D10A0900500FFFFFFFF0516401F00";
                // streamPeer.PutPartialData(ConvertHelper.FromHexString(vendorListLoaded));
                var vendorListLoaded =
                    BinaryStringToByteArray(System.IO.File.ReadAllText("C:\\source\\vendorList.txt")
                        .RemoveLineEndings());
                streamPeer.PutPartialData(vendorListLoaded);

                break;
        }
        
        var clientModelTransform = clientModel.Transform;

        // ignore new client coords if they're too far away from what server knows
        if (clientModelTransform.origin.DistanceTo(new Vector3((float)CurrentCharacter.X, 
                (float) (clientModelTransform.origin.y + oldY - CurrentCharacter.Y), (float)CurrentCharacter.Z)) < 50)
        {
            clientModelTransform.origin =
                new Vector3((float)CurrentCharacter.X, (float) (clientModelTransform.origin.y + oldY - CurrentCharacter.Y), (float)CurrentCharacter.Z);
            clientModel.Transform = clientModelTransform;
        }
        oldY = CurrentCharacter.Y;
    }

    private int CharacterScreenCreateDeleteSelect()
    {
        if (rcvBuffer[0] == 0x2A)
        {
            var charIndex = rcvBuffer[17] / 4 - 1;

            Console.WriteLine($"Delete character [{charIndex}] - [{charListData![charIndex]!.Name}]");
            DbCharacters.DeleteCharacterFromDb(charListData[charIndex]!.DbId);

            // TODO: reinit session after delete
            // await HandleClientAsync(client, (ushort) (currentPlayerIndex + 1), true);

            CloseConnection();

            return -1;
        }

        var len = rcvBuffer[0] - 20 - 5;
        var charDataBytesStart = rcvBuffer[0] - 5;
        var nameCheckBytes = rcvBuffer.Range(20, rcvBuffer.Length);
        var charDataBytes = rcvBuffer.Range(charDataBytesStart, rcvBuffer[0]);
        var sb = new StringBuilder();
        var firstLetterCharCode = (((nameCheckBytes[1] & 0b11111) << 3) + (nameCheckBytes[0] >> 5));
        var firstLetterShouldBeRussian = false;

        for (var i = 1; i < len; i++)
        {
            var currentCharCode = (((nameCheckBytes[i] & 0b11111) << 3) + (nameCheckBytes[i - 1] >> 5));

            if (currentCharCode % 2 == 0)
            {
                // English
                var currentLetter = (char)(currentCharCode / 2);
                sb.Append(currentLetter);
            }
            else
            {
                // Russian
                var currentLetter = currentCharCode >= 193
                    ? (char)((currentCharCode - 192) / 2 + 'а')
                    : (char)((currentCharCode - 129) / 2 + 'А');
                sb.Append(currentLetter);

                if (i == 2)
                {
                    // we assume first letter was russian if second letter is, this is a hack
                    firstLetterShouldBeRussian = true;
                }
            }
        }

        string name;

        if (firstLetterShouldBeRussian)
        {
            firstLetterCharCode += 1;
            var firstLetter = firstLetterCharCode >= 193
                ? (char)((firstLetterCharCode - 192) / 2 + 'а')
                : (char)((firstLetterCharCode - 129) / 2 + 'А');
            name = firstLetter + sb.ToString()[1..];
        }
        else
        {
            name = sb.ToString();
        }

        var isNameValid = Login.IsNameValid(name);
        Console.WriteLine(isNameValid ? $"SRV: Name [{name}] OK" : $"SRV: Name [{name}] already exists!");

        if (!isNameValid)
        {
            streamPeer.PutPartialData(CommonPackets.NameAlreadyExists(currentPlayerIndex));
        }
        else
        {
            var isGenderFemale = (charDataBytes[1] >> 4) % 2 == 1;
            var faceType = ((charDataBytes[1] & 0b111111) << 2) + (charDataBytes[0] >> 6);
            var hairStyle = ((charDataBytes[2] & 0b111111) << 2) + (charDataBytes[1] >> 6);
            var hairColor = ((charDataBytes[3] & 0b111111) << 2) + (charDataBytes[2] >> 6);
            var tattoo = ((charDataBytes[4] & 0b111111) << 2) + (charDataBytes[3] >> 6);

            if (isGenderFemale)
            {
                faceType = 256 - faceType;
                hairStyle = 255 - hairStyle;
                hairColor = 255 - hairColor;
                tattoo = 255 - tattoo;
            }

            var charIndex = (rcvBuffer[17] / 4 - 1);

            var newCharacterData = CharacterData.CreateNewCharacter(currentPlayerIndex, name,
                isGenderFemale, faceType, hairStyle, hairColor, tattoo);

            charListData!.AddNewCharacter(newCharacterData, charIndex);
            DbCharacters.AddNewCharacterToDb(charListData.PlayerId, newCharacterData,
                charIndex);

            streamPeer.PutPartialData(CommonPackets.NameCheckPassed(currentPlayerIndex));

            return charIndex;
        }

        return -1;
    }

    private async Task MoveToNewPlayerDungeonAsync(CharacterData selectedCharacter)
    {
        // while (ns.CanRead)
        // var str = Console.ReadLine();
        //
        // if (string.IsNullOrEmpty(str) || !str.Equals("def"))
        // {
        //     continue;
        // }
        // move to dungeon
        // var newDungeonCoords = new WorldCoords(701, 4501.62158203125, 900, 1.55);
        var newDungeonCoords = new WorldCoords(-1098, 4501.62158203125, 1900, 0);
        var playerCoords = new WorldCoords(-1098.69506835937500, 4501.61474609375000, 1900.05493164062500,
            1.57079637050629);
        streamPeer.PutPartialData(
            // selectedCharacter.GetTeleportAndUpdateCharacterByteArray(new WorldCoords(669.5963745117188, 4501.63134765625, 931.5966796875, -1)));
            selectedCharacter.GetTeleportAndUpdateCharacterByteArray(playerCoords,
                Convert.ToString(selectedCharacter.PlayerIndex, 16).PadLeft(4, '0')));

        currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_INITIATED;
        await ToSignal(GetTree().CreateTimer(0.3f), "timeout");

        // get into instance
        // commented: no inkpot, no npc29 id 33129, no tokenst id 33130, no ct_lab 33120 33150 33114, no telep1 33116, no tutomsg 33146 33147 33148 33151 33154 33156 33124
        var enterNewGame_6 =
            "BF002C0100067A2C0C10802F811F010BE2E00320A14B02000000000000000050649101A000039AFE00850900F8F9AF00063E00C044620050C62200C0762200441619000A2F809DF50B60E1337CA2838DC436A88C45F0FBF144C1882C3200145E4020A0F00CA2838DC436A88C45F0FBF144856FD0ED2CFE558F4806568F480606F8212C400D3E9F1045629756C62241A476A2E550140014433A29DE0785170000F8252CA01F3E19A14662B053C62249C2762280441619890A5900F0FFFFFF0F" +
            "C1002C0100067A150B2483CF38A391B89495B1C813319E680C140500C550EA8000C0CF62813FF001CD2512ABA232160995CB130124B2C80050C80280FFFFFFFFC2132C1C0004FC0C5800161F00095D12000000000000000080228B0C000518D0F407286401C0FFFFFFFFEF7F85CFF001002612038032160100B6130120B2C8005078810081C233646DCF15EB822612257CD01517BE01000000000000000000000000A0F00C19D976C5DBB489440D7E72C5856F0000000000000000000000000000" +
            "C8002C0100067AFF2B7C46E11908C0E68AF5411389FE18E28A0BDF00000000000000000000000000F00360E1337C008089C400A08C450080ED4400882C3200145E4040A0F00C737F75C514A18944208271C5856F00000000000000000000000000F8E53EF0193E00C044620050C62200C0762200441619000A2F30205078069906BAE283DA44A22BC1B9E2C23700000000000000000000000000FC741FF8019FB95C2231DE2A6311FBDE3C1100228B0C003F061690C027C396489CBFC958E4DBD74E0480C8220300" +
            "C9002C0100067A0C2C2041E10502007E022C2081CF1B9A91D8F292B1080EB09D080091450680C20B0800FC065840029F58C72331AE2863911E863B1100228B0C0085171800F8093F80043E1FD34662154FC622EEA8782200441619000A2F4000F02B5300097CE8AF8BC446968C45D242F14400882C3200145EA000E0A7BF0212F838E512899941198B744FE689001059640028BCC001C06F7F0524F079E92512B36132164182C6130120B2C80050780103809FDE02AEE10320A14B0200000000000000005064910100" +
            "1B002C0100067A7A0BB846010634FD010A131050C80280FFFFFF7F" +
            "2D002C01006DF78A2CDBE1400F61016A1098F9F435FEF22F6101FD10006DFED71FC0CF62813F10547EFED90900";
        // var enterGameResponse_5 =
        //     $"2b002c0100ac04{playerIndexStr}08400362206056ab814350b51705d07028d006090040768804816f27de915c7c803f";
        // await ns.WriteAsync(ConvertHelper.FromHexString(enterGameResponse_5));

        // while (await ns.ReadAsync(rcvBuffer) != 0x13)
        // {
        // }

        // 
        // tp.AddRange(enterNewGame_4);

        streamPeer.PutPartialData(ConvertHelper.FromHexString(enterNewGame_6));
        // await ns.WriteAsync(tp.ToArray());
        // await ns.WriteAsync(ConvertHelper.FromHexString(
        //     "BC002C0100EE45B0A67C46E11B000000000000000000000000007E6FA67C860F00A8A6180094B10800B09D080091450680C20B0808149E213AB8AE181B3491084130AEB8F00D000000000000000000000000003F71547EC0379753538CB7CA58C4BE374F0480C82203C0AF251524F065D8D414E76F3216F9F6B5130120B2C80050788100805F4E2A48E07B43B729B6BC642C82036C270240649101A0F0020200BF725490C017EB80538C2BCA58A487E14E0480C8220340E105060000"));
        Console.WriteLine(
            $"SRV: Teleported client [{MinorByte(selectedCharacter.PlayerIndex) * 256 + MajorByte(selectedCharacter.PlayerIndex)}] to default new player dungeon");

        streamPeer.PutPartialData(TestHelper.GetNewPlayerDungeonMobData(newDungeonCoords));

        currentState = ClientState.INGAME_DEFAULT;
        UpdateCoordsWithoutAxisFlip(playerCoords);
        // }
    }

    public void CloseConnection()
    {
        Console.WriteLine($"SRV {playerIndexStr}: Closing connection");
        QueueFree();
    }

    public void SendPingResponse()
    {
        var clientPingBytesForComparison = rcvBuffer.Range(17, 38);

        var clientPingBytesForPong = rcvBuffer.Range(9, 21);
        var clientPingBinaryStr =
            ByteArrayToBinaryString(clientPingBytesForComparison, false, true);

        // if (clientPingBinaryStr[0] == '0')
        // {
        //     // random different packet, idk
        //     return;
        // }

        if (string.IsNullOrEmpty(pingPreviousClientPingString))
        {
            pingPreviousClientPingString = clientPingBinaryStr;
        }

        else
        {
            var pingHasChanges = string.Compare(clientPingBinaryStr, pingPreviousClientPingString,
                StringComparison.Ordinal);

            if (pingHasChanges != 0)
            {
                var coords = CoordsHelper.GetCoordsFromPingBytes(rcvBuffer);
                CurrentCharacter.X = coords.x;
                CurrentCharacter.Y = coords.y;
                CurrentCharacter.Z = coords.z;
                CurrentCharacter.T = coords.turn;
                Console.WriteLine(coords.ToDebugString());

                pingPreviousClientPingString = clientPingBinaryStr;
            }
        }

        var topByteToXor = clientPingBytesForPong[5];

        if (pingShouldXorTopBit)
        {
            topByteToXor ^= 0b10000000;
        }

        if (pingCounter == 0)
        {
            var first = (ushort)((clientPingBytesForPong[7] << 8) + clientPingBytesForPong[6]);
            first -= 0xE001;
            pingCounter = (ushort)(0xE001 + first / 12);
        }

        var pong = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, topByteToXor, MinorByte(pingCounter),
            MajorByte(pingCounter), 0x00, 0x00, 0x00, 0x00, 0x00
        };

        Array.Copy(clientPingBytesForPong, pong, 5);
        Array.Copy(clientPingBytesForPong, 8, pong, 8, 4);
        streamPeer.PutPartialData(Packet.ToByteArray(pong, 1));
        pingShouldXorTopBit = !pingShouldXorTopBit;
        pingCounter++;

        //overflow
        if (pingCounter < 0xE001)
        {
            pingCounter = 0xE001;
        }
    }

    private void PickupItemToInventory()
    {
        var clientItemID_1 = rcvBuffer[21] >> 1;
        var clientItemID_2 = rcvBuffer[22];
        var clientItemID_3 = rcvBuffer[23] % 2;
        var clientItemID = (clientItemID_3 << 15) + (clientItemID_2 << 7) + clientItemID_1;

        var clientSlot_raw = rcvBuffer[24];
        var clientSlot = (clientSlot_raw - 0x32) / 2;
        Console.WriteLine($"CLI: Move item [{clientItemID}] to slot [{clientSlot}]");

        var clientSync_1 = rcvBuffer[17];
        var clientSync_2 = rcvBuffer[18];

        var clientSyncOther_1 = (rcvBuffer[10] & 0b11000000) >> 4;
        var clientSyncOther_2 = rcvBuffer[11];
        var clientSyncOther_3 = rcvBuffer[12] & 0b111111;
        var clientSyncOther = (ushort)((clientSyncOther_3 << 10) + (clientSyncOther_2 << 2) + clientSyncOther_1);

        var serverItemID_1 = (clientItemID & 0b111111) << 2;
        var serverItemID_2 = (clientItemID & 0b11111111000000) >> 6;
        var serverItemID_3 = (clientItemID & 0b1100000000000000) >> 14;

        var moveResult = new byte[]
        {
            0x2E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte((ushort)clientItemID), MajorByte((ushort)clientItemID), 
            0xE8, 0xC7, 0xA0, 0xB0, 0x6E, 0xA6, 0x88, 0x98, 0x95, 0xB1, 0x28, 0x09, 0xDC, 0x85, 0xC8, 0xDF, 0x02, 0x0C, 
            MinorByte(clientSyncOther), MajorByte(clientSyncOther), 0x01, 0xFC, clientSync_1, clientSync_2, 0x10, 0x80,
            0x82, 0x20, (byte)(clientSlot_raw * 2), (byte)serverItemID_1, (byte)serverItemID_2, (byte)serverItemID_3,
            0x20, 0x4E, 0x00, 0x00, 0x00
        };

        streamPeer.PutPartialData(moveResult);
    }

    private void MoveEntity(WorldCoords coords, ushort entityId)
    {
        MoveEntity(coords.x, coords.y, coords.z, coords.turn, entityId);
    }

    public void MoveEntity(double x0, double y0, double z0, double t0, ushort entityId)
    {
        // best guess for X and Z: decimal value in packet = 4095 - coord_value, where coord_value is in 0..63 range
        // for Y max value becomes 2047 with the same formula
        // technically, it's not even decimal, as it's possible to move by ~50 units if 0 is sent instead of 4095 
        var xDec = 4095 - (1 - (int)Math.Truncate((x0 - Math.Truncate(x0))) * 64);
        var yDec = 2047 - (int)Math.Truncate((y0 - Math.Truncate(y0)) * 64);
        var zDec = 4095 - (1 - (int)Math.Truncate((z0 - Math.Truncate(z0))) * 64);
        var x = 32768 + (int)x0;
        var y = 1200 + (int)y0;
        var z = 32768 + (int)z0;
        var x_1 = (byte)(((x & 0b1111111) << 1) + 1);
        var x_2 = (byte)((x & 0b111111110000000) >> 7);
        var y_1 = (byte)(((y & 0b1111111) << 1) + ((x & 0b1000000000000000) >> 15));
        var z_1 = (byte)(((z & 0b11) << 6) + ((y & 0b1111110000000) >> 7));
        var z_2 = (byte)((z & 0b1111111100) >> 2);
        var z_3 = (byte)((z & 0b1111110000000000) >> 10);
        var id_1 = (byte)(((entityId & 0b111) << 5) + 0b10001);
        var id_2 = (byte)((entityId & 0b11111111000) >> 3);
        var id_3 = (byte)((entityId & 0b1111100000000000) >> 11);
        var xdec_1 = (byte)((xDec & 0b111111) << 2);
        var ydec_1 = (byte)(((yDec & 0b11) << 6) + ((xDec & 0b111111000000) >> 6));
        var ydec_2 = (byte)((yDec & 0b1111111100) >> 2);
        var zdec_1 = (byte)(((zDec & 0b111111) << 2) + ((yDec & 0b110000000000) >> 10));
        while (Math.Abs(t0) > 2 * Mathf.Pi)
        {
            t0 -= Math.Sign(t0) * 2 * Mathf.Pi;
        }
        var turn = (int) (t0 * 256 / 2 / Mathf.Pi);
        
        var turn_1 = (byte)(((turn & 0b11) << 6) + ((zDec & 0b111111000000) >> 6));
        var turn_2 = (byte)((turn & 0b11111100) >> 2);
        var movePacket = new byte[]
        {
            0x17, 0x00, 0x2c, 0x01, 0x00, x_1, x_2, y_1, z_1, z_2, z_3, 0x2D, id_1, id_2, id_3, 0x6A, 0x10, xdec_1, 
            ydec_1, ydec_2, zdec_1, turn_1, turn_2
        };

        streamPeer.PutPartialData(movePacket);
    }

    public void ChangeHealth(ushort entityId, int healthDiff)
    {
        var currentPlayerId = ByteSwap(currentPlayerIndex);
        var playerId_1 = (byte)(((currentPlayerId & 0b1111) << 4) + 0b0111);
        var playerId_2 = (byte)((currentPlayerId & 0b111111110000) >> 4);
        var mobId_1 = (byte)((entityId & 0b1111111) << 1);
        var mobId_2 = (byte)((entityId & 0b111111110000000) >> 7);
        var hpMod = healthDiff < 0 ? 0b1110 : 0b1100;
        healthDiff = Math.Abs(healthDiff);
        var dmg_1 = (byte)(((healthDiff & 0b1111) << 4) + hpMod + ((entityId & 0b1000000000000000) >> 15));
        var dmg_2 = (byte)((healthDiff & 0b111111110000) >> 4);
        var dmg_3 = (byte)((healthDiff & 0b11111111000000000000) >> 12);
        var playerId_3 = (byte)(0b10000000 + ((currentPlayerId & 0b1111000000000000) >> 12));
        var dmgPacket = new byte[]
        {
            0x1F, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(entityId), MajorByte(entityId), 0x48, 0x43, 0x65, 0x00, 
            0x00, 0x00, 0x80, 0xA0, 0x03, 0x00, 0x00, 0xE0, playerId_1, playerId_2, playerId_3, 0x00, 0x24, mobId_1, 
            mobId_2, dmg_1, dmg_2, dmg_3
        };
        streamPeer.PutPartialData(dmgPacket);
    }

    public void DropLoot(ushort lootBagId, double x, double y, double z)
    {
        var xArr = CoordsHelper.EncodeServerCoordinate(x);
        var yArr = CoordsHelper.EncodeServerCoordinate(y);
        var zArr = CoordsHelper.EncodeServerCoordinate(z);
        var x_1 = ((xArr[0] & 0b111) << 5) + 0b01111;
        var x_2 = ((xArr[1] & 0b111) << 5) + ((xArr[0] & 0b11111000) >> 3);
        var x_3 = ((xArr[2] & 0b111) << 5) + ((xArr[1] & 0b11111000) >> 3);
        var x_4 = ((xArr[3] & 0b111) << 5) + ((xArr[2] & 0b11111000) >> 3);
        var y_1 = ((yArr[0] & 0b111) << 5) + ((xArr[3] & 0b11111000) >> 3);
        var y_2 = ((yArr[1] & 0b111) << 5) + ((yArr[0] & 0b11111000) >> 3);
        var y_3 = ((yArr[2] & 0b111) << 5) + ((yArr[1] & 0b11111000) >> 3);
        var y_4 = ((yArr[3] & 0b111) << 5) + ((yArr[2] & 0b11111000) >> 3);
        var z_1 = ((zArr[0] & 0b111) << 5) + ((yArr[3] & 0b11111000) >> 3);
        var z_2 = ((zArr[1] & 0b111) << 5) + ((zArr[0] & 0b11111000) >> 3);
        var z_3 = ((zArr[2] & 0b111) << 5) + ((zArr[1] & 0b11111000) >> 3);
        var z_4 = ((zArr[3] & 0b111) << 5) + ((zArr[2] & 0b11111000) >> 3);
        var z_5 = 0b01100000 + ((zArr[3] & 0b11111000) >> 3);
        
        var lootBagPacket = new byte[]
        {
            0x1D, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(lootBagId), MajorByte(lootBagId), 0x5C, 0x86, (byte) x_1, 
            (byte) x_2, (byte) x_3, (byte) x_4, (byte) y_1, (byte) y_2, (byte) y_3, (byte) y_4, (byte) z_1, (byte) z_2, 
            (byte) z_3, (byte) z_4, (byte) z_5, 0x20, 0x91, 0x45, 0x06, 0x00
        };
        
        streamPeer.PutPartialData(lootBagPacket);
    }

    private static int GetDestinationIdFromDamagePacket(byte[] rcvBuffer)
    {
        var destBytes = rcvBuffer.Range(28, rcvBuffer.Length);

        return ((destBytes[2] & 0b11111) << 11) + ((destBytes[1]) << 3) + ((destBytes[0] & 0b11100000) >> 5);
    }

    private static int GetDestinationIdFromFistDamagePacket(byte[] rcvBuffer)
    {
        var destBytes = rcvBuffer.Range(21, rcvBuffer.Length);

        return ((destBytes[2] & 0b11111) << 11) + ((destBytes[1]) << 3) + ((destBytes[0] & 0b11100000) >> 5);
    }

    private void UpdateCoordsWithoutAxisFlip(WorldCoords coords)
    {
        UpdateCoordsWithoutAxisFlip(coords.x, coords.y, coords.z, coords.turn);
    }

    private void UpdateCoordsWithoutAxisFlip(double x, double y, double z, double turn)
    {
        var clientModelTransform = clientModel!.Transform;
        clientModelTransform.origin =
            new Vector3((float) x, (float) y, (float) z);
        clientModel.Transform = clientModelTransform;
        oldY = CurrentCharacter.Y;
    }
}