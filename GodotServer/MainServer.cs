using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Godot;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using LiteDB;
using SphServer.DataModels;
// ReSharper disable NotAccessedField.Local

#pragma warning disable CS4014

namespace SphServer
{
	// ReSharper disable once ClassNeverInstantiated.Global
	public partial class MainServer : Node
	{
		private static readonly int playerIndex = 0x4F6F;
		private static int playerCount;
		// private static bool liveServerCoords = false;
		public static Encoding? Win1251;
		// private static bool sendEntPing = true;
		private static TCPServer tcpServer = null!;
		private static readonly PackedScene ClientScene = (PackedScene) ResourceLoader.Load("res://Client.tscn");
		public const int CLIENT_OBJECT_VISIBILITY_DISTANCE = 100;
		public static MainServer MainServerNode = null!;
		public static readonly Random Rng = new(Guid.NewGuid().GetHashCode());
		public static readonly Dictionary<int, SphGameObject> GameObjectDataDb = SphObjectDb.GameObjectDataDb;
		public const string gameDataPath = "c:\\source\\_sphFilesDecode\\params\\";
		public static readonly LiteDatabase Db = new (@"Filename=C:\_sphereStuff\sph.db;Connection=shared;");
		public static readonly LiteDatabase ItemDb = new (@"Filename=C:\_sphereStuff\sph_items.db;Connection=shared;");
		public static ILiteCollection<Player> PlayerCollection => Db.GetCollection<Player>("Players");
		public static ILiteCollection<CharacterData> CharacterCollection => Db.GetCollection<CharacterData>("Characters");
		public static ILiteCollection<Clan> ClanCollection => Db.GetCollection<Clan>("Clans");
		public static readonly ConcurrentDictionary<ushort, Client> ActiveClients = new();

		public static ILiteCollection<SphGameObject> ExistingGameObjects =>
			Db.GetCollection<SphGameObject>("GameObjects");

		public static readonly ILiteCollection<ObjectPacket> ObjectPacketCollection =
			ItemDb.GetCollection<ObjectPacket>("ObjectPackets");

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

			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			Win1251 = Encoding.GetEncoding(1251);
			tcpServer = new TCPServer();
			ObjectPacketTools.RegisterBsonMapperForBit();

			try
			{
				tcpServer.Listen(port);
			}
			catch (SocketException se)
			{
				Console.WriteLine(se.Message);
			}

			if (!ClanCollection.Exists(x => x.Id == Clan.DefaultClan.Id))
			{
				ClanCollection.Insert(Clan.DefaultClan.Id,Clan.DefaultClan);
			}

			// TODO: will be storing later
			ExistingGameObjects.DeleteAll();
			ExistingGameObjects.EnsureIndex(x => x.Id);
			ExistingGameObjects.EnsureIndex(x => x.GameId);
			
			// special case for basic sword, to be removed later
			if (ExistingGameObjects.FindById(2825) is null)
			{
				ExistingGameObjects.Insert(2825, GameObjectDataDb[1]);
			}

			Console.WriteLine("Server up, waiting for connections...");
			MainServerNode = this;
		}

		public override void _Process(double delta)
		{
			if (!tcpServer.IsConnectionAvailable()) return;

			var streamPeer = tcpServer.TakeConnection();
			streamPeer.SetNoDelay(true);
			var client = ClientScene.Instantiate<Client>();
			playerCount += 1;
			client.StreamPeer = streamPeer;
			client.ID = getNewPlayerIndex();
			ActiveClients[client.ID] = client;
			AddChild(client);
		}
	}
}
