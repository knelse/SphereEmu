using System.Net.Sockets;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.World;
using SphServer.Server.Config;
using SphServer.Server.Handlers;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;

namespace SphServer.Server;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class SphereServer : Node
{
	private static TcpServer tcpServer = null!;

	private static readonly PackedScene ClientScene =
		(PackedScene)ResourceLoader.Load("res://Godot/Scenes/Client.tscn");

	public static SphereServer ServerNode = null!;
	private ConnectionHandler connectionHandler = null!;
	private WorldChunkStreamer? worldChunkStreamer;
	private TerrainGroundStreamer? terrainGroundStreamer;

	public WorldChunkStreamer? WorldChunks => worldChunkStreamer;
	public TerrainGroundStreamer? TerrainGround => terrainGroundStreamer;

	public override void _Ready()
	{
		// Headless tools instantiate MainServer for terrain + placements only.
		if (MonsterSpawnSlotHeadlessBake.IsActive
			|| AlchemyMaterialSpawnSlotHeadlessBake.IsActive
			|| WorldChunkSplitHeadless.IsActive
			|| TerrainGroundPackHeadless.IsActive)
		{
			ServerNode = this;
			return;
		}

		SphLogger.Initialize(ServerConfig.AppConfig.LogPath);
		SphPacketLogger.Initialize();
		SphLogger.Info("Starting SphServer...");

		BalanceConfig.PreloadAll();
		InitializeCollections();

		SetupTcpServer();
		ServerNode = this;

		worldChunkStreamer = GetNodeOrNull<WorldChunkStreamer>("WorldChunkStreamer");
		if (worldChunkStreamer is null)
		{
			worldChunkStreamer = new WorldChunkStreamer { Name = "WorldChunkStreamer" };
			AddChild(worldChunkStreamer);
		}

		terrainGroundStreamer = FindTerrainGroundStreamer();
		if (terrainGroundStreamer is null)
		{
			terrainGroundStreamer = new TerrainGroundStreamer { Name = "TerrainGroundStreamer" };
			AddChild(terrainGroundStreamer);
		}

		AddChild(new MonsterSpawnerActivationManagerNode());

		connectionHandler = new ConnectionHandler(ClientScene, this);

		SphLogger.Info("Server up, waiting for connections...");
	}

	public override void _Process(double delta)
	{
		if (MonsterSpawnSlotHeadlessBake.IsActive
			|| AlchemyMaterialSpawnSlotHeadlessBake.IsActive
			|| WorldChunkSplitHeadless.IsActive
			|| TerrainGroundPackHeadless.IsActive
			|| tcpServer is null)
		{
			return;
		}

		if (!tcpServer.IsConnectionAvailable())
		{
			return;
		}

		var streamPeer = tcpServer.TakeConnection();

		connectionHandler.Handle(streamPeer);
	}

	private TerrainGroundStreamer? FindTerrainGroundStreamer()
	{
		if (GetNodeOrNull<TerrainGroundStreamer>("TerrainGroundStreamer") is { } direct)
		{
			return direct;
		}

		foreach (var node in FindChildren("*", recursive: true))
		{
			if (node is TerrainGroundStreamer streamer)
			{
				return streamer;
			}
		}

		return null;
	}

	private static void InitializeCollections()
	{
		DbConnection.Initialize(ServerConfig.AppConfig);
	}

	private static void SetupTcpServer()
	{
		var port = ServerConfig.AppConfig.Port;

		tcpServer = new TcpServer();
		BitStreamExtensions.RegisterBsonMapperForBit();

		try
		{
			tcpServer.Listen(port);
			SphLogger.Info($"TCP server listening on port {port}");
		}
		catch (SocketException se)
		{
			SphLogger.Error($"Failed to start TCP server on port {port}", se);
		}
	}
}
