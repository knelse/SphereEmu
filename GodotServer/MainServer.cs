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
		public static int currentId = 54678;
		public const int CLIENT_OBJECT_VISIBILITY_DISTANCE = 100;
		public static MainServer MainServerNode = null!;
		public static readonly RandomNumberGenerator Rng = new();
		public static readonly Dictionary<string, SortedSet<int>> GameObjectDataOld = new();
		public static readonly Dictionary<int, GameObjectData> GameObjectDataDb = new();
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
			LoadGameObjectData();
			
			const int port = 25860;
			Rng.Randomize();

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

			if (!ClanCollection.Exists(x => x.Id == Clan.DefaultClan.Id))
			{
				ClanCollection.Insert(Clan.DefaultClan.Id,Clan.DefaultClan);
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

		private void LoadGameObjectData()
		{
			var objectFiles = System.IO.Directory.EnumerateFiles(gameDataPath, "group_*").ToList();

			foreach (var objFile in objectFiles)
			{
				var fileName = System.IO.Path.GetFileNameWithoutExtension(objFile);
				var firstUnderscore = fileName.Find('_');
				var objKindName = fileName.Substring(firstUnderscore + 1);
				var contents = System.IO.File.ReadAllLines(objFile);

				foreach (var entry in contents)
				{
					var entrySplit = entry.Split(TabCharacter, StringSplitOptions.None);
					Enum.TryParse<KarmaTypes>(entrySplit[8], out var minKarma);
					Enum.TryParse<KarmaTypes>(entrySplit[9], out var maxKarma);

					if (string.IsNullOrWhiteSpace(entrySplit[0]))
					{
						// TODO: later gator
						continue;
					}
					var gameId = int.Parse(entrySplit[0]);

					if (gameId is >= 4740 and <= 4749 or 4302 or 4304 or 4306 or 4308 or 4310 or 4312 or 4314 
						or 4316 or 4318 or 4320 or 4450 or 4192 or 4193 or 4194 or 4199 or 4186 or 4187 or >= 4242 and <= 4249)
					{
						// event armor
						continue;
					}
					var range = int.Parse(entrySplit[38]);
					var duration = int.Parse(entrySplit[42]);
					var objKind = GameObjectDataHelper.GetKindBySphereName(objKindName);
					var tier = objKind == GameObjectKind.Monster || string.IsNullOrWhiteSpace(entrySplit[49]) ||
							   entrySplit[49].Length < 4
						? -1
						: int.Parse(entrySplit[49].Substring(2, 2)) + 1;
					var gameObj = new GameObjectData
					{
						ObjectKind = objKind,
						GameId = gameId,
						ObjectType = GameObjectDataHelper.GetTypeBySphereName(entrySplit[1]),
						ModelNameGround = entrySplit[2],
						ModelNameInventory = entrySplit[3],
						HpCost = int.Parse(entrySplit[4]),
						MpCost = int.Parse(entrySplit[5]),
						TitleMinusOne = int.Parse(entrySplit[6]),
						DegreeMinusOne = int.Parse(entrySplit[7]),
						MinKarmaLevel = minKarma,
						MaxKarmaLevel = maxKarma,
						StrengthReq = int.Parse(entrySplit[10]),
						AgilityReq = int.Parse(entrySplit[11]),
						AccuracyReq = int.Parse(entrySplit[12]),
						EnduranceReq = int.Parse(entrySplit[13]),
						EarthReq = int.Parse(entrySplit[14]),
						AirReq = int.Parse(entrySplit[15]),
						WaterReq = int.Parse(entrySplit[16]),
						FireReq = int.Parse(entrySplit[17]),
						PAtkNegative = int.Parse(entrySplit[18]),
						MAtkNegativeOrHeal = int.Parse(entrySplit[19]),
						MPHeal = int.Parse(entrySplit[20]),
						t1 = int.Parse(entrySplit[21]),
						MaxHpUp = int.Parse(entrySplit[22]),
						MaxMpUp = int.Parse(entrySplit[23]),
						PAtkUpNegative = int.Parse(entrySplit[24]),
						PDefUp = int.Parse(entrySplit[25]),
						MDefUp = int.Parse(entrySplit[26]),
						StrengthUp = int.Parse(entrySplit[27]),
						AgilityUp = int.Parse(entrySplit[28]),
						AccuracyUp = int.Parse(entrySplit[29]),
						EnduranceUp = int.Parse(entrySplit[30]),
						EarthUp = int.Parse(entrySplit[31]),
						AirUp = int.Parse(entrySplit[32]),
						WaterUp = int.Parse(entrySplit[33]),
						FireUp = int.Parse(entrySplit[34]),
						MAtkUpNegative = int.Parse(entrySplit[35]),
						Weight = int.Parse(entrySplit[36]),
						Durability = int.Parse(entrySplit[37]),
						_range = range,
						UseTime = int.Parse(entrySplit[39]),
						VendorCost = int.Parse(entrySplit[40]),
						MutatorId = int.Parse(entrySplit[41]),
						_duration = duration,
						ReuseDelayHours = int.Parse(entrySplit[43]),
						t2 = int.Parse(entrySplit[44]),
						t3 = int.Parse(entrySplit[45]),
						t4 = int.Parse(entrySplit[46]),
						t5 = int.Parse(entrySplit[47]),
						t6 = entrySplit[48],
						t7 = entrySplit[49],
						Tier = tier,
						Range = range > 100 ? range % 100 : range,
						Radius = range > 100 ? range / 100 : 0,
						Duration = duration > 100 ? (duration - 100) * 5 : duration * 300
					};

					if (GameObjectDataDb.ContainsKey(gameId))
					{
						// 4251: special case, no longer an "old" robe, now it's an event amulet
						if (gameId == 4251 && GameObjectDataDb[gameId].ObjectType == GameObjectType.Robe)
						{
							GameObjectDataDb.Remove(gameId);
						}
						else
						{
							Console.WriteLine($"Duplicate object: {gameObj.ToDebugString()}");
							continue;
						}
					}
					GameObjectDataDb.Add(gameId, gameObj);
				}
			}
		}
	}
}
