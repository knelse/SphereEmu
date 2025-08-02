using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Providers;
using SphServer.Server.Handlers;

#pragma warning disable CS4014

namespace SphServer;

public partial class SphereServer : Node
{
    public const int CLIENT_OBJECT_VISIBILITY_DISTANCE = 100;

    private static uint worldObjectIndex = 0x1000;
    private static int playerCount;
    public static Encoding? Win1251;
    private static TcpServer tcpServer = null!;
    private static readonly PackedScene ClientScene = (PackedScene) ResourceLoader.Load("res://Client.tscn");
    public static SphereServer ServerNode = null!;
    public static readonly Random Rng = new (Guid.NewGuid().GetHashCode());
    private ConnectionHandler connectionHandler = null!;
    public static readonly Dictionary<int, SphGameObject> SphGameObjectDb = SphObjectDb.GameObjectDataDb;
    public static AppConfig AppConfig = null!;

    public override void _Ready ()
    {
        AppConfig = ServerConfig.Get();
        SphLogger.Initialize(AppConfig.LogPath);
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

    public static ushort GetNewWorldObjectIndex ()
    {
        if (worldObjectIndex > 65535)
        {
            throw new ArgumentException("Reached max number of connections");
        }

        return (ushort) Interlocked.Increment(ref worldObjectIndex);
    }

    private static void InitializeCollections ()
    {
        DbConnectionProvider.Initialize(AppConfig);
    }

    private static void SetupTcpServer ()
    {
        var port = AppConfig.Port;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Win1251 = Encoding.GetEncoding(1251);
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