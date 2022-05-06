﻿using System.Drawing;
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
        private static int playerIndex = 0x044e;
        private static readonly byte[] transmissionEndPacket = Packet.ToByteArray();
        private static int playerCount;

        private static ushort getNewPlayerIndex()
        {
            if (playerIndex > 65535)
            {
                throw new ArgumentException("Reached max number of connections");
            }

            return (ushort) Interlocked.Increment(ref playerIndex);
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

                var ingameServerPing = $"10002c0100de{playerIndexStr}6f08408193eee408";

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

                Interlocked.Increment(ref playerCount);

                var enterGameResponse_3 =
                    $"bf002c0100ac{playerIndexStr}6f08406102000a821000fc0900090000501004813f21004006008082200cb0b70120350000140481e0d512a03e0000a020080502750061010000054130602105d00700002808c2c1d82a803e000040411010d4db00fa0000000a8290e06a0920010000501004053746803e00008082202c8c29014800000014048121d01460300000a020080dc6a600de030000054170703407500000002808c2035d248057000040411020e00a01140400000a821041c409c02f000000";
                var enterGameResponse_4 =
                    $"c6002c0100ac{playerIndexStr}6f0840411028f81201c80000000a826051980870c54ffa5f10840b1c258016000080822060c82801b400000014044123430900380900a020081b344a00522300000541e088410430030000280842070d22c00600004041103c5c1001e00700000a82f0015107502a000050100490164c002d010080822084dc20023011000014044144101100030000a0200823aa560016010000054190b12a03500000002808c20c2c268002000040411068184901140000000a82900300000000000000";

                var enterGameResponse_4_1 =
                    $"9b002c0100ac{playerIndexStr}6f0840411076524901809698000a82a003af0ea010050010a254520080860d020000004000000040632320a0b14156666656364637c7c50580426323535313f31593c2029204a5038263e31233e3224323e38233a30010730212e44404328616a6e232576606d003120312e4440432861626f74556c6f665c6167606c2e3031203c294d4944405120300c294d494440512030072002c0100ac{playerIndexStr}6f084063838c2dcc8d6c6e2c0cae8c8b0bc046a6a626e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f6f47c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f084063838c2dcc8d6c6e2c0caeec0b4d8e8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f0f40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f084063838c2dcc8d6c6e2c0caeec0b0e8d8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f0f40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f084063838c2dcc8d6c6e2c0caeec4b8e8c8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f0f40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f08406383ac4d6c8c8b0b200caeec4b8e8c8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a54d6c0c40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f08406383aced8dac8c6d8e8b0be04b8e8c8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e6e47c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f08406383aced8dac8c6dee0b4d8e8b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e0e40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f08406383aced8dac8c6dee0b0e8d8b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e0e40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f08406383aced8dac8c6dee4b8e8c8b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e0e40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f084063830c2e4c2eac6d8e8b0b808b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc565ccec6c47c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f084063830c2e4c2eac6d8e8bab2d4caf6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a54d8c0c40c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f084063830c2e4c2eac6d8e8b4beeedad6d8e8b0b20e62b26850524094a0704c7c62566c6458646c60567";
                var enterGameResponse_4_2 =
                    $"460120e60424c88908640c2d4cc5a54d8c0c40c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a24060072002c0100ac{playerIndexStr}6f084063830c2e4c2eac6d8e8b6baeac8c6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc565ccec0c40c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600c8002c0100ac{playerIndexStr}6f084063830caf0e8e2c8cae8c8b0ba08c6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d6c0a0ead4caecca50caf6c670a0ead4cae6c882dadcc8dcea50caf0c80c70724068429a929890a2406008429a929890a2406a0b1212000b00018fe2c02863f0b8187cf622050b420804521f2c2602fb038a81a2c1089170979828542f960b110105830ea2d9ab1160eb2789005440f631111045948c5441653978a05052c2a211716b8b880054695065b002c0100ac{playerIndexStr}6f08c022a326b4d0445e6c4a2d3822058b0e5878340c161f3d840528ec22d46f21028b115990c8a2546d618ab638d1054a7969917ad1c204b0501de6110058acea2d589916ad2e0b1758ccc082c61635b4b01112002c0100dd179d8a50a1a741ae11016000";

                var enterGameResponse_4_3 = $"16002c0100ac{playerIndexStr}6f08404110722c6c009001000000";

                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_3));
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4));
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4_1));
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4_2));
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4_3));
                Thread.Sleep(200);
                var enterGameResponse_5 =
                    $"2b002c0100ac{playerIndexStr}6f08400362206056ab814350b51705d07028d006090040768804816f27de915c7c803f";
                await ns.WriteAsync(Convert.FromHexString(enterGameResponse_5));
                Thread.Sleep(700);

                var somedata = $"1b002c0100ac{playerIndexStr}6f084043424009a00183000000000000000000";

                await ns.WriteAsync(Convert.FromHexString(somedata));
                Thread.Sleep(50);

                while (await ns.ReadAsync(rcvBuffer) == 0 || rcvBuffer[0] != 0x69)
                {
                }

                var somedata_1_binary =
                    @"110000110000000000101100000000010000000001000100000011010001110100001100000100001000000010001111100000011001111101110100000001100100011011100000001100111010100110011110001010010001101010011000111001100001100011001010001001001001000000100100000000100100000001100100100100
010000000111100000000001110110000010000010000100011111100011011100010001110111000110001010011011101100100100111001100001100001010001001110011110101000100100000000000100000101100101100100000000001111100000111001101001110110000000000100001111100111010011000010100110110010
001011000111111111111000100100100001100100011101110001001011001000100000000001000100000101100001100100000000011111100001110100111111000110001000000101001111110100101101001010100111100010000011000001001001011000111010100010100101010101111001001100001000000000001001000101
000101000001101000000000011111110001000001000001000110111000000110101100000110101110110010100111111010010101110111000100011000110100100011100110011000001001000000001001000000011001001001000100000001111000000100011110010000100001000001000111111000100100000100111010000101
100010100111110010010111001110011000011001001100000001100100000010001001000000000001000001011001011001000000000011111000111001011100000001100001000001001011111010001100111111111010000110100010000011110110011110001011001000010110000011010010010001110010001000000000010001
000001011000011001000000000111111010101101011100000001100010000001011011111101100010010010101001110010100011111010100011100110001101101000010001000100010110010010000010000000000010010001010001010000011000000000"
                        .RemoveLineEndings();

                var sd1 = BitHelper.BinaryStringToByteArray(somedata_1_binary);
                
                Thread.Sleep(500);

                var somedata_1 = 
                    "c3002c0100440d1d0c10808f819f740646e033a99e291a98e618ca2490240240649101e007608211f8dc47718a6ec93986144e7a890010596400f839a760043e74c29b22c7ff892191dc4b2200441619007e1d3f18814fd2d2a788304963a8a557930800914506801fc41046e06b06bb29fa577118d23998240240649101e047908411f8904e858a7c9739864c0640890010596400f8e5c06104be8cffa1a20f678b2160d2472200441619007ead7018816fd892a728fa8e6368444592080091450600c5002c0100440dea9818818f451fa888e69963684596940800914506801faa2946e0f3d4bd294267d4185af1a4250240649101e047a58a11f8bcea8b8ace291186acdf61890010596400f8dddd6204be9bcfa4a2b55286a10e2a5a2200441619007e2fbd1881afb4c6a8883f8b63082790940800914506801f263146e0fb61ca29ba79561812fd24250240649101e027808d11f854fa788a008539862e2474890010596400f8d145d2193e0012ba24000000000000008003451619000a3020d70850980000c5002c0100440d74917446411000f02201140000000a821000d60aa0000000501004814153000500008082200cf89b022800000014048140e01440010000a020080500a7000a000000054130284404500000002808c201d92a8002000040411010723801140000000a8290f0c80aa0000000501004054856000500008082202c289b022800000014048101000840010000a020080d668c000a000000054170502b0450000000286401c0ffffffff0fae10cdf00190d02501000000000000001c28b2c80000c5002c0100440d708568460106e41a010a130050100400b6428016000080822004082b02b4000000140441e0fd10a0050000a020080333a8002d000000054120803f0468010000280842411c27400b00004041100c7c3e01020000000a827060c80a10000000501004045e4580000000808220241c43020400000014044161221320000000a020080bb2a6000100000005416008e20408000000280842853e2a400b0000c08f8813cef00190d02501000000000000001c28b2c800508001b94680c2040000c6002c0100440d449c7046411000042a01900000000a821010bb09a0050000501004814656001500008082200c289a020c000000140481e0d61440050000a0200805b4a60009000000054130983505600000002808c241cd20c0030000404110106a4d01060000000a82a0606b0ac0000000501084059c55800a00008082203078a402300000001404a181161ae0100000a020080e0f95000f000000054178704f0588020000f8152602193e0012ba24000000000000000026451619000a3020d70850980000c5002c0100440d8589404641100036290188ffffff0b8210808b0ae005000050100481f35000ef01008082200c484e02ec010000140481a0630840140000a0200807979300c400000005414050510210040000280882428b12802f0000404110165e9400c00000000a5900f0ffffffffdba4a4327c0024744900000000000000004c8a2c32001460a03011a030010005410028bd04f8000000280842402d2a80040000404110046c5101580000000a82300015096001000050788531baafb9b0b1af391a00c8002c0100440db8a854860f80842e0900000000000000804991450680020c142602142600a0200800b9a8000b000000054108d0450588000000280882400e2040030000404110066e5101360000000aaf3046f7351736f6350703f0939ca4327c0024744900000000000000004c8a2c32001460a03011a0300100054100689c0450000000280842c0e32440020000404110042027015e0000000a8230d0dd077000000050788531baafb9b0b1afb91a805fc71095e10320a14b0200000000000000605264910100bf002c0100440d1d43544601060a13010a130050100400fb53002e0000808220041c4e0218000000140441a0eb19e0000000a020080396930007000000855718a3fb9a0b1bfb9a9b01f85d4e52193e";
                var somedata_2 =
                    "0012ba24000000000000000026451619000a3050980850980080822000a02b034c000000140421c0dd14800b0000a020080217a20021000000054118585102f000000028bcc218ddd75cd8d8d75c0cc04f4589caf00190d02501000000000000003029b2c800508081c24480c2040000ca002c0100440d2a4a5446411000509400240000000a821050a20490000000501004810e25001900008082200cb028010c000000145e618cee6b2e6cec6b8e06e0f7a24465f80048e8920000000000000000981459640028c0406122406102000a820010a204a0000000501004812854000900008082200c6c280148000000145e618cee6b2e6cec6b4e06e0974a4ac9f80048e8920000000000000000001459640028c0805c23406102000a5900f0ffffffff8357471c7c002474490000000000000000008a2c320000b9002c0100440df0ea88430106e41a010a130050100480a94d000f000080822004609501e8030000140441c0201220130500a02008031a7100540b0000054120985102a0a5000028084201d42600a901004041100c448600640000000a8270803204d0070000f003fee45d7c80d76d49001330c900483cc7000a2a329b20290a3020d70850c80280ffffff7f854b01c0ef4f087af151175e18d3cc852533664520ef2ba2c884618429c0805c2340210b00feffffff152e0400c6002c0100440dec6de48b0f08342e09704711198010e018409145e62016156040ae11a0900500ffffffffbf574be4c50784269704149d900c804d6a0ca080227334420b3020d70850c80280ffffff7f854b01c04fa08e79f1014bb625014d922403d0dc1c0328b2c88c8cd0020cc8350214b200e0ffffffffc742cabdf80082d692006562920100f48e01145964ce61510106e41a010a5900f0ffffff5ff08080dfd82af7e2031e4e4b028edb480690ca3c065064913987450518906b04286401c0ffffff3fc5002c0100440dea6de08b0f489e2e0970d11319002cbae85f4145661b16156040ae11a0900500ffffffff857750bbfb224b0b6b934b733b9b9901f8b95a822f3e0012ba240000000000000000004516993d90120518906b04286401c0ffffff7fe11dd4eebec8d2c2dae4d2dcce6072007e6e8ce08b0fb0372d09e01425190018b508404145661b16156040ae11a0900500ffffffff857750bbfb224b0b6b934b733b9b9901f88d29812f3e607fba240083e3630030f562004516993d90120518906b0400ba002c0100440d634ae04b210b00feffffff0befa076f7459616d62697e676069303f00b34c55e7c801c6f4900e747c9001892c70d8a2732f922b40003728d00852c00f8ffffffff199b022f3ea00db524a084b66400bc7fe3764511997a0c5a8001b94680421600fcffffff2b5c08007e8ee6409f0f80842e090000000000000000409145260f16156040ae11a0900500ffffffff3fbc487ec507404297040000000000000000a0c82233fa880a3040170950c80280ffffff7fc7002c0100440d60adfc8a0f80842e090000000000000000409145e6f6111560802e12a0900500ffffffffbf41537ec507404297040000000000000000a0c822b3fb880a3040170950c80280ffffffffc21b000470026804147e40c606a6f0060101a70005013f7f537ec507404297040000000000000000a0c82293fb880a3040170950c80280ffffffffc21b0004fc01480114fe40461d1e5cbe1cbef00601017f00550085371010b001180428bcc1c00009403b40e10d0808c400de010a6f50507012b00700c9002c0100440dfea6fc4ae10d0c0c40024c020a6f70704008900b507803040465005e80c21b24247003d80314de40212112001ae02770caaff80048e8920000000000000000001459644a1f510106e822010a5900f0ffffffff0338e5577c002474490000000000000000008a2c32a78fa80003749100852c00f8ffffffff1522f22b3e0012ba24000000000000000000451699d447548001ba4880421600fcffffffffc856f9151f00095d120000000000000000";
                var somedata_3 =
                    "80228bccea232ac0005d2440210b00feffffff01bf002c0100440d399cfc8a0f80842e090000000000000000409145a6f5111560802e12a0900500ffffffffbf47567ec507404297040000000000000000a0c82213fa880a3040170950c80280ffffffff1f242bbfe20320a14b020000000000000000506491a97d440518a08b04286401c0ffffffff4fd9945ff10190d02501000000000000000028b2c8cc3ea2020cd0450214b200e0ffffffff0700c4aff80048e8920000000000000000001459645e1f510106e822010a5900f0ffffff0fc6002c0100440d668cfc8a0f80842e09000000000000000040914526f6111560802e12a0900500ffffffff3fb5427ec507404297040000000000000000a0c82213fa880a3040170950c80280ffffffffc21b00049002940214de20204011c011a0f00602019a00b900853718083006f80328bc0141c035802740e10d0a02da0128010a6f6010800f700450788383808b008180c21b20049c03200514de2021c0144022a0f0060a0166001501853758084805180a28bc0143401b005840e10d1a123201a60000ba002c0100440d6c85908c0f80842e0900000000000000004091450680020cc0150214582000a0f00d016919000005711100000003008567881e0000681d0000801e000028fc008502c54f581192f10190d02501000000000000000028b2c800508001b84280020b040014be81202d0300a0e0ffffff1f4000a0f009006e110000761100008567881e0000681d00000000000028fc008502c0effd1092f10190d02501000000000000000028b2c800508001b84280020b040000c7002c0100440def87904ce11b08ce32000006feffffff0104000a9f0000be000090be0000507886e7010000cb01000000000080c20f502800fc665021191f00095d12000000000000000080228b0c000518802b0428b0400040e11b08d032000008feffffff0104000a9f00d0c60000e0c60000507806e5010080d001000000000080c20f505000fce00f21191f00095d12000000000000000080228b0c000518802b0428b0400040e11b02d23200000ae02200000006000acf103d0000d03a0000003d000000c4002c0100440df087904ce1072814287e719c908c0f80842e0900000000000000004091450680020cc0150214582000a0f00d016919000005861100000003008567881e0000781d0000681e000028fc004541c6cfe71392f10190d02501000000000000000028b2c800508001b84280020b040014be416039030060e0ffffff1f4000a0f00901410c0000440c00008567681e0000b01c00000000000028fc800205c0cf901592f10190d02501000000000000000028b2c800508001b84280020b040000c7002c0100440d86ac904ce11b02d032000008dc1800000004000acfa03c0000103a00000000000050f8010a0a801faf2224e30320a14b02000000000000000050649101a0000370850005160800287c0381590600800000000000800040e113029a2200009a2200000acf60390000a03c00000000000050f8010f0f80df312424e30320a14b02000000000000000050649101a0000370850005160800287c830073060000c1ffffff3f800040e11300b81a0000cc1a00000acf60390000b03c00000000000000c5002c0100440dc790904ce1071e32007e1399908c0f80842e0900000000000000004091450680020cc0150214582000a0f00d04681900000400000000000200854f084889000048890000283c83e5000080f200000000000040e1073c3c007eb2a6908c0f80842e0900000000000000004091450680020cc0150214582000a0f00d02c819000000ffffffff000200854f08105e0000585e0000283c03f3000040e100000000000040e1070a02007e419c908c0f80842e0900000000000000004091450600c6002c0100440d419c904c0106e00a010a2c100050f88680b40c008002ba080000800180c233440f0000b40e0000400f0000147e804281e2a78f0ac9f80048e892000000000000000000";
                var somedata_4 =
                    "1459640028c0005c2140810502000adf305090010050f0ffffff0f200050f80400b8060000bc060080c233580e0000400f000000000000147e408102e0275009faf80048e8920000000000000000201559646e245101068838010a2c8001f08bdd047d7c00b47549007094c6000048451e8a2c324792a80003449c00c0002c0100440db19ba04f81053c007e8daca08f0fa0eb2e0900cce0180040af38429145a64a121560808813a0c0020e003f4553d0c707f866970400ea7b0c00985874a1c822d32b890a3040c4095060810080dfad29e8e3031406480218f7480600e9430e546491599044051820e20428b0800380428b405f2040e1476b0afaf80062ec920020de8d0100458bf61559645e255101068838010a2c3000f09b35057d7c000b7449002019c600009644828a2c320792a80003449c000516200000b7002c0100440d3583a08f0f80842e0900000000000000a05191456654121560808813a0c0020500bf5a53d0c707404297040000000000000090a7c8229323890a3040c40950608100809fad29e8e30320a14b0200000000000000a8576491499344051820e20428b0000180428ba0ce363ee187b30afaf8e4ffdf8cae19e68e39b35e8a001459641e245101068838010a2c7000f0f348057d7c002474490000000000000000f58a2c326f92a80003449c000516200000c5002c0100440db4d0a08f0f48852e090024b31800e8ac285891456652121560808813a0c0022d00bf874ad0c7071212990c9d3ea30c7357a034aec8229328890a3040c40950608102008516817db780c2cf3d15f4f10119cf2501000d140300bc101129b2c8e447a2020c10710214586003e0c79708b0f80048e89200000000000000006a1542640028c0805c2340210b00feffffff7fa59720b00f80842e0900000000000000a05691450680020c6c520214b200e0ffffffbfc0021f008527203c000000bb002c0100440db5a814870fe8682d0950271a19803ee9785f9145a6b5101560609312a0900500ffffffff05169000f8d9a282c03e0012ba24000000000000008079451619000a30b0490950c80280ffffffff020bb000149ec0ee0000e007150902fb0048e89200000000000000005c1559640028c0c0262540210b00feffffff0b2c6001507882be0300805f2e2a08ec0320a14b02000000000000000856649101a00003b8a800852c00f8ffffff2fb0c00240e109100e000000b9002c0100440dbaa820b00f80842e0900000000000000205791450680020ce0a20214b200e0ffffffbfc0021100852758380000f8e50082c03e0012ba2400000000000000001a451619000a30808b0a50c80280ffffffff020b3400149e40e10000e0778b0a02fb0048e8920000000000000000401459640028c0002e2a40210b00feffffff0b2cb00150788284030080df79288fe20320a14b020000000000000058554b9101a00003858900852c00f8ffffff2fb0801000c0002c0100440d8d9320b00f80842e0900000000000000004091450680020c484e0214b200e0ffffffbfc0020a008527103c0000f83d4e82c03e0012ba24000000000000000040451619000a3020390950c80280ffffffff020b2400149ea0f00000e007390902fb0048e8920000000000000000381459640028c080e42440210b00feffffff0b2cf002507882c30300805f771f08ec0320a14b02000000000000001853649101a00003929300852c00f8ffffff2fb0c00140e109060f000000c0002c0100440df6a720b00f80842e0900000000000000a04391450680020c740c0114b200e0ffffffbfc0025c008527e83a0000f81d4e82c03e0012ba24000000000000008009451619000a30d0310450c80280ffffffff020b1800149e60ee0000e0d7f50c02fb0048e8920000000000000000821559640028c040c71040210b00feffffff0b2c7000507882ba0300809fe52408ec0320a14b0200000000000000d850649101a000031d4300852c00f8ffffff2fb0c00140e109ee0e000000c0002c0100440de8";
                var somedata_5 =
                    "ca20b00f80842e0900000000000000004091450680020c5c4e0214b200e0ffffffbfc00213008527e03b0000f8b99b82c03e0012ba24000000000000000058451619000a3070390950c80280ffffffff020b7001149e00ef0000e077210a02fb0048e8920000000000000000dc1459640028c0c0e52440210b00feffffff0b2c100250788289030080df8a1208ec0320a14b02000000000000003055649101a00003979300852c00f8ffffff2fb0800740e109240e000000cb002c0100440d284a20b00fdefba1a8a8706368f38ea2d85e91450680020ca8280114b200e0ffffffbfc00212008527103a0000f8952881c03e0012ba24000000000000000014451619000a30a0a20450c80280ffffffff020b2400149e20f00000e0d7a10402fb0048e8920000000000000000fe1559640028c0808a1240210b00feffffff0b2c2003507882840300801f8b12e8e30320a14b020000000000000000506491c99244051850510228b04000c0af45092cf10190d02501000000000000000028b2c8441c22c8002c0100440d2d4a604901060a13010a5900f0ffffff5f608100805f8812d7e10320a14b020000000000000000506491e929440518785102286401c0ffffff7f810514007e51a8e08b0f80842e0900000000000000004091456600a44701065e94000a5900f0ffffff5f7807b5bb2fb2b4b036b934b7b3981a80df8612f8e20320a14b0200000000000000005064910900e9518081172580421600fcffffff17de41edee8b2c2dac4d2ecded2ca606e087a344aff80048e8920000000000000000181559640000c5002c0100440d384af44a0106e41a010a5900f0ffffff5ff884cb0900763a00010a809f8c12bde20320a14b0200000000000000b052649101a00003728d00852c00f8ffffff2f7c02e604403b1dc00005c02f4389e2f0a9eaab1b77045d23fb495c1dff29b2c8ecc3a2020cc8350214b200e0ffffffbfc002c0493f1a25e8c30704529604f046870c003c5744a4c822b3aec10a3020d70850c80280ffffffff5f0c22c5e10326a04b02549c4506000e2afe5064919922440518906b04286401c0ffffff3fbf002c0100440d318814478105cc000a1e10f0a341a4387c002474490000000000000000118a2c325d84a80003728d00852c00f8ffffff2fb0c00640c103027e2e881c870fc8332d09d00e1c198037e618409145a68c10156040ae11a0900500ffffffff0516801f287840c00fa28ee2f00190d0250100000000000000ac28b2c82c11a2020cc8350214b200e0ffffffbfc002a502bf164c8ac307404297040000000000000060a6c822734f880a3020d70850c80280ffffffff020b680900c7002c0100440d3788c08b0f80842e090000000000000000409145261e03176040ae11a0900500ffffffff3f41448ac307404297040000000000000000a0c8227346880a3020d70850c80280ffffffff020b6000fc54ad280e1f00095d12000000000000004090228b4c0f212ac0805c2340210b00feffffff0b2c6011f0b32a63fa7c002474490000000000000000c78a2c328db0a80003728d00852c00f8ffffff57681100a8835dfc6031893e1f00095d1200000000000000c0af228b4c282c2ac0805c2300c5002c0100440db098449f428bc0aa8877e4c7484af4f90048e89200000000000000001c1559646a61510106e41a01145a0489eb6017bfa94daec3074c1f9804f840a50cc0f588fcafc8223354880a3000af0e50c80280ffffffff020b7800fcb0cab8171ff051dcf09b50e5b1ef92893180028b4c3a2c2ac000bc3a40210b00feffffff0b1e10f03388843e7cd78513c752f8edc768b98bc5028a2c32ff1aac0003f0ea00852c00f8ffffffff69c4412f3e0012ba24000000000000000000451699158c32bc002c0100440d1a71d04b0106e0d5010a5900f0ffffffff9b51c23e7cf5761045b11b1b43230a15c53f0a2c326db2a80003f0ea00852c00f8ffffffff416d922f3e0012ba2400000000000000000045169945922e05188057";
                var somedata_6 =
                    "07286401c0ffffffff4f6408dbf10190d02501000000000000000028b2c80c56a0020cc0ab0314b200e0ffffffff873204bef800f4dc92008e709101b0628e01145364ae61510106e0d5010a5900f0ffffff5f7807b5bb2fb2b4b036b934b7b39919002d002c0100440dec5fb8860f80842e0900000000000000004091450680020cc83502142620a0900500ffffffff";
                // await ns.WriteAsync(sd1);
                await ns.WriteAsync(Convert.FromHexString(somedata_1));
                await ns.WriteAsync(Convert.FromHexString(somedata_2));
                await ns.WriteAsync(Convert.FromHexString(somedata_3));
                await ns.WriteAsync(Convert.FromHexString(somedata_4));
                await ns.WriteAsync(Convert.FromHexString(somedata_5));
                await ns.WriteAsync(Convert.FromHexString(somedata_6));

                Task.Run(async () =>
                {
                    Thread.Sleep(3000);
                    await ns.WriteAsync(Convert.FromHexString($"14002c010000{playerIndexStr}6f08c04260fed39010b01700"));
                });

                Task.Run(async () =>
                {
                    Thread.Sleep(9000);

                    while (ns.CanWrite)
                    {
                        await ns.WriteAsync(Convert.FromHexString($"13002c010000{playerIndexStr}6f08c042a0ffd39008b007"));
                        Thread.Sleep(6000);
                    }
                });

                Task.Run(async () =>
                {
                    var index = 0;

                    // var bitmap = new Bitmap("C:\\Download\\vd.bmp");
                    // var colorMask = new bool [bitmap.Height, bitmap.Width];
                    //
                    // for (var i = 0; i < bitmap.Height; i++)
                    // {
                    //     for (var j = 0; j < bitmap.Width; j++)
                    //     {
                    //         colorMask[i, j] = bitmap.GetPixel(i, j).R > 75;
                    //     }
                    // }

                    while (index < 255)
                    {
                        // if (colorMask[index / 90, index % 90])
                        // {
                        //Console.ReadLine();
                        // var entity = TestHelper.GetTestEntityData(index);
                        // await ns.WriteAsync(entity);
                        // }
                        index++;
                        // if (index % 10 == 0) {
                        //     Thread.Sleep(1);
                        // }
                    }
                });

                var pongStrStart = "12002c0100";
                var oldClientPingStr = "";
                Thread.Sleep(300);

                var somedata_x = "20002c0100e0d411568843e325000046c32300000000349e400041e309081400";
                await ns.WriteAsync(Convert.FromHexString(somedata_x));
                Thread.Sleep(50);

                while (ns.CanRead)
                {
                    while (await ns.ReadAsync(rcvBuffer) != 38)
                    {
                    }

                    var clientPingBytes = rcvBuffer[17..38];

                    var clientPingStr = Convert.ToHexString(rcvBuffer[9..38]);
                    var clientPingBinaryStr = BitHelper.ByteArrayToBinaryString(clientPingBytes, false, true);

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
                            // var coordsFile = File.Open(coordsFilePath, FileMode.Create);
                            // coordsFile.Write(Encoding.ASCII.GetBytes(x + "\n" + y + "\n" + z + "\n" + turn));
                            // coordsFile.Close();
                            oldClientPingStr = clientPingBinaryStr;
                        }
                    }

                    var temp = Convert.FromHexString(clientPingStr[12..14])[0];
                    temp += 7;
                    var temp2 = Convert.FromHexString(clientPingStr[14..16])[0];
                    temp2 += 14;
                    var pongStr = pongStrStart + clientPingStr[..12] + Convert.ToHexString(new[] { temp, temp2 }) +
                                  clientPingStr[16..24] + "00";
                    await ns.WriteAsync(Convert.FromHexString(pongStr));
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