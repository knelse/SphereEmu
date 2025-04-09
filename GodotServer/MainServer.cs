using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Godot;
using LiteDB;
using Newtonsoft.Json;
using SphereHelpers.Extensions;
using SphServer.DataModels;

// ReSharper disable NotAccessedField.Local

#pragma warning disable CS4014

namespace SphServer;

// ReSharper disable once ClassNeverInstantiated.Global
public partial class MainServer : Node
{
    public const int CLIENT_OBJECT_VISIBILITY_DISTANCE = 100;
    public const string PacketDefinitionExtension = ".spd";
    public const string ExportedPartExtension = ".spdp";
    private static uint playerIndex = 0x4F6F;
    private static uint worldObjectIndex = 0x1000;

    private static int playerCount;

    // private static bool liveServerCoords = false;
    public static Encoding? Win1251;

    // private static bool sendEntPing = true;
    private static TcpServer tcpServer = null!;
    private static readonly PackedScene ClientScene = (PackedScene) ResourceLoader.Load("res://Client.tscn");
    public static MainServer MainServerNode = null!;
    public static readonly Random Rng = new (Guid.NewGuid().GetHashCode());
    public static readonly ConcurrentDictionary<ushort, Client> ActiveClients = new ();
    public static LiteDatabase Db;

    public static ILiteCollection<Clan> ClanCollection;
    public static ILiteCollection<Player> PlayerCollection;
    public static ILiteCollection<Character> CharacterCollection;
    public static ILiteCollection<Item> ItemCollection;

    public static ILiteCollection<ItemContainer> ItemContainerCollection;

    public static ILiteCollection<Mob> MonsterCollection;
    public static ILiteCollection<Vendor> VendorCollection;

    public static ILiteCollection<SphGameObject> GameObjectCollection;

    // public static readonly ILiteCollection<ObjectPacket> LiveServerObjectPacketCollection =
    //     LiveServerObjectPacketDb.GetCollection<ObjectPacket>("ObjectPackets");

    public static readonly ConcurrentDictionary<ulong, Node> ActiveNodes = new ();
    public static readonly ConcurrentDictionary<ushort, WorldObject> ActiveWorldObjects = new ();

    public static readonly Dictionary<int, SphGameObject> SphGameObjectDb = SphObjectDb.GameObjectDataDb;

    public static Dictionary<string, string> AppConfig;

    static MainServer ()
    {
    }

    private static ushort getNewPlayerIndex ()
    {
        if (playerIndex > 65535)
        {
            throw new ArgumentException("Reached max number of connections");
        }

        // return (ushort) Interlocked.Increment(ref playerIndex);
        return (ushort) playerIndex;
    }

    public static ushort GetNewWorldObjectIndex ()
    {
        if (worldObjectIndex > 65535)
        {
            throw new ArgumentException("Reached max number of connections");
        }

        return (ushort) Interlocked.Increment(ref worldObjectIndex);
    }

    public override void _Ready ()
    {
        const int port = 25860;
        using var configFile = File.OpenRead("appsettings.json");
        using var configReader = new StreamReader(configFile);
        AppConfig = JsonConvert.DeserializeObject<Dictionary<string, string>>(configReader.ReadToEnd());

        Db = new LiteDatabase(AppConfig["LiteDbConnectionString"]);
        ClanCollection = Db.GetCollection<Clan>("Clans");
        PlayerCollection = Db.GetCollection<Player>("Players");
        CharacterCollection = Db.GetCollection<Character>("Characters");
        ItemCollection = Db.GetCollection<Item>("Items");
        ItemContainerCollection =
            Db.GetCollection<ItemContainer>("ItemContainers");
        MonsterCollection = Db.GetCollection<Mob>("Monsters");
        VendorCollection = Db.GetCollection<Vendor>("Vendors");
        GameObjectCollection =
            Db.GetCollection<SphGameObject>("GameObjects");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Win1251 = Encoding.GetEncoding(1251);
        tcpServer = new TcpServer();
        BitStreamExtensions.RegisterBsonMapperForBit();

        try
        {
            tcpServer.Listen(port);
        }
        catch (SocketException se)
        {
            Console.WriteLine(se.Message);
        }

        ItemCollection.DeleteAll();
        MonsterCollection.DeleteAll();
        ItemContainerCollection.DeleteAll();
        VendorCollection.DeleteAll();

        var time = DateTime.Now;
        if (GameObjectCollection.Count() == 0)
        {
            // load db
            Console.WriteLine("Filling object collection");
            foreach (var dbEntry in SphGameObjectDb)
            {
                GameObjectCollection.Insert(dbEntry.Key, dbEntry.Value);
            }

            Console.WriteLine($"Time elapsed: {(DateTime.Now - time).TotalMilliseconds} ms");
        }

        if (!ClanCollection.Exists(x => x.Id == Clan.DefaultClan.Id))
        {
            ClanCollection.Insert(Clan.DefaultClan.Id, Clan.DefaultClan);
        }

        // special case for basic sword, to be removed later
        if (ItemCollection.FindById(2825) is null)
        {
            ItemCollection.Insert(2825, Item.CreateFromGameObject(GameObjectCollection.FindById(1)));
        }

        ItemCollection.EnsureIndex(x => x.GameObjectDbId);
        ItemCollection.EnsureIndex(x => x.GameId);
        GameObjectCollection.EnsureIndex(x => x.GameId);
        GameObjectCollection.EnsureIndex(x => x.GameObjectDbId);
        GameObjectCollection.EnsureIndex(x => x.GameObjectType);
        GameObjectCollection.EnsureIndex(x => x.ObjectKind);
        PlayerCollection.EnsureIndex(x => x.Login);
        // LiveServerObjectPacketCollection.EnsureIndex(x => x.GameId);

        // if (VendorCollection.FindById(2837) is null)
        // {
        //     // until we stop clearing item collection, we need to create it to get proper itemid
        //     // healing powder 1
        //     var hpPowder = Item.CreateFromGameObject(GameObjectCollection.FindById(601));
        //     hpPowder.ItemCount = 1000;
        //     hpPowder.Id = ItemCollection.Insert(hpPowder);
        //
        //     var newPlayerDungeonVendor = new Vendor
        //     {
        //         Id = 2837,
        //         ItemIdsOnSale = new List<int>
        //         {
        //             hpPowder.Id
        //         },
        //         Name = "Test",
        //         FamilyName = "Vendor"
        //     };
        //
        //     VendorCollection.Insert(newPlayerDungeonVendor);
        // }

        Console.WriteLine("Server up, waiting for connections...");
        MainServerNode = this;
        WorldObjectSpawner.InstantiateObjects();
    }

    public override void _Process (double delta)
    {
        if (!tcpServer.IsConnectionAvailable())
        {
            return;
        }

        var streamPeer = tcpServer.TakeConnection();
        streamPeer.SetNoDelay(true);
        var client = ClientScene.Instantiate<Client>();
        playerCount += 1;
        client.StreamPeer = streamPeer;
        client.LocalId = getNewPlayerIndex();
        ActiveClients[client.LocalId] = client;
        ActiveNodes[client.GetInstanceId()] = client;
        AddChild(client);
    }

    public static Client? GetClient (ushort localId)
    {
        return ActiveClients.ContainsKey(localId) ? ActiveClients[localId] : null;
    }
}