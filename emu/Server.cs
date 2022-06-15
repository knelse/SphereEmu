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
                    Console.WriteLine("Cannot load file coords");
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

                NewPlayerDungeonFromKeyPress(ns, selectedCharacter);

                // var enterGame2Binary = (await File.ReadAllTextAsync("C:\\source\\enterGame2.txt")).RemoveLineEndings();

                // Giras, Anhelm near portal
                // var enterGameResponse_3 =
                //     $"bf002c010000044f6f08406102000a82000000000000000050108400d9620048000080822008fcdb0000320000140461605a1b00a90100a02008044d5e00f5010000054128e0ab03080b000028088201dd3b803e00004041100e98bc00f40100000a8280b04204d007000050108484b52c0009000080822028503601f401000014046161c90b40020000a020080c32710083010000054168b8d303f01e0000280882c3d821800200004041101e9a9200bc0200000a820081770aa020000000";
                // var enterGameResponse_4 =
                // $"bf002c010000044f6f08406102000a820000000000000000501084007234004800008082200800ad030032000014046120621e00a90100a0200804e70a00f5010000054128e8ae07080b000028088201a83f803e00004041100ee2e701f40100000a828050550fd0070000501084844f7a000900008082202860f403f4010000140461a1061e40020000a020080c56e40083010000054168587c07f01e0000280882437127800200004041101edeee01bc0200000a8200611407a020000000";

                // var enterGameResponse_4_1 =
                // $"c6002c010000044f6f084041102284d701f80500000a8230010000000000005010040a047600320000808220584cb103e4e193fe1704e182d31fa0050000a020081892ed002d0000000541d0307807c84d02002808c2c6c13680d4080040411038a6e000c80000000a82d0b1f609a00100005010040f846f80f701008082207ce83b02940a0000140401a4de1d004b0000a0200821d4c8004c040000054110891302c00000002808c24855384045000040411048d0fb00020000000a825092c00e1000000000";
                // var enterGameResponse_4_2 =
                // $"94002c010000044f6f084041104cf0aa01020000000a822053300fa000000050108419887900050000808220d014800128000000140401e7a81660000000a0200839000000000000000541d831d703005a62022808828e0c22804214004088ccf500000a823041fa06d0020000501004801f4100fa000080860d020000004000000040e3422100003a12e0284d2e6caeb1111000" +
                // $"72002c010000044f6f08406383acccccac6c8c6e8e8b0b0080c646a6a626e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc565aecc0ca007240624c88908640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f084063838c2dcc8d6c6e2c0cae8c8b0bc046a6a626e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f6f47c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f084063838c2dcc8d6c6e2c0caeec0b4d8e8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f0f40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f084063838c2dcc8d6c6e2c0caeec0b0e8d8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f0f40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f084063838c2dcc8d6c6e2c0caeec4b8e8c8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5050f0f0f40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f08406383ac4d6c8c8b0b200caeec4b8e8c8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a54d6c0c40c5850e8f0e8008640c2d4cee8bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f08406383aced8dac8c6d8e8b0be04b8e8c8b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e6e47c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f08406383aced8dac8c6dee0b4d8e8b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e0e40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f08406383aced8dac8c6dee0b0e8d8b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e0e40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f08406383aced8dac8c6dee4b8e8c8b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a58d8c6d47c5a58d4e0e40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f084063830c2e4c2eac6d8e8b0b808b0b808b0ba026e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc565ccec6c47c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // $"72002c010000044f6f084063830c2e4c2eac6d8e8bab2d4caf6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a54d8c0c40c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600";

                // var enterGameResponse_4_3 = 
                // "72002c010000044f6f084063830c2e4c2eac6d8e8b4beeedad6d8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc5a54d8c0c40c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // "72002c010000044f6f084063830c2e4c2eac6d8e8b6baeac8c6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d4cc565ccec0c40c5a54d8c0c40c5850e8f0ee08bac8cedcb8c2dec0c84c70724068429a929890a2406008429a929890a240600" +
                // "c5002c010000044f6f084063830caf0e8e2c8cae8c8b0ba08c6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d6c0a0ead4caecca50caf6c670a0ead4cae6c882dadcc8dcea50caf0c80c70724068429a929890a2406008429a929890a2406a0b1212000b000383a2d0246500b814ed162a08bb420804521f2c2603bb038881f2c10c2028b846cc1422180b058e8082c182d17cd3c0b07593cc802a207b38868a12ca4c2308ba939c882021695900b0b5c5c00" +
                // "5f002c010000044f6f08c002a34ab3c8a8092d3491179b520b8e48c1a203161e0d83c5470f61010abb08f55b88c062441624b228555b98a22d4e7481725f5aa446ee30012c54d37d040016ab7a0b56a645abcbc2051633b2a0b1450d2d6c0c";
// stats 5f002c010000044f6f08c002a34ab3c8a8092d3491179b520b8e48c1a203161e0d83c5470f61010abb08f55b88c062441624b228555b98a22d4e7481725f5aa446ee30012c54d37d040016ab7a0b56a645abcbc2051633b2a0b1450d2d6c0c
// hp mp c5002c010000044f6f084063830caf0e8e2c8cae8c8b0ba08c6c8e8b0b20e62b26850524094a0704c7c62566c6458646c60567460120e60424c88908640c2d6c0a0ead4caecca50caf6c670a0ead4cae6c882dadcc8dcea50caf0c80c70724068429a929890a2406008429a929890a2406a0b1212000b000383a2d0246500b814ed162a08bb420804521f2c2603bb038881f2c10c2028b846cc1422180b058e8082c182d17cd3c0b07593cc802a207b38868a12ca4c2308ba939c882021695900b0b5c5c00
                // await ns.WriteAsync(BitHelper.BinaryStringToByteArray(enterGame2Binary));
                //Thread.Sleep(100);

                while (await ns.ReadAsync(rcvBuffer) != 0x13)
                {
                }

                // only in dungeon?
                while (await ns.ReadAsync(rcvBuffer) == 0 || rcvBuffer[0] != 0x12)
                {
                }

                var enterNewGame_2 =
                    //   "C0002c010000044f6f08406102000A8240810506400600005010841C000000000000808220E864810100000000140461470B0C0068890920445A6000000509A02848C04E411028B6C000C80000001A38040A0000008D8D8080C60659999959D918DD1C17178053D91DC8185A981C489A1BC859585B990E08F03AFF3BB83A38080AF03AFF3BB83A38084A4C0D0C8D8ACB5C9919404A8A8B020048901311C8185A98DC175919DB97195BD819088F0F480C085352531215480C000853525312150872002c010000044f6f084063838C2DCC8D6C6E2C0CAE8C8B0BE00E640C2D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5050F0F6F47C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f084063838C2DCC8D6C6E2C0CAEEC0B4D8E8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5050F0F0F40C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f084063838C2DCC8D6C6E2C0CAEEC0B0E8D8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5050F0F0F40C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f084063838C2DCC8D6C6E2C0CAEEC4B8E8C8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5050F0F0F40C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f08406383AC4D6C8C8B0B200CAEEC4B8E8C8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A54D6C0C40C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f08406383ACED8DAC8C6D8E8B0BE04B8E8C8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A58D8C6D47C5A58D4E6E47C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f08406383ACED8DAC8C6DEE0B4D8E8B0B808B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A58D8C6D47C5A58D4E0E40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f08406383ACED8DAC8C6DEE0B0E8D8B0B808B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A58D8C6D47C5A58D4E0E40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f08406383ACED8DAC8C6DEE4B8E8C8B0B808B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A58D8C6D47C5A58D4E0E40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f084063830C2E4C2EAC6D8E8B0B808B0B808B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C565CCEC6C47C5A54D8C0C40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f084063830C2E4C2EAC6D8E8BAB2D4CAF6C8E8B0B204C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A54D8C0C40C5A54D8C0C40C5850E8F";
                    "C0002c010000044f6f08406102000A8240B17601400600005010841C000000000000808220E860050200000000140461272B10006889092044598100000509A02848C04E411028B60201C80000001A38040A0000008D8D8080C60659999959D918DD1C17178053D91DC8185A981C489A1BC859585B990E08F03AFF3BB83A38080AF03AFF3BB83A38084A4C0D0C8D8ACB5C9919404A8A8B020048901311C8185A98DC175919DB97195BD819088F0F480C085352531215480C0008535253121508" +
                    "72002c010000044f6f084063838C2DCC8D6C6E2C0CAE8C8B0BE00E640C2D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5050F0F6F47C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f084063838C2DCC8D6C6E2C0CAEEC0B4D8E8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5050F0F0F40C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f084063838C2DCC8D6C6E2C0CAEEC0B0E8D8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5050F0F0F40C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f084063838C2DCC8D6C6E2C0CAEEC4B8E8C8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5050F0F0F40C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f08406383AC4D6C8C8B0B200CAEEC4B8E8C8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A54D6C0C40C5850E8F0E20C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f08406383ACED8DAC8C6D8E8B0BE04B8E8C8B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A58D8C6D47C5A58D4E6E47C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f08406383ACED8DAC8C6DEE0B4D8E8B0B808B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A58D8C6D47C5A58D4E0E40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f08406383ACED8DAC8C6DEE0B0E8D8B0B808B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A58D8C6D47C5A58D4E0E40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f08406383ACED8DAC8C6DEE4B8E8C8B0B808B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A58D8C6D47C5A58D4E0E40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f084063830C2E4C2EAC6D8E8B0B808B0B808B0B002D4C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C565CCEC6C47C5A54D8C0C40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f084063830C2E4C2EAC6D8E8BAB2D4CAF6C8E8B0B204C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A54D8C0C40C5A54D8C0C40C5850E8F";
                await ns.WriteAsync(Convert.FromHexString(enterNewGame_2));
                // await ns.WriteAsync(Convert.FromHexString(
                //     "BE002c010000044f6f08406102000A820000000000000000501004876757000500008082204C000000000000001404C122E81520DEB5FFBF2008178FB3002D0000000541C0909C05680100002808C246E52C00E60500404110642C6701140000000A823073390BA00000005010041CCC5980010000808220E40000000000000014046147731600688909A020083A9BB30070F5000021D29C05002808C284E92C400B0000404110004E6701E80300001A360803000000020000008D8D800072002c010000044f6f08406383ACCCCCAC6C8C6E8E8B0B000485C646A6A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC565AECC0CA007440624C88908640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f084063838C2DCC8D6C6E2C0CAE8C8B0B80C646A6A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5050F0F6F47C5850E8F0E8008640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f084063838C2DCC8D6C6E2C0CAEEC0B4D8E8B0BA0A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5050F0F0F40C5850E8F0E8008640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f084063838C2DCC8D6C6E2C0CAEEC0B0E8D8B0BA0A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5050F0F0F40C5850E8F0E8008640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f084063838C2DCC8D6C6E2C0CAEEC4B8E8C8B0BA0A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5050F0F0F40C5850E8F0E8008640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f08406383AC4D6C8C8B0B200CAEEC4B8E8C8B0BA0A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5A54D6C0C40C5850E8F0E8008640C2D4CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f08406383ACED8DAC8C6D8E8B0BE04B8E8C8B0BA0A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5A58D8C6D47C5A58D4E6E47C5850E8F0EE08BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f08406383ACED8DAC8C6DEE0B4D8E8B0B808B0BA0A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5A58D8C6D47C5A58D4E0E40C5850E8F0EE08BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f08406383ACED8DAC8C6DEE0B0E8D8B0B808B0BA0A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5A58D8C6D47C5A58D4E0E40C5850E8F0EE08BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f08406383ACED8DAC8C6DEE4B8E8C8B0B808B0BA0A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5A58D8C6D47C5A58D4E0E40C5850E8F0EE08BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f084063830C2E4C2EAC6D8E8B0B808B0B808B0BA0A626E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC565CCEC6C47C5A54D8C0C40C5850E8F0EE08BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f084063830C2E4C"));
                // // await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4));
                Thread.Sleep(100);
                var enterNewGame_3 =
                    //    "0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f084063830C2E4C2EAC6D8E8B4BEEEDAD6D8E8B0B204C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A54D8C0C40C5A54D8C0C40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A0472002c010000044f6f084063830C2E4C2EAC6D8E8B6BAEAC8C6C8E8B0B204C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C565CCEC0C40C5A54D8C0C40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04C7002c010000044f6f084063830CAF0E8E2C8CAE8C8B0BA08C6C8E8B0B204C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A60686660A0EAD4CAECCA50CAF6C670A0EAD4CAE6C882DADCC8DCEA50CAF0CE00C84C70724068429A929890A2406008429A929890AA4B1212000B000745E04722F04911783C80B42B24521F2C2001607B0408045022C1460B1000B065834C0C241160FB2808045042C246031010B0A5854C0C2021617B0C0445E64222F3491171BB0E08045072C3C60F10139002c010000044f6f08C002041621B01081C5882C486451020B13589CD802051629B05081C52AC48205172DB87081C50C2C686051030B1B0412002c010000044f6f0020CA6C5C0C016000";
                    "0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f084063830C2E4C2EAC6D8E8B4BEEEDAD6D8E8B0B204C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C5A54D8C0C40C5A54D8C0C40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "72002c010000044f6f084063830C2E4C2EAC6D8E8B6BAEAC8C6C8E8B0B204C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A6068646C565CCEC0C40C5A54D8C0C40C5850E8F0E204CEE8BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A04" +
                    "77002c010000044f6f084063830CAF0E8E2C8CAE8C8B0BA08C6C8E8B0B204C0E24CD0DE42CACAD4C0704789DFF1D5C1D1C0405789DFF1D5C1D1C0425A60686660A0EAD4CAECCA50CAF6C670A0EAD4CAE6C882DADCC8DCEA50CAF0CE00C84C70724068429A929890A2406008429A929890AA4B121200000"; // +
                // "12002C0100010061090060289C7710016000";
                await ns.WriteAsync(Convert.FromHexString(enterNewGame_3));
                // await ns.WriteAsync(Convert.FromHexString(
                //     "2EAC6D8E8BAB2D4CAF6C8E8B0BA026E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5A54D8C0C40C5A54D8C0C40C5850E8F0EE08BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f084063830C2E4C2EAC6D8E8B4BEEEDAD6D8E8B0BA026E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC5A54D8C0C40C5A54D8C0C40C5850E8F0EE08BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060072002c010000044f6f084063830C2E4C2EAC6D8E8B6BAEAC8C6C8E8B0BA026E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D4CC565CCEC0C40C5A54D8C0C40C5850E8F0EE08BAC8CEDCB8C2DEC0C84C70724068429A929890A2406008429A929890A24060077002c010000044f6f084063830CAF0E8E2C8CAE8C8B0BA08C6C8E8B0BA026E64B26850524094A0704C7C62566C6458646C605674601E00424C88908640C2D6C0A0EAD4CAECCA50CAF6C670A0EAD4CAE6C882DADCC8DCEA50CAF0C80C70724068429A929890A2406008429A929890A2406A0B12120000012002c010000044f6f309CE93E6416016000A0002c010000044f6f08C002107911103459086C2916035B8A05012C0A9117862A8B43F405B2C5221179A148B658045B30DA2C1A64E1208B075940A4091611C5838554B36031CDBDA0804525E5C2821617B0C0487E2C32B6140B4DE4C526D082D37FD1010B4FEFC5A7D802D46B114AB31081C5882C4864516AB430E5589CE80225E2B4484D69D10158A8C062156DC18AB5688558B8C862461634B0A891858D00"));
                // // await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4_1));
                Thread.Sleep(100);
                // var enterNewGame_5 = //"2B002c010000044f6f0840036220E088AB814300000000D0702870900D0040768804010016F39171310035";
                // "2B002c010000044f6f08400362204000A0814300000000D0702870900D00007088048138B6F311DE410049";
                // await ns.WriteAsync(Convert.FromHexString(enterNewGame_5));
                // await ns.WriteAsync(Convert.FromHexString(enterGameResponse_4_3));

                while (await ns.ReadAsync(rcvBuffer) == 0 || rcvBuffer[0] != 0x69)
                {
                }

                // CreateEchoPingThread(currentPlayerIndex, ns);
                CreateSixSecondPingThread(currentPlayerIndex, ns);

                // var somedata_x = "20002c010000044f6f8843e325000046c32300000000349e400041e309081400";
                // await ns.WriteAsync(Convert.FromHexString(somedata_x));
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
                            await ns.WriteAsync(Convert.FromHexString(
                                "2E002C010000044F6FE8162D6A5A6F89895B168CBB09D88DF020CA89A02FC544D1180822068F8A302204E000000"));
                            break;
                        //echo
                        case 0x08:
                            await ns.WriteAsync(CommonPackets.Echo(currentPlayerIndex));
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

        private static void NewPlayerDungeonFromKeyPress(NetworkStream ns, CharacterData selectedCharacter)
        {
            Task.Run(async () =>
            {
                while (ns.CanRead)
                {
                    var str = Console.ReadLine();

                    if (string.IsNullOrEmpty(str) || !str.Equals("def"))
                    {
                        continue;
                    }
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
                    Console.WriteLine($"SRV: Teleported client [{selectedCharacter.PlayerIndex}] to default new player dungeon");
                }
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
    }
}