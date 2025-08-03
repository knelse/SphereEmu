using System;
using System.Collections.Generic;
using LiteDB;
using SphServer.DataModels;
using SphServer.Server;
using SphServer.Server.Config;

namespace SphServer.Providers;

public static class DbConnectionProvider
{
    public static LiteDatabase Db { get; private set; } = null!;

    public static ILiteCollection<Clan> ClanCollection { get; private set; } = null!;
    public static ILiteCollection<Player> PlayerCollection { get; private set; } = null!;
    public static ILiteCollection<Character> CharacterCollection { get; private set; } = null!;
    public static ILiteCollection<Item> ItemCollection { get; private set; } = null!;
    public static ILiteCollection<ItemContainer> ItemContainerCollection { get; private set; } = null!;
    public static ILiteCollection<Mob> MonsterCollection { get; private set; } = null!;
    public static ILiteCollection<Vendor> VendorCollection { get; private set; } = null!;
    public static ILiteCollection<SphGameObject> GameObjectCollection { get; private set; } = null!;

    public static void Initialize (AppConfig config)
    {
        SphLogger.Info("Initializing database connection...");
        Db = new LiteDatabase(config.LiteDbConnectionString);

        SphLogger.Info("Setting up database collections...");
        ClanCollection = Db.GetCollection<Clan>("Clans");
        PlayerCollection = Db.GetCollection<Player>("Players");
        CharacterCollection = Db.GetCollection<Character>("Characters");
        ItemCollection = Db.GetCollection<Item>("Items");
        ItemContainerCollection = Db.GetCollection<ItemContainer>("ItemContainers");
        MonsterCollection = Db.GetCollection<Mob>("Monsters");
        VendorCollection = Db.GetCollection<Vendor>("Vendors");
        GameObjectCollection = Db.GetCollection<SphGameObject>("GameObjects");

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
        ItemCollection.DeleteAll();
        MonsterCollection.DeleteAll();
        ItemContainerCollection.DeleteAll();
        VendorCollection.DeleteAll();

        var time = DateTime.Now;
        if (GameObjectCollection.Count() == 0)
        {
            SphLogger.Info("Filling object collection");
            foreach (var dbEntry in SphereServer.SphGameObjectDb)
            {
                GameObjectCollection.Insert(dbEntry.Key, dbEntry.Value);
            }

            SphLogger.Info($"Object collection filled. Time elapsed: {(DateTime.Now - time).TotalMilliseconds} ms");
        }

        if (!ClanCollection.Exists(x => x.Id == Clan.DefaultClan.Id))
        {
            ClanCollection.Insert(Clan.DefaultClan.Id, Clan.DefaultClan);
        }

        if (ItemCollection.FindById(2825) is null)
        {
            ItemCollection.Insert(2825, Item.CreateFromGameObject(GameObjectCollection.FindById(1)));
        }
    }

    private static void CreateIndexes ()
    {
        ItemCollection.EnsureIndex(x => x.GameObjectDbId);
        ItemCollection.EnsureIndex(x => x.GameId);
        GameObjectCollection.EnsureIndex(x => x.GameId);
        GameObjectCollection.EnsureIndex(x => x.GameObjectDbId);
        GameObjectCollection.EnsureIndex(x => x.GameObjectType);
        GameObjectCollection.EnsureIndex(x => x.ObjectKind);
        PlayerCollection.EnsureIndex(x => x.Login);
    }
}