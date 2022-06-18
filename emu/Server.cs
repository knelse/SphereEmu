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

                Console.WriteLine("CLI: Enter game");
                await ns.WriteAsync(selectedCharacter!.ToGameDataByteArray());
                Interlocked.Increment(ref playerCount);

                MoveToNewPlayerDungeon(ns, selectedCharacter);

                while (await ns.ReadAsync(rcvBuffer) != 0x13)
                {
                }

                // only in dungeon?
                while (await ns.ReadAsync(rcvBuffer) == 0 || rcvBuffer[0] != 0x12)
                {
                }

                WorldDataTest.SendNewCharacterWorldData(ns, playerIndexStr);

                // while (await ns.ReadAsync(rcvBuffer) == 0 || rcvBuffer[0] != 0x69)
                // {
                // }

                CreateSixSecondPingThread(currentPlayerIndex, ns);

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
                            break;
                        // move item
                        case 0x1A:
                            await PickupItemToInventory(ns, rcvBuffer);
                            break;
                        //echo
                        case 0x08:
                            await ns.WriteAsync(CommonPackets.Echo(currentPlayerIndex));
                            break;
                        //damage
                        case 0x20:
                            var damage = (byte)(10 + RNGHelper.GetUniform() * 8);
                            var damageStr = Convert.ToString(damage, 16).PadLeft(2, '0');
                            //X0 YZ 1T => X2 YZ 7T
                            var source = rcvBuffer[25..28];
                            var dest = rcvBuffer[28..31];
                            var selfDamage = source[0] == dest[0] && source[1] == dest[1] && source[2] == dest[2];

                            if (selfDamage)
                            {
                                source[0] += 2;
                                source[2] += 0x60;
                                await ns.WriteAsync(Convert.FromHexString(
                                    $"10002c01000004{playerIndexStr}0840{Convert.ToHexString(source)}{damageStr}00"));
                            }

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
                        Console.WriteLine(coords.ToDebugString());

                        if (!LiveServerCoords)
                        {
                            var coordsFile = File.Open(PingCoordsFilePath, FileMode.Create);
                            coordsFile.Write(Encoding.ASCII.GetBytes(
                                coords.x + "\n" + coords.y + "\n" + coords.z + "\n" + coords.turn));
                            coordsFile.Close();
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
                    Thread.Sleep(3500);
                    // var str = Console.ReadLine();
                    //
                    // if (string.IsNullOrEmpty(str) || !str.Equals("def"))
                    // {
                    //     continue;
                    // }
                    // move to dungeon
                    await ns.WriteAsync(
                        selectedCharacter.GetTeleportAndUpdateCharacterByteArray(701, 4501.62158203125, 900, 1.55));
                    Thread.Sleep(100);

                    // get into instance
                    // commented: no inkpot, no npc29 id 33129, no tokenst id 33130, no ct_lab 33120 33150 33114, no telep1 33116, no tutomsg 33146 33147 33148 33151 33154 33156 33124
                    var enterNewGame_6 =
                    "BF002C010050E1EA0B1080AF801F5620E2E00320A14B02000000000000000050649101A00003BC8300850900F8750502063E008017220050C6220080302200441619000A2F809DF5030CE4337CBCF8264436A88C45E0F76944C1882C3200145E4020A0F00CBCF8264436A88C45E0F76944856FD0ED2CFE558F4806568F480606F87D05420D3EC2DE16229756C622824830A2E550140014430E0CB20385170000F88905A21F3ECEBD1322B053C6229284302280441619890A5900F0FFFFFF0F" +
                    "C1002C010050E1698124838F8EF984A89495B18827228D680C140500C5D0AF6600C04F2D903FF00166BC10A9A23216112AAF110124B2C80050C80280FFFFFFFFC2132C1C0004FCB60201161F00095D12000000000000000080228B0C000518E01D04286401C0FFFFFFFF0F2C90CFF00100BC1001803216010084110120B2C8005078810081C233646DCF15EB822612257CD01517BE01000000000000000000000000A0F00C19D976C5DBB489440D7E72C5856F0000000000000000000000000000" +
                    "C8002C010050E160817C46E11908C0E68AF5411389FE18E28A0BDF00000000000000000000000000F0F30BE4337C00002F4400A08C450000614400882C3200145E4040A0F00C737F75C514A18944208271C5856F00000000000000000000000000F86905F2193E008017220050C6220080302200441619000A2F30205078069906BAE283DA44A22BC1B9E2C23700000000000000000000000000FCB802F9011F8DC60B11DE2A6311F63D1B1100228B0C003FBD4090C0C779F24294BFC958C4B70F460480C8220300" +
                    "C9002C010050E17A812041E10502007E7B8120818FC80B85C8F292B1081C208C080091450680C20B0800FCF80241021F4FF10811AE2863113D8C181100228B0C0085171800F8FD0582043EC2591322154FC622DC51342200441619000A2F4000F0130C04097C30A02A4446968C45A485684400882C3200145EA000E047180812F890355E889841198BE89ED888001059640028BCC001C08F2C1024F0112DBC10B16132168104A5110120B2C8005078010380DF5920AEE10320A14B0200000000000000005064910100" +
                    "1B002C010050E16781B84601067807010A131050C80280FFFFFF7F" +
                    "2D002C01007B058B2CE1202AFC2B106A1060FAF435FEF24F2C10FD10546DFED71FC04F2D903F10587FFED90900";
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

        private static async Task PickupItemToInventory(NetworkStream ns, byte[] rcvBuffer)
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
    }
}