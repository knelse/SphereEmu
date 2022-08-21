using System;
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
        LootRatityType ratityType)
    {
        var bag = (LootBagNode) LootBagScene.Instance();
        bag.LootBag = new LootBag();
        bag.LootBag.ID = MainServer.AddToGameObjects(bag.LootBag);
        bag.LootBag.X = x;
        bag.LootBag.Y = y;
        bag.LootBag.Z = z;
        bag.LootBag.ParentNode = bag;
        // TODO: item gen logic
        var itemCount = (int) RNGHelper.GetUniform() * 3 + 1;
        Console.WriteLine(itemCount);

        for (var i = 0; i < itemCount; i++)
        {
            bag.LootBag[i] = new Item();
        }
        
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
            if (ent is CharacterData charData
                && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
                MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
            {
                Console.WriteLine($"{ent.ID} {ent.X} {ent.Y} {ent.Z}");
                ShowForClient(charData.ID);
            }
        }
    }

    public void UpdatePositionForEveryClientInRadius()
    {
        foreach (var ent in MainServer.GameObjects.Values)
        {
            if (ent is CharacterData charData
                && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
                MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
            {
                charData.Client.MoveEntity(X, Y, Z, Turn, ID);
            }
        }
    }

    public void ShowForClient(ushort clientId)
    {
        var xArr = CoordsHelper.EncodeServerCoordinate(X);
        var yArr = CoordsHelper.EncodeServerCoordinate(Y);
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
            (byte) z_2,
            (byte) z_3, (byte) z_4, (byte) z_5, 0x20, 0x91, 0x45, 0x06, 0x00
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

        switch (Count)
        {
            case 1:
                itemList = new byte[]
                {
                    0x1C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID),
                    MajorByte(ID), 0x5C, 0x46, 0x61, 0x02, 0x00, 0x0A, 0x13, 0x00, 0x50, 0x10, 0x04,
                    0x00, 0x00, 0x39, weight_1, weight_2, weight_3, weight_4, 0x00
                };

                break;
            case 2:
                itemList = new byte[]
                {
                    0x27, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID),
                    MajorByte(ID), 0x5C, 0x46, 0x61, 0x02, 0x00, 0x0A, 0x13, 0x00, 0x50, 0x10, 0x04,
                    0x00, 0x00, 0x1F, weight_1, weight_2, weight_3, weight_4, 0x80, 0x82, 0x20, 0x04, 0x5C, 0xF8,
                    0x00, weight_5,
                    weight_6, weight_7, weight_8, 0x00
                };

                break;
            case 3:
                itemList = new byte[]
                {
                    0x2E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID),
                    MajorByte(ID), 0x5C, 0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, 0x50, 0xA3, 0x0C,
                    0x30, 0x00, 0x00, 0x00, 0x50, 0x10, 0x84, 0x80, 0x86, 0x65, 0x00, 0x08, 0x00, 0x00, 0x80,
                    0x82, 0x20, 0x08, 0xF0, 0x28, 0x03, 0x2C, 0x00, 0x00, 0x00, 0x00
                };

                break;
            case 4:
                itemList = new byte[]
                {
                    0x38, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID),
                    MajorByte(ID), 0x5C, 0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, 0x50, 0xA3, 0x0C,
                    0x30, 0x00, 0x00, 0x00, 0x50, 0x10, 0x84, 0x80, 0x86, 0x65, 0x00, 0x08, 0x00, 0x00, 0x80,
                    0x82, 0x20, 0x08, 0xF0, 0x28, 0x03, 0x2C, 0x00, 0x00, 0x00, 0x14, 0x04, 0x61, 0xE0, 0x61, 0x19,
                    0x80, 0x19, 0x00, 0x00, 0x00
                };

                break;
            default:
                Console.WriteLine($"Item list for count {Count} not implemented");

                return;
        }

        Client.TryFindClientByIdAndSendData(clientId, itemList);
    }
}