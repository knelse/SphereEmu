using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using emu.DataModels;
using emu.Db;
using emu.Helpers;
using emu.Packets;
using static emu.Helpers.MiscHelper;

#pragma warning disable CS4014
#pragma warning disable CA1416

namespace emu
{
    internal class Server
    {
        private const int BUFSIZE = 1024;
        private static int playerIndex = 0x4f6f;
        private static int playerCount;
        public static bool LiveServerCoords = false;
        public static Encoding Win1251 = null!;
        private static DateTime startTime = DateTime.Now;
        private static bool sendEntPing = true;

        private static ushort getNewPlayerIndex()
        {
            if (playerIndex > 65535)
            {
                throw new ArgumentException("Reached max number of connections");
            }

            // return (ushort) Interlocked.Increment(ref playerIndex);
            return (ushort)playerIndex;
        }

        public static async Task Main()
        {
            const int port = 25860;
            TcpListener? tcpListener = null;
            Console.WindowWidth = 256;
            RNGHelper.SetSeedFromSystemTime();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Win1251 = Encoding.GetEncoding(1251);

            try
            {
                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Start();
            }
            catch (SocketException se)
            {
                Console.WriteLine(se.Message);
                Environment.Exit(se.ErrorCode);
            }

            Console.WriteLine("Server up, waiting for connections...");

            while (true)
            {
                try
                {
                    var client = await tcpListener.AcceptTcpClientAsync();
                    var currentPlayerIndex = getNewPlayerIndex();
                    Task.Run(() => HandleClientAsync(client, currentPlayerIndex));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client, ushort currentPlayerIndex, bool reconnect = false)
        {
            NetworkStream? ns = null;
            var fileCoords = Array.Empty<string>();
            var startCoords = WorldCoords.ShipstoneCenter;
            var clientData = new ClientData();

            if (File.Exists(ClientData.PingCoordsFilePath))
            {
                fileCoords = await File.ReadAllLinesAsync(ClientData.PingCoordsFilePath);
            }

            if (fileCoords.Length > 0)
            {
                try
                {
                    var x = double.Parse(fileCoords[0]);
                    var y = double.Parse(fileCoords[1]);
                    var z = double.Parse(fileCoords[2]);
                    var turn = double.Parse(fileCoords[3]);
                    startCoords = new WorldCoords(x, y, z, turn);
                }
                catch
                {
                    SetColorAndWriteLine(ConsoleColor.Red, "Cannot load file coords");
                }
            }

            try
            {
                await Task.Yield();
                ns = client.GetStream();

                var playerIndexStr = Convert.ToHexString(new[]
                {
                    BitHelper.GetSecondByte(currentPlayerIndex),
                    BitHelper.GetFirstByte(currentPlayerIndex)
                });

                SetColorAndWriteLine(ConsoleColor.Yellow, "Handling client " + playerIndexStr);

                Console.WriteLine("SRV: Ready to load initial data");
                await ns.WriteAsync(reconnect
                    ? CommonPackets.ReadyToLoadInitialDataReconnect
                    : CommonPackets.ReadyToLoadInitialData);

                var rcvBuffer = new byte[BUFSIZE];

                while (await ns.ReadAsync(rcvBuffer) == 0)
                {
                }

                Console.WriteLine("CLI: Connection initialized");
                await ns.WriteAsync(CommonPackets.ServerCredentials(currentPlayerIndex));
                Console.WriteLine("SRV: Credentials sent");

                while (await ns.ReadAsync(rcvBuffer) <= 12)
                {
                }

                Console.WriteLine("CLI: Login data sent");
                TestHelper.DumpLoginData(rcvBuffer);
                (var login, var password) = LoginHelper.GetLoginAndPassword(rcvBuffer);

                var charListData =
                    await Login.CheckLoginAndGetPlayerCharactersAsync(login, password, currentPlayerIndex);

                if (charListData == null)
                {
                    // TODO: actual incorrect pwd packet
                    await ns.WriteAsync(CommonPackets.AccountAlreadyInUse(currentPlayerIndex));
                    CloseConnection(client, ns);

                    return;
                }

                await ns.WriteAsync(CommonPackets.CharacterSelectStartData(currentPlayerIndex));
                Console.WriteLine("SRV: Character select screen data - initial");
                Thread.Sleep(50);

                await ns.WriteAsync(charListData.ToByteArray(currentPlayerIndex));
                Console.WriteLine("SRV: Character select screen data - player characters");
                Thread.Sleep(50);

                CreateFifteenSecondPingThread(currentPlayerIndex, ns);

                CreateTransmissionEndPacketPingThread(ns);

                var selectedCharacterIndex = await CharacterScreenCreateDeleteSelectAsync(client, currentPlayerIndex, ns, rcvBuffer, charListData);

                if (selectedCharacterIndex == -1)
                {
                    selectedCharacterIndex = rcvBuffer[17] / 4 - 1;
                }

                var selectedCharacter = charListData[selectedCharacterIndex];

                if (LiveServerCoords)
                {
                    selectedCharacter!.X = startCoords.x;
                    selectedCharacter!.Y = startCoords.y;
                    selectedCharacter!.Z = startCoords.z;
                    selectedCharacter!.T = startCoords.turn;
                }

                clientData.CurrentCharacter = selectedCharacter!;

                Console.WriteLine("CLI: Enter game");
                await ns.WriteAsync(selectedCharacter!.ToGameDataByteArray());
                Interlocked.Increment(ref playerCount);

                while (await ns.ReadAsync(rcvBuffer) != 0x13)
                {
                }

                // only in dungeon?
                // while (await ns.ReadAsync(rcvBuffer) == 0 || rcvBuffer[0] != 0x12)
                // {
                // }

                WorldDataTest.SendNewCharacterWorldData(ns, playerIndexStr);

                MoveToNewPlayerDungeon(ns, selectedCharacter);

                // while (await ns.ReadAsync(rcvBuffer) == 0 || rcvBuffer[0] != 0x69)
                // {
                // }

                CreateSixSecondPingThread(currentPlayerIndex, ns);

                // for mob kill / move
                Task.Run (async () =>
                {
                    while (ns.CanWrite)
                    {
                        await ns.WriteAsync(TestHelper.GetTestMobData());
                        Thread.Sleep(1000);
                    }
                });

                // Task.Run(async () =>
                // {
                //     while (true)
                //     {
                //         Console.ReadLine();
                //         await ns.WriteAsync(BitHelper.BinaryStringToByteArray(File.ReadAllText("C:\\source\\mobMovePacket.txt").RemoveLineEndings()));
                //         Console.WriteLine("Mob move?");
                //     }
                // });

                var newPlayerDungeonMobHp = 64;

                Thread.Sleep(50);

                while (ns.CanRead)
                {
                    var length = await ns.ReadAsync(rcvBuffer);

                    if (length == 0)
                    {
                        continue;
                    }

                    switch (rcvBuffer[0])
                    {
                        // ping
                        case 0x26:
                            await clientData.SendPingResponse(rcvBuffer, ns);
                            await MoveEntityAsync(ns, clientData.CurrentCharacter.X, clientData.CurrentCharacter.Y,clientData.CurrentCharacter.Z, 0, 0xB08);
                            break;
                        // move item
                        case 0x1A:
                            await PickupItemToInventoryAsync(ns, rcvBuffer);
                            break;
                        //echo
                        case 0x08:
                            await ns.WriteAsync(CommonPackets.Echo(currentPlayerIndex));
                            break;
                        //damage
                        // case 0x19:
                        case 0x20:
                            var damage = (byte)(10 + RNGHelper.GetUniform() * 8);
                            var destId = (ushort) GetDestinationIdFromDamagePacket(rcvBuffer);
                            var playerIndexByteSwap = ((currentPlayerIndex & 0b11111111) << 8) +
                                                      ((currentPlayerIndex & 0b1111111100000000) >> 8);
                            var selfDamage = destId == playerIndexByteSwap;

                            if (selfDamage)
                            {
                                var selfDamagePacket = new byte[] {0x10, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04,  
                                    BitHelper.GetSecondByte(currentPlayerIndex), BitHelper.GetFirstByte(currentPlayerIndex), 
                                    0x08, 0x40, (byte) (rcvBuffer[25] + 2), rcvBuffer[26], (byte) (rcvBuffer[27] + 0x60), damage, 0x00};

                                await ns.WriteAsync(selfDamagePacket);
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
                                        0x1B, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, BitHelper.GetFirstByte(destId), 
                                        BitHelper.GetSecondByte(destId), 0x48, 0x43, 0xA1, 0x0B, src_1, src_2, src_3, 
                                        dmg_1, 0xEA, 0x0A, 0x6D, hp_1, hp_2, 0x00, 0x04, 0x50, 0x07, 0x00
                                    };
                                    await ns.WriteAsync(damagePacket);
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
                                        0x04, BitHelper.GetFirstByte(destId),
                                        BitHelper.GetSecondByte(destId), 0x48, 0x43, 0xA1, 0x09, src_3, src_2, src_1,
                                        0x00, 0x7e, BitHelper.GetFirstByte((ushort)playerIndexByteSwap),
                                        BitHelper.GetSecondByte((ushort)playerIndexByteSwap), 0x08, 0x40, 0x41, 0x0A,
                                        0x34, 0x3A, 0x93, 0x00, 0x00, 0x7E, 0x14, 0xCE, 0x14, 0x47, 0x81, 0x05, 0x3A, 
                                        0x93, 0x7E, BitHelper.GetFirstByte(destId), BitHelper.GetSecondByte(destId), 
                                        0x00, 0xC0, src_4, src_5, src_6, 0x01, 0x58, 0xE4, totalMoney_1, totalMoney_2, 
                                        0x16, 0x28, karma_1, 0x80, 0x46, 0x40, moneyReward_1, moneyReward_2
                                    };
                                    await ns.WriteAsync(Packet.ToByteArray(deathPacket));
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

                            // await ns.WriteAsync(TestHelper.GetEntityData(
                            //     new WorldCoords(669.1638793945312, 4501.63134765625, 931.0355224609375, -1), 4816,
                            //     7654, 4816));

                            var i = 0;
                            
                            // while (ns.CanWrite)
                            // {
                            //     var vendorList =
                            //         $"27002C010000044f6f0840A362202D10E097164832142600400108E0DF08000000004000000000";
                            //     await ns.WriteAsync(Convert.FromHexString(vendorList));
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
                            //         await ns.WriteAsync(TestHelper.GetEntityData(
                            //             new WorldCoords(671.1638793945312 + x, 4501.63134765625, 932.0355224609375 + y,
                            //                 -1), 971, 7654 + i, entTypeId));
                            //
                            //         i++;
                            //     }
                            //
                            //     Thread.Sleep(1350);
                            // }

                            // var vendorListLoaded = $"30002C01000004FE8D14870F80842E0900000000000000004091456696101560202D10A0900500FFFFFFFF0516401F00";
                            // await ns.WriteAsync(Convert.FromHexString(vendorListLoaded));
                            var vendorListLoaded = BitHelper.BinaryStringToByteArray(File.ReadAllText("C:\\source\\vendorList.txt").RemoveLineEndings());
                            await ns.WriteAsync(vendorListLoaded);
                            break;
                        default:
                            continue;
                    }
                }

                CloseConnection(client, ns);
            }

            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                CloseConnection(client, ns);
            }
        }
        
        internal class ClientData
        {
            private ushort pingCounter;
            private bool pingShouldXorTopBit;
            public static readonly string PingCoordsFilePath = LiveServerCoords 
                ? "C:\\_sphereDumps\\currentWorldCoords" 
                : "C:\\source\\clientCoordsSaved";
            private string? pingPreviousClientPingString;
            public CharacterData CurrentCharacter;
        
            public async Task SendPingResponse(byte[] rcvBuffer, NetworkStream ns)
            {
                var clientPingBytesForComparison = rcvBuffer[17..38];

                var clientPingBytesForPong = rcvBuffer[9..21];
                var clientPingBinaryStr =
                    BitHelper.ByteArrayToBinaryString(clientPingBytesForComparison, false, true);

                if (clientPingBinaryStr[0] == '0')
                {
                    // random different packet, idk
                    return;
                }

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

                        if (!LiveServerCoords)
                        {
                            try
                            {
                                var coordsFile = File.Open(PingCoordsFilePath, FileMode.Create);
                                coordsFile.Write(Encoding.ASCII.GetBytes(
                                    coords.x + "\n" + coords.y + "\n" + coords.z + "\n" + coords.turn));
                                coordsFile.Close();
                            }
                            catch
                            {
                                
                            }
                        }

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
                    0x00, 0x00, 0x00, 0x00, 0x00, topByteToXor, BitHelper.GetFirstByte(pingCounter),
                    BitHelper.GetSecondByte(pingCounter), 0x00, 0x00, 0x00, 0x00, 0x00
                };
                
                Array.Copy(clientPingBytesForPong, pong, 5);
                Array.Copy(clientPingBytesForPong, 8, pong, 8, 4);
                await ns.WriteAsync(Packet.ToByteArray(pong, 1));
                pingShouldXorTopBit = !pingShouldXorTopBit;
                pingCounter++;

                //overflow
                if (pingCounter < 0xE001)
                {
                    pingCounter = 0xE001;
                }
            }
        }

        private static void MoveToNewPlayerDungeon(NetworkStream ns, CharacterData selectedCharacter)
        {
            Task.Run(async () =>
            {
                // while (ns.CanRead)
                // {
                    Thread.Sleep(3000);
                    // var str = Console.ReadLine();
                    //
                    // if (string.IsNullOrEmpty(str) || !str.Equals("def"))
                    // {
                    //     continue;
                    // }
                    // move to dungeon
                    // var newDungeonCoords = new WorldCoords(701, 4501.62158203125, 900, 1.55);
                    var newDungeonCoords = new WorldCoords(-1098, 4501.62158203125, 1900, 1.55);
                    await ns.WriteAsync(
                        // selectedCharacter.GetTeleportAndUpdateCharacterByteArray(new WorldCoords(669.5963745117188, 4501.63134765625, 931.5966796875, -1)));
                        selectedCharacter.GetTeleportAndUpdateCharacterByteArray(new WorldCoords(-1098.69506835937500, 4501.61474609375000, 1900.05493164062500, 1.57079637050629), Convert.ToString(selectedCharacter.PlayerIndex, 16).PadLeft(4, '0')));
                    Thread.Sleep(100);

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
                    // await ns.WriteAsync(Convert.FromHexString(enterGameResponse_5));

                    // while (await ns.ReadAsync(rcvBuffer) != 0x13)
                    // {
                    // }

                    // 
                    // tp.AddRange(enterNewGame_4);

                    await ns.WriteAsync(Convert.FromHexString(enterNewGame_6));
                    // await ns.WriteAsync(tp.ToArray());
                    // await ns.WriteAsync(Convert.FromHexString(
                    //     "BC002C0100EE45B0A67C46E11B000000000000000000000000007E6FA67C860F00A8A6180094B10800B09D080091450680C20B0808149E213AB8AE181B3491084130AEB8F00D000000000000000000000000003F71547EC0379753538CB7CA58C4BE374F0480C82203C0AF251524F065D8D414E76F3216F9F6B5130120B2C80050788100805F4E2A48E07B43B729B6BC642C82036C270240649101A0F0020200BF725490C017EB80538C2BCA58A487E14E0480C8220340E105060000"));
                    Console.WriteLine($"SRV: Teleported client [{BitHelper.GetFirstByte(selectedCharacter.PlayerIndex) * 256 + BitHelper.GetSecondByte(selectedCharacter.PlayerIndex)}] to default new player dungeon");

                    await ns.WriteAsync(TestHelper.GetNewPlayerDungeonMobData(newDungeonCoords));
                    // }
            });
        }

        private static async Task<int> CharacterScreenCreateDeleteSelectAsync(TcpClient client, ushort currentPlayerIndex,
            NetworkStream ns, byte[] rcvBuffer, ClientInitialData charListData)
        {
            while (await ns.ReadAsync(rcvBuffer) != 0x15)
            {
                if (rcvBuffer[0] == 0x2a)
                {
                    var charIndex = rcvBuffer[17] / 4 - 1;

                    Console.WriteLine($"Delete character [{charIndex}] - [{charListData[charIndex]!.Name}]");
                    await DbCharacters.DeleteCharacterFromDbAsync(charListData[charIndex]!.DbId);

                    // TODO: reinit session after delete
                    // await HandleClientAsync(client, (ushort) (currentPlayerIndex + 1), true);

                    CloseConnection(client, ns);

                    return -1;
                }

                if (rcvBuffer[0] < 0x1b)
                {
                    continue;
                }

                var len = rcvBuffer[0] - 20 - 5;
                var charDataBytesStart = rcvBuffer[0] - 5;
                var nameCheckBytes = rcvBuffer[20..];
                var charDataBytes = rcvBuffer[charDataBytesStart..rcvBuffer[0]];
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

                var isNameValid = await Login.IsNameValidAsync(name);
                Console.WriteLine(isNameValid ? $"SRV: Name [{name}] OK" : $"SRV: Name [{name}] already exists!");

                if (!isNameValid)
                {
                    await (ns.WriteAsync(CommonPackets.NameAlreadyExists(currentPlayerIndex)));
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

                    charListData.AddNewCharacter(newCharacterData, charIndex);
                    await DbCharacters.AddNewCharacterToDbAsync(charListData.PlayerId, newCharacterData,
                        charIndex);

                    await (ns.WriteAsync(CommonPackets.NameCheckPassed(currentPlayerIndex)));

                    return charIndex;
                }
            }

            return -1;
        }

        private static void CreateTransmissionEndPacketPingThread(NetworkStream ns)
        {
            Thread.Sleep(100);
            Task.Run(async () =>
            {
                while (ns.CanWrite)
                {
                    await ns.WriteAsync(CommonPackets.TransmissionEndPacket);
                    Thread.Sleep(3000);
                }
            });
        }

        private static void CreateFifteenSecondPingThread(ushort currentPlayerIndex, NetworkStream ns)
        {
            Thread.Sleep(200);
            Task.Run(async () =>
            {
                while (ns.CanWrite)
                {
                    await ns.WriteAsync(CommonPackets.FifteenSecondPing(currentPlayerIndex));
                    Thread.Sleep(15000);
                }
            });
        }

        private static void CreateSixSecondPingThread(ushort currentPlayerIndex, NetworkStream ns)
        {
            Thread.Sleep(300);
            Task.Run(async () =>
            {
                while (ns.CanWrite)
                {
                    await ns.WriteAsync(CommonPackets.SixSecondPing(currentPlayerIndex));
                    Thread.Sleep(6000);
                }
            });
        }

        private static void CloseConnection(TcpClient client, NetworkStream? ns)
        {
            SetColorAndWriteLine(ConsoleColor.Yellow, "Client disconnect...");

            Interlocked.Decrement(ref playerCount);
            ns?.Close();
            client.Close();
        }

        private static async Task PickupItemToInventoryAsync(NetworkStream ns, byte[] rcvBuffer)
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
            var clientSyncOther = (ushort) ((clientSyncOther_3 << 10) + (clientSyncOther_2 << 2) + clientSyncOther_1);

            var serverItemID_1 = (clientItemID & 0b111111) << 2;
            var serverItemID_2 = (clientItemID & 0b11111111000000) >> 6;
            var serverItemID_3 = (clientItemID & 0b1100000000000000) >> 14;

            var moveResult = new byte[]
            {
                0x2E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, BitHelper.GetFirstByte((ushort) clientItemID), 
                BitHelper.GetSecondByte((ushort) clientItemID), 0xE8, 0xC7, 0xA0, 0xB0, 0x6E, 0xA6, 0x88, 0x98, 0x95, 
                0xB1, 0x28, 0x09, 0xDC, 0x85, 0xC8, 0xDF, 0x02, 0x0C, BitHelper.GetFirstByte(clientSyncOther), 
                BitHelper.GetSecondByte(clientSyncOther), 0x01, 0xFC, clientSync_1, clientSync_2, 0x10, 0x80, 
                0x82, 0x20, (byte) (clientSlot_raw * 2), (byte) serverItemID_1, (byte) serverItemID_2, (byte) serverItemID_3, 0x20, 0x4E, 0x00, 
                0x00, 0x00
            };

            sendEntPing = false;

            await ns.WriteAsync(moveResult);
        }

        private static async Task MoveEntityAsync(NetworkStream ns, WorldCoords coords, ushort entityId)
        {
            await MoveEntityAsync(ns, coords.x, coords.y, coords.z, coords.turn, entityId);
        }
        private static async Task MoveEntityAsync(NetworkStream ns, double x0, double y0, double z0, double t0, ushort entityId)
        {
            // TODO: figure out how decimal part is sent, for now we'll use only the integer part
            // var xDec = (int) Math.Truncate((x0 - Math.Truncate(x0)) * 255) + 3584;
            // var yDec = (int)Math.Truncate((y0 - Math.Truncate(y0)) * 255) + 1792;
            // var zDec = (int) Math.Truncate((z0 - Math.Truncate(z0)) * 255) + 3840;
            var x = 32768 + (int)x0;
            var y = 1200 + (int)y0;
            var z = 32768 + (int)z0;
            var x_1 = (byte) (((x & 0b1111111) << 1) + 1);
            var x_2 = (byte) ((x & 0b111111110000000) >> 7);
            var y_1 = (byte) (((y & 0b1111111) << 1) + ((x & 0b1000000000000000) >> 15));
            var z_1 = (byte) (((z & 0b11) << 6) + ((y & 0b1111110000000) >> 7));
            var z_2 = (byte) ((z & 0b1111111100) >> 2);
            var z_3 = (byte) ((z & 0b1111110000000000) >> 10);
            var id_1 = (byte) (((entityId & 0b111) << 5) + 0b10001);
            var id_2 = (byte) ((entityId & 0b11111111000) >> 3);
            var id_3 = (byte) ((entityId & 0b1111100000000000) >> 11);
            // var xdec_1 = (byte) ((xDec & 0b111111) << 2);
            // var ydec_1 = (byte) (((yDec & 0b11) << 6) + ((xDec & 0b111111000000) >> 6));
            // var ydec_2 = (byte) ((yDec & 0b1111111100) >> 2);
            // var zdec_1 = (byte) (((zDec & 0b111111) << 2) + ((yDec & 0b110000000000) >> 10));
            // full turn is roughly 6.28126716613769, 1 degree is 0.0174479643503825
            var turn = ((int)(t0 * 20.29845)) % 128 + (t0 > 0 ? 0 : 128);
            // var turn_1 = (byte) (((turn & 0b11) << 6) + ((zDec & 0b111111000000) >> 6));
            var turn_1 = (byte) (((turn & 0b11) << 6) + 0b111110);
            var turn_2 = (byte)((turn & 0b11111100) >> 2);
            var movePacket = new byte[]
            {
                0x17, 0x00, 0x2c, 0x01, 0x00, x_1, x_2, y_1, z_1, z_2, z_3, 0x2d, id_1,
                id_2, id_3, 0x6A, 0x10, 0x84, 0x3b, 0xf5, 0xc9, turn_1, turn_2
            }; //, xdec_1, ydec_1, ydec_2, zdec_1, turn_1, turn_2};

            await ns.WriteAsync(movePacket);
        }

        private static int GetDestinationIdFromDamagePacket(byte[] rcvBuffer)
        {
            var destBytes = rcvBuffer[28..];

            return ((destBytes[2] & 0b11111) << 11) + ((destBytes[1]) << 3) + ((destBytes[0] & 0b11100000) >> 5);
        }
        
        private static int GetDestinationIdFromFistDamagePacket(byte[] rcvBuffer)
        {
            var destBytes = rcvBuffer[21..];

            return ((destBytes[2] & 0b11111) << 11) + ((destBytes[1]) << 3) + ((destBytes[0] & 0b11100000) >> 5);
        }
    }
}