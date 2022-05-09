using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using emu.DataModels;
using emu.Helpers;
#pragma warning disable CS4014

namespace emu
{
    internal class Server
    {
        private const int BUFSIZE = 1024;
        private static int playerIndex = 0x044f;
        private static readonly byte[] transmissionEndPacket = Packet.ToByteArray();
        private static int playerCount;

        private static ushort getNewPlayerIndex()
        {
            if (playerIndex > 65535)
            {
                throw new ArgumentException("Reached max number of connections");
            }

            // return (ushort) Interlocked.Increment(ref playerIndex);
            return (ushort) playerIndex;
        }
        
        public static async Task Main ()
        {
            const int port = 25860;
            TcpListener? tcpListener = null;
            Console.WindowWidth = 256;

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

        private static async Task HandleClientAsync(TcpClient client, ushort currentPlayerIndex)
        {
            NetworkStream? ns = null;
            var coordsFilePath = "C:\\source\\clientCoordsSaved";
            var fileCoords = Array.Empty<string>();
            var startCoords = WorldCoords.UmradCenter;

            if (File.Exists(coordsFilePath))
            {
                fileCoords = await File.ReadAllLinesAsync(coordsFilePath);
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
                    Console.WriteLine("Cannot load file coords");
                }
            }

            try
            {
                await Task.Yield();
                ns = client.GetStream();

                var clientData = TestHelper.GetTestCharData();

                var playerIndexStr = Convert.ToHexString(new[]
                {
                    BitHelper.GetSecondByte(currentPlayerIndex),
                    BitHelper.GetFirstByte(currentPlayerIndex)
                });
                var enterGameResponse_1 =
                    //Convert.FromHexString("4a012c010018{playerIndexStr}6f0800c2e0284d2e6c0e006e1a981819fb953b4560e61f43cb73af4455d93941370d7900f0000004000400040004000400040004000400040004000400040004000400040004000400000000000400000004000400040000000400040004000400040004000400040004000400000000000000000000000000000000000000000000000000000000000400040004000000000000000000040000000400f000000024000032005203d407405800e803803e00f401800400f4018004000c0680f7002800805700280800bf00000000000090010000005cf1530b00b400000000002781d4089801c00600c00f40a9006809001301600080450000000000000000000000000000000000000000000000000000000000000000000000002800800200280000000000000000000000003200284401d01233fca14a531652809054b5170500");
                    TestHelper.GetEnterGameData_1(startCoords, currentPlayerIndex);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Handling client " + playerIndexStr);
                Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine("SRV: Ready to load initial data");
                await ns.WriteAsync(CommonPackets.ReadyToLoadInitialData);

                var rcvBuffer = new byte[BUFSIZE];

                while (await ns.ReadAsync(rcvBuffer) == 0)
                {
                }

                Console.WriteLine("CLI: ack");
                await ns.WriteAsync(CommonPackets.ServerCredentials(currentPlayerIndex));
                Console.WriteLine("SRV: Credentials");

                while (await ns.ReadAsync(rcvBuffer) <= 12)
                {
                }

                Console.WriteLine("CLI: Login data");
                TestHelper.DumpLoginData(rcvBuffer);

                await ns.WriteAsync(CommonPackets.CharacterSelectStartData(currentPlayerIndex));
                Console.WriteLine("SRV: Initial data 1");
                Thread.Sleep(50);
                await ns.WriteAsync(clientData.ToByteArray(currentPlayerIndex));
                Console.WriteLine("SRV: Initial data 2");
                Thread.Sleep(50);

                var ingameServerPing = $"10002c0100ac{playerIndexStr}6f08408193eee408";
                
                Task.Run(async () =>
                {
                    while (ns.CanWrite)
                    {
                        await ns.WriteAsync(Convert.FromHexString(ingameServerPing));
                        Thread.Sleep(15000);
                    }
                
                    Console.WriteLine("1000 ping exit");
                });

                Task.Run(async () =>
                {
                    while (ns.CanWrite)
                    {
                        await ns.WriteAsync(transmissionEndPacket);
                        Thread.Sleep(3000);
                    }
                
                    Console.WriteLine("Transmission end ping exit");
                });

                while (await ns.ReadAsync(rcvBuffer) != 0x15)
                {
                }

                Console.WriteLine("CLI: Enter game");
                await ns.WriteAsync(enterGameResponse_1);
                // await ns.WriteAsync(Convert.FromHexString(
                    // "4f012c0100ac044f6f0800c2e0284d2e6c0ea0a6d2ced2d8601a981819cc7353c540b2a6c31d2363c4c618a0bfe71b7900f0040004000400040004000400040004000400040004000400040004000400040004000400000004000400000004000400040000000400040004000400040004000400040004000400040004000400000000000000000000000000000000000000000000000400040004000000000000000400040000000400f0d0070024000032005203d407405800e803803e00f401800400f4018004000c0680f7002800805700280800bf000000400b009001000000e4e1530b00b400000000e42681d4089001800600bc0f40a9006009001301600040450004004000000400000000000000000000000000000000000000000000000000000000002800800200280000000000000000000c00003200284401d0127374a24a531652809014b4170500"));
                Interlocked.Increment(ref playerCount);

                var enterGame2Binary = (await File.ReadAllTextAsync("C:\\source\\enterGame2.txt")).RemoveLineEndings();

                // Giras, Anhelm near portal
                // var enterGameResponse_3 =
                //     $"bf002c0100ac044f6f08406102000a82000000000000000050108400d9620048000080822008fcdb0000320000140461605a1b00a90100a02008044d5e00f5010000054128e0ab03080b000028088201dd3b803e00004041100e98bc00f40100000a8280b04204d007000050108484b52c0009000080822028503601f401000014046161c90b40020000a020080c32710083010000054168b8d303f01e0000280882c3d821800200004041101e9a9200bc0200000a820081770aa020000000";
                var enterGameResponse_4 =
                    $"bf002c0100ac044f6f08406102000a820000000000000000501084007234004800008082200800ad030032000014046120621e00a90100a0200804e70a00f5010000054128e8ae07080b000028088201a83f803e00004041100ee2e701f40100000a828050550fd0070000501084844f7a000900008082202860f403f4010000140461a1061e40020000a020080c56e40083010000054168587c07f01e0000280882437127800200004041101edeee01bc0200000a8200611407a020000000";

                var enterGameResponse_4_1 =
                    $"c6002c0100ac044f6f084041102284d701f80500000a8230010000000000005010040a047600320000808220584cb103e4e193fe1704e182d31fa0050000a020081892ed002d0000000541d0307807c84d02002808c2c6c13680d4080040411038a6e000c80000000a82d0b1f609a00100005010040f846f80f701008082207ce83b02940a0000140401a4de1d004b0000a0200821d4c8004c040000054110891302c00000002808c24855384045000040411048d0fb00020000000a825092c00e1000000000";
                var enterGameResponse_4_2 =
                    $"94002c0100ac044f6f084041104cf0aa01020000000a822053300fa000000050108419887900050000808220d014800128000000140401e7a81660000000a0200839000000000000000541d831d703005a62022808828e0c22804214004088ccf500000a823041fa06d0020000501004801f4100fa000080860d020000004000000040e3422100003a12e0284d2e6caeb1111000" +
                    $"72002c0100ac044f6f08406383acccccac6c8c6e8e8b0b0080c646a6a626e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc565aecc0ca007240624c88908640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f084063838c2dcc8d6c6e2c0cae8c8b0bc046a6a626e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f6f47c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f084063838c2dcc8d6c6e2c0caeec0b4d8e8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f0f40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f084063838c2dcc8d6c6e2c0caeec0b0e8d8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f0f40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f084063838c2dcc8d6c6e2c0caeec4b8e8c8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f0f40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f08406383ac4d6c8c8b0b200caeec4b8e8c8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a54d6c0c40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f08406383aced8dac8c6d8e8b0be04b8e8c8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e6e47c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f08406383aced8dac8c6dee0b4d8e8b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e0e40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f08406383aced8dac8c6dee0b0e8d8b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e0e40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f08406383aced8dac8c6dee4b8e8c8b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e0e40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f084063830c2e4c2eac6d8e8b0b808b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc565ccec6c47c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                    $"72002c0100ac044f6f084063830c2e4c2eac6d8e8bab2d4caf6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a54d8c0c40c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600";

                var enterGameResponse_4_3 = 
                                            "72002c0100ac044f6f084063830c2e4c2eac6d8e8b4beeedad6d8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a54d8c0c40c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                                            "72002c0100ac044f6f084063830c2e4c2eac6d8e8b6baeac8c6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc565ccec0c40c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                                            "c5002c0100ac044f6f084063830caf0e8e2c8cae8c8b0ba08c6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d6c0a0ead4caecca50caf6c670a0ead4cae6c882dadcc8dcea50caf0c80c70724068429a929890a2406008429a929890a2406a0b1212000b000383a2d0246500b814ed162a08bb420804521f2c2603bb038881f2c10c2028b846cc1422180b058e8082c182d17cd3c0b07593cc802a207b38868a12ca4c2308ba939c882021695900b0b5c5c00" +
                                            "5f002c0100ac044f6f08c002a34ab3c8a8092d3491179b520b8e48c1a203161e0d83c5470f61010abb08f55b88c062441624b228555b98a22d4e7481725f5aa446ee30012c54d37d040016ab7a0b56a645abcbc2051633b2a0b1450d2d6c0c";
// stats 5f002c0100ac044f6f08c002a34ab3c8a8092d3491179b520b8e48c1a203161e0d83c5470f61010abb08f55b88c062441624b228555b98a22d4e7481725f5aa446ee30012c54d37d040016ab7a0b56a645abcbc2051633b2a0b1450d2d6c0c
// hp mp c5002c0100ac044f6f084063830caf0e8e2c8cae8c8b0ba08c6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d6c0a0ead4caecca50caf6c670a0ead4cae6c882dadcc8dcea50caf0c80c70724068429a929890a2406008429a929890a2406a0b1212000b000383a2d0246500b814ed162a08bb420804521f2c2603bb038881f2c10c2028b846cc1422180b058e8082c182d17cd3c0b07593cc802a207b38868a12ca4c2308ba939c882021695900b0b5c5c00
                // await ns.WriteAsync(BitHelper.BinaryStringToByteArray(enterGame2Binary));
                Thread.Sleep(100);
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4));
                Thread.Sleep(100);
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4_1));
                Thread.Sleep(100);
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4_2));
                Thread.Sleep(100);
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4_3));
                Thread.Sleep(100);
                var enterGameResponse_5 =
                    $"2b002c0100ac{playerIndexStr}6f08400362206056ab814350b51705d07028d006090040768804816f27de915c7c803f";
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_5));
                Thread.Sleep(300);

                var somedata = $"1b002c0100ac{playerIndexStr}6f084043424009a00183000000000000000000";

                await ns.WriteAsync(Convert.FromHexString(somedata));
                Thread.Sleep(50);

                while (await ns.ReadAsync(rcvBuffer) == 0 || rcvBuffer[0] != 0x69)
                {
                }
                
                await WorldDataTest.SendWorldData(ns);

                Task.Run(async () =>
                {
                    // echo
                    Thread.Sleep(1000);
                    while (ns.CanWrite)
                    {
                        await ns.WriteAsync(Convert.FromHexString($"14002c0100ac{playerIndexStr}6f08c04260fed39010b01700"));
                        Thread.Sleep(3000);
                    }
                });
                //
                Task.Run(async () =>
                {
                    Thread.Sleep(2000);
                
                    while (ns.CanWrite)
                    {
                        await ns.WriteAsync(Convert.FromHexString($"13002c010000{playerIndexStr}6f08c042a0ffd39008b007"));
                        Thread.Sleep(6000);
                    }
                });

                // Task.Run(async () =>
                // {
                //     var time = TimeHelper.EncodeCurrentSphereDateTime();
                //     await ns.WriteAsync(Convert.FromHexString(
                //         // $"3e002c0100ee1dc9c478800f80842e0900000000000000004091450680020cc407031c0000000000000000000040d49e87d93408a6f007f60391e100f1c1"));
                //         // $"1f002c010000{playerIndexStr}6f084022800ca00681822090d002010400000014248004"));
                //         // $"3a002c010000{playerIndexStr}6f0840410a3870d406007e63c2dc47810550177ef1c10840223ef8a086018042095f00c042609ab040f94f2c52c3e8030000"));
                //         $"1f002c010000{playerIndexStr}6f0840223e981c0280822090d84a030400000014248004"));
                // });

                // Task.Run(async () =>
                // {
                //     var index = 0;
                //
                //     // var bitmap = new Bitmap("C:\\Download\\vd.bmp");
                //     // var colorMask = new bool [bitmap.Height, bitmap.Width];
                //     //
                //     // for (var i = 0; i < bitmap.Height; i++)
                //     // {
                //     //     for (var j = 0; j < bitmap.Width; j++)
                //     //     {
                //     //         colorMask[i, j] = bitmap.GetPixel(i, j).R > 75;
                //     //     }
                //     // }
                //
                //     while (index < 255)
                //     {
                //         // if (colorMask[index / 90, index % 90])
                //         // {
                //         //Console.ReadLine();
                //         // var entity = TestHelper.GetTestEntityData(index);
                //         // await ns.WriteAsync(entity);
                //         // }
                //         index++;
                //         // if (index % 10 == 0) {
                //         //     Thread.Sleep(1);
                //         // }
                //     }
                // });

                var oldClientPingStr = "";
                Thread.Sleep(300);

                var somedata_x = "20002c0100e0d411568843e325000046c32300000000349e400041e309081400";
                await ns.WriteAsync(Convert.FromHexString(somedata_x));
                Thread.Sleep(50);
                
                var shouldXorTopBit = false;
                var pingCounter = (ushort) 0;

                while (ns.CanRead)
                {
                    
                    while (await ns.ReadAsync(rcvBuffer) != 38)
                    {
                    }

                    var clientPingBytesForComparison = rcvBuffer[17..38];

                    var clientPingBytesForPong = rcvBuffer[9..21];
                    var clientPingBinaryStr = BitHelper.ByteArrayToBinaryString(clientPingBytesForComparison, false, true);

                    if (clientPingBinaryStr[0] == '0')
                    {
                        // random different packet, idk
                        continue;
                    }
                    
                    if (string.IsNullOrEmpty(oldClientPingStr))
                    {
                        Console.WriteLine(clientPingBinaryStr.ToArray());
                        oldClientPingStr = clientPingBinaryStr;
                    }
                    
                    else
                    {
                        var pingHasChanges = string.Compare(clientPingBinaryStr, oldClientPingStr, StringComparison.Ordinal);

                        if (pingHasChanges != 0)
                        {
                            for (var i = 0; i < clientPingBinaryStr.Length; i++)
                            {
                                if (clientPingBinaryStr[i] != oldClientPingStr[i])
                                {
                                    Console.ForegroundColor = ConsoleColor.Magenta;
                                }
                                Console.Write(clientPingBinaryStr[i]);
                                Console.ForegroundColor = ConsoleColor.White;
                            }

                            var x = CoordsHelper.DecodeClientCoordinate(rcvBuffer[21..26]);
                            var y = CoordsHelper.DecodeClientCoordinate(rcvBuffer[25..30]);
                            var z = CoordsHelper.DecodeClientCoordinate(rcvBuffer[29..34]);
                            var turn = CoordsHelper.DecodeClientCoordinate(rcvBuffer[33..38]);
                            Console.WriteLine();
                            Console.WriteLine("X: " + (int) x + " Y: " + (int) y +  " Z: " + (int) z + " Turn: " + (int) turn);
                            var coordsFile = File.Open(coordsFilePath, FileMode.Create);
                            coordsFile.Write(Encoding.ASCII.GetBytes(x + "\n" + y + "\n" + z + "\n" + turn));
                            coordsFile.Close();
                            oldClientPingStr = clientPingBinaryStr;
                        }
                    }
                    var topByteToXor = clientPingBytesForPong[5];

                    if (shouldXorTopBit)
                    {
                        topByteToXor ^= 0b10000000;
                    }

                    if (pingCounter == 0)
                    {
                        var first = (ushort) ((clientPingBytesForPong[7] << 8) + clientPingBytesForPong[6]);
                        first -= 0xE001;
                        pingCounter = (ushort) (0xE001 + first / 12);
                    }

                    var pong = new byte []
                    {
                        0x00, 0x00, 0x00, 0x00, 0x00, topByteToXor, BitHelper.GetFirstByte(pingCounter), 
                        BitHelper.GetSecondByte(pingCounter), 0x00, 0x00, 0x00, 0x00, 0x00
                    };
                    Array.Copy(clientPingBytesForPong, pong, 5);
                    Array.Copy(clientPingBytesForPong, 8, pong, 8, 4);
                    await ns.WriteAsync( Packet.ToByteArray(pong, 1));
                    shouldXorTopBit = !shouldXorTopBit;
                    pingCounter++;

                    //overflow
                    if (pingCounter < 0xE001)
                    {
                        pingCounter = 0xE001;
                    }
                }

                while (ns.CanRead)
                {
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Client disconnect...");
                Console.ForegroundColor = ConsoleColor.White;

                Interlocked.Decrement(ref playerCount);
                ns.Close();
                client.Close();
            }
            
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(e);
                Console.WriteLine("Client disconnect...");
                Console.ForegroundColor = ConsoleColor.White;

                Interlocked.Decrement(ref playerCount);
                ns?.Close();
            }
        }
    }
}