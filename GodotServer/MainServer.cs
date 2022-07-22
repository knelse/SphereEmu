using System;
using Godot;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using SphServer.Helpers;
#pragma warning disable CS4014

namespace SphServer
{
    public class MainServer : Node
    {
        private static int playerIndex = 0x4f6f;
        private static int playerCount;
        public static bool LiveServerCoords = false;
        public static Encoding? Win1251;
        private static DateTime startTime = DateTime.Now;
        private static bool sendEntPing = true;
        private static TCP_Server tcpServer;
        [Export] public PackedScene Client = null!;

        private static ushort getNewPlayerIndex()
        {
            if (playerIndex > 65535)
            {
                throw new ArgumentException("Reached max number of connections");
            }

            // return (ushort) Interlocked.Increment(ref playerIndex);
            return (ushort)playerIndex;
        }
        
        public override void _Ready()
        {
            const int port = 25860;
            RNGHelper.SetSeedFromSystemTime();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Win1251 = Encoding.GetEncoding(1251);
            tcpServer = new TCP_Server();

            try
            {
                tcpServer.Listen(port);
            }
            catch (SocketException se)
            {
                Console.WriteLine(se.Message);
            }

            Console.WriteLine("Server up, waiting for connections...");
        }

        public override void _Process(float delta)
        {
            if (tcpServer.IsConnectionAvailable())
            {
                var streamPeer = tcpServer.TakeConnection();
                streamPeer.SetNoDelay(true);
                var client = (Client) Client.Instance();
                client.streamPeer = streamPeer;
                client.currentPlayerIndex = getNewPlayerIndex();
                AddChild(client);
            }
        }
    }
}
