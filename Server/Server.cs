using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Providers;
using SphServer.Server.Handlers;

// ReSharper disable NotAccessedField.Local

#pragma warning disable CS4014

namespace SphServer;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class Server : Node
{
    public const int CLIENT_OBJECT_VISIBILITY_DISTANCE = 100;
    public const string PacketDefinitionExtension = ".spd";
    public const string ExportedPartExtension = ".spdp";

    private static uint worldObjectIndex = 0x1000;
    private static int playerCount;
    public static Encoding? Win1251;
    private static TcpServer tcpServer = null!;
    private static PackedScene? clientScene;
    private static PackedScene ClientScene => clientScene ??= (PackedScene)ResourceLoader.Load("res://Client.tscn");
    public static Server ServerNode = null!;
    public static readonly Random Rng = new(Guid.NewGuid().GetHashCode());
    public static readonly ConcurrentDictionary<ushort, Client> ActiveClients = new();
    public static readonly ConcurrentDictionary<ulong, Node> ActiveNodes = new();
    public static readonly ConcurrentDictionary<ushort, WorldObject> ActiveWorldObjects = new();

    private static Dictionary<int, SphGameObject>? sphGameObjectDb;
    public static Dictionary<int, SphGameObject> SphGameObjectDb => sphGameObjectDb ??= SphObjectDb.GameObjectDataDb;

    public static AppConfig AppConfig = null!;

    public override void _Ready()
    {
        AppConfig = AppConfigProvider.Provide();
        SphLogger.Initialize(AppConfig.LogPath);
        SphLogger.Info("Starting SphServer...");

        InitializeCollections();
        SetupTcpServer();
        ServerNode = this;
        WorldObjectSpawner.InstantiateObjects();

        SphLogger.Info("Server up, waiting for connections...");
    }

    public override void _Process(double delta)
    {
        if (!tcpServer.IsConnectionAvailable())
        {
            return;
        }

        HandleNewConnection();
    }

    public static ushort GetNewWorldObjectIndex()
    {
        if (worldObjectIndex > 65535)
        {
            throw new ArgumentException("Reached max number of connections");
        }

        return (ushort)Interlocked.Increment(ref worldObjectIndex);
    }

    public static Client? GetClient(ushort localId)
    {
        return ActiveClients.GetValueOrDefault(localId);
    }

    private static void InitializeCollections()
    {
        DbConnectionProvider.Initialize(AppConfig);
    }

    private static void SetupTcpServer()
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

    private void HandleNewConnection()
    {
        ConnectionHandler.HandleNewConnection(tcpServer, ClientScene, ActiveClients, ActiveNodes, this, ref playerCount);
    }
}