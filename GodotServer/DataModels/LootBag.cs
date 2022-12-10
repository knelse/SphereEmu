using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitStreams;
using Godot;
using SphServer;
using SphServer.DataModels;
using SphServer.Helpers;
using SphServer.Packets;
using static SphServer.Helpers.BitHelper;

public partial class Item : IGameEntity
{
    public ushort Id { get; set; }
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
    public SphGameObject SphGameObject { get; set; }
    public int ItemCount { get; set; } = 1;
}

public enum LootRatityType
{
    DEFAULT_MOB,
    NAMED_MOB,
    PLAYER
}

public partial class LootBag : IGameEntity
{
    public ushort Id { get; set; }
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
    public SphGameObject SphGameObject { get; set; } // unused for now

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
        var bag = LootBagScene.Instantiate<LootBagNode>();
        bag.LootBag = new LootBag();
        bag.LootBag.Id = MainServer.AddToGameObjects(bag.LootBag);
        bag.LootBag.TitleLevelMinusOne = (byte) level;
        bag.LootBag.X = x;
        bag.LootBag.Y = y;
        bag.LootBag.Z = z;
        bag.LootBag.ParentNode = bag;
        var itemCount = count == -1 ? MainServer.Rng.Next(1, 5) : count;

        for (var i = 0; i < itemCount; i++)
        {
            var item = new Item
            {
                SphGameObject = LootHelper.GetRandomObjectData(level)//, i == 0 ? -1 : 2402)
            };
            item.Id = MainServer.AddToGameObjects(item);
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
                Console.WriteLine($"Sending {Id} ({ent.X}, {ent.Y}, {ent.Z}) to cli {charData.Player.Index}");
                ShowForClient(charData.Player.Index);
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
                charData.Client.MoveEntity(X, -Y, Z, Turn, Id);
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
            0x1D, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Id), MajorByte(Id), 0x5C, 0x86, (byte) x_1,
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

        var item_0_id = Item0?.Id ?? 0;
        var item_1_id = Item1?.Id ?? 0;
        var item_2_id = Item2?.Id ?? 0;
        var item_3_id = Item3?.Id ?? 0;
        
        var item0_1 = (byte) ((item_0_id & 0b1111) << 4);
        var item0_2 = (byte) ((item_0_id >> 4) & 0b11111111);
        var item0_3 = (byte) ((item_0_id >> 12) & 0b1111);
                
        var item1_1 = (byte) ((item_1_id & 0b1) << 7);
        var item1_2 = (byte) ((item_1_id >> 1) & 0b11111111);
        var item1_3 = (byte) ((item_1_id >> 9) & 0b1111111);
                
        var item2_1 = (byte) ((item_2_id & 0b111111) << 2);
        var item2_2 = (byte) ((item_2_id >> 6) & 0b11111111);
        var item2_3 = (byte) ((item_2_id >> 14) & 0b11);
                
        var item3_1 = (byte) ((item_3_id & 0b111) << 5);
        var item3_2 = (byte) ((item_3_id >> 3) & 0b11111111);
        var item3_3 = (byte) ((item_3_id >> 11) & 0b11111);
        
        switch (Count)
        {
            case 1:
                itemList = new byte[]
                {
                    0x19, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Id), MajorByte(Id), 0x5C, 0x46, 0x61, 0x02, 
                    0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, 0x70, 0x0D, 
                    0x00, 0x00, 0x00 
                };

                break;
            case 2:
                itemList = new byte[]
                {
                    // 0x27, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(ID), MajorByte(ID), 0x5C, 0x46, 0x61, 0x02, 
                    // 0x00, 0x0A, 0x13, 0x00, 0x50, 0x10, 0x04, item0_1, item0_2, item0_3, weight_1, weight_2, weight_3, weight_4, 
                    // 0x80, 0x82, 0x20, 0x04, item1_1, item1_2, item1_3, weight_5, weight_6, weight_7, weight_8, 0x00
                    0x23, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Id), MajorByte(Id), 0x5C, 0x46, 0x61, 0x02, 
                    0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, /*weight*/ 0xC0, 0x00, 0x00, 0x00, 0x50, 0x10, 
                    0x84, item1_1, item1_2, item1_3, /*weight*/ 0x00, 0x4B, 0x00, 0x00, 0x00
                    
                };

                break;
            case 3:
                itemList = new byte[]
                {
                    0x2E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Id), MajorByte(Id), 0x5C, 0x46, 0x61, 0x02, 
                    0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, 0x30, 0x00, 0x00, 0x00, 0x50, 0x10, 0x84, 
                    item1_1, item1_2, item1_3, 0x00, 0x08, 0x00, 0x00, 0x80, 0x82, 0x20, 0x08, item2_1, item2_2, item2_3, 
                    0x2C, 0x00, 0x00, 0x00, 0x00
                };

                break;
            case 4:
                itemList = new byte[]
                {
                    0x38, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(Id), MajorByte(Id), 0x5C, 0x46, 0x61, 0x02, 
                    0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, 0x30, 0x00, 0x00, 0x00, 0x50, 0x10, 0x84, 
                    item1_1, item1_2, item1_3, 0x00, 0x08, 0x00, 0x00, 0x80, 0x82, 0x20, 0x08, item2_1, item2_2, 
                    item2_3, 0x2C, 0x00, 0x00, 0x00, 0x14, 0x04, 0x61, item3_1, item3_2, item3_3, 0x80, 0x19, 0x00, 
                    0x00, 0x00
                };

                break;
            default:
                Console.WriteLine($"Item list for count {Count} not implemented");

                return;
        }

        Client.TryFindClientByIdAndSendData(clientId, itemList);
    }

    private ObjectPacket FindSimilarObjectPacketInDb(SphGameObject gameObject, ushort id)
    {
        Console.WriteLine(gameObject.ToDebugString());
        var weaponArmorNotShiftedId = 243; //
        var weaponArmorShiftedId = 153; //
        var ringNotShiftedId = 666;
        var ringShiftedId = 637; //
        var mantraId = 354;
        var alchemyId = 374;
        var powderId = 1;
        var foodId = 292;
        var keyId = 296;
        var mantraBookId = 317;
        var tokenId = 330;
        var diamondRingId = 569;
        
        ObjectPacket result;
        // Console.WriteLine("-------------------------------------------------------");
        var objectType = gameObject.ObjectType.GetPacketObjectType();
        // some ring broken
        Console.WriteLine(Enum.GetName(objectType));
        var suffixMod = gameObject.Suffix == ItemSuffix.None
            ? (ushort)81
            : (ushort)GameObjectDataHelper.ObjectTypeToSuffixLocaleMap[gameObject.ObjectType][gameObject.Suffix].value;

        var dbId = -1;
        if (GameObjectDataHelper.WeaponsAndArmor.Contains(gameObject.ObjectType))
        {
            dbId = suffixMod > 1000 ? weaponArmorShiftedId : weaponArmorNotShiftedId;
        }

        else if (GameObjectDataHelper.Mantras.Contains(gameObject.ObjectType))
        {
            dbId = mantraId;
        }

        else if (GameObjectDataHelper.Powders.Contains(gameObject.ObjectType))
        {
            dbId = powderId;
        }

        else if (GameObjectDataHelper.AlchemyMaterials.Contains(gameObject.ObjectType))
        {
            dbId = alchemyId;
        }

        else if (gameObject.ObjectType is GameObjectType.Ring)
        {
            dbId = suffixMod > 1000 ? ringShiftedId : ringNotShiftedId;
        }

        if (dbId == -1)
        {
            Console.WriteLine($"NOT FOUND: Type: {Enum.GetName(gameObject.ObjectType)} Suffix: {suffixMod} {Enum.GetName(gameObject.Suffix)}");
            dbId = 4;
        }

        result = MainServer.ObjectPacketCollection.FindOne(x => x.DbId == dbId);

        result.Id = id;
        result.GameId = (ushort) gameObject.GameId;
        result.BagId = Id;
        result.SuffixMod = suffixMod;
        result.Count = (ushort) gameObject.ItemCount;

        result.GameObject = gameObject;
        result.FriendlyName = MainServer.GameObjectDataDb[result.GameId].Localisation[Locale.Russian];
        var type = result.GameObject.ObjectType;
        result.Type = (ushort) objectType;
        
        if (GameObjectDataHelper.ObjectTypeToSuffixLocaleMap.ContainsKey(type))
        {
            result.GameObject.Suffix = GameObjectDataHelper.ObjectTypeToSuffixLocaleMap[type]
                .GetSuffixById(result.SuffixMod);
        }

        Console.WriteLine(result.DbId);

        Console.WriteLine(result.ToDebugString());
        // Console.WriteLine("-------------------------------------------------------");
        // var filterDbIds = new HashSet<int>
        // {
        //     4, 10, 18, 33, 34, 58, 75, 152, 153, 155, 235, 236, 237, 240, 242, 246, 301, 308, 309, 318, 319, 384, 393, 395, 457, 459, 489, 528, 532, 533, 558,
        //     578, 585, 588, 590, 593, 600, 608, 633, 643, 646, 659,
        //     431 // armor, breaks sometimes ?
        // };
        // var packets1 = MainServer.ObjectPacketCollection
        //     .Find(x => x.ObjectType == objectType && !filterDbIds.Contains(x.DbId));
        // if (!packets1.Any())
        // {
        //     Console.WriteLine($"not found object type: {objectType} gid: {gameObject.GameId}");
        //     // try again for alchemy
        //     if (objectType is ObjectType.AlchemyMetal or ObjectType.AlchemyMineral or ObjectType.AlchemyPlant)
        //     {
        //         var packets2 = MainServer.ObjectPacketCollection.Find(x =>
        //             !filterDbIds.Contains(x.DbId) && x.ObjectType == ObjectType.AlchemyMetal
        //             || x.ObjectType == ObjectType.AlchemyMineral 
        //             || x.ObjectType == ObjectType.AlchemyPlant);
        //         var collCount = packets2.Count();
        //         var random = MainServer.Rng.RandiRange(0, collCount - 1);
        //         result = packets2.ToList()[random];
        //     }
        //     else
        //     {
        //         // get any wepon/armor packet
        //         result = MainServer.ObjectPacketCollection.FindOne(x => x.ObjectType == ObjectType.WeaponSword);
        //     }
        // }
        // else
        // {
        //     var collCountAny = packets1.Count();
        //     var randomAny = MainServer.Rng.RandiRange(0, collCountAny - 1);
        //     var collCountWithSuffix = packets1.Count(x => x.SuffixMod != 81);
        //     var randomWithSuffix = MainServer.Rng.RandiRange(0, collCountWithSuffix - 1);
        //     result = gameObject.Suffix == ItemSuffix.None
        //         ? packets1.ToList()[randomAny]
        //         : packets1.Any(x => x.SuffixMod != 81)
        //             ? packets1.Where(x => x.SuffixMod != 81).ToList()[randomWithSuffix]
        //             : packets1.ToList()[randomAny];
        // }
        return result;
    }

    private byte[] GetItemBytes(int itemIndex, bool bitShiftForRings = false)
    {
        var packet = FindSimilarObjectPacketInDb(this[itemIndex]!.SphGameObject, this[itemIndex]!.Id);
        return this[itemIndex].SphGameObject.GetLootItemBytes(Id, this[itemIndex].Id, bitShiftForRings);
    }

    public byte[] GetContentsPacket()
    {
        var memoryStream = new MemoryStream();
        var stream = new BitStream(memoryStream)
        {
            AutoIncreaseStream = true
        };
        for (var i = 0; i < Count; i++)
        {
            var similarPacket = FindSimilarObjectPacketInDb(this[i]!.SphGameObject, this[i]!.Id);
            similarPacket.ToStream(stream);
            if (i < Count - 1)
            {
                if (GameObjectDataHelper.WeaponsAndArmor.Contains(similarPacket.GameObject.ObjectType))
                {
                    stream.WriteByte(0x7E >> 1, 7);
                }
                else
                {
                    var seekBack = GameObjectDataHelper.Mantras.Contains(similarPacket.GameObject.ObjectType)
                                   || GameObjectDataHelper.Powders.Contains(similarPacket.GameObject.ObjectType)
                                   || GameObjectDataHelper.AlchemyMaterials.Contains(
                                       similarPacket.GameObject.ObjectType)
                        ? 1
                        : 0;
                    stream.SeekBack(seekBack);
                    stream.WriteByte(0x7E);
                }
            }
        }

        var readLength = stream.Offset * 8 + stream.Bit;
        stream.Seek(0, 0);
        var similarPacketBytes = stream.ReadBytes(readLength);
        Console.WriteLine(Convert.ToHexString(similarPacketBytes));

        // var item0ByteList = new List<byte>(GetItemBytes(0));

        // if (Count >= 2)
        // {
        //     Console.WriteLine($"IDs: {Item0.Id} {Item1.Id}");
        //     var item1Bytes = GetItemBytes(1, Item0.SphGameObject is { ObjectType: GameObjectType.Ring, Suffix: ItemSuffix.Strength or ItemSuffix.Agility
        //         or ItemSuffix.Accuracy or ItemSuffix.Endurance or ItemSuffix.Earth or ItemSuffix.Water
        //         or ItemSuffix.Air or ItemSuffix.Fire
        //     });
        //     byte[] resultArray;
        //
        //     if (SphGameObject.Mantras.Contains(Item0.SphGameObject.ObjectType))
        //     {
        //         item0ByteList.Add((byte) (((item1Bytes[0] & 0b1) << 7) + 0b0111111));
        //
        //         for (var i = 1; i < item1Bytes.Length; i++)
        //         {
        //             item0ByteList.Add((byte) (((item1Bytes[i] & 0b1) << 7) + (item1Bytes[i - 1] >> 1)));
        //         }
        //
        //         item0ByteList.Add((byte) (item1Bytes[^1] >> 1));
        //         resultArray = item0ByteList.ToArray();
        //     }
        //     // add 7E as constant, shift +- 2
        //     else
        //     {
        //         var isFullStatRing = Item0.SphGameObject.Suffix is ItemSuffix.Strength or ItemSuffix.Agility
        //             or ItemSuffix.Accuracy or ItemSuffix.Endurance or ItemSuffix.Earth or ItemSuffix.Water
        //             or ItemSuffix.Air or ItemSuffix.Fire;
        //
        //         if (Item0.SphGameObject.ObjectType != GameObjectType.Ring || !isFullStatRing)
        //         {
        //             item0ByteList.RemoveAt(item0ByteList.Count - 1);
        //         }
        //
        //         var baseLength = item0ByteList.Count;
        //         item0ByteList.Add(0x7E);
        //         item0ByteList.AddRange(item1Bytes);
        //         resultArray = item0ByteList.ToArray();
        //
        //         if (Item0.SphGameObject.ObjectType == GameObjectType.Ring && isFullStatRing)
        //         {
        //             
        //             for (var i = baseLength - 1; i < resultArray.Length - 1; i++)
        //             {
        //                 resultArray[i] = (byte) ((resultArray[i] >> 2) + ((resultArray[i + 1] & 0b11) << 6));
        //             }
        //             resultArray[^1] >>= 2;
        //         }
        //         else
        //         {
        //             for (var i = resultArray.Length - 1; i >= baseLength + 1; i--)
        //             {
        //                 resultArray[i] = (byte) (((resultArray[i] & 0b111111) << 2) + (resultArray[i - 1] >> 6));
        //             }
        //             resultArray[baseLength] <<= 2;
        //         }
        //     }
        //
        //     var packet = Packet.ToByteArray(resultArray.ToArray(), 3);
        //     Console.WriteLine(ConvertHelper.ToHexString(packet));
        //
        //     return packet;
        // }
        
        return Packet.ToByteArray(similarPacketBytes, 3); 
    }
}