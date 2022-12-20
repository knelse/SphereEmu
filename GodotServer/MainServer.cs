using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Godot;
using System.Net.Sockets;
using System.Text;
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
		public const string gameDataPath = "c:\\source\\_sphFilesDecode\\params\\";
		public static readonly ConcurrentDictionary<ushort, Client> ActiveClients = new();
		public static readonly LiteDatabase Db = new (@"Filename=C:\_sphereStuff\sph.db;Connection=shared;");
		public static readonly LiteDatabase LiveServerObjectPacketDb = new (@"Filename=C:\_sphereStuff\sph_items.db;Connection=shared;");
		public static readonly ILiteCollection<Clan> ClanCollection = Db.GetCollection<Clan>("Clans");
		public static readonly ILiteCollection<Player> PlayerCollection = Db.GetCollection<Player>("Players");
		public static readonly ILiteCollection<Character> CharacterCollection = Db.GetCollection<Character>("Characters");
		public static readonly ILiteCollection<Item> ItemCollection = Db.GetCollection<Item>("Items");
		public static readonly ILiteCollection<ItemContainer> ItemContainerCollection = Db.GetCollection<ItemContainer>("ItemContainers");
		public static readonly ILiteCollection<Mob> MonsterCollection = Db.GetCollection<Mob>("Monsters");
		public static readonly ILiteCollection<Vendor> VendorCollection = Db.GetCollection<Vendor>("Vendors");
		public static readonly ILiteCollection<SphGameObject> GameObjectCollection =
			Db.GetCollection<SphGameObject>("GameObjects");
		public static readonly ILiteCollection<ObjectPacket> LiveServerObjectPacketCollection =
			LiveServerObjectPacketDb.GetCollection<ObjectPacket>("ObjectPackets");
		public static readonly ConcurrentDictionary<ulong, Node> ActiveNodes = new ();

		public static readonly Dictionary<int, SphGameObject> SphGameObjectDb = SphObjectDb.GameObjectDataDb;

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

			ItemCollection.DeleteAll();
			MonsterCollection.DeleteAll();
			ItemContainerCollection.DeleteAll();
			VendorCollection.DeleteAll();

			var time = DateTime.Now;
			if (GameObjectCollection.Count() == 0)
			{
				// load db
				Console.WriteLine("Filling object collection");
				foreach (var dbEntry in SphGameObjectDb)
				{
					GameObjectCollection.Insert(dbEntry.Key, dbEntry.Value);
				}

				Console.WriteLine($"Time elapsed: {(DateTime.Now - time).TotalMilliseconds} ms");
			}

			if (!ClanCollection.Exists(x => x.Id == Clan.DefaultClan.Id))
			{
				ClanCollection.Insert(Clan.DefaultClan.Id,Clan.DefaultClan);
			}
			
			// special case for basic sword, to be removed later
			if (ItemCollection.FindById(2825) is null)
			{
				ItemCollection.Insert(2825,  Item.CreateFromGameObject(GameObjectCollection.FindById(1)));
			}

			ItemCollection.EnsureIndex(x => x.GameObjectDbId);
			ItemCollection.EnsureIndex(x => x.GameId);
			GameObjectCollection.EnsureIndex(x => x.GameId);
			GameObjectCollection.EnsureIndex(x => x.GameObjectDbId);
			GameObjectCollection.EnsureIndex(x => x.ObjectType);
			GameObjectCollection.EnsureIndex(x => x.ObjectKind);
			PlayerCollection.EnsureIndex(x => x.Login);
			LiveServerObjectPacketCollection.EnsureIndex(x => x.GameId);

			if (VendorCollection.FindById(2837) is null)
			{
				// until we stop clearing item collection, we need to create it to get proper itemid
				// healing powder 1
				var hpPowder = Item.CreateFromGameObject(GameObjectCollection.FindById(601));
				hpPowder.ItemCount = 1000;
				hpPowder.Id = ItemCollection.Insert(hpPowder);
				
				var hpPowder1 = Item.CreateFromGameObject(GameObjectCollection.FindById(875));
				hpPowder1.ItemCount = 1000;
				hpPowder1.Id = ItemCollection.Insert(hpPowder1);

				var go = SphGameObject.CreateFromGameObject(GameObjectCollection.FindById(4171));
				// go.Suffix = ItemSuffix.Precision;

				var test = Item.CreateFromGameObject(go);
				test.Id = ItemCollection.Insert(test);
				
				var newPlayerDungeonVendor = new Vendor
				{
					Id = 2837,
					ItemIdsOnSale = new List<int>
					{
						hpPowder.Id,
						test.Id
					},
					Name = "Test",
					FamilyName = "Vendor"
				};

				VendorCollection.Insert(newPlayerDungeonVendor);
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
			client.LocalId = getNewPlayerIndex();
			ActiveClients[client.LocalId] = client;
			ActiveNodes[client.GetInstanceId()] = client;
			AddChild(client);
		}
	}
}
