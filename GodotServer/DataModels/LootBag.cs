using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer;
using SphServer.DataModels;
using SphServer.Helpers;
using static SphServer.Helpers.BitHelper;

public class Item : IGameEntity
{
    public ushort ID { get; set; }
    public ushort Unknown { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Turn { get; set; }
    public ushort CurrentHP { get; set; }
    public ushort MaxHP { get; set; }
    public ushort TypeID { get; set; }
    public byte TitleLevelMinusOne { get; set; }
    public byte DegreeLevelMinusOne { get; set; }
}

public enum LootRatityType
{
    DEFAULT_MOB,
    NAMED_MOB,
    PLAYER
}

public class LootBag : IGameEntity
{
    public ushort ID { get; set; }
    public ushort Unknown { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Turn { get; set; }
    public ushort CurrentHP { get; set; }
    public ushort MaxHP { get; set; }
    public ushort TypeID { get; set; }
    public byte TitleLevelMinusOne { get; set; }
    public byte DegreeLevelMinusOne { get; set; }

    public Item? Item0;
    public Item? Item1;
    public Item? Item2;
    public Item? Item3;

    private static readonly PackedScene LootBagScene = (PackedScene) ResourceLoader.Load("res://LootBag.tscn");
    
    public LootBagNode ParentNode;
    private static byte itemSuffixModTest = 0x0;

    public Item? this[int index]
    {
        get
        {
            if (index is < 0 or > 3)
            {
                return null;
            }

            return index == 0 ? Item0 : index == 1 ? Item1 : index == 2 ? Item2 : Item3;
        }

        set
        {
            if (index < 0 || index > 3)
            {
                return;
            }

            if (index == 0)
            {
                Item0 = value;
            }
            else if (index == 1)
            {
                Item1 = value;
            }
            else if (index == 2)
            {
                Item2 = value;
            }
            else
            {
                Item3 = value;
            }
        }
    }

    public int Count => (Item0 is null ? 0 : 1) + (Item1 is null ? 0 : 1) + (Item2 is null ? 0 : 1) +
                        (Item3 is null ? 0 : 1);

    public static LootBag Create(double x, double y, double z, int level, int sourceTypeId,
        LootRatityType ratityType, int count = -1)
    {
        var bag = (LootBagNode) LootBagScene.Instance();
        bag.LootBag = new LootBag();
        bag.LootBag.ID = MainServer.AddToGameObjects(bag.LootBag);
        bag.LootBag.TitleLevelMinusOne = (byte) level;
        bag.LootBag.X = x;
        bag.LootBag.Y = y;
        bag.LootBag.Z = z;
        bag.LootBag.ParentNode = bag;
        // TODO: item gen logic
        var itemCount = count == -1 ? (int) (RNGHelper.GetUniform() * 3) + 1 : count;

        for (var i = 0; i < itemCount; i++)
        {
            var item = new Item();
            item.ID = MainServer.AddToGameObjects(item);
            bag.LootBag[i] = item;
        }

        bag.Transform = bag.Transform.Translated(new Vector3((float) x, (float) y, (float) z));
        MainServer.MainServerNode.AddChild(bag);
        return bag.LootBag;
    }

    public static LootBag CreateFromEntity(IGameEntity ent)
    {
        return Create(ent.X, ent.Y, ent.Z, 0, ent.TypeID, LootRatityType.DEFAULT_MOB);
    }

    public void ShowForEveryClientInRadius()
    {
        foreach (var ent in MainServer.GameObjects.Values)
        {
            if (ent is CharacterData charData)
                // && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
                // MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
            {
                Console.WriteLine($"Sending {ID} ({ent.X}, {ent.Y}, {ent.Z}) to cli {charData.ID}");
                ShowForClient(charData.ID);
            }
        }
    }

    public void UpdatePositionForEveryClientInRadius()
    {
        foreach (var ent in MainServer.GameObjects.Values)
        {
            // TODO: proper load/unload for client
            if (ent is CharacterData charData)
                // && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
                // MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
            {
                charData.Client.MoveEntity(X, -Y, Z, Turn, ID);
            }
        }
    }

    public void ShowForClient(ushort clientId)
    {
        var xArr = CoordsHelper.EncodeServerCoordinate(X);
        var yArr = CoordsHelper.EncodeServerCoordinate(-Y);
        var zArr = CoordsHelper.EncodeServerCoordinate(Z);
        var x_1 = ((xArr[0] & 0b111) << 5) + 0b01111;
        var x_2 = ((xArr[1] & 0b111) << 5) + ((xArr[0] & 0b11111000) >> 3);
        var x_3 = ((xArr[2] & 0b111) << 5) + ((xArr[1] & 0b11111000) >> 3);
        var x_4 = ((xArr[3] & 0b111) << 5) + ((xArr[2] & 0b11111000) >> 3);
        var y_1 = ((yArr[0] & 0b111) << 5) + ((xArr[3] & 0b11111000) >> 3);
        var y_2 = ((yArr[1] & 0b111) << 5) + ((yArr[0] & 0b11111000) >> 3);
        var y_3 = ((yArr[2] & 0b111) << 5) + ((yArr[1] & 0b11111000) >> 3);
        var y_4 = ((yArr[3] & 0b111) << 5) + ((yArr[2] & 0b11111000) >> 3);
        var z_1 = ((zArr[0] & 0b111) << 5) + ((yArr[3] & 0b11111000) >> 3);
        var z_2 = ((zArr[1] & 0b111) << 5) + ((zArr[0] & 0b11111000) >> 3);
        var z_3 = ((zArr[2] & 0b111) << 5) + ((zArr[1] & 0b11111000) >> 3);
        var z_4 = ((zArr[3] & 0b111) << 5) + ((zArr[2] & 0b11111000) >> 3);
        var z_5 = 0b01100000 + ((zArr[3] & 0b11111000) >> 3);

        var lootBagPacket = new byte[]
        {
            0x1D, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID), MajorByte(ID), 0x5C, 0x86, (byte) x_1,
            (byte) x_2, (byte) x_3, (byte) x_4, (byte) y_1, (byte) y_2, (byte) y_3, (byte) y_4, (byte) z_1,
            (byte) z_2, (byte) z_3, (byte) z_4, (byte) z_5, 0x20, 0x91, 0x45, 0x06, 0x00
        };

        Client.TryFindClientByIdAndSendData(clientId, lootBagPacket);
    }

    public void ShowDropitemListForClient(ushort clientId)
    {
        byte[] itemList;
        // 25 and 30 bits should be enough for every item in game, we're not going to use it for now
        // we'll figure out weight for 3-4 slot containers later
        var weight = 1234;
        var weight2 = 5678;
        var weight_1 = (byte) (weight % 2 == 1 ? 0b10000000 : 0);
        var weight_2 = (byte) ((weight & 0b111111110) >> 1);
        var weight_3 = (byte) ((weight & 0b11111111000000000) >> 9);
        var weight_4 = (byte) ((weight & 0b1111111100000000000000000) >> 17);

        var weight_5 = (byte) ((weight2 & 0b111111) << 2);
        var weight_6 = (byte) ((weight2 & 0b11111111000000) >> 6);
        var weight_7 = (byte) ((weight2 & 0b1111111100000000000000) >> 14);
        var weight_8 = (byte) ((weight2 & 0b111111110000000000000000000000) >> 22);

        // TODO: actual count. Will test with 1 for now
        switch (1)
        {
            case 1:
                var id_1 = Item0.ID >> 12;
                var id_2 = (Item0.ID & 0b111100000000) >> 8;
                var id_3 = (Item0.ID & 0b11110000) >> 4;
                var id_4 = Item0.ID & 0b1111;
                itemList = new byte[]
                {
                    0x19, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID), MajorByte(ID), 0x5C, 0x46, 0x61, 0x02, 
                    0x00, 0x0A, 0x82, 0x00, (byte) (id_4 << 4), (byte) ((id_2 << 4) + id_3), (byte) id_1, 0x70, 0x0D, 0x00, 0x00, 0x00 
                };

                break;
            case 2:
                itemList = new byte[]
                {
                    0x27, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID), MajorByte(ID), 0x5C, 0x46, 0x61, 0x02, 
                    0x00, 0x0A, 0x13, 0x00, 0x50, 0x10, 0x04, 0x00, 0x00, 0x1F, weight_1, weight_2, weight_3, weight_4, 
                    0x80, 0x82, 0x20, 0x04, 0x5C, 0xF8, 0x00, weight_5, weight_6, weight_7, weight_8, 0x00
                };

                break;
            case 3:
                itemList = new byte[]
                {
                    0x2E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID), MajorByte(ID), 0x5C, 0x46, 0x61, 0x02, 
                    0x00, 0x0A, 0x82, 0x00, 0x50, 0xA3, 0x0C, 0x30, 0x00, 0x00, 0x00, 0x50, 0x10, 0x84, 0x80, 0x86, 
                    0x65, 0x00, 0x08, 0x00, 0x00, 0x80, 0x82, 0x20, 0x08, 0xF0, 0x28, 0x03, 0x2C, 0x00, 0x00, 0x00, 
                    0x00
                };

                break;
            case 4:
                itemList = new byte[]
                {
                    0x38, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID), MajorByte(ID), 0x5C, 0x46, 0x61, 0x02, 
                    0x00, 0x0A, 0x82, 0x00, 0x50, 0xA3, 0x0C, 0x30, 0x00, 0x00, 0x00, 0x50, 0x10, 0x84, 0x80, 0x86,
                    0x65, 0x00, 0x08, 0x00, 0x00, 0x80, 0x82, 0x20, 0x08, 0xF0, 0x28, 0x03, 0x2C, 0x00, 0x00, 0x00, 
                    0x14, 0x04, 0x61, 0xE0, 0x61, 0x19, 0x80, 0x19, 0x00, 0x00, 0x00
                };

                break;
            default:
                Console.WriteLine($"Item list for count {Count} not implemented");

                return;
        }

        Client.TryFindClientByIdAndSendData(clientId, itemList);
    }

    public byte[] GetContentsPacket()
    {
        var loot = GetRandomObjectData();

        var objid_1 = (byte) (((loot.GameId & 0b11) << 6) + 0b100110);
        var objid_2 = (byte) ((loot.GameId >> 2) & 0b11111111);
        var objid_3 = (byte) (((loot.GameId >> 10) & 0b1111) + 0b00010000);

        var bagid_1 = (byte) (((ID) & 0b111) << 5);
        var bagid_2 = (byte) ((ID >> 3) & 0b11111111);
        var bagid_3 = (byte) ((ID >> 11) & 0b11111);
        
        Console.WriteLine(loot.ToDebugString());

        if (loot.ObjectType is GameObjectType.MantraBlack or GameObjectType.MantraWhite)
        {
            return new byte[]
            {
                0x28, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Item0.ID), MajorByte(Item0.ID),
                (byte) (loot.ObjectType == GameObjectType.MantraBlack ? 0xA4 : 0xA0), 0x8F, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, objid_1, objid_2, objid_3, 0x15, 0x60, bagid_1, 
                bagid_2, bagid_3, 0xA0, 0xC0, 0x02, 0x01, 0x00
            };
        }

        if (loot.ObjectType is GameObjectType.Flower or GameObjectType.Metal or GameObjectType.Mineral 
            or GameObjectType.Powder or GameObjectType.Powder_Area or GameObjectType.Elixir_Castle 
            or GameObjectType.Elixir_Trap)
        {
            var count = loot.ObjectType == GameObjectType.Powder ? MainServer.Rng.RandiRange(1, 19) : 1;
            byte typeid_1 = 0;
            byte typeid_2 = 0;

            switch (loot.ObjectType)
            {
                case GameObjectType.Flower:
                    typeid_1 = 0x64;
                    typeid_2 = 0x89;
                    break;
                case GameObjectType.Metal:
                    typeid_1 = 0x68;
                    typeid_2 = 0x89;
                    break;
                case GameObjectType.Mineral:
                    typeid_1 = 0x60;
                    typeid_2 = 0x89;
                    break;
                case GameObjectType.Powder:
                case GameObjectType.Powder_Area:
                    typeid_1 = 0x14;
                    typeid_2 = 0x87;
                    break;
                case GameObjectType.Elixir_Castle:
                case GameObjectType.Elixir_Trap:
                    typeid_1 = 0x60;
                    typeid_2 = 0x87;
                    break;
            }

            return new byte[]
            {
                0x30, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Item0.ID), MajorByte(Item0.ID), typeid_1, typeid_2,
                0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45,
                objid_1, objid_2, objid_3, 0x15, 0x60, bagid_1, bagid_2, bagid_3, 0xA0, 0x90, 0x05, 0x00, 0xFF, 0xFF,
                0xFF, 0xFF, 0x05, 0x16, (byte) ((count & 0b11111) << 3), (byte) ((count >> 5) & 0b11111111), 0x00
            };
        }

        if (loot.ObjectType is GameObjectType.Crossbow or GameObjectType.Axe or GameObjectType.Sword or GameObjectType.Amulet or GameObjectType.Armor
            or GameObjectType.Belt or GameObjectType.Bracelet or GameObjectType.Gloves or GameObjectType.Helmet or GameObjectType.Pants
            or GameObjectType.Shield or GameObjectType.Shoes or GameObjectType.Robe)
        {
            
            byte typeid_1 = 0;
            byte typeid_2 = 0;
        
            switch (loot.ObjectType)
            {
                case GameObjectType.Crossbow:
                    typeid_1 = 0xD8;
                    typeid_2 = 0x87;
                    break;
                case GameObjectType.Sword:
                    typeid_1 = 0xD0;
                    typeid_2 = 0x87;
                    break;
                case GameObjectType.Axe:
                    typeid_1 = 0xD4;
                    typeid_2 = 0x87;
                    break;
                case GameObjectType.Amulet:
                    typeid_1 = 0xBC;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Armor:
                    typeid_1 = 0xB8;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Bracelet:
                    typeid_1 = 0xDC;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Belt:
                    typeid_1 = 0xCC;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Gloves:
                    typeid_1 = 0x18;
                    typeid_2 = 0x8C;
                    break;
                case GameObjectType.Helmet:
                    typeid_1 = 0xD4;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Pants:
                    typeid_1 = 0xD8;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Shield:
                    typeid_1 = 0xD0;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Shoes:
                    typeid_1 = 0x10;
                    typeid_2 = 0x8C;
                    break;
                case GameObjectType.Robe:
                    typeid_1 = 0xE4;
                    typeid_2 = 0x8B;
                    break;
            }
            objid_3 = (byte) (((loot.GameId >> 10) & 0b1111) + (0x1 << 4));

            if (loot.ObjectType is GameObjectType.Sword or GameObjectType.Axe)
            {
                byte typeIdMod_1 = 0;
                byte typeIdMod_2 = 0;

                switch (loot.Suffix)
                {
                    case ItemSuffix.Exhaustion:
                    case ItemSuffix.Ether:
                    case ItemSuffix.Valor:
                    case ItemSuffix.Fatigue:
                        typeIdMod_1 = 0x44;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Damage:
                    case ItemSuffix.Disease:
                    case ItemSuffix.Cruelty:
                    case ItemSuffix.Instability:
                        typeIdMod_1 = 0x45;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Haste:
                    case ItemSuffix.Range:
                    case ItemSuffix.Speed:
                    case ItemSuffix.Distance:
                        typeIdMod_1 = 0x46;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Disorder:
                    case ItemSuffix.Decay:
                    case ItemSuffix.Chaos:
                    case ItemSuffix.Devastation:
                        typeIdMod_1 = 0x47;
                        typeIdMod_2 = 0x01;
                        break;
                    // case ItemSuffix.Exhaustion:
                    case ItemSuffix.Weakness:
                    // case ItemSuffix.Valor:
                    case ItemSuffix.Penetration:
                        typeIdMod_1 = 0x48;
                        typeIdMod_2 = 0x01;
                        break;
                    // case ItemSuffix.Damage:
                    case ItemSuffix.Interdict:
                    // case ItemSuffix.Cruelty:
                    case ItemSuffix.Value:
                        typeIdMod_1 = 0x49;
                        typeIdMod_2 = 0x01;
                        break;
                        
                }

                byte itemSuffixMod = 0x0;

                switch (loot.Suffix)
                {
                    case ItemSuffix.Exhaustion:
                    case ItemSuffix.Damage:
                    case ItemSuffix.Haste:
                    case ItemSuffix.Disorder:
                        itemSuffixMod = 0x0;
                        break;
                    case ItemSuffix.Ether:
                    case ItemSuffix.Disease:
                    case ItemSuffix.Range:
                    case ItemSuffix.Decay:
                    case ItemSuffix.Weakness:
                    case ItemSuffix.Interdict:
                        itemSuffixMod = 0x20;
                        break;
                    case ItemSuffix.Valor:
                    case ItemSuffix.Cruelty:
                    case ItemSuffix.Speed:
                    case ItemSuffix.Chaos:
                        itemSuffixMod = 0x80;
                        break;
                    case ItemSuffix.Fatigue:
                    case ItemSuffix.Instability:
                    case ItemSuffix.Distance:
                    case ItemSuffix.Devastation:
                    case ItemSuffix.Penetration:
                    case ItemSuffix.Value:
                        itemSuffixMod = 0xA0;
                        break;
                        
                }
                
                objid_3 = (byte) (((loot.GameId >> 10) & 0b1111) + itemSuffixMod);
                
                return new byte[]
                {
                    0x2B, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Item0.ID), MajorByte(Item0.ID),
                    typeid_1, typeid_2, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, objid_1, objid_2, objid_3, 
                    typeIdMod_1, typeIdMod_2, bagid_1, 
                    bagid_2, bagid_3, 0xA0, 0x90, 0x05, 0x00, 0xFF, 0xFF, 0xFF, 0xFF
                };
            }
            
            return new byte[]
            {
                0x2B, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Item0.ID), MajorByte(Item0.ID),
                typeid_1, typeid_2, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, objid_1, objid_2, objid_3, 0x15, 0x60, bagid_1, 
                bagid_2, bagid_3, 0xA0, 0x90, 0x05, 0x00, 0xFF, 0xFF, 0xFF, 0xFF
            };
        }
        
        if (loot.ObjectType == GameObjectType.Ring)
        {
            byte ringTypeId_1 = 0;
            byte ringTypeId_2 = 0;

            switch (loot.Suffix)
            {                
                case ItemSuffix.Durability:
                case ItemSuffix.Precision:
                case ItemSuffix.Absorption:
                case ItemSuffix.Strength:
                    ringTypeId_1 = 0x44;
                    ringTypeId_2 = 0x01;
                    break;
                case ItemSuffix.Accuracy:
                case ItemSuffix.Agility:
                case ItemSuffix.Safety:
                case ItemSuffix.Health:
                    ringTypeId_1 = 0x45;
                    ringTypeId_2 = 0x01;
                    break;
                case ItemSuffix.Earth:
                case ItemSuffix.Endurance:
                case ItemSuffix.Life:
                case ItemSuffix.Meditation:
                    ringTypeId_1 = 0x46;
                    ringTypeId_2 = 0x01;
                    break;
                case ItemSuffix.Air:
                case ItemSuffix.Water:
                case ItemSuffix.Prana:
                case ItemSuffix.Ether:
                    ringTypeId_1 = 0x47;
                    ringTypeId_2 = 0x01;
                    break;
                case ItemSuffix.Fire:
                case ItemSuffix.Value:
                // case ItemSuffix.Absorption:
                // case ItemSuffix.Durability:
                    ringTypeId_1 = 0x48;
                    ringTypeId_2 = 0x01;
                    break;
                default:
                    Console.WriteLine($"Wrong suffix {Enum.GetName(typeof(ItemSuffix), loot.Suffix)}");
                    break;
            };
            byte itemSuffixMod = 0;

            switch (loot.Suffix)
            {
                case ItemSuffix.Durability:
                case ItemSuffix.Safety:
                case ItemSuffix.Life:
                case ItemSuffix.Prana:
                    itemSuffixMod = 0;
                    break;
                case ItemSuffix.Precision:
                case ItemSuffix.Agility:
                case ItemSuffix.Endurance:
                case ItemSuffix.Water:
                case ItemSuffix.Fire:
                    itemSuffixMod = 0x20;
                    break;
                case ItemSuffix.Absorption:
                case ItemSuffix.Health:
                case ItemSuffix.Meditation:
                case ItemSuffix.Ether:
                    itemSuffixMod = 0x80;
                    break;
                case ItemSuffix.Strength:
                case ItemSuffix.Accuracy:
                case ItemSuffix.Earth:
                case ItemSuffix.Air:
                case ItemSuffix.Value:
                    itemSuffixMod = 0xA0;
                    break;
            }
            objid_3 = (byte) (((loot.GameId >> 10) & 0b1111) + itemSuffixMod);
            
            // technically, live server has different values per suffix group for 0x98 0x1A at the end but these
            // seem to be safe to ignore
            return new byte[]
            {
                0x3C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Item0.ID), MajorByte(Item0.ID),
                0xE0, 0x8B, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, objid_1, objid_2, objid_3, ringTypeId_1, ringTypeId_2, bagid_1, 
                bagid_2, bagid_3, 0xA0, 0x90, 0x05, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x85, 0x77, 0x50, 0xBB, 0xFB, 0x22,
                0x4B, 0x0B, 0x6B, 0x93, 0x4B, 0x73, 0x3B, 0x83, 0x98, 0x1A, 0x00
            };
        }

        Console.WriteLine($"Unhandled game object: {loot.ToDebugString()}");

        return new byte[] { };
    }

    public GameObjectData GetRandomObjectData()
    {
        var tierFilter = Math.Min(TitleLevelMinusOne, (byte) 74) / 5 + 1;
        Console.WriteLine(tierFilter);
        var typeFilter = new HashSet<GameObjectType>
        {
            GameObjectType.Flower,
            GameObjectType.Metal,
            GameObjectType.Mineral,
            GameObjectType.Amulet,
            GameObjectType.Armor,
            GameObjectType.Robe,
            GameObjectType.Belt,
            GameObjectType.Bracelet,
            GameObjectType.Gloves,
            GameObjectType.Helmet,
            GameObjectType.Pants,
            GameObjectType.Ring,
            GameObjectType.Shield,
            GameObjectType.Shoes,
            // Flag,
            // Guild,
            GameObjectType.MantraBlack,
            GameObjectType.MantraWhite,
            GameObjectType.Elixir_Castle, 
            GameObjectType.Elixir_Trap,
            GameObjectType.Powder,
            GameObjectType.Powder_Area,
            GameObjectType.Crossbow,
            GameObjectType.Axe,
            GameObjectType.Sword,
        };

        var overrideFilter = new HashSet<GameObjectType>
        {
        };

        typeFilter = overrideFilter.Count > 0 ? overrideFilter : typeFilter;

        var kindFilter = new HashSet<GameObjectKind>
        {
            GameObjectKind.Alchemy,
            GameObjectKind.Crossbow_New,
            GameObjectKind.Armor_New,
            GameObjectKind.Armor_Old, // "Old" robes only
            GameObjectKind.Axe_New,
            GameObjectKind.Powder,
            GameObjectKind.Magical_New,
            GameObjectKind.MantraBlack,
            GameObjectKind.MantraWhite,
            GameObjectKind.Sword_New,
        };

        var tierAgnosticTypes = new HashSet<GameObjectType>
        {
            GameObjectType.Flower,
            GameObjectType.Metal,
            GameObjectType.Mineral,
        };

        var lootPool = MainServer.GameObjectDataDb
            .Where(x => 
                kindFilter.Contains(x.Value.ObjectKind) && typeFilter.Contains(x.Value.ObjectType) 
                                                        && (x.Value.Tier == tierFilter 
                                                            || tierAgnosticTypes.Contains(x.Value.ObjectType)))
            .Select(y => y.Value)
            .ToList();

        var random = MainServer.Rng.RandiRange(0, lootPool.Count - 1);
        var item = lootPool.ElementAt(random);

        if (item.ObjectType == GameObjectType.Ring)
        {
            var suffixFilter = new List<ItemSuffix>
            {
                ItemSuffix.Health,
                ItemSuffix.Accuracy,
                ItemSuffix.Air,
                ItemSuffix.Durability,
                ItemSuffix.Life,
                ItemSuffix.Endurance,
                ItemSuffix.Fire,
                ItemSuffix.Absorption,
                ItemSuffix.Meditation,
                ItemSuffix.Strength,
                ItemSuffix.Earth,
                ItemSuffix.Safety,
                ItemSuffix.Prana,
                ItemSuffix.Agility,
                ItemSuffix.Water,
                ItemSuffix.Value,
                ItemSuffix.Precision,
                ItemSuffix.Ether,
            };
            var suffix = suffixFilter.ElementAt(MainServer.Rng.RandiRange(0, suffixFilter.Count - 1));
            item.Suffix = suffix;
        }

        if (item.ObjectType is GameObjectType.Sword or GameObjectType.Axe)
        {
            var suffixFilter = new List<ItemSuffix>
            {
                ItemSuffix.Cruelty,
                ItemSuffix.Chaos,
                ItemSuffix.Instability,
                ItemSuffix.Devastation,
                ItemSuffix.Value,
                ItemSuffix.Exhaustion,
                ItemSuffix.Haste,
                ItemSuffix.Ether,
                ItemSuffix.Range,
                ItemSuffix.Weakness,
                ItemSuffix.Valor,
                ItemSuffix.Speed,
                ItemSuffix.Fatigue,
                ItemSuffix.Distance,
                ItemSuffix.Penetration,
                ItemSuffix.Damage,
                ItemSuffix.Disorder,
                ItemSuffix.Disease,
                ItemSuffix.Decay,
                ItemSuffix.Interdict,
            };
            var suffix = suffixFilter.ElementAt(MainServer.Rng.RandiRange(0, suffixFilter.Count - 1));
            item.Suffix = suffix;
        }
        return item;
    }
}