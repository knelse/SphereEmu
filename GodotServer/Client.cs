using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Godot;
using SphServer;
using SphServer.DataModels;
using SphServer.Db;
using SphServer.Helpers;
using SphServer.Packets;
using static SphServer.Helpers.BitHelper;
using File = System.IO.File;
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
    public ushort ID = 0x4F6F;
    public StreamPeerTCP StreamPeer = null!;
    private const bool reconnect = false;
    public const int BUFSIZE = 1024;
    private string playerIndexStr = null!;
    private readonly byte[] rcvBuffer = new byte[BUFSIZE];
    public const bool LiveServerCoords = false;
    private ushort counter;
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
    private readonly FileSystemWatcher watcher = new ("C:\\source\\", "itemDropPacketTest.txt");
    private static LootBag testLootBag;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        playerIndexStr = ConvertHelper.ToHexString(new[]
        {
            MajorByte(ID),
            MinorByte(ID)
        });
        watcher.NotifyFilter = NotifyFilters.LastWrite;
        watcher.EnableRaisingEvents = true;
        watcher.Changed += (sender, args) =>
        {
            if (args.ChangeType == WatcherChangeTypes.Changed)
            {
                StreamPeer.PutData(CommonPackets.DespawnEntity(testLootBag.ID));
                MainServer.GameObjects.TryRemove(testLootBag.ID, out var _);
                MainServer.GameObjects.TryRemove(testLootBag.Item0.ID, out var _);
                // MainServer.currentId -= 2;
                testLootBag.ParentNode.QueueFree();
                testLootBag = LootBag.Create(-1102.69506835937500, 4500.61474609375000, 1899.05493164062500, 0, 0,
                    LootRatityType.DEFAULT_MOB, 1);
            }
        };
    }

    public override async void _Process(float delta)
    {
        if (StreamPeer.GetStatus() != StreamPeerTCP.Status.Connected)
        {
            CloseConnection();
        }

        clientModel ??= GetNode<StaticBody>("ClientModel");

        switch (currentState)
        {
            case ClientState.I_AM_BREAD:
                Console.WriteLine($"CLI {playerIndexStr}: Ready to load initial data");
                StreamPeer.PutData(reconnect
                    ? CommonPackets.ReadyToLoadInitialDataReconnect
                    : CommonPackets.ReadyToLoadInitialData);
                currentState = ClientState.INIT_READY_FOR_INITIAL_DATA;

                break;
            case ClientState.INIT_READY_FOR_INITIAL_DATA:
                if (StreamPeer.GetBytes(rcvBuffer) == 0)
                {
                    return;
                }

                Console.WriteLine($"CLI {playerIndexStr}: Connection initialized");
                StreamPeer.PutData(CommonPackets.ServerCredentials(ID));
                Console.WriteLine($"SRV {playerIndexStr}: Credentials sent");
                currentState = ClientState.INIT_WAITING_FOR_LOGIN_DATA;

                break;
            case ClientState.INIT_WAITING_FOR_LOGIN_DATA:
                if (StreamPeer.GetBytes(rcvBuffer) <= 12)
                {
                    return;
                }

                Console.WriteLine($"CLI {playerIndexStr}: Login data sent");
                (var login, var password) = LoginHelper.GetLoginAndPassword(rcvBuffer);

                charListData =
                    Login.CheckLoginAndGetPlayerCharacters(login, password, ID);
                Console.WriteLine("Fetched char list data");
                await (ToSignal(GetTree().CreateTimer(0.05f), "timeout"));

                if (charListData == null)
                {
                    // TODO: actual incorrect pwd packet
                    Console.WriteLine($"SRV {playerIndexStr}: Incorrect password!");
                    StreamPeer.PutData(CommonPackets.AccountAlreadyInUse(ID));
                    CloseConnection();

                    return;
                }

                StreamPeer.PutData(CommonPackets.CharacterSelectStartData(ID));
                Console.WriteLine("SRV: Character select screen data - initial");
                Thread.Sleep(100);

                StreamPeer.PutData(charListData.ToByteArray(ID));
                Console.WriteLine("SRV: Character select screen data - player characters");
                Thread.Sleep(100);
                currentState = ClientState.INIT_WAITING_FOR_CHARACTER_SELECT;

                break;
            case ClientState.INIT_WAITING_FOR_CHARACTER_SELECT:
                if (selectedCharacterIndex == -1)
                {
                    if (StreamPeer.GetBytes(rcvBuffer) == 0x15)
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
                CurrentCharacter.Client = this;

                Console.WriteLine("CLI: Enter game");
                // TODO: this ID is mostly static 0x4F6F for testing, fix later
                MainServer.TryAddToGameObjects(CurrentCharacter.ID, CurrentCharacter);
                StreamPeer.PutData(CurrentCharacter.ToGameDataByteArray());
                currentState = ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK;
                break;
            case ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK:
                if (StreamPeer.GetBytes(rcvBuffer) == 0x13)
                {
                    return;
                }
                // Interlocked.Increment(ref playerCount);

                var worldData = CommonPackets.NewCharacterWorldData(CurrentCharacter.ID);
                StreamPeer.PutData(worldData[0]);
                Thread.Sleep(50);
                StreamPeer.PutData(worldData[1]);
                currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY;
                await ToSignal(GetTree().CreateTimer(3), "timeout");
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
            StreamPeer.PutData(CommonPackets.FifteenSecondPing(ID));
            timeSinceLastFifteenSecondPing = 0;
        }

        if (timeSinceLastSixSecondPing >= 6)
        {
            StreamPeer.PutData(CommonPackets.SixSecondPing(ID));
            timeSinceLastSixSecondPing = 0;
        }

        if (timeSinceLastTransmissionEndPing >= 3)
        {
            StreamPeer.PutData(CommonPackets.TransmissionEndPacket);
            timeSinceLastTransmissionEndPing = 0;
        }

        var length = StreamPeer.GetBytes(rcvBuffer);

        if (length == 0)
        {
            return;
        }
        
        switch (rcvBuffer[0])
        {
            // ping
            case 0x26:
                SendPingResponse();
                break;
            // interact (move item, open loot container)
            case 0x1A:
                if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0xC1)
                {
                    // item pickup
                    PickupItemToInventory();
                }
                else if (rcvBuffer[13] == 0x5c && rcvBuffer[14] == 0x46 && rcvBuffer[15] == 0xe1)
                {
                    var containerId = rcvBuffer[11] + rcvBuffer[12] * 0x100;
                    // open loot container
                    if (MainServer.GameObjects.TryGetValue(containerId, out IGameEntity ent) && ent is LootBag bag)
                    {
                        bag.ShowDropitemListForClient(ID);
                        var type = (byte) (counter % 2 == 1 ? 0xD0 : 0xD8);
                        var txt = File.ReadAllText("C:\\source\\itemDropPacketTest.txt").RemoveLineEndings();
                        var test = new List<byte>
                        {
                            0x2C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(bag.Item0.ID), MajorByte(bag.Item0.ID), 
                            // type, 0x87, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                            // 0x40, 0x91, 0x45, 0x66, 0xBC, 0x23, 0x48, 0x01, 0x06, 0x1E, 0x31, 0x01, 0x0A, 0x59, 0x00, 
                            // 0xF0, 0xFF, 0xFF, 0xFF, 0x0F
                        
                        };
                        test.AddRange(BinaryStringToByteArray(txt));
                        StreamPeer.PutData(test.ToArray());
                    }
                }

                break;
            // echo
            case 0x08:
                // StreamPeer.PutData(CommonPackets.Echo(ID));

                break;
            // damage
            // case 0x19:
            case 0x20:
                var damage = (byte) 43;// (byte)(10 + RNGHelper.GetUniform() * 8);
                var destId = (ushort)GetDestinationIdFromDamagePacket(rcvBuffer);
                var playerIndexByteSwap = ByteSwap(ID);
                var selfDamage = destId == playerIndexByteSwap;

                if (selfDamage)
                {
                    var selfDamagePacket = new byte[]
                    {
                        0x10, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(ID), 
                        MinorByte(ID), 0x08, 0x40, (byte)(rcvBuffer[25] + 2), rcvBuffer[26], 
                        (byte)(rcvBuffer[27] + 0x60), damage, 0x00
                    };
                    StreamPeer.PutData(selfDamagePacket);
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
                        StreamPeer.PutData(damagePacket);
                    }
                    else
                    {
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
                        StreamPeer.PutData(Packet.ToByteArray(deathPacket));
                        MainServer.GameObjects.TryGetValue(destId, out var mobEnt);
                        var mob = (Mob) mobEnt!;
                        // LootBag.CreateFromEntity(mob);
                        LootBag.Create(mob.ParentNode.GlobalTransform.origin.x, mob.ParentNode.GlobalTransform.origin.y, 
                            mob.ParentNode.GlobalTransform.origin.z, 0, 0, LootRatityType.DEFAULT_MOB);
                        mob.ParentNode.SetInactive();
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

                // StreamPeer.PutData(TestHelper.GetEntityData(
                //     new WorldCoords(669.1638793945312, 4501.63134765625, 931.0355224609375, -1), 4816,
                //     7654, 4816));

                var i = 0;

                // while (ns.CanWrite)
                // {
                //     var vendorList =
                //         $"27002C010000044f6f0840A362202D10E097164832142600400108E0DF08000000004000000000";
                //     StreamPeer.PutData(ConvertHelper.FromHexString(vendorList));
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
                //         StreamPeer.PutData(TestHelper.GetEntityData(
                //             new WorldCoords(671.1638793945312 + x, 4501.63134765625, 932.0355224609375 + y,
                //                 -1), 971, 7654 + i, entTypeId));
                //
                //         i++;
                //     }
                //
                //     System.Threading.Thread.Sleep(1350);
                // }

                // var vendorListLoaded = $"30002C01000004FE8D14870F80842E0900000000000000004091456696101560202D10A0900500FFFFFFFF0516401F00";
                // StreamPeer.PutData(ConvertHelper.FromHexString(vendorListLoaded));
                var vendorListLoaded =
                    BinaryStringToByteArray(System.IO.File.ReadAllText("C:\\source\\vendorList.txt")
                        .RemoveLineEndings());
                StreamPeer.PutData(vendorListLoaded);

                break;
        }
        
        var clientModelTransform = clientModel.Transform;

        // ignore new client coords if they're too far away from what server knows
        // if (clientModelTransform.origin.DistanceTo(new Vector3((float)CurrentCharacter.X, 
        //         (float) (clientModelTransform.origin.y + oldY - CurrentCharacter.Y), (float)CurrentCharacter.Z)) < 50)
        // {
        clientModelTransform.origin =
            new Vector3((float)CurrentCharacter.X, (float) CurrentCharacter.Y, (float)CurrentCharacter.Z);
        clientModel.Transform = clientModelTransform;
        // }
    }

    private int CharacterScreenCreateDeleteSelect()
    {
        if (rcvBuffer[0] == 0x2A)
        {
            var charIndex = rcvBuffer[17] / 4 - 1;

            Console.WriteLine($"Delete character [{charIndex}] - [{charListData![charIndex]!.Name}]");
            DbCharacters.DeleteCharacterFromDb(charListData[charIndex]!.DbId);

            // TODO: reinit session after delete
            // await HandleClientAsync(client, (ushort) (ID + 1), true);

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
            StreamPeer.PutData(CommonPackets.NameAlreadyExists(ID));
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

            var newCharacterData = CharacterData.CreateNewCharacter(ID, name,
                isGenderFemale, faceType, hairStyle, hairColor, tattoo);

            charListData!.AddNewCharacter(newCharacterData, charIndex);
            DbCharacters.AddNewCharacterToDb(charListData.PlayerId, newCharacterData,
                charIndex);

            StreamPeer.PutData(CommonPackets.NameCheckPassed(ID));

            return charIndex;
        }

        return -1;
    }

    private async Task MoveToNewPlayerDungeonAsync(CharacterData selectedCharacter)
    {
        var newDungeonCoords = new WorldCoords(-1098, -4501.62158203125, 1900);
        var playerCoords = new WorldCoords(-1098.69506835937500, -4501.61474609375000, 1900.05493164062500,
            1.57079637050629);
        StreamPeer.PutData(selectedCharacter.GetNewPlayerDungeonTeleportAndUpdateStatsByteArray(playerCoords));

        currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_INITIATED;
        await ToSignal(GetTree().CreateTimer(0.5f), "timeout");

        StreamPeer.PutData(CommonPackets.LoadNewPlayerDungeon);
        Console.WriteLine(
            $"SRV: Teleported client [{MinorByte(selectedCharacter.ID) * 256 + MajorByte(selectedCharacter.ID)}] to default new player dungeon");
        var mobX = newDungeonCoords.x - 50;
        var mobY = newDungeonCoords.y;
        var mobZ = newDungeonCoords.z + 19.5;
        
        Mob.Create(mobX, mobY, mobZ, 0, 1260, 0, 1009, 1241);
        // for debug
        // testLootBag = LootBag.Create(-1102.69506835937500, 4500.61474609375000, 1899.05493164062500, 0, 0,
        //     LootRatityType.DEFAULT_MOB, 1);
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1899.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1899.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1899.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1899.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1899.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1899.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1898.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1898.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1898.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1898.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1898.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1898.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1900.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1900.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1900.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1900.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1900.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1900.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);
        
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1895.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1895.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1895.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1895.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1895.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1895.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1896.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1896.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1896.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1896.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1896.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1896.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1897.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1897.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1897.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1897.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1897.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1897.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);
        
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1901.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1901.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1901.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1901.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1901.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1901.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1902.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1902.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1902.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1902.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1902.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1902.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1903.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1903.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1903.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1903.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1903.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1903.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);
        
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1904.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1904.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1904.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1904.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1904.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1904.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);
        testLootBag = LootBag.Create(-1098.49506835937500, -4501.61474609375000, 1905.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4500.51474609375000, 1905.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4499.41474609375000, 1905.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4498.31474609375000, 1905.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4497.21474609375000, 1905.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);        
        testLootBag = LootBag.Create(-1098.49506835937500, -4496.11474609375000, 1905.05493164062500, 0, 0,
            LootRatityType.DEFAULT_MOB, 1);
        // LootBag.Create(-1102.69506835937500, 4500.61474609375000, 1900.05493164062500, 0, 0,
        //     LootRatityType.DEFAULT_MOB, 2);
        // LootBag.Create(-1102.69506835937500, 4500.61474609375000, 1901.05493164062500, 0, 0,
        //     LootRatityType.DEFAULT_MOB, 3);
        // LootBag.Create(-1102.69506835937500, 4500.61474609375000, 1902.05493164062500, 0, 0,
        //     LootRatityType.DEFAULT_MOB, 4);

        currentState = ClientState.INGAME_DEFAULT;
        var clientTransform = clientModel!.Transform;
        clientTransform.origin.x = (float) playerCoords.x;
        clientTransform.origin.y = (float) playerCoords.y;
        clientTransform.origin.z = (float) playerCoords.z;
        clientModel.Transform = clientTransform;
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
                CurrentCharacter.Turn = coords.turn;
                Console.WriteLine(coords.ToDebugString());

                pingPreviousClientPingString = clientPingBinaryStr;
            }
        }

        var xored = clientPingBytesForPong[5];

        if (pingShouldXorTopBit)
        {
            xored ^= 0b10000000;
        }

        if (counter == 0)
        {
            var first = (ushort)((clientPingBytesForPong[7] << 8) + clientPingBytesForPong[6]);
            first -= 0xE001;
            counter = (ushort)(0xE001 + first / 12);
        }

        var pong = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, xored, MinorByte(counter), MajorByte(counter), 0x00, 0x00, 0x00, 0x00, 0x00
        };

        Array.Copy(clientPingBytesForPong, pong, 5);
        Array.Copy(clientPingBytesForPong, 8, pong, 8, 4);
        StreamPeer.PutData(Packet.ToByteArray(pong, 1));
        pingShouldXorTopBit = !pingShouldXorTopBit;
        counter++;

        //overflow
        if (counter < 0xE001)
        {
            counter = 0xE001;
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

        StreamPeer.PutData(moveResult);
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

        StreamPeer.PutData(movePacket);
    }

    public void ChangeHealth(ushort entityId, int healthDiff)
    {
        var currentPlayerId = ByteSwap(ID);
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
        StreamPeer.PutData(dmgPacket);
    }

    public static void TryFindClientByIdAndSendData(ushort clientId, byte[] data)
    {
        if (MainServer.GameObjects.TryGetValue(clientId, out IGameEntity ent) && ent is CharacterData characterData)
        {
            characterData.Client.StreamPeer.PutData(data);
        }
    }

    public float DistanceTo(Vector3 end)
    {
        return clientModel!.GlobalTransform.origin.DistanceTo(end);
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

    // private void UpdateCoordsWithoutAxisFlip(WorldCoords coords)
    // {
    //     UpdateCoordsWithoutAxisFlip(coords.x, coords.y, coords.z, coords.turn);
    // }

    // private void UpdateCoordsWithoutAxisFlip(double x, double y, double z, double turn)
    // {
    //     var clientModelTransform = clientModel!.Transform;
    //     clientModelTransform.origin =
    //         new Vector3((float) x, (float) y, (float) z);
    //     clientModel.Transform = clientModelTransform;
    //     oldY = CurrentCharacter.Y;
    // }
}