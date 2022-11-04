using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Godot;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SphServer.DataModels;
using SphServer.Helpers;
#pragma warning disable CS4014

namespace SphServer
{
    public class MainServer : Node
    {
        private static readonly int playerIndex = 0x4F6F;
        private static int playerCount;
        public static bool LiveServerCoords = false;
        public static Encoding? Win1251;
        private static DateTime startTime = DateTime.Now;
        private static bool sendEntPing = true;
        private static TCP_Server tcpServer;
        private static readonly PackedScene ClientScene = (PackedScene) ResourceLoader.Load("res://Client.tscn");
        public static readonly ConcurrentDictionary<int, IGameEntity> GameObjects = new ();
        public static int currentId = 100;
        public const int CLIENT_OBJECT_VISIBILITY_DISTANCE = 100;
        public static MainServer MainServerNode = null!;
        public static readonly RandomNumberGenerator Rng = new();
        public static Dictionary<string, SortedSet<int>> ItemTypeNameToIdMapping = new();

        private static ushort getNewPlayerIndex()
        {
            if (playerIndex > 65535)
            {
                throw new ArgumentException("Reached max number of connections");
            }

            // return (ushort) Interlocked.Increment(ref playerIndex);
            return (ushort)playerIndex;
        }

        public static ushort AddToGameObjects(IGameEntity ent)
        {
            while (!GameObjects.TryAdd(Interlocked.Increment(ref currentId), ent));
            ent.ID = (ushort) currentId;
            ShowDebugInfo(ent);

            return (ushort) currentId;
        }

        public static bool TryAddToGameObjects(int id, IGameEntity ent)
        {
            ShowDebugInfo(ent);

            return GameObjects.TryAdd(id, ent);
        }

        private static void ShowDebugInfo(IGameEntity ent)
        {
            GD.Print($"SRV: NEW ENT ID: {ent.ID:####0}\tType: {ent.TypeID:####0}\tX: {(int) ent.X:####0}\tY: {(int) ent.Y:####0}\tZ: {(int) ent.Z:####0}\t" +
                     $"T: {ent.Turn:##0.#####}\tLevel: {ent.TitleLevelMinusOne:##0}\tHP: {ent.CurrentHP:####0}/{ent.MaxHP}");
        }
        
        public override void _Ready()
        {
            // TODO: item filtering by subtype (e.g. crossbows should not contain letters, crystals and helmets -- thanks game)
            var pref = System.IO.File.ReadAllLines("c:\\source\\_sphFilesDecode\\params\\grouppref.cfg").ToList();
            var axes = System.IO.File.ReadAllLines("c:\\source\\_sphFilesDecode\\params\\group_axes.cfg").ToList();
            axes.ForEach(val =>
            {
                pref.Add($"axes\t{val}");
            });

            foreach (var str in pref)
            {
                var split = str.Split(new [] {'\t'}, StringSplitOptions.RemoveEmptyEntries);
                if (!ItemTypeNameToIdMapping.ContainsKey(split[0]))
                {
                    ItemTypeNameToIdMapping.Add(split[0], new SortedSet<int>());
                }
                try
                {
                    ItemTypeNameToIdMapping[split[0]].Add(int.Parse(split[1]));
                }
                catch
                {
                    // ignored
                }
            }
            
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
            MainServerNode = this;
        }

        public override void _Process(float delta)
        {
            if (!tcpServer.IsConnectionAvailable()) return;

            var streamPeer = tcpServer.TakeConnection();
            streamPeer.SetNoDelay(true);
            var client = ClientScene.Instance<Client>();
            playerCount += 1;
            client.StreamPeer = streamPeer;
            client.ID = getNewPlayerIndex();
            AddChild(client);
        }
    }
}
