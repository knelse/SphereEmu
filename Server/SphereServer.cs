using System.Net.Sockets;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Server.Config;
using SphServer.Server.Handlers;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;
using SphServer.Sphere.Game.WorldObject.Spawner;

namespace SphServer.Server;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class SphereServer : Node
{
	private static int playerCount;
	private static TcpServer tcpServer = null!;

	private static readonly PackedScene ClientScene =
		(PackedScene) ResourceLoader.Load("res://Godot/Scenes/Client.tscn");

	public static SphereServer ServerNode = null!;
	private ConnectionHandler connectionHandler = null!;

	public override void _Ready ()
	{
		SphLogger.Initialize(ServerConfig.AppConfig.LogPath);
		SphLogger.Info("Starting SphServer...");

		InitializeCollections();
		SetupTcpServer();
		ServerNode = this;
		WorldObjectSpawner.InstantiateObjects();

		connectionHandler = new ConnectionHandler(ClientScene, this);

		SphLogger.Info("Server up, waiting for connections...");
	}

	public override void _Process (double delta)
	{
		if (!tcpServer.IsConnectionAvailable())
		{
			return;
		}

		var streamPeer = tcpServer.TakeConnection();

		connectionHandler.Handle(streamPeer);
	}

	private static void InitializeCollections ()
	{
		DbConnection.Initialize(ServerConfig.AppConfig);
	}

	private static void SetupTcpServer ()
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
