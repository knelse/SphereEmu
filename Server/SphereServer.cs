using System.Net.Sockets;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
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

	public override void _Ready()
	{
		// Headless spawn-slot bake instantiates MainServer for terrain + spawners only.
		if (MonsterSpawnSlotHeadlessBake.IsActive || AlchemyMaterialSpawnSlotHeadlessBake.IsActive)
		{
			ServerNode = this;
			return;
		}

		SphLogger.Initialize(ServerConfig.AppConfig.LogPath);
		SphLogger.Info("Starting SphServer...");

		InitializeCollections();
		SetupTcpServer();
		ServerNode = this;
		AddChild(new MonsterSpawnerActivationManagerNode());

		connectionHandler = new ConnectionHandler(ClientScene, this);

		SphLogger.Info("Server up, waiting for connections...");
	}

	public override void _Process(double delta)
	{
		if (MonsterSpawnSlotHeadlessBake.IsActive
			|| AlchemyMaterialSpawnSlotHeadlessBake.IsActive
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
