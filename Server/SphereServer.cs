using System.Net.Sockets;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Providers;
using SphServer.Server.Config;
using SphServer.Server.Handlers;

namespace SphServer.Server;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class SphereServer : Node
{
    private static int playerCount;
    private static TcpServer tcpServer = null!;
    private static readonly PackedScene ClientScene = (PackedScene) ResourceLoader.Load("res://Client.tscn");
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
        DbConnectionProvider.Initialize(ServerConfig.AppConfig);
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