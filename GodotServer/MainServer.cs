using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Godot;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LiteDB;
using SphServer.DataModels;
using SphServer.Helpers;
#pragma warning disable CS4014

namespace SphServer
{
	// ReSharper disable once ClassNeverInstantiated.Global
	public partial class MainServer : Node
	{
		private static readonly int playerIndex = 0x4F6F;
		private static int playerCount;
		public static bool LiveServerCoords = false;
		public static Encoding? Win1251;
		private static DateTime startTime = DateTime.Now;
		private static bool sendEntPing = true;
		private static TCPServer tcpServer;
		private static readonly PackedScene ClientScene = (PackedScene) ResourceLoader.Load("res://Client.tscn");
		public static readonly ConcurrentDictionary<int, IGameEntity> GameObjects = new ();
		public static int currentId = 54678;
		public const int CLIENT_OBJECT_VISIBILITY_DISTANCE = 100;
		public static MainServer MainServerNode = null!;
		public static readonly RandomNumberGenerator Rng = new();
		public static readonly Dictionary<string, SortedSet<int>> GameObjectDataOld = new();
		public static readonly Dictionary<int, SphGameObject> GameObjectDataDb = SphObjectDb.GameObjectDataDb;
		public const string gameDataPath = "c:\\source\\_sphFilesDecode\\params\\";
		private static readonly char[] TabCharacter = {'\t'};
		public static readonly LiteDatabase Db = new (@"Filename=C:\_sphereStuff\sph.db;Connection=shared;");
		public static ILiteCollection<Player> PlayerCollection => Db.GetCollection<Player>("Players");
		public static ILiteCollection<CharacterData> CharacterCollection => Db.GetCollection<CharacterData>("Characters");
		public static ILiteCollection<Clan> ClanCollection => Db.GetCollection<Clan>("Clans");

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
			ent.Id = (ushort) currentId;
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
			GD.Print($"SRV: NEW ENT ID: {ent.Id:####0}\tType: {ent.TypeID:####0}\tX: {(int) ent.X:####0}\tY: {(int) ent.Y:####0}\tZ: {(int) ent.Z:####0}\t" +
					 $"T: {ent.Turn:##0.#####}\tLevel: {ent.TitleLevelMinusOne:##0}\tHP: {ent.CurrentHP:####0}/{ent.MaxHP}");
		}
		
		public override void _Ready()
		{
			const int port = 25860;
			Rng.Randomize();

			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			Win1251 = Encoding.GetEncoding(1251);
			tcpServer = new TCPServer();

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
			AddChild(client);
		}
	}
}
