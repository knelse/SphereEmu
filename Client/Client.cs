using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BitStreams;
using Godot;
using SphServer;
using SphServer.DataModels;
using SphServer.Db;
using SphServer.Helpers;
using SphServer.Helpers.ConsoleCommands;
using SphServer.Packets;
using SphServer.Providers;
using SphServer.Repositories;
using static SphServer.Helpers.BitHelper;
using static SphServer.Helpers.Cities;
using static SphServer.Helpers.Continents;
using static SphServer.Helpers.PoiType;

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

public partial class Client : Node
{
    private const bool reconnect = false;
    public const int BUFSIZE = 1024;
    public const bool LiveServerCoords = false;
    public const ushort CurrentCharacterInventoryId = 0xA001;

    // public static readonly string PingCoordsFilePath = LiveServerCoords
    //     ? @"C:\_sphereDumps\currentWorldCoords"
    //     : @"D:\SphereDev\SphereSource\source\clientCoordsSaved";

    private static ItemContainer _testItemContainer = null!;
    public static readonly ConcurrentDictionary<int, ushort> GlobalToLocalIdMap = new ();

    private readonly byte[] rcvBuffer = new byte[BUFSIZE];

    // private readonly FileSystemWatcher watcher = new (@"D:\SphereDev\SphereSource\source\", "statUpdatePacket.txt");
    private StaticBody3D? clientModel;
    private ushort counter;
    public Character? CurrentCharacter;
    private ClientState currentState = ClientState.I_AM_BREAD;
    public int GlobalId;
    public ushort LocalId = 0x4F6F;
    private int newPlayerDungeonMobHp = 64;
    private string? pingPreviousClientPingString;
    private bool pingShouldXorTopBit;
    private Player? player;
    private string playerIndexStr = null!;
    private int selectedCharacterIndex = -1;
    public StreamPeerTcp StreamPeer = null!;
    private double timeSinceLastFifteenSecondPing = 1000;
    private double timeSinceLastSixSecondPing = 1000;
    private double timeSinceLastTransmissionEndPing = 1000;
    private static ushort NewEntityIndex = 0x1000;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready ()
    {
        playerIndexStr = Convert.ToHexString(new[]
        {
            MajorByte(LocalId),
            MinorByte(LocalId)
        });
        // watcher.NotifyFilter = NotifyFilters.LastWrite;
        // watcher.EnableRaisingEvents = true;
        // watcher.Changed += (_, args) =>
        Task.Run(() =>
        {
            // var consoleHistoryFilePath = @"C:\_sphereDumps\emuConsoleHistory.txt";
            // var consoleHistory = Path.Exists(consoleHistoryFilePath)
            //     ? File.ReadAllLines(consoleHistoryFilePath).ToList()
            //     : new List<string>();
            // var consoleHistoryIndex = consoleHistory.Count;
            // var consoleHistoryContent = string.Empty;
            while (true)
            {
                // var shouldUpdateInput = false;
                // var key = Console.ReadKey(false);
                // if (key.Key == ConsoleKey.UpArrow)
                // {
                //     consoleHistoryIndex = Math.Max(consoleHistoryIndex - 1, 0);
                //     shouldUpdateInput = true;
                // }
                //
                // else if (key.Key == ConsoleKey.DownArrow)
                // {
                //     consoleHistoryIndex++;
                //     consoleHistoryIndex = Math.Min(consoleHistoryIndex + 1, consoleHistory.Count);
                //     shouldUpdateInput = true;
                // }
                //
                // if (char.IsLetterOrDigit(key.KeyChar) || char.IsPunctuation(key.KeyChar) || char.IsSeparator(key.KeyChar) || char.IsSymbol(key.KeyChar))
                // {
                //     consoleHistoryContent += key.KeyChar;
                // }
                //
                // if (shouldUpdateInput)
                // {
                //     Console.SetCursorPosition(0, Console.CursorTop);
                //     Console.Write(new string(' ', Console.BufferWidth));
                //     Console.SetCursorPosition(0, Console.CursorTop - 1);
                //     if (consoleHistoryIndex < consoleHistory.Count)
                //     {
                //         consoleHistoryContent = consoleHistory[consoleHistoryIndex];
                //         Console.Write(consoleHistory[consoleHistoryIndex]);
                //     }
                //     else
                //     {
                //         consoleHistoryContent = string.Empty;
                //     }
                //
                //     continue;
                // }

                var input = Console.ReadLine();
                try
                {
                    if (CurrentCharacter is null)
                    {
                        Console.WriteLine("Character is null");
                        continue;
                    }

                    var parser = ConsoleCommandParser.Get(CurrentCharacter);
                    var result = parser.Parse(input);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        });
    }

    public override async void _Process (double delta)
    {
        if (StreamPeer.GetStatus() != StreamPeerTcp.Status.Connected)
        {
            // TODO: sync state
            CloseConnection();
            ActiveClientsRepository.Delete(LocalId);
            ActiveNodesRepository.Delete(LocalId);
            QueueFree();
        }

        clientModel ??= GetNode<StaticBody3D>("ClientModel");

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
                await ToSignal(GetTree().CreateTimer(0.2f), "timeout");
                StreamPeer.PutData(CommonPackets.ServerCredentials(LocalId));
                Console.WriteLine($"SRV {playerIndexStr}: Credentials sent");
                currentState = ClientState.INIT_WAITING_FOR_LOGIN_DATA;

                break;
            case ClientState.INIT_WAITING_FOR_LOGIN_DATA:
                if (StreamPeer.GetBytes(rcvBuffer) <= 12)
                {
                    return;
                }

                Console.WriteLine($"CLI {playerIndexStr}: Login data sent");
                var (login, password) = LoginHelper.GetLoginAndPassword(rcvBuffer);

                player =
                    Login.CheckLoginAndGetPlayer(login, password, LocalId);
                Console.WriteLine("Fetched char list data");
                await ToSignal(GetTree().CreateTimer(0.05f), "timeout");

                if (player == null)
                {
                    // TODO: actual incorrect pwd packet
                    Console.WriteLine($"SRV {playerIndexStr}: Incorrect password!");
                    StreamPeer.PutData(CommonPackets.CannotConnect(LocalId));
                    CloseConnection();

                    return;
                }

                player.Index = LocalId;

                StreamPeer.PutData(CommonPackets.CharacterSelectStartData(LocalId));
                Console.WriteLine("SRV: Character select screen data - initial");
                Thread.Sleep(100);

                var playerInitialData = player.ToInitialDataByteArray();

                StreamPeer.PutData(playerInitialData);
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

                    if (rcvBuffer[0] == 0x2A)
                    {
                        if (rcvBuffer[0] == 0x2A)
                        {
                            var charIndex = rcvBuffer[17] / 4 - 1;
                            var charId = player!.Characters[charIndex].Id;
                            Console.WriteLine(
                                $"Delete character [{charIndex}] - [{player!.Characters[charIndex].Name}]");
                            player!.Characters.RemoveAt(charIndex);
                            DbConnectionProvider.PlayerCollection.Update(player);
                            DbConnectionProvider.CharacterCollection.Delete(charId);

                            // TODO: reinit session after delete
                            // await HandleClientAsync(client, (ushort) (ID + 1), true);

                            CloseConnection();
                        }
                    }

                    if (rcvBuffer[0] < 0x1b ||
                        rcvBuffer[13] != 0x08 || rcvBuffer[14] != 0x40 || rcvBuffer[15] != 0x80 ||
                        rcvBuffer[16] != 0x05)
                    {
                        return;
                    }

                    selectedCharacterIndex = CharacterScreenCreateDeleteSelect();
                }

                if (selectedCharacterIndex == -1)
                {
                    return;
                }

                CurrentCharacter = player!.Characters[selectedCharacterIndex];
                CurrentCharacter.ClientIndex = LocalId;
                // CurrentCharacter.ClientIndex = 0x20E8;
                // CurrentCharacter.X = 337;
                // CurrentCharacter.Y = -158;
                // CurrentCharacter.Z = -1450;
                // CurrentCharacter.X = 424;
                // CurrentCharacter.Y = -153;
                // CurrentCharacter.Z = -1278;
                CurrentCharacter.X = 80;
                CurrentCharacter.Y = -150;
                CurrentCharacter.Z = 200;
                CurrentCharacter.Angle = 0.75;
                CurrentCharacter.Money = 99999999;

                Console.WriteLine("CLI: Enter game");
                StreamPeer.PutData(CurrentCharacter.ToGameDataByteArray());
                currentState = ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK;
                break;
            case ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK:
                if (StreamPeer.GetBytes(rcvBuffer) == 0x13)
                {
                    return;
                }
                // Interlocked.Increment(ref playerCount);

                var worldData = CommonPackets.NewCharacterWorldData(CurrentCharacter.ClientIndex);
                StreamPeer.PutData(worldData[0]);
                StreamPeer.PutData(Convert.FromHexString(
                    "BA002C010000004F6F08C002D07911C8BD10445E0C222F08C91685C80B03581CC002011609B05080C5022C1860D1000B07593CC802021611B09080C5042C286051010B0B585CC00213799189BCD0445E6CC08203161DB0F080C5072C406011020B11588CC882441625B03081C5892D506091020B1558AC422C5870D1820B1758CCD082061635B0B0C1C603848F1535B10F2B6391702035D1F643F24F411072A0D901900100000A5290530F0000D0001170AA2A48410E32000000"));
                StreamPeer.PutData(Convert.FromHexString(
                    "83002C010000004F6F08406102000A824011820E400600005010841C000000000000808220E888A00300000000140461A70B1D0068890920445DE8000005419820480768010000280802402D3B007D0000404110706CD901060000000A120050908089820450142400A720013B0541A0C041072003000068E010280000003436020200"));

                Thread.Sleep(50);
                // StreamPeer.PutData(worldData[1]);
                // currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY;
                // await ToSignal(GetTree().CreateTimer(3), "timeout");
                // currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT;
                currentState = ClientState.INGAME_DEFAULT;
                break;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY:
                return;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT:
                await MoveToNewPlayerDungeonAsync(CurrentCharacter);
                // StreamPeer.PutData(CurrentCharacter.GetTeleportByteArray(new WorldCoords(2584, 160, 1426, -1.5)));
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
            StreamPeer.PutData(CommonPackets.FifteenSecondPing(LocalId));
            timeSinceLastFifteenSecondPing = 0;
        }

        if (timeSinceLastSixSecondPing >= 6)
        {
            StreamPeer.PutData(CommonPackets.SixSecondPing(LocalId));
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

        // todo: proper handling by packet type
        var receiveStream = new BitStream(rcvBuffer);
        receiveStream.CutStream(0, length);

        // if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0xC3)
        // {
        //     // clan
        //     var clientLocalId = (ushort) ((rcvBuffer[11] << 8) + rcvBuffer[12]);
        //     // there might be a better way to select action
        //     var shouldCreate = rcvBuffer[18] == 0x00 && rcvBuffer[19] == 0x00 && rcvBuffer[20] == 0x00;
        //     if (shouldCreate)
        //     {
        //         var nameLength = (rcvBuffer[16] >> 5) + ((rcvBuffer[17] & 0b11111) << 3) - 5;
        //         var name = new List<byte>();
        //
        //         for (var i = 0; i < nameLength; i++)
        //         {
        //             name.Add((byte) ((rcvBuffer[i + 21] >> 5) + ((rcvBuffer[i + 22] & 0b11111) << 3)));
        //         }
        //
        //         var clanNameBytes = name.ToArray();
        //         var clanNameString = MainServer.Win1251.GetString(clanNameBytes);
        //
        //         Console.WriteLine($"[{clientLocalId:X}] Create clan [{clanNameString}]");
        //
        //         var characterNameBytes = MainServer.Win1251.GetBytes(CurrentCharacter.Name);
        //
        //         // change clan rank
        //         var responseStream = GetWriteBitStream();
        //         responseStream.WriteBytes(new byte[]
        //         {
        //             (byte) (characterNameBytes.Length + 27), 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00,
        //             MajorByte(clientLocalId), MinorByte(clientLocalId), 0x08, 0x40, 0xC3, 0x22, 0x20, 0xA0, 0x71
        //         }, 16, true);
        //         responseStream.WriteByte(0x1, 4);
        //         responseStream.WriteByte((byte) (characterNameBytes.Length + 5));
        //         responseStream.WriteByte(0x1);
        //         responseStream.WriteByte(0x0);
        //         responseStream.WriteByte(MajorByte(clientLocalId));
        //         responseStream.WriteByte(MinorByte(clientLocalId));
        //         responseStream.WriteByte(0x0);
        //         responseStream.WriteBytes(characterNameBytes, characterNameBytes.Length, true);
        //         responseStream.WriteByte(0x0D);
        //         responseStream.WriteByte(0x12);
        //         responseStream.WriteByte(0x2);
        //         responseStream.WriteByte(0xA, 4);
        //         responseStream.WriteByte(0x0);
        //         var response = responseStream.GetStreamData();
        //         StreamPeer.PutData(response);
        //
        //         CurrentCharacter.Clan = new Clan
        //         {
        //             Id = 999,
        //             Name = clanNameString
        //         };
        //     }
        // }

        // else
        // {
        switch (rcvBuffer[0])
        {
            // ping
            case 0x26:
                SendPingResponse();
                break;

            //     // interact (move item, open loot container)
            //     case 0x1A:
            //         if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0xC1)
            //         {
            //             // item pickup to target slot
            //             PickupItemToTargetSlot();
            //         }
            //         else if (rcvBuffer[13] == 0x5c && rcvBuffer[14] == 0x46 && rcvBuffer[15] == 0xe1)
            //         {
            //             var containerId = rcvBuffer[11] + rcvBuffer[12] * 0x100;
            //             // open loot container
            //             var bag = DbConnectionProvider.ItemContainerCollection.Include(x => x.Contents).FindById(containerId);
            //             if (bag is not null)
            //             {
            //                 bag.ShowFourSlotBagDropitemListForClient(LocalId);
            //                 var packet = bag.GetContentsPacket(LocalId);
            //                 packet[6] = 0x04;
            //                 StreamPeer.PutData(packet);
            //             }
            //         }
            //         else if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0x43)
            //         {
            //             // chat message
            //             HandleChatMessage();
            //         }
            //
            //         break;
            //     case 0x16:
            //         // click on item to pick up or click on "Pickup all" button (repeats for every item)
            //         if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0x23)
            //         {
            //             PickupItemToInventory();
            //         }
            //
            //         break;
            //     case 0x18:
            //         // move to a different slot
            //         if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0x81)
            //         {
            //             MoveItemToAnotherSlot();
            //         }
            //         // use item from inventory
            //         else
            //         {
            //             UseItem();
            //         }
            //
            //         break;
            //     case 0x2D:
            //         // drop item to ground
            //         if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0x63)
            //         {
            //             DropItemToGround();
            //         }
            //
            //         break;
            //     case 0x15:
            //     case 0x19:
            //     case 0x1B:
            //     case 0x1F:
            //     case 0x23:
            //         // item in hand
            //         if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 &&
            //             (rcvBuffer[15] == 0xA3 || rcvBuffer[15] == 0x83))
            //         {
            //             MainhandTakeItem();
            //         }
            //
            //         break;
            //     // echo
            //     case 0x08:
            //         // StreamPeer.PutData(CommonPackets.Echo(ID));
            //         break;
            //     // damage or trade
            case 0x19:
            case 0x20:
                if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0x03)
                {
                    // BuyItemFromTarget();
                }
                else
                {
                    DamageTarget();
                }

                break;
            // vendor trade
            case 0x31:
            case 0x36:
                if (rcvBuffer[13] == 0x08 && rcvBuffer[14] == 0x40 && rcvBuffer[15] == 0xA3)
                {
                    receiveStream.ReadBits(364);
                    var vendorLocalId = receiveStream.ReadUInt16();
                    if (vendorLocalId == 0)
                    {
                        // first vendor open is 0x31, then client sends another 0x31 request to close the trade window,
                        // and later it's 0x36 to open 0x31 to close. Sphere =/
                        break;
                    }

                    var vendorId = GetGlobalObjectId(vendorLocalId);
                    var vendorWorldObject = ActiveWorldObjectRepository.Get((ushort) vendorId);
                    if (vendorWorldObject is null)
                    {
                        Console.WriteLine($"Vendor world object [{vendorId}] not found");
                        break;
                    }

                    vendorWorldObject.ClientInteract(LocalId, ClientInteractionType.OpenTrade);

                    // var vendor = DbConnectionProvider.VendorCollection.FindById(vendorId);
                    // if (vendor is null)
                    // {
                    //     Console.WriteLine($"Vendor [{vendorId}] not found");
                    // }
                    // else
                    // {
                    //     Console.WriteLine($"Vendor [{vendor.Name} {vendor.FamilyName}]");
                    //     var vendorSlotList = vendor.GetItemSlotListForClient(LocalId);
                    //     StreamPeer.PutData(vendorSlotList);
                    //     var itemContents = Packet.ItemsToPacket(LocalId, vendorLocalId, vendor.ItemsOnSale);
                    //
                    //     StreamPeer.PutData(itemContents);
                    // }
                }

                break;
            //     // group create/leave 08 40 23 23
            //     case 0x13:
            //         if (rcvBuffer[13] != 0x08 || rcvBuffer[14] != 0x40 || rcvBuffer[15] != 0x23 ||
            //             rcvBuffer[16] != 0x23)
            //         {
            //             break;
            //         }
            //
            //         var clientLocalId = (ushort) ((rcvBuffer[11] << 8) + rcvBuffer[12]);
            //         var action = rcvBuffer[17];
            //         switch (action)
            //         {
            //             case 0x00:
            //             {
            //                 // create
            //                 Console.WriteLine($"[{clientLocalId:X}] Create group");
            //                 var createResponse = new byte[]
            //                 {
            //                     0x13, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(clientLocalId),
            //                     MinorByte(clientLocalId),
            //                     0x08, 0x40, 0x23, 0x23, 0xA0, 0xA0, 0x91, 0x11, 0x90, 0x00
            //                 };
            //                 StreamPeer.PutData(createResponse);
            //                 break;
            //             }
            //             case 0x80:
            //                 // leave or disband
            //                 Console.WriteLine($"[{clientLocalId:X}] Leave group");
            //                 var leaveResponse = new byte[]
            //                 {
            //                     0x0F, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(clientLocalId),
            //                     MinorByte(clientLocalId),
            //                     0x08, 0x40, 0x23, 0x23, 0xA0, 0x01
            //                 };
            //                 StreamPeer.PutData(leaveResponse);
            //                 break;
            //         }
            //
            //         break;
        }
        // }

        var clientModelTransform = clientModel.Transform;

        // ignore new client coords if they're too far away from what server knows
        // if (clientModelTransform.origin.DistanceTo(new Vector3((float)CurrentCharacter.X, 
        //         (float) (clientModelTransform.origin.y + oldY - CurrentCharacter.Y), (float)CurrentCharacter.Z)) < 50)
        // {
        clientModelTransform.Origin =
            new Vector3((float) CurrentCharacter.X, (float) CurrentCharacter.Y, (float) CurrentCharacter.Z);
        clientModel.Transform = clientModelTransform;
        // }
    }

    private void HandleChatMessage ()
    {
        // 1A002C4109222C01003C57	638B	084043	008120E0570000600000
        try
        {
            var chatTypeVal = ((rcvBuffer[18] & 0b11111) << 3) + (rcvBuffer[17] >> 5);

            var firstPacket = rcvBuffer[..26];
            var packetCount = (firstPacket[23] >> 5) + ((firstPacket[24] & 0b11111) << 3);
            var packetStart = 26;
            var decodeList = new List<byte[]>();

            if (packetCount < 2)
            {
                Console.WriteLine("Broken client packet again reee");
                return;
            }

            for (var i = 0; i < packetCount; i++)
            {
                var packetLength = rcvBuffer[packetStart + 1] * 256 + rcvBuffer[packetStart];
                var packetEnd = packetStart + packetLength;
                var packetDecode = rcvBuffer[packetStart..packetEnd];
                packetStart = packetEnd;
                decodeList.Add(packetDecode);
            }

            var msgBytes = new List<byte>();
            var clientMessageContentBytes = new List<byte[]>
            {
                firstPacket[11..]
            };

            foreach (var decoded in decodeList)
            {
                var messagePart = decoded[21..];
                clientMessageContentBytes.Add(decoded[11..]);
                for (var j = 0; j < messagePart.Length - 1; j++)
                {
                    var msgByte = ((messagePart[j + 1] & 0b11111) << 3) + (messagePart[j] >> 5);
                    msgBytes.Add((byte) msgByte);
                }
            }

            // var chatTypeOverride = MainServer.Rng.Next(0, 17);
            // Console.WriteLine(chatTypeOverride);
            //
            // clientMessageContentBytes[0][6] = (byte)((clientMessageContentBytes[0][6] & 0b11111) + ((chatTypeOverride & 0b111) << 5));
            // clientMessageContentBytes[0][7] = (byte)((clientMessageContentBytes[0][7] & 0b11100000) + (chatTypeOverride >> 3));

            var serverResponseBytes = new List<byte>();

            // client_id 084043 content
            byte[] GetResponseArray (byte[] clientBytes)
            {
                var responseBytes = new byte[clientBytes.Length + 7];
                responseBytes[0] = (byte) (responseBytes.Length % 256);
                responseBytes[1] = (byte) (responseBytes.Length / 256);
                responseBytes[2] = 0x2C;
                responseBytes[3] = 0x01;
                responseBytes[4] = 0x00;
                responseBytes[5] = 0x00;
                responseBytes[6] = 0x00;
                Array.Copy(clientBytes, 0, responseBytes, 7, clientBytes.Length);
                return responseBytes;
            }

            serverResponseBytes.AddRange(GetResponseArray(clientMessageContentBytes[0]));
            serverResponseBytes.AddRange(GetResponseArray(clientMessageContentBytes[1]));
            // StreamPeer.PutData(serverResponseBytes.ToArray());

            for (var i = 2; i < clientMessageContentBytes.Count; i++)
            {
                // StreamPeer.PutData(GetResponseArray(clientMessageContentBytes[i]));
                // Console.WriteLine(Convert.ToHexString(GetResponseArray(clientMessageContentBytes[i])));
            }

            var chatString = SphereServer.Win1251.GetString(msgBytes.ToArray());
            var nameClosingTagIndex = chatString.IndexOf("</l>: ", StringComparison.OrdinalIgnoreCase);
            var nameStart = chatString.IndexOf("\\]\"", nameClosingTagIndex - 30, StringComparison.OrdinalIgnoreCase);
            var name = chatString[(nameStart + 4)..nameClosingTagIndex];
            var message = chatString[(nameClosingTagIndex + 6)..].TrimEnd((char) 0); // weird but necessary

            var response = ChatHelper.GetChatMessageBytesForServerSend(chatString, name, chatTypeVal);
            StreamPeer.PutData(response);

            Console.WriteLine($"CLI: [{chatTypeVal}] {name}: {message}");

            if (message.StartsWith("/tp"))
            {
                // TODO: actual client commands
                var coords = message.Split(" ", StringSplitOptions.RemoveEmptyEntries);

                if (coords.Length < 2)
                {
                    Console.WriteLine("Incorrect coods. Usage: /tp X Y Z OR /tp <name>");
                    return;
                }

                if (coords.Length == 2 && char.IsLetter(coords[1][0]))
                {
                    WorldCoords tpCoords;
                    if (coords[1].Equals("Shipstone", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Shipstone)];
                    }
                    else if (coords[1].Equals("Bangville", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Bangville)];
                    }
                    else if (coords[1].Equals("Torweal", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Torweal)];
                    }
                    else if (coords[1].Equals("Sunpool", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Sunpool)];
                    }
                    else if (coords[1].Equals("Umrad", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Umrad)];
                    }
                    else if (coords[1].Equals("ChoiceIsland", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][Other]["ChoiceIsland"];
                    }
                    else if (coords[1].Equals("Arena", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][Other]["Arena"];
                    }
                    else
                    {
                        Console.WriteLine($"Unknown teleport destination: {coords[1]}");
                        return;
                    }

                    StreamPeer.PutData(CurrentCharacter.GetTeleportByteArray(tpCoords));
                    return;
                }

                if (coords.Length < 4)
                {
                    Console.WriteLine("Incorrect coords. Usage: /tp X Y Z OR /tp <name>");
                    return;
                }

                var teleportCoords =
                    new WorldCoords(double.Parse(coords[1]), -double.Parse(coords[2]), double.Parse(coords[3]));

                StreamPeer.PutData(CurrentCharacter.GetTeleportByteArray(teleportCoords));
            }

            if (message.StartsWith("/buff"))
            {
                var jumpx4 =
                    "3F002C0100A01A29C678800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49E9FD93408ACF007F70391E0004F6F00";
                //	 3F002C0100500199AB78800F80842E090000000000000000409145068002C0C0D72AC0010000000000000000000044EDF9994D83C00A0F07F70391E1005FAB00
                // var runSpeed =
                // 	"3F002C0100720A2EC278800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49ECFE13408A8F00704046C28004F6F00";
                //   3F002C01002CEF8F9578800F80842E090000000000000000409145068002C0400903C0010000000000000000000044EDF91C4E83800A0F0704046C2800250C
                var test =
                    "3F002C010012DF127E78800F80842E090000000000000000409145068002C0C0DB13C0010000000000000000000044ED799B4D83000A0F07E80304AF044F6F";
                // working
                // var jumpx4 =
                // 	"3F002C010082EB07B278800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49E9FD93408ACF007F70391E0004F6F00";
                // var runSpeed =
                // 	"3F002C0100720A2EC278800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49ECFE13408A8F00704046C28004F6F00";
                StreamPeer.PutData(Convert.FromHexString(jumpx4));
                // StreamPeer.PutData(Convert.FromHexString(runSpeed));
                StreamPeer.PutData(Convert.FromHexString(test));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private int CharacterScreenCreateDeleteSelect ()
    {
        var len = rcvBuffer[0] - 20 - 5;
        var charDataBytesStart = rcvBuffer[0] - 5;
        var nameCheckBytes = rcvBuffer[20..];
        var charDataBytes = rcvBuffer[charDataBytesStart..(charDataBytesStart + rcvBuffer[0])];
        var sb = new StringBuilder();
        var firstLetterCharCode = ((nameCheckBytes[1] & 0b11111) << 3) + (nameCheckBytes[0] >> 5);
        var firstLetterShouldBeRussian = false;

        for (var i = 1; i < len; i++)
        {
            var currentCharCode = ((nameCheckBytes[i] & 0b11111) << 3) + (nameCheckBytes[i - 1] >> 5);

            if (currentCharCode % 2 == 0)
            {
                // English
                var currentLetter = (char) (currentCharCode / 2);
                sb.Append(currentLetter);
            }
            else
            {
                // Russian
                var currentLetter = currentCharCode >= 193
                    ? (char) ((currentCharCode - 192) / 2 + 'а')
                    : (char) ((currentCharCode - 129) / 2 + 'А');
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
                ? (char) ((firstLetterCharCode - 192) / 2 + 'а')
                : (char) ((firstLetterCharCode - 129) / 2 + 'А');
            name = firstLetter + sb.ToString()[1..];
        }
        else
        {
            name = sb.ToString();
        }

        var isNameValid = true; // Login.IsNameValid(name);
        Console.WriteLine(isNameValid ? $"SRV: Name [{name}] OK" : $"SRV: Name [{name}] already exists!");

        if (!isNameValid)
        {
            StreamPeer.PutData(CommonPackets.NameAlreadyExists(LocalId));
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

            var charIndex = rcvBuffer[17] / 4 - 1;

            var newCharacterData =
                Character.CreateNewCharacter(LocalId, name, isGenderFemale, faceType, hairStyle, hairColor, tattoo);

            DbConnectionProvider.CharacterCollection.Insert(newCharacterData);
            player!.Characters.Insert(charIndex, newCharacterData);
            DbConnectionProvider.PlayerCollection.Update(player!);

            StreamPeer.PutData(CommonPackets.NameCheckPassed(player!.Index));

            return charIndex;
        }

        return -1;
    }

    private async Task MoveToNewPlayerDungeonAsync (Character selectedCharacter)
    {
        var newDungeonCoords = new WorldCoords(-1098, -4501.62158203125, 1900);
        var playerCoords = new WorldCoords(-1098.69506835937500, -4501.61474609375000, 1900.05493164062500,
            1.57079637050629);
        // var playerCoords = new WorldCoords(0, 150, 0);
        // StreamPeer.PutData(CurrentCharacter.GetTeleportByteArray(playerCoords));
        // StreamPeer.PutData(selectedCharacter.GetNewPlayerDungeonTeleportAndUpdateStatsByteArray(playerCoords));
        // here some stats are updated because satiety gets applied. We'll figure that out later, for now just flat

        // currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_INITIATED;
        // await ToSignal(GetTree().CreateTimer(0.5f), "timeout");

        // StreamPeer.PutData(CommonPackets.LoadNewPlayerDungeon);
        // ConsoleHelper.WriteLine(
        // $"SRV: Teleported client [{MinorByte(selectedCharacter.ClientIndex) * 256 + MajorByte(selectedCharacter.ClientIndex)}] to default new player dungeon");

        currentState = ClientState.INGAME_DEFAULT;
        var clientTransform = clientModel!.Transform;
        clientTransform.Origin.X = (float) playerCoords.x;
        clientTransform.Origin.Y = (float) playerCoords.y;
        clientTransform.Origin.Z = (float) playerCoords.z;
        clientModel.Transform = clientTransform;

        CurrentCharacter.MaxHP = 110;
        CurrentCharacter.MaxSatiety = 100;
        CurrentCharacter.Money = 10;
        CurrentCharacter.UpdateCurrentStats();
    }

    public void CloseConnection ()
    {
        Console.WriteLine($"SRV {playerIndexStr}: Closing connection");
        QueueFree();
    }

    public void SendPingResponse ()
    {
        var clientPingBytesForComparison = rcvBuffer[17..55];

        var clientPingBytesForPong = rcvBuffer[9..30];
        var clientPingBinaryStr =
            StringConvertHelpers.ByteArrayToBinaryString(clientPingBytesForComparison, false, true);

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
                if (Math.Abs(coords.x - CurrentCharacter.X) < 100000)
                {
                    CurrentCharacter.X = coords.x;
                    CurrentCharacter.Y = coords.y;
                    CurrentCharacter.Z = coords.z;
                    CurrentCharacter.Angle = coords.turn;
                }
                // Console.WriteLine(coords.ToDebugString());

                pingPreviousClientPingString = clientPingBinaryStr;

                // File.WriteAllText(@"c:\_sphereDumps\localPing", $"{coords.x} {coords.y} {coords.z}");
            }
        }

        var xored = clientPingBytesForPong[5];

        if (pingShouldXorTopBit)
        {
            xored ^= 0b10000000;
        }

        if (counter == 0)
        {
            var first = (ushort) ((clientPingBytesForPong[7] << 8) + clientPingBytesForPong[6]);
            first -= 0xE001;
            counter = (ushort) (0xE001 + first / 12);
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

    private void PickupItemToTargetSlot ()
    {
        var clientItemID_1 = rcvBuffer[21] >> 1;
        var clientItemID_2 = rcvBuffer[22];
        var clientItemID_3 = rcvBuffer[23] % 2;
        var clientItemID = (clientItemID_3 << 15) + (clientItemID_2 << 7) + clientItemID_1;

        var globalItemId = GetGlobalObjectId((ushort) clientItemID);
        var item = DbConnectionProvider.ItemCollection.FindById(globalItemId);

        var clientSlot_raw = rcvBuffer[24];
        var targetSlotId = clientSlot_raw >> 1;
        var targetSlot = Enum.IsDefined(typeof (BelongingSlot), targetSlotId)
            ? (BelongingSlot) targetSlotId
            : BelongingSlot.Unknown;

        if (targetSlot is BelongingSlot.Unknown || !item.IsValidForSlot(targetSlot) ||
            !CurrentCharacter.CanUseItem(item))
        {
            Console.WriteLine(
                $"Item {item.Localisation[Locale.Russian]} [{globalItemId}] couldn't be used in slot [{Enum.GetName(targetSlot)}]");
            return;
        }

        var clientSlot = (clientSlot_raw - 0x32) / 2;
        Console.WriteLine(
            $"CLI: Move item {item.Localisation[Locale.Russian]} ({item.ItemCount}) [{clientItemID}] to slot raw [{clientSlot_raw}] " +
            $"[{Enum.GetName(typeof (BelongingSlot), clientSlot_raw >> 1)}] actual [{clientSlot}]");

        var clientSync_1 = rcvBuffer[17];
        var clientSync_2 = rcvBuffer[18];

        var clientSyncOther_1 = (rcvBuffer[10] & 0b11000000) >> 4;
        var clientSyncOther_2 = rcvBuffer[11];
        var clientSyncOther_3 = rcvBuffer[12] & 0b111111;
        var clientSyncOther = (ushort) ((clientSyncOther_3 << 10) + (clientSyncOther_2 << 2) + clientSyncOther_1);

        var serverItemID_1 = (clientItemID & 0b111111) << 2;
        var serverItemID_2 = (clientItemID & 0b11111111000000) >> 6;
        var serverItemID_3 = (clientItemID & 0b1100000000000000) >> 14;

        var moveResult = new byte[]
        {
            0x2E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte((ushort) clientItemID),
            MajorByte((ushort) clientItemID),
            0xE8, 0xC7, 0xA0, 0xB0, 0x6E, 0xA6, 0x88, 0x98, 0x95, 0xB1, 0x28, 0x09, 0xDC, 0x85, 0xC8, 0xDF, 0x02, 0x0C,
            MinorByte(clientSyncOther), MajorByte(clientSyncOther), 0x01, 0xFC, clientSync_1, clientSync_2, 0x10, 0x80,
            0x82, 0x20, (byte) (clientSlot_raw << 1), (byte) serverItemID_1, (byte) serverItemID_2,
            (byte) serverItemID_3,
            0x20, 0x4E, 0x00, 0x00, 0x00
        };

        CurrentCharacter.Items[targetSlot] = globalItemId;
        Console.WriteLine($"{Enum.GetName((BelongingSlot) targetSlotId)} now has {item.Localisation[Locale.Russian]} " +
                          $"({item.ItemCount}) [{globalItemId}]");
        StreamPeer.PutData(moveResult);
        var oldContainer = item.ParentContainerId is null
            ? null
            : DbConnectionProvider.ItemContainerCollection.FindById(item.ParentContainerId);
        // TODO: check in next process in node instead of this
        if (oldContainer?.RemoveItemByIdAndDestroyContainerIfEmpty(globalItemId) ?? false)
        {
            RemoveEntity(GetLocalObjectId(LocalId, oldContainer.Id));
        }

        if (!Item.IsInventorySlot(targetSlot))
        {
            if (CurrentCharacter.UpdateCurrentStats())
            {
                PacketHelper.UpdateStatsForClient(CurrentCharacter);
            }
        }
    }

    private void DropItemToGround ()
    {
        // TODO: support later
        // likely contains client coords to drop at and response should have server coords

        Console.WriteLine("Drop item to ground, TBD");
    }

    private void PickupItemToInventory ()
    {
        // TODO: remove kaitai
        // var packet = new PickupItemRequest(kaitaiStream);
        // var clientItemID = (ushort) packet.ItemId;
        // Console.WriteLine($"Pickup request: {clientItemID}");
        //
        // var itemId = GetGlobalObjectId(clientItemID);
        //
        // var item = DbConnectionProvider.ItemCollection.FindById(itemId);
        // var parentId = item?.ParentContainerId;
        // if (parentId is null)
        // {
        //     Console.WriteLine($"Item local [{clientItemID}] global [{itemId}] not found or has no parent container");
        //     return;
        // }
        //
        // var container = DbConnectionProvider.ItemContainerCollection.FindById(parentId);
        //
        // if (container is null)
        // {
        //     Console.WriteLine($"Container [{parentId}] not found");
        //     return;
        // }
        //
        // var slotId = container.Contents.First(x => x.Value == itemId).Key << 1;
        // var packetObjectType = (int) item.ObjectType.GetPacketObjectType();
        // var type_1 = (byte) ((packetObjectType & 0b111111) << 2);
        // var type_2 = (byte) (0b11000000 + (packetObjectType >> 6));
        //
        // var targetSlot = CurrentCharacter.FindEmptyInventorySlot();
        //
        // var targetSlot_1 = (byte) (((int) targetSlot & 0b111111) << 2);
        // var targetSlot_2 = (byte) ((((int) targetSlot >> 6) & 0b11) + ((clientItemID & 0b111111) << 2));
        // var itemId_1 = (byte) ((clientItemID >> 6) & 0b11111111);
        // var itemId_2 = (byte) ((clientItemID >> 14) & 0b11);
        //
        // var clientId_1 = (byte) ((ByteSwap(LocalId) & 0b1111111) << 1);
        // var clientId_2 = (byte) ((ByteSwap(LocalId) >> 7) & 0b11111111);
        // var clientId_3 = (byte) (((ByteSwap(LocalId) >> 15) & 0b1) + 0b00010000);
        //
        // var bagId_1 = (byte) ((ByteSwap(LocalId) & 0b111111) << 2);
        // var bagId_2 = (byte) ((ByteSwap(LocalId) >> 6) & 0b11111111);
        // var bagId_3 = (byte) ((ByteSwap(LocalId) >> 14) & 0b11);
        //
        // var pickupResult = new byte[]
        // {
        //     0x36, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte((ushort) parentId), MajorByte((ushort) parentId), 0x5c,
        //     0x46, 0x41, 0x02, (byte) slotId, 0x7e, MinorByte(clientItemID), MajorByte(clientItemID), type_1, type_2,
        //     0x00, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x02, 0x0C, bagId_1,
        //     bagId_2,
        //     bagId_3, 0xFC, clientId_1, clientId_2, clientId_3, 0x80, 0x82, 0x20, targetSlot_1, targetSlot_2, itemId_1,
        //     itemId_2, 0xC8, 0x00,
        //     0x00, 0x00, 0x00
        // };
        //
        // CurrentCharacter.Items[targetSlot.Value] = itemId;
        //
        // if (container.RemoveItemBySlotIdAndDestroyContainerIfEmpty(slotId >> 1))
        // {
        //     RemoveEntity(GetLocalObjectId(LocalId, container.Id));
        // }
        //
        // StreamPeer.PutData(pickupResult);
    }

    private void MoveItemToAnotherSlot ()
    {
        // ideally we'd support swapping items but client simply doesn't send anything if slot is occupied
        // var clientID_1 = rcvBuffer[11];
        // var clientID_2 = rcvBuffer[12];
        var newSlotRaw = rcvBuffer[21];
        var oldSlotRaw = rcvBuffer[22];
        var oldSlotId = rcvBuffer[22] >> 1;
        var newSlotId = rcvBuffer[21] >> 1;
        Console.WriteLine(
            $"Move to another slot request: from [{Enum.GetName(typeof (BelongingSlot), oldSlotId)}] " +
            $"to [{Enum.GetName(typeof (BelongingSlot), newSlotId)}]");
        var targetSlot = Enum.IsDefined(typeof (BelongingSlot), newSlotId)
            ? (BelongingSlot) newSlotId
            : BelongingSlot.Unknown;
        var oldSlot = Enum.IsDefined(typeof (BelongingSlot), oldSlotId)
            ? (BelongingSlot) oldSlotId
            : BelongingSlot.Unknown;

        var returnToOldSlot = false;

        if (targetSlot is BelongingSlot.Unknown || oldSlot is BelongingSlot.Unknown ||
            !CurrentCharacter.Items.ContainsKey(oldSlot))
        {
            Console.WriteLine($"Item not found in slot [{Enum.GetName(oldSlot)}]");
            returnToOldSlot = true;
        }

        var globalOldItemId = CurrentCharacter.Items[oldSlot];

        var item = DbConnectionProvider.ItemCollection.FindById(globalOldItemId);

        if (!item.IsValidForSlot(targetSlot) || !CurrentCharacter.CanUseItem(item))
        {
            Console.WriteLine($"Item [{globalOldItemId}] couldn't be used in slot [{Enum.GetName(targetSlot)}]");
            returnToOldSlot = true;
        }

        if (returnToOldSlot)
        {
            newSlotRaw = oldSlotRaw;
        }

        Console.WriteLine($"Item found: {globalOldItemId}");
        var oldItemLocalId = GetLocalObjectId(globalOldItemId);
        var newSlot_1 = (byte) ((newSlotRaw & 0b11111) << 3);
        var newSlot_2 = (byte) (((oldItemLocalId & 0b1111) << 4) + (newSlotRaw >> 5));
        var oldItem_1 = (byte) ((oldItemLocalId >> 4) & 0b11111111);
        var oldItem_2 = (byte) (oldItemLocalId >> 12);

        var moveResult = new byte[]
        {
            0x20, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(LocalId), MinorByte(LocalId), 0x08, 0x40, 0x41, 0x10,
            oldSlotRaw, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x82, newSlot_1, newSlot_2, oldItem_1,
            oldItem_2, 0xC0, 0x44, 0x00, 0x00, 0x00
        };
        if (!returnToOldSlot)
        {
            CurrentCharacter.Items[targetSlot] = globalOldItemId;
            CurrentCharacter.Items.Remove(oldSlot);

            if (CurrentCharacter.UpdateCurrentStats())
            {
                PacketHelper.UpdateStatsForClient(CurrentCharacter);
            }
            // TODO: character state shouldn't be stored in starting dungeon
            // DbConnectionProvider.CharacterCollection.Update(CurrentCharacter.Id, CurrentCharacter);
        }

        StreamPeer.PutData(moveResult);
    }

    public ushort GetLocalObjectId (int globalId)
    {
        // TODO: at some point we'll run out of 65536 globalIds and will have to keep client-specific object lists
        return (ushort) globalId;
    }

    public static ushort GetLocalObjectId (int clientId, int globalId)
    {
        // TODO: at some point we'll run out of 65536 globalIds and will have to keep client-specific object lists
        return (ushort) globalId;
    }

    public int GetGlobalObjectId (ushort localId)
    {
        return localId;
    }

    public static int GetGlobalObjectId (ushort clientId, ushort localId)
    {
        return localId;
    }

    private void RemoveEntity (ushort id)
    {
        StreamPeer.PutData(CommonPackets.DespawnEntity(id));
    }

    private void MainhandTakeItem ()
    {
        // TODO: remove kaitai
        // dynamic packet = rcvBuffer[0] switch
        // {
        //     0x15 => new MainhandEquipPowder(kaitaiStream),
        //     0x19 => new MainhandEquipSword(kaitaiStream),
        //     0x1b => new MainhandReequipPowderPowder(kaitaiStream),
        //     0x1f => new MainhandReequipPowderSword(kaitaiStream),
        //     0x23 => new MainhandReequipSwordSword(kaitaiStream)
        // };
        //
        // var slotState = (MainhandSlotState) packet.MainhandState;
        // var localItemId = (ushort) packet.EquipItemId;
        //
        // Console.WriteLine($"Mainhand: {Enum.GetName(slotState)} [{localItemId}]");
        //
        // if (slotState == MainhandSlotState.Empty)
        // {
        //     var currentItemId = CurrentCharacter.Items[BelongingSlot.MainHand];
        //     var currentItem = DbConnectionProvider.ItemCollection.FindById(currentItemId);
        //     CurrentCharacter.PAtk -= currentItem.PAtkNegative;
        //     CurrentCharacter.MAtk -= currentItem.MAtkNegativeOrHeal;
        //     CurrentCharacter.Items.Remove(BelongingSlot.MainHand);
        // }
        // else if (slotState == MainhandSlotState.Fists)
        // {
        //     CurrentCharacter.Items[BelongingSlot.MainHand] = CurrentCharacter.Fists.Id;
        // }
        // else
        // {
        //     var itemId = GetGlobalObjectId(localItemId);
        //     var item = DbConnectionProvider.ItemCollection.FindById(itemId);
        //     var currentItem = CurrentCharacter.Items.ContainsKey(BelongingSlot.MainHand)
        //         ? DbConnectionProvider.ItemCollection.FindById(CurrentCharacter.Items[BelongingSlot.MainHand])
        //         : null;
        //     if (currentItem is not null)
        //     {
        //         CurrentCharacter.PAtk -= currentItem.PAtkNegative;
        //         CurrentCharacter.MAtk -= currentItem.MAtkNegativeOrHeal;
        //     }
        //
        //     CurrentCharacter.PAtk += item.PAtkNegative;
        //     CurrentCharacter.MAtk += item.MAtkNegativeOrHeal;
        //     CurrentCharacter.Items[BelongingSlot.MainHand] = itemId;
        //     // UpdateStatsForClient();
        // }
    }

    private void BuyItemFromTarget ()
    {
        // TODO: remove kaitai
        // var packet = new BuyItemRequest(kaitaiStream);
        // var slotId = (byte) packet.SlotId;
        // var clientId = packet.Header.ClientId;
        //
        // var vendorGlobalId = GetGlobalObjectId((ushort) packet.VendorId);
        // var vendor = DbConnectionProvider.VendorCollection.FindById(vendorGlobalId);
        //
        // if (vendor is null)
        // {
        //     Console.WriteLine($"Unknown vendor [{vendorGlobalId}]");
        //     return;
        // }
        //
        // var item = vendor.ItemsOnSale.Count > slotId ? vendor.ItemsOnSale[slotId] : null;
        //
        // if (item is null)
        // {
        //     Console.WriteLine($"Vendor [{vendorGlobalId}] has nothing in slot [{slotId}]");
        //     return;
        // }
        //
        // var localization = item.ObjectType is GameObjectType.FoodApple ? "Apple" : item.Localisation[Locale.Russian];
        //
        // Console.WriteLine($"Buy request: [{clientId}] slot [{slotId}] {localization} " +
        //                   $"({packet.Quantity}) {packet.CostPerOne}t ea " +
        //                   $"from {vendor.Name} {vendor.FamilyName} {vendorGlobalId}");
        //
        // var clientSlotId = CurrentCharacter.FindEmptyInventorySlot();
        //
        // if (clientSlotId is null)
        // {
        //     Console.WriteLine("No empty slots!");
        //     return;
        // }
        //
        // var totalCost = (int) (packet.Quantity * packet.CostPerOne);
        // if (CurrentCharacter.Money < totalCost)
        // {
        //     Console.WriteLine("Not enough money!");
        //     return;
        // }
        //
        // var clone = Item.Clone(item);
        // clone.ItemCount = (int) packet.Quantity;
        // clone.ParentContainerId = LocalId;
        //
        // var characterUpdateStream = new BitStream(new MemoryStream())
        // {
        //     AutoIncreaseStream = true
        // };
        //
        // CurrentCharacter.Money -= totalCost;
        // CurrentCharacter.Items[clientSlotId.Value] = clone.Id;
        //
        // characterUpdateStream.WriteBytes(
        //     new byte[]
        //     {
        //         0x2B, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(LocalId), MinorByte(LocalId), 0x08, 0x40, 0x41, 0x10
        //     }, 13, true);
        // characterUpdateStream.WriteBit(0);
        // characterUpdateStream.WriteByte((byte) clientSlotId);
        // characterUpdateStream.WriteUInt16(GetLocalObjectId(clone.Id));
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteUInt16((ushort) packet.Quantity);
        // characterUpdateStream.WriteByte(0, 7);
        // characterUpdateStream.WriteBytes(new byte[] { 0x0, 0x1A, 0x38, 0x04 }, 4, true);
        // characterUpdateStream.WriteInt32(CurrentCharacter.Money);
        // characterUpdateStream.WriteByte(0x0D);
        // characterUpdateStream.WriteByte(0x04);
        // characterUpdateStream.WriteByte(0b110, 7);
        // characterUpdateStream.WriteUInt16(GetLocalObjectId(clone.Id));
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteByte(0, 1);
        // characterUpdateStream.WriteUInt16((ushort) packet.Quantity);
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteByte(0, 7);
        // characterUpdateStream.WriteUInt16((ushort) packet.Quantity);
        // characterUpdateStream.WriteByte(0, 1);
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteByte(0x32);
        //
        // var characterUpdateResult = characterUpdateStream.GetStreamData();
        // StreamPeer.PutData(characterUpdateResult);
        //
        // var buyResult = Packet.ItemsToPacket(LocalId, clientId, new List<Item> { clone });
        // // buyResult[^1] = 0;
        // StreamPeer.PutData(buyResult);
    }

    private void UseItem ()
    {
        var itemId = (ushort) (rcvBuffer[11] + rcvBuffer[12] * 0x100);
        Console.WriteLine($"Use item [{itemId}]");

        var teleportPacket =
            CurrentCharacter.GetTeleportByteArray(new WorldCoords(2290.30395507812500, 155, -2388.89477539062500));

        StreamPeer.PutData(teleportPacket);
    }

    private void DamageTarget ()
    {
        // TODO: actual damage with actual mob node
        var receiveStream = new BitStream(rcvBuffer);
        receiveStream.ReadBits(172);
        var targetId = receiveStream.ReadUInt16();
        var packet = PacketPart.LoadDefinedWithOverride("fist_attack_target");
        PacketPart.UpdateEntityId(packet, targetId);
        PacketPart.UpdateValue(packet, "attacker_id", CurrentCharacter.ClientIndex, 16);
        PacketPart.UpdateValue(packet, "30000_minus_damage", 30000);
        var packetBytes = PacketPart.GetBytesToWrite(packet);
        StreamPeer.PutData(packetBytes);
        return;

        var paAbs = Math.Abs(CurrentCharacter.PAtk);
        var currentItem = DbConnectionProvider.ItemCollection.FindById(CurrentCharacter.Items[BelongingSlot.MainHand]);
        var damagePa = currentItem.PAtkNegative == 0
            ? 0
            : SphereServer.Rng.Next((int) (paAbs * 0.65), (int)
                (paAbs * 1.4));
        var damageMa = CurrentCharacter.MAtk;
        var totalDamage = (ushort) (damageMa + damagePa);
        var destId = (ushort) GetDestinationIdFromDamagePacket(rcvBuffer);
        var playerIndexByteSwap = ByteSwap(LocalId);
        var selfDamage = destId == playerIndexByteSwap;
        var selfHeal = damageMa > 0;

        if (selfDamage)
        {
            var id_1 = (byte) (((ByteSwap(LocalId) & 0b111) << 5) + 0b00010);
            var id_2 = (byte) ((ByteSwap(LocalId) >> 3) & 0b11111111);
            var type = selfHeal ? 0b10000000 : 0b10100000;
            var id_3 = (byte) (((ByteSwap(LocalId) >> 11) & 0b11111) + type);

            if (selfHeal)
            {
                if (CurrentCharacter.CurrentHP + totalDamage > CurrentCharacter.MaxHP)
                {
                    totalDamage = (ushort) (CurrentCharacter.MaxHP - CurrentCharacter.CurrentHP);
                }

                CurrentCharacter.CurrentHP += totalDamage;
            }
            else
            {
                if (CurrentCharacter.CurrentHP < totalDamage)
                {
                    totalDamage = CurrentCharacter.CurrentHP;
                }

                CurrentCharacter.CurrentHP -= totalDamage;
            }

            var selfDamagePacket = new byte[]
            {
                0x11, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(LocalId), MinorByte(LocalId), 0x08, 0x40, id_1,
                id_2, id_3, MinorByte(totalDamage), MajorByte(totalDamage), 0x00
            };
            StreamPeer.PutData(selfDamagePacket);
        }
        else
        {
            newPlayerDungeonMobHp = Math.Max(0, newPlayerDungeonMobHp - totalDamage);

            if (newPlayerDungeonMobHp > 0)
            {
                var src_1 = (byte) ((playerIndexByteSwap & 0b1111111) << 1);
                var src_2 = (byte) ((playerIndexByteSwap & 0b111111110000000) >> 7);
                var src_3 = (byte) ((playerIndexByteSwap & 0b1000000000000000) >> 15);
                var dmg_1 = (byte) (0x60 - totalDamage * 2);
                var hp_1 = (byte) ((newPlayerDungeonMobHp & 0b1111) << 4);
                var hp_2 = (byte) ((newPlayerDungeonMobHp & 0b11110000) >> 4);
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
                var moneyReward = (byte) (10 + SphereServer.Rng.Next(0, 9));
                CurrentCharacter.Money += moneyReward;
                var totalMoney_1 = (byte) (((CurrentCharacter.Money & 0b11111) << 3) + 0b100);
                var totalMoney_2 = (byte) ((CurrentCharacter.Money & 0b11100000) >> 5);
                CurrentCharacter.KarmaCount += 1;
                var karma_1 = (byte) (((CurrentCharacter.KarmaCount & 0b1111111) << 1) + 1);
                var src_1 = (byte) ((playerIndexByteSwap & 0b1000000000000000) >> 15);
                var src_2 = (byte) ((playerIndexByteSwap & 0b111111110000000) >> 7);
                var src_3 = (byte) ((playerIndexByteSwap & 0b1111111) << 1);
                var src_4 = (byte) (((playerIndexByteSwap & 0b111) << 5) + 0b01111);
                var src_5 = (byte) ((playerIndexByteSwap & 0b11111111000) >> 3);
                var src_6 = (byte) ((playerIndexByteSwap & 0b1111100000000000) >> 11);

                var moneyReward_1 = (byte) (((moneyReward & 0b11) << 6) + 1);
                var moneyReward_2 = (byte) ((moneyReward & 0b1111111100) >> 2);

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
                var mob = DbConnectionProvider.MonsterCollection.FindById((int) destId);
                if (mob.ParentNodeId is not null)
                {
                    var parentNode = ActiveNodesRepository.Get(mob.ParentNodeId.Value) as MobNode;
                    if (parentNode is not null)
                    {
                        ItemContainer.Create(parentNode.GlobalTransform.Origin.X,
                            parentNode.GlobalTransform.Origin.Y,
                            parentNode.GlobalTransform.Origin.Z, 0, LootRatity.DEFAULT_MOB);
                        parentNode.SetInactive();
                    }
                }
            }
        }
    }

    // private void MoveEntity(WorldCoords coords, ushort entityId)
    // {
    // 	MoveEntity(coords.x, coords.y, coords.z, coords.turn, entityId);
    // }

    public void MoveEntity (double x0, double y0, double z0, double t0, ushort entityId)
    {
        // best guess for X and Z: decimal value in packet = 4095 - coord_value, where coord_value is in 0..63 range
        // for Y max value becomes 2047 with the same formula
        // technically, it's not even decimal, as it's possible to move by ~50 units if 0 is sent instead of 4095 
        var xDec = 4095 - (1 - (int) Math.Truncate(x0 - Math.Truncate(x0)) * 64);
        var yDec = 2047 - (int) Math.Truncate((y0 - Math.Truncate(y0)) * 64);
        var zDec = 4095 - (1 - (int) Math.Truncate(z0 - Math.Truncate(z0)) * 64);
        var x = 32768 + (int) x0;
        var y = 1200 + (int) y0;
        var z = 32768 + (int) z0;
        var x_1 = (byte) (((x & 0b1111111) << 1) + 1);
        var x_2 = (byte) ((x & 0b111111110000000) >> 7);
        var y_1 = (byte) (((y & 0b1111111) << 1) + ((x & 0b1000000000000000) >> 15));
        var z_1 = (byte) (((z & 0b11) << 6) + ((y & 0b1111110000000) >> 7));
        var z_2 = (byte) ((z & 0b1111111100) >> 2);
        var z_3 = (byte) ((z & 0b1111110000000000) >> 10);
        var id_1 = (byte) (((entityId & 0b111) << 5) + 0b10001);
        var id_2 = (byte) ((entityId & 0b11111111000) >> 3);
        var id_3 = (byte) ((entityId & 0b1111100000000000) >> 11);
        var xdec_1 = (byte) ((xDec & 0b111111) << 2);
        var ydec_1 = (byte) (((yDec & 0b11) << 6) + ((xDec & 0b111111000000) >> 6));
        var ydec_2 = (byte) ((yDec & 0b1111111100) >> 2);
        var zdec_1 = (byte) (((zDec & 0b111111) << 2) + ((yDec & 0b110000000000) >> 10));
        while (Math.Abs(t0) > 2 * Mathf.Pi)
        {
            t0 -= Math.Sign(t0) * 2 * Mathf.Pi;
        }

        var turn = (int) (t0 * 256 / 2 / Mathf.Pi);

        var turn_1 = (byte) (((turn & 0b11) << 6) + ((zDec & 0b111111000000) >> 6));
        var turn_2 = (byte) ((turn & 0b11111100) >> 2);
        var movePacket = new byte[]
        {
            0x17, 0x00, 0x2c, 0x01, 0x00, x_1, x_2, y_1, z_1, z_2, z_3, 0x2D, id_1, id_2, id_3, 0x6A, 0x10, xdec_1,
            ydec_1, ydec_2, zdec_1, turn_1, turn_2
        };

        StreamPeer.PutData(movePacket);
    }

    public void ChangeHealth (ushort entityId, int healthDiff)
    {
        var currentPlayerId = ByteSwap(LocalId);
        var playerId_1 = (byte) (((currentPlayerId & 0b1111) << 4) + 0b0111);
        var playerId_2 = (byte) ((currentPlayerId & 0b111111110000) >> 4);
        var mobId_1 = (byte) ((entityId & 0b1111111) << 1);
        var mobId_2 = (byte) ((entityId & 0b111111110000000) >> 7);
        var hpMod = healthDiff < 0 ? 0b1110 : 0b1100;
        healthDiff = Math.Abs(healthDiff);
        var dmg_1 = (byte) (((healthDiff & 0b1111) << 4) + hpMod + ((entityId & 0b1000000000000000) >> 15));
        var dmg_2 = (byte) ((healthDiff & 0b111111110000) >> 4);
        var dmg_3 = (byte) ((healthDiff & 0b11111111000000000000) >> 12);
        var playerId_3 = (byte) (0b10000000 + ((currentPlayerId & 0b1111000000000000) >> 12));
        var dmgPacket = new byte[]
        {
            0x1F, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(entityId), MajorByte(entityId), 0x48, 0x43, 0x65, 0x00,
            0x00, 0x00, 0x80, 0xA0, 0x03, 0x00, 0x00, 0xE0, playerId_1, playerId_2, playerId_3, 0x00, 0x24, mobId_1,
            mobId_2, dmg_1, dmg_2, dmg_3
        };
        var resultHp = CurrentCharacter.CurrentHP - healthDiff;
        if (resultHp > CurrentCharacter.MaxHP)
        {
            resultHp = CurrentCharacter.MaxHP;
        }

        if (resultHp < 0)
        {
            resultHp = 0;
        }

        CurrentCharacter.CurrentHP = (ushort) resultHp;
        StreamPeer.PutData(dmgPacket);
    }

    public static void TryFindClientByIdAndSendData (ushort clientId, byte[] data)
    {
        var client = ActiveClientsRepository.Get(clientId);
        if (client is null)
        {
            Console.WriteLine($"Trying to send packet to inexistent client [{clientId}]");
        }

        client?.StreamPeer.PutData(data);
    }

    public float DistanceTo (Vector3 end)
    {
        return clientModel?.GlobalTransform.Origin.DistanceTo(end) ?? float.MaxValue;
    }

    private static int GetDestinationIdFromDamagePacket (byte[] rcvBuffer)
    {
        var destBytes = rcvBuffer[28..];

        return ((destBytes[2] & 0b11111) << 11) + (destBytes[1] << 3) + ((destBytes[0] & 0b11100000) >> 5);
    }

    public bool IsReadyForGameLogic => currentState == ClientState.INGAME_DEFAULT;

    public static ushort GetNewEntityIndex ()
    {
        return NewEntityIndex++;
    }

    // private static int GetDestinationIdFromFistDamagePacket(byte[] rcvBuffer)
    // {
    // 	var destBytes = rcvBuffer.Range(21, rcvBuffer.Length);
    //
    // 	return ((destBytes[2] & 0b11111) << 11) + ((destBytes[1]) << 3) + ((destBytes[0] & 0b11100000) >> 5);
    // }
}