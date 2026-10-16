using Godot;
using SphereHelpers.Extensions;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.World;
using SphServer.Server.Config;
using SphServer.Server.Debug;
using SphServer.Server.Login.Secure;
using SphServer.Server.Handlers;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Server;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class SphereServer : Node
{
	private static readonly PackedScene ClientScene =
		(PackedScene) ResourceLoader.Load ("res://Godot/Scenes/Client.tscn");

	public static SphereServer ServerNode = null!;
	private ConnectionHandler connectionHandler = null!;
	private SecureLoginServer? secureLogin;
	private WorldChunkStreamer? worldChunkStreamer;
	private TerrainGroundStreamer? terrainGroundStreamer;

	public WorldChunkStreamer? WorldChunks => worldChunkStreamer;
	public TerrainGroundStreamer? TerrainGround => terrainGroundStreamer;

	public override void _Ready ()
	{
		// Headless bakes stop after terrain and placements
		if (MonsterSpawnSlotHeadlessBake.IsActive
			|| AlchemyMaterialSpawnSlotHeadlessBake.IsActive
			|| WorldChunkSplitHeadless.IsActive
			|| TerrainGroundPackHeadless.IsActive)
		{
			ServerNode = this;
			return;
		}

		SphLogger.Initialize (ServerConfig.AppConfig.LogPath);
		SphPacketLogger.Initialize ();
		SphLogger.Info ("Starting SphServer...");

		BalanceConfig.PreloadAll ();
		InitializeCollections ();

		BitStreamExtensions.RegisterBsonMapperForBit ();
		ServerNode = this;

		worldChunkStreamer = GetNodeOrNull<WorldChunkStreamer> ("WorldChunkStreamer");
		if (worldChunkStreamer is null)
		{
			worldChunkStreamer = new WorldChunkStreamer { Name = "WorldChunkStreamer" };
			AddChild (worldChunkStreamer);
		}

		terrainGroundStreamer = FindTerrainGroundStreamer ();
		if (terrainGroundStreamer is null)
		{
			terrainGroundStreamer = new TerrainGroundStreamer { Name = "TerrainGroundStreamer" };
			AddChild (terrainGroundStreamer);
		}

		AddChild (new MonsterSpawnerActivationManagerNode ());

		connectionHandler = new ConnectionHandler (ClientScene, this);
		StartSecureLogin ();

		// TEMP: admin UI dummy — delete AdminDebugDummyClient.cs + this call to remove.
		AdminDebugDummyClient.TrySpawn (this, ClientScene);

		SphLogger.Info ("Server up, waiting for connections...");
	}

	public override void _PhysicsProcess (double delta)
	{
		if (MonsterSpawnSlotHeadlessBake.IsActive
			|| AlchemyMaterialSpawnSlotHeadlessBake.IsActive
			|| WorldChunkSplitHeadless.IsActive
			|| TerrainGroundPackHeadless.IsActive)
		{
			return;
		}

		secureLogin?.Poll ();
	}

	private TerrainGroundStreamer? FindTerrainGroundStreamer ()
	{
		if (GetNodeOrNull<TerrainGroundStreamer> ("TerrainGroundStreamer") is { } direct)
		{
			return direct;
		}

		foreach (var node in FindChildren ("*", recursive: true))
		{
			if (node is TerrainGroundStreamer streamer)
			{
				return streamer;
			}
		}

		return null;
	}

	private static void InitializeCollections ()
	{
		DbConnection.Initialize (ServerConfig.AppConfig);
	}

	public override void _ExitTree ()
	{
		PersistConnectedClientsAndCloseDb ();
		base._ExitTree ();
	}

	public override void _Notification (int what)
	{
		if (what == NotificationWMCloseRequest || what == NotificationCrash)
		{
			PersistConnectedClientsAndCloseDb ();
		}

		base._Notification (what);
	}

	private static bool databaseClosed;

	private static void PersistConnectedClientsAndCloseDb ()
	{
		if (databaseClosed || DbConnection.Db is null)
		{
			databaseClosed = true;
			return;
		}

		databaseClosed = true;
		foreach (var client in ActiveClients.GetAll ().Values)
		{
			client.SaveCharacter ();
		}

		DbConnection.Close ();
		SphLogger.Info ("Saved connected characters and closed the database");
	}

	private void StartSecureLogin ()
	{
		var gamePort = ServerConfig.AppConfig.Port;
		var udpPort = ServerConfig.AppConfig.UdpPort;
		if (udpPort == 0 && gamePort > 65533)
		{
			SphLogger.Error ($"Game port {gamePort} leaves no room for the UDP port");
			return;
		}

		if (udpPort == 0)
		{
			udpPort = (ushort) (gamePort + 2);
		}

		secureLogin = new SecureLoginServer (connectionHandler);
		secureLogin.Start (udpPort);
	}
}
