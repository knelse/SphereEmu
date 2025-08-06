using System;
using LiteDB;
using SphServer.Server.Config;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Sphere.Game;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Shared.Db;

public static class DbConnection
{
    public static LiteDatabase Db { get; private set; } = null!;
    public static ILiteCollection<ClanDbEntry> Clans { get; private set; } = null!;
    public static ILiteCollection<PlayerDbEntry> Players { get; private set; } = null!;
    public static ILiteCollection<CharacterDbEntry> Characters { get; private set; } = null!;
    public static ILiteCollection<ItemDbEntry> Items { get; private set; } = null!;
    public static ILiteCollection<ItemContainerDbEntry> ItemContainers { get; private set; } = null!;
    public static ILiteCollection<MonsterDbEntry> Monsters { get; private set; } = null!;
    public static ILiteCollection<NpcInteractable> NpcInteractables { get; private set; } = null!;
    public static ILiteCollection<SphGameObject> GameObjects { get; private set; } = null!;

    public static void Initialize (AppConfig config)
    {
        SphLogger.Info("Initializing database connection...");
        Db = new LiteDatabase(config.LiteDbConnectionString);

        SphLogger.Info("Setting up database collections...");
        Clans = Db.GetCollection<ClanDbEntry>("Clans");
        Players = Db.GetCollection<PlayerDbEntry>("Players");
        Characters = Db.GetCollection<CharacterDbEntry>("Characters");
        Items = Db.GetCollection<ItemDbEntry>("Items");
        ItemContainers = Db.GetCollection<ItemContainerDbEntry>("ItemContainers");
        Monsters = Db.GetCollection<MonsterDbEntry>("Monsters");
        NpcInteractables = Db.GetCollection<NpcInteractable>("NpcInteractables");
        GameObjects = Db.GetCollection<SphGameObject>("GameObjects");

        InitializeData();
        CreateIndexes();
        SphLogger.Info("Database initialization completed");
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
    }

    private static void InitializeData ()
    {
        Items.DeleteAll();
        Monsters.DeleteAll();
        // ItemContainers.DeleteAll();
        // Vendors.DeleteAll();

        var time = DateTime.Now;
        if (GameObjects.Count() == 0)
        {
            SphLogger.Info("Filling object collection");
            foreach (var dbEntry in GameObjectDb.Db)
            {
                GameObjects.Insert(dbEntry.Key, dbEntry.Value);
            }

            SphLogger.Info($"Object collection filled. Time elapsed: {(DateTime.Now - time).TotalMilliseconds} ms");
        }

        if (!Clans.Exists(x => x.Id == ClanDbEntry.DefaultClanDbEntry.Id))
        {
            Clans.Insert(ClanDbEntry.DefaultClanDbEntry.Id, ClanDbEntry.DefaultClanDbEntry);
        }

        if (Items.FindById(2825) is null)
        {
            Items.Insert(2825, ItemDbEntry.CreateFromGameObject(GameObjects.FindById(1)));
        }
    }

    private static void CreateIndexes ()
    {
        Items.EnsureIndex(x => x.GameObjectDbId);
        Items.EnsureIndex(x => x.GameId);
        GameObjects.EnsureIndex(x => x.GameId);
        GameObjects.EnsureIndex(x => x.GameObjectDbId);
        GameObjects.EnsureIndex(x => x.GameObjectType);
        GameObjects.EnsureIndex(x => x.ObjectKind);
        Players.EnsureIndex(x => x.Login);
    }
}