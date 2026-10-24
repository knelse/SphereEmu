using Godot;
using SphereHelpers.Extensions;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.World;
using SphServer.Server.Config;
using SphServer.Server.Debug;
using SphServer.Server.Login.Secure;
using SphServer.Server.Handlers;
using SphServer.Server.Simulation;
using SphServer.Server.Simulation.Motion;
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
	private WorldObjectCollisionStreamer? objectCollisionStreamer;
	private MotionWorldStream? motionStream;
	private readonly WorldSimulation worldSimulation = new WorldSimulation ();

	public WorldChunkStreamer? WorldChunks => worldChunkStreamer;
	public TerrainGroundStreamer? TerrainGround => terrainGroundStreamer;
	public WorldObjectCollisionStreamer? ObjectCollision => objectCollisionStreamer;
	public MotionWorldStream? Motion => motionStream;
	public WorldSimulation Simulation => worldSimulation;

	public override void _Ready ()
	{
		// Ticks stay on real time
		Engine.PhysicsJitterFix = 0.0;
		Engine.MaxPhysicsStepsPerFrame = 3;

		// Headless bakes stop after terrain and placements
		if (MonsterSpawnSlotHeadlessBake.IsActive
			|| AlchemyMaterialSpawnSlotHeadlessBake.IsActive
			|| WorldChunkSplitHeadless.IsActive
			|| TerrainGroundPackHeadless.IsActive
			|| ObjectCollisionPackHeadless.IsActive
			|| ModelBoundsPackHeadless.IsActive)
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

		objectCollisionStreamer = FindObjectCollisionStreamer ();
		if (objectCollisionStreamer is null)
		{
			objectCollisionStreamer = new WorldObjectCollisionStreamer { Name = "WorldObjectCollisionStreamer" };
			AddChild (objectCollisionStreamer);
		}

		motionStream = new MotionWorldStream { Name = "MotionWorldStream" };
		AddChild (motionStream);

		if (terrainGroundStreamer is not null
			&& objectCollisionStreamer is not null
			&& terrainGroundStreamer.LoadRadiusMeters > 0f)
		{
			objectCollisionStreamer.LoadRadiusMeters = terrainGroundStreamer.LoadRadiusMeters;
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
		// Godot already repeats _PhysicsProcess up to max_physics_steps_per_frame
		worldSimulation.Step ();
		SendWorldDatagrams ();
		// LiteDB stays on this thread, after the tick
		worldSimulation.DrainWriter ();
	}

	private void SendWorldDatagrams ()
	{
		foreach (var client in ActiveClients.GetAll ().Values)
		{
			if (!PlayerMotion.IsInGame (client))
			{
				continue;
			}

			if (!worldSimulation.TryGetWorldDatagram (client.EntityID, out var datagram))
			{
				continue;
			}

			// Snapshot plaintext starts with channel 2
			var wire = new byte[1 + datagram.Length];
			wire[0] = WorldDatagram.Channel;
			datagram.CopyTo (wire, 1);
			client.SendSessionPacket (wire);
		}
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

	private WorldObjectCollisionStreamer? FindObjectCollisionStreamer ()
	{
		if (GetNodeOrNull<WorldObjectCollisionStreamer> ("WorldObjectCollisionStreamer") is { } direct)
		{
			return direct;
		}

		foreach (var node in FindChildren ("*", recursive: true))
		{
			if (node is WorldObjectCollisionStreamer streamer)
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
		var clients = ActiveClients.GetAll ().Values.ToList ();
		foreach (var client in clients)
		{
			client.SaveCharacter ();
		}

		DbConnection.Close ();
		SphLogger.Info ("Saved connected characters and closed the database");

		var dropped = 0;
		foreach (var client in clients)
		{
			if (client.IsAdminDebugDummy)
			{
				continue;
			}

			SecureLoginServer.Active?.DropClient (client);
			dropped++;
		}

		SphLogger.Info ($"Dropped {dropped} session(s)");
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
