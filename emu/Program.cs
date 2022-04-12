using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using emu.DataModels;
using emu.Helpers;

namespace emu
{
    internal class Server
    {
        private const int BUFSIZE = 1024;

        enum ServerConnectionState
        {
            NONE,
            SERVER_INIT,
            SERVER_CREDENTIALS,
            SUBSCRIPTION_ERROR,
            START_DATA_SENT_1,
            START_DATA_SENT_2,
        }

        enum ClientConnectionState
        {
            NONE,
            SERVER_INIT_OK,
            SERVER_CREDENTIALS_OK,
            LOGIN_DATA,
        }

        enum ServerSyncPacketType : byte
        {
            CONNECTION_LIMIT_REACHED = 0x64,
            CONNECTION_CREATE_OK = 0xC8,
            UNKNOWN_1 = 0x01,
            UNKNOWN_2 = 0xF4,
            EVERYTHING_ELSE = 0x2C
        }
        
        List <byte> readyToLoadInitialDataList = new List <byte>
        {
            0x0a, 0x00, (byte) ServerSyncPacketType.CONNECTION_CREATE_OK, 0x00, 0xdc, 0x07, 0x00, 0x00, 0x61, 0x5d
        };
        
        List <byte> authServerResponseList = new List<byte>
        {
            0x38, 0x00, 0x2c, 0x01, 0x00, 0x28, 0x3c, 
            0x55, 0x37, 0x08, 0x40, 0x20, 0x10, 0x98, 0x5f, 0x52, 0x1c, 0x35, 0x7c, 0x12, 
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1a, 0x3b, 0x12, 0x01, 0x00, 
            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 
            0xff, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x8d, 0x9d, 0x01, 0x00, 0x00, 0x00
        };

        public static void Main (string[] args)
        {
            const int port = 25860;

            var testChar = new CharacterData
            {
                MaxHP = 1234,
                MaxMP = 5678,
                Strength = 123,
                Agility = 456,
                Accuracy = 789,
                Endurance = 12345,
                Earth = 44,
                Air = 55,
                Water = 66,
                Fire = 77,
                PDef = 88,
                MDef = 99,
                Karma = KarmaTypes.Benign,
                MaxSatiety = 4444,
                TitleLevelMinusOne = 43,
                DegreeLevelMinusOne = 32,
                TitleXP = 1111,
                DegreeXP = 2222,
                CurrentSatiety = 3333,
                CurrentHP = 55,
                CurrentMP = 66,
                AvailableTitleStats = 77,
                AvailableDegreeStats = 88,
                IsGenderFemale = false,
                Name = "UwUwHaTsThIs",
                FaceType = 0b00001100,
                HairStyle = 0b00001100,
                HairColor = 0b00001100,
                Tattoo = 0b00001100,
                Boots = 0b00001100,
                Pants = 0b00001100,
                Armor = 0b00001100,
                Helmet = 0b00001100,
                Gloves = 0b00001100,
            };

            var clientData = new ClientInitialData
            {
                Character1 = testChar,
                Character2 = testChar,
                Character3 = testChar
            };
            // var cli = BitHelper.ByteArrayToBinaryString(clientData.ToByteArray())[104..];
            //
            // for (var i = 0; i < cli.Length; i+=8)
            // {
            //     Console.WriteLine(cli[i..(i+8)]);
            // }
            // var str =
            //     "6c002c010046044f6f08406079e501bc0200000000000004000000000004000000000000000c9001000004000000000038010000c800e401bc020c001c00e8c0c8c8004ca59da5b10100000000000000000000000000e8c0c8c8c0c0c0c0c000c0c000fcffffff03000000006c002c010046044f6f08406079911fb4142800dc02a8ff67019c0094004000d8ff1b03d0051490018c00280108257d00000000000000901fb414b0002000e8ccc8cc04acb995b1cd9501000000000000000000000000e8ccc8ccc0c0c0c0c09cc1c000fcffffff03000000006c002c010046044f6f0840607949122413e8fff300e8ff3b013c0128001c00f0ff8b02880114900148003800880804000000000000008c10241318000000c0c0c0c80004919185c9e501000000000000000000000000c0c0c0c8c4c8c800c4c4c8d0c0fcffffff0300000000";
            // var bytes = Convert.FromHexString(str);
            //
            // var outp = new StringBuilder();
            // for (int i = 0; i < bytes.Length; i++)
            // {
            //     outp.AppendLine(Convert.ToString(bytes[i], 16).PadLeft(2, '0'));
            // }
            //
            // File.WriteAllText("C:\\output.txt", outp.ToString());

            TcpListener? tcpListener = null;
            
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Start();
                // var udpClient = new UdpClient(port);
                // udpClient.BeginReceive(DataReceived, udpClient);
            }
            catch (SocketException se)
            {
                Console.WriteLine(se.Message);
                Environment.Exit(se.ErrorCode);
            }
            
            Console.WriteLine("Server up, waiting for connections...");
            var subscriptionErrorResponse = new List<byte>
            {
                0x0e, 0x00, 0x2c, 0x01, 0x00, 0x64, 0x43, 0x22, 0x3c, 0x08, 0x40, 0xc0, 0x01, 0x00
            };
            
            // var readyToLoadInitialData = 
            //     "0a00c800dc070000615d";
            // var serverCredResponse = 
            //     "3800 2c01 0028 3c55 3708 4020 1098 5f52 1c35 7c12 0200 0000 0000 0000 001a 3b12 0100 ffff ffff ffff ffff 0000 0000 0000 0000 8d9d 0100 0000";
            // var startDataResponse_1 =
            //     "52002c01008232f6e408408010010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";
            // var startDataResponse_2 =
            //     "6c002c01008632f6e4084060794d02bc0204000400040008000400040008000400100010000c9001000004000000000038010000c8004c02bc020c001c00e8c0c8c8004ca59da5b10100000000000000000000000000e8c0c8c8c0c0c0c0c000c0c0d8fcffffff0300000000";
            // var startDataResponse_3 =
            //     "6c002c01008632f6e4084060794912d4140c0014010c00e001c00128006400f0ffa70e0c0c1490014800380088080400000000000000dc10a81218000000c0c0c0c80004919185c9e501000000000000000000000000c0c0c0c8c4c8c800c4c4c8d0c0fcffffff0300000000";
            
            // 1 SESSION
            var readyToLoadInitialData_readable = 
                "0a00 c800 1405 00001f42";
            var serverCredResponse_readable =
                "3800 2c01 0000 044f 6f08 4020 1088 0e7d 1c35 7c12 0200 0000 0000 0000 001a 3b12 0100 ffff ffff ffff ffff 0000 0000 0000 0000 8d9d 0100 0000";
            var startDataResponse_1_readable = 
                "5200 2c01 0000 044f 6f08 4080 1001 0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";
            var startDataResponse_2_readable =
                "6c00 2c01 0000 044f 6f08 4060 " +
                "79ff d2bc 0200 0000 0000 0004 " +
                "0000 0000 0004 0000 0000 0000 " +
                "000c 9001 0000 0400 0000 0000 " +
                "3801 0000 c800 e401 bc02 0c00 " +
                "1c00 e8c0 c8c8 004c a59d a5b1 " +
                "0100 0000 0000 0000 0000 0000 " +
                "0000 e8c0 c8c8 c0c0 c0c0 c000 " +
                "c0c0 00fc ffff ff03 0000 0000 " +
                "6c00 2c01 0046 044f 6f08 4060 " +
                "7991 1fb4 1428 00dc 02a8 ff67 " +
                "019c 0094 0040 00d8 ff1b 03d0 " +
                "0514 9001 8c00 2801 0825 7d00 " +
                "0000 0000 0000 901f b414 b000 " +
                "2000 e8cc c8cc 04ac b995 b1cd " +
                "9501 0000 0000 0000 0000 0000 " +
                "0000 e8cc c8cc c0c0 c0c0 c09c " +
                "c1c0 00fc ffff ff03 0000 0000 " +
                "6c00 2c01 0046 044f 6f08 4060 " +
                "7949 1224 13e8 fff3 00e8 ff3b " +
                "013c 0128 001c 00f0 ff8b 0288 " +
                "0114 9001 4800 3800 8808 0400 " +
                "0000 0000 0000 8c10 2413 1800 " +
                "0000 c0c0 c0c8 0004 9191 85c9 " +
                "e501 0000 0000 0000 0000 0000 " +
                "0000 c0c0 c0c8 c4c8 c800 c4c4 " +
                "c8d0 c0fc ffff ff03 0000 0000";

            var startDataResponseBinary = @"
01101100
00000000
00101100
00000001
00000000
00000000
00000100
01001111
01101111
00001000
01000000
01100000
01111001
01100110
11111111
00111111
11111111
00000011
11110000
11000011
01110000
01111000
00011100
00000101
11111111
11111110
11111111
00111000
00111000
00000100
00111100
01111000
00000000
11111111
11111111
11111111
11111111
00010111
11111100
11111111
10111111
00000011
11111100
00000001
10000100
10000000
10000000
00010000
00111000
01111001
00000000
00111000
00100000
10100011
11100111
10000001
10111100
10000010
10001110
00011100
00011110
00011100
11101110
11000000
11001000
11001000
00000101
01001000
10110101
10011101
10100101
10110001
10100101
10100101
10100101
10100101
10100101
10100101
10100101
10100101
10100101
10100101
10100101
10100101
10100101
10100101
11101010
11000000
11001000
11001100
11000100
11010110
11010000
11110010
11011000
00000010
11001100
11001111
00000000
10111100
11111111
11110001
11001111
11111111
11111111
11111111
11111111
11111111
01101100
00000000
00101100
00000001
00000000
01000110
00000100
01001111
01101111
00001000
01000000
01100000
01111001
10010001
00011111
10110100
00010100
00101000
00000000
11011100
00000010
10101000
11111111
01100111
00000001
10011100
00000000
10010100
00000000
01000000
00000000
11011000
11111111
00011011
00000011
11010000
00000101
00010100
10010000
00000001
10001100
00000000
00101000
00000001
00001000
00100101
01111101
00000000
00000000
00000000
00000000
00000000
00000000
00000000
10010000
00011111
10110100
00010100
10110000
00000000
00100000
00000000
11101000
11001100
11001000
11001100
00000100
10101100
10111001
10010101
10110001
11001101
10010101
00000001
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
11101000
11001100
11001000
11001100
11000000
11000000
11000000
11000000
11000000
10011100
11000001
11000000
00000000
11111100
11111111
11111111
11111111
00000011
00000000
00000000
00000000
00000000
01101100
00000000
00101100
00000001
00000000
01000110
00000100
01001111
01101111
00001000
01000000
01100000
01111001
01001001
00010010
00100100
00010011
11101000
11111111
11110011
00000000
11101000
11111111
00111011
00000001
00111100
00000001
00101000
00000000
00011100
00000000
11110000
11111111
10001011
00000010
10001000
00000001
00010100
10010000
00000001
01001000
00000000
00111000
00000000
10001000
00001000
00000100
00000000
00000000
00000000
00000000
00000000
00000000
00000000
10001100
00010000
00100100
00010011
00011000
00000000
00000000
00000000
11000000
11000000
11000000
11001000
00000000
00000100
10010001
10010001
10000101
11001001
11100101
00000001
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
11000000
11000000
11000000
11001000
11000100
11001000
11001000
00000000
11000100
11000100
11001000
11010000
11000000
11111100
11111111
11111111
11111111
00000011
00000000
00000000
00000000
00000000
";

            // var startDataResponse_2_from_binary = new List<byte>();
            // startDataResponseBinary = startDataResponseBinary.ReplaceLineEndings("\n").Replace("\n", "");
            //
            // for (var i = 0; i < startDataResponseBinary.Length; i+=8)
            // {
            //     var currByte = startDataResponseBinary[i..(i + 8)];
            //     startDataResponse_2_from_binary.Add(Convert.ToByte(currByte, 2));
            // }
            
            var transmissionEndPacket = 
                "0400f401";
            var readyToLoadInitialData = readyToLoadInitialData_readable.Replace(" ", "");
            var serverCredResponse = serverCredResponse_readable.Replace(" ", "");
            var startDataResponse_1 = startDataResponse_1_readable.Replace(" ", "");

            var pongPacket = "12002c01002d03890ab9dec7f7601f016000";
            
            while (true)
            {
                NetworkStream? ns = null;
                var sb = new StringBuilder();

                try
                {
                    var client = tcpListener.AcceptTcpClient();
                    ns = client.GetStream();
                    var clientState = ClientConnectionState.NONE;
                    var serverState = ServerConnectionState.NONE;
                
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Handling client...");
                    Console.ForegroundColor = ConsoleColor.White;
                
                    Console.WriteLine("SRV: Ready to load initial data");
                    ns.Write(Convert.FromHexString(readyToLoadInitialData));
                    serverState = ServerConnectionState.SERVER_INIT;
                
                    var pongThread = Task.Run(() =>
                    {
                        while (ns.CanWrite)
                        {
                            ns.Write(Convert.FromHexString(pongPacket));
                            //Console.WriteLine("SRV: Pong");
                            Thread.Sleep(2000);
                        }
                    });
                
                    var rcvBuffer = new byte[BUFSIZE];
                    var dataWritten = false;
                
                    var totalBytesReceived = 0;
                
                    var clientBytes = new List<byte>();
                
                    var bytesRcvd = 0;
                
                    while ((bytesRcvd = ns.Read(rcvBuffer, 0, rcvBuffer.Length)) == 0) {}
                    // client ack
                    Console.WriteLine("CLI: ack");
                    ns.Write(Convert.FromHexString(serverCredResponse));
                    Console.WriteLine("SRV: Credentials");
                
                    while ((bytesRcvd = ns.Read(rcvBuffer, 0, rcvBuffer.Length)) <= 12) {}
                    // client login
                    Console.WriteLine("CLI: Login data");
                    DumpLoginData(rcvBuffer);
                    
                    ns.Write(Convert.FromHexString(startDataResponse_1));
                    Console.WriteLine("SRV: Initial data 1");
                    Thread.Sleep(50);
                    ns.Write(clientData.ToByteArray());
                    Console.WriteLine("SRV: Initial data 2");
                    Thread.Sleep(50);
                    ns.Write(Convert.FromHexString(transmissionEndPacket));
                    Console.WriteLine("SRV: transmission end");
                
                    var counter = 0;
                
                    while (counter < 20)
                    {
                        while ((bytesRcvd = ns.Read(rcvBuffer, 0, rcvBuffer.Length)) == 0)
                        {
                        }
                    
                        counter++;
                    }
                
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Closing connection...");
                    Console.ForegroundColor = ConsoleColor.White;
                    ns.Close();
                    client.Close();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    ns.Close();
                }
            }
        }

        private static void DumpLoginData(byte[] rcvBuffer)
        {
            var clientLoginDataFile = File.Open("C:\\source\\client_login_dump", FileMode.Append);
            var first2 = rcvBuffer[..2];
            var next3 = rcvBuffer[2..5];
            var next012c01 = rcvBuffer[5..8];
            var next3_2 = rcvBuffer[8..11];
            var next_7 = rcvBuffer[11..18];
            var loginEnd = 18;

            for (; loginEnd < rcvBuffer.Length; loginEnd++)
            {
                if (rcvBuffer[loginEnd] == 0)
                {
                    break;
                }
            }

            var login = rcvBuffer[18..loginEnd];
            var passwordEnd = loginEnd + 1;

            for (; passwordEnd < rcvBuffer.Length; passwordEnd++)
            {
                if (rcvBuffer[passwordEnd] == 0)
                {
                    break;
                }
            }
            var password = rcvBuffer[(loginEnd + 1)..passwordEnd];
                    
            //clientLoginDataFile.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(rcvBuffer[..bytesRcvd]) + "\t" + Encoding.GetEncoding("windows-1251").GetString(rcvBuffer[..bytesRcvd]) + "\n"));
                    
            clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + Convert.ToHexString(login) + "\t" + "Password: " + Convert.ToHexString(password) + "\n"));

            var loginDecode = new char[login.Length];
            login[0] -= 2;

            for (var i = 0; i < login.Length; i++)
            {
                loginDecode[i] = (char) (login[i] / 4  - 1 + 'A');
            }
            clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + new string(loginDecode) + "\n"));
                    
            clientLoginDataFile.Close();
        }
    }
}