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

    enum ItemType
    {
        Arbalet,
        Sword,
        Axe,
        Amulet,
        Bracelet,
        Ring,
        Armor,
        Shield,
        Gloves,
        Helm,
        Belt, 
        Shoes,
        Pants,
        Mineral,
        Flower,
        Metal,
        MantraWhite,
        MantraBlack,
        Powder
    }

    private static int itemTypeCount => Enum.GetValues(typeof(ItemType)).Length;

    private int GetRandomFromSet(SortedSet<int> set)
    {
        return set.ElementAt(MainServer.Rng.RandiRange(0, set.Count - 1));
    }

    private int GetRandomObjectId(ItemType type)
    {
        switch (type)
        {
            case ItemType.Arbalet:
                var set1 = MainServer.ItemTypeNameToIdMapping["arbs"];
                var set2 = MainServer.ItemTypeNameToIdMapping["arbs_n"];

                foreach (var val in set2)
                {
                    set1.Add(val);
                }
                return GetRandomFromSet(set1);
            case ItemType.Sword:
                var set3 = MainServer.ItemTypeNameToIdMapping["swords"];
                var set4 = MainServer.ItemTypeNameToIdMapping["swords_n"];

                foreach (var val in set4)
                {
                    set3.Add(val);
                }
                return GetRandomFromSet(set3);
            case ItemType.Axe:
                var set5 = MainServer.ItemTypeNameToIdMapping["axes"];
                var set6 = MainServer.ItemTypeNameToIdMapping["axes_n"];

                foreach (var val in set6)
                {
                    set5.Add(val);
                }
                return GetRandomFromSet(set5);
            case ItemType.Amulet: 
            case ItemType.Bracelet:
            case ItemType.Ring:
                var set9 = MainServer.ItemTypeNameToIdMapping["magdef"];
                var set10 = MainServer.ItemTypeNameToIdMapping["magdef_n"];

                foreach (var val in set10)
                {
                    set9.Add(val);
                }
                return GetRandomFromSet(set9);
                break;
            case ItemType.Armor:
            case ItemType.Shield:
            case ItemType.Gloves:
            case ItemType.Helm:
            case ItemType.Belt:
            case ItemType.Shoes:
            case ItemType.Pants:
                var set7 = MainServer.ItemTypeNameToIdMapping["armor"];
                var set8 = MainServer.ItemTypeNameToIdMapping["armor_n"];

                foreach (var val in set8)
                {
                    set7.Add(val);
                }
                return GetRandomFromSet(set7);
            // case ItemType.Shield:
            //     break;
            // case ItemType.Gloves:
            //     break;
            // case ItemType.Helm:
            //     break;
            // case ItemType.Belt:
            //     break;
            // case ItemType.Shoes:
            //     break;
            // case ItemType.Pants:
            //     break;
            case ItemType.Mineral:
            case ItemType.Flower:
            case ItemType.Metal:
                return GetRandomFromSet(MainServer.ItemTypeNameToIdMapping["alch"]);
            // case ItemType.Flower:
            //     break;
            // case ItemType.Metal:
            //     break;
            case ItemType.MantraBlack:
                return GetRandomFromSet(MainServer.ItemTypeNameToIdMapping["mantra_b"]);
            case ItemType.MantraWhite:
                return GetRandomFromSet(MainServer.ItemTypeNameToIdMapping["mantra_w"]);
            case ItemType.Powder:
                return GetRandomFromSet(MainServer.ItemTypeNameToIdMapping["powder"]);
        }

        return 0;
    }

    public byte[] GetContentsPacket()
    {
        var typeFilter = new SortedSet<int>
        {
            (int) ItemType.Flower,
            (int) ItemType.Metal,
            (int) ItemType.Mineral,
            (int) ItemType.MantraBlack,
            (int) ItemType.MantraWhite,
            (int) ItemType.Powder,
            (int) ItemType.Arbalet,
            (int) ItemType.Sword,
            (int) ItemType.Axe,
            (int) ItemType.Amulet,
            (int) ItemType.Bracelet,
            (int) ItemType.Helm,
            (int) ItemType.Armor,
            (int) ItemType.Shield,
            (int) ItemType.Gloves,
            (int) ItemType.Shoes,
            (int) ItemType.Belt,
            (int) ItemType.Pants,
        };
        var type = (ItemType) GetRandomFromSet(typeFilter);
        var itemid = GetRandomObjectId(type);

        var objid_1 = (byte) (((itemid & 0b11) << 6) + 0b100110);
        var objid_2 = (byte) ((itemid >> 2) & 0b11111111);
        var objid_3 = (byte) (((itemid >> 10) & 0b1111) + 0b00010000);

        var bagid_1 = (byte) (((ID) & 0b111) << 5);
        var bagid_2 = (byte) ((ID >> 3) & 0b11111111);
        var bagid_3 = (byte) ((ID >> 11) & 0b11111);
        
        Console.WriteLine($"{Enum.GetName(typeof(ItemType), type)} {itemid}");

        if (type is ItemType.MantraBlack or ItemType.MantraWhite)
        {
            return new byte[]
            {
                0x28, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Item0.ID), MajorByte(Item0.ID),
                (byte) (type == ItemType.MantraBlack ? 0xA4 : 0xA0), 0x8F, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, objid_1, objid_2, objid_3, 0x15, 0x60, bagid_1, 
                bagid_2, bagid_3, 0xA0, 0xC0, 0x02, 0x01, 0x00
            };
        }

        if (type is ItemType.Flower or ItemType.Metal or ItemType.Mineral or ItemType.Powder)
        {
            var count = type == ItemType.Powder ? MainServer.Rng.RandiRange(1, 19) : 1;
            byte typeid_1 = 0;
            byte typeid_2 = 0;

            switch (type)
            {
                case ItemType.Flower:
                    typeid_1 = 0x64;
                    typeid_2 = 0x89;

                    break;
                case ItemType.Metal:
                    typeid_1 = 0x68;
                    typeid_2 = 0x89;

                    break;
                case ItemType.Mineral:
                    typeid_1 = 0x60;
                    typeid_2 = 0x89;

                    break;
                case ItemType.Powder:
                    typeid_1 = 0x14;
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

        if (type is ItemType.Arbalet or ItemType.Axe or ItemType.Sword or ItemType.Amulet or ItemType.Armor
            or ItemType.Belt or ItemType.Bracelet or ItemType.Gloves or ItemType.Helm or ItemType.Pants
            or ItemType.Shield or ItemType.Shoes)
        {
            
            byte typeid_1 = 0;
            byte typeid_2 = 0;

            switch (type)
            {
                case ItemType.Arbalet:
                    typeid_1 = 0xD8;
                    typeid_2 = 0x87;
                    break;
                case ItemType.Sword:
                    typeid_1 = 0xD0;
                    typeid_2 = 0x87;
                    break;
                case ItemType.Axe:
                    typeid_1 = 0xD4;
                    typeid_2 = 0x87;
                    break;
                case ItemType.Amulet:
                    typeid_1 = 0xBC;
                    typeid_2 = 0x8B;
                    break;
                case ItemType.Armor: // only chain armor typeID for now
                    typeid_1 = 0xB8;
                    typeid_2 = 0x8B;
                    break;
                case ItemType.Bracelet:
                    typeid_1 = 0xDC;
                    typeid_2 = 0x8B;
                    break;
                case ItemType.Belt:
                    typeid_1 = 0xCC;
                    typeid_2 = 0x8B;
                    break;
                case ItemType.Gloves:
                    typeid_1 = 0x18;
                    typeid_2 = 0x8C;
                    break;
                case ItemType.Helm:
                    typeid_1 = 0xD4;
                    typeid_2 = 0x8B;
                    break;
                case ItemType.Pants:
                    typeid_1 = 0xD8;
                    typeid_2 = 0x8B;
                    break;
                case ItemType.Shield:
                    typeid_1 = 0xC0;
                    typeid_2 = 0x8B;
                    break;
                case ItemType.Shoes:
                    typeid_1 = 0x10;
                    typeid_2 = 0x8C;
                    break;
            }
            // B0 B_ B8 BC C0 C_ C_ CC D_ D4 D8 DC 
            var prefix = 1;// MainServer.Rng.RandiRange(0, 15);
            // currently, only "of damage" would work
            objid_3 = (byte) (((itemid >> 10) & 0b1111) + (prefix << 4));
            Console.WriteLine($"{typeid_1:X} {typeid_2:X}");
            return new byte[]
            {
                0x2B, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Item0.ID), MajorByte(Item0.ID),
                typeid_1, typeid_2, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, objid_1, objid_2, objid_3, 0x15, 0x60, bagid_1, 
                bagid_2, bagid_3, 0xA0, 0x90, 0x05, 0x00, 0xFF, 0xFF, 0xFF, 0xFF
            };
        }

        return new byte[] { };
    }
}