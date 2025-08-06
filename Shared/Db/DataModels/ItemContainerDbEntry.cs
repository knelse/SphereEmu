// TODO: not yet refactored properly

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LiteDB;
using SphServer.Client;
using SphServer.Packets;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.Loot;
using SphServer.System;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;
using SphereServer = SphServer.Server.SphereServer;

namespace SphServer.Shared.Db.DataModels;

public class ItemContainerDbEntry
{
    [BsonId] public int Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Angle { get; set; }
    public int TitleMinusOne { get; set; }
    public int DegreeMinusOne { get; set; }

    [BsonIgnore]
    private static readonly PackedScene LootBagScene =
        (PackedScene) ResourceLoader.Load("res://Godot/Scenes/LootBag.tscn");

    public Dictionary<int, int> Contents { get; set; } = new ();
    public ulong? ParentNodeId { get; set; }

    public static ItemContainerDbEntry CreateHierarchyWithContents (double x, double y, double z,
        int level, //int sourceTypeId,
        LootRatity ratity, int count = -1)
    {
        var bag = LootBagScene.Instantiate<Godot.Nodes.LootBagNode>();
        ActiveNodes.Add(bag.GetInstanceId(), bag);
        var levelOverride = SphRng.Rng.Next(0, 61);
        bag.ItemContainerDbEntry = new ItemContainerDbEntry
        {
            TitleMinusOne = (byte) level,
            X = x,
            Y = y,
            Z = z,
            ParentNodeId = bag.GetInstanceId()
        };
        bag.ItemContainerDbEntry.Id = DbConnection.ItemContainers.Insert(bag.ItemContainerDbEntry);

        var itemCount = count == -1 ? SphRng.Rng.Next(1, 5) : count;

        for (var i = 0; i < itemCount; i++)
        {
            var randomObj = LootRandomizer.GetRandomLootObject(levelOverride > 0 ? levelOverride : level);
            var item = ItemDbEntry.CreateFromGameObject(randomObj);
            item.ParentContainerId = bag.ItemContainerDbEntry.Id;
            DbConnection.Items.Insert(item);
            bag.ItemContainerDbEntry.Contents[i] = item.Id;
        }

        bag.Transform = bag.Transform.Translated(new Vector3((float) x, (float) y, (float) z));
        SphereServer.ServerNode.CallDeferred("add_child", bag);
        DbConnection.ItemContainers.Update(bag.ItemContainerDbEntry);

        SphLogger.Info($"Added item container ID: {bag.ItemContainerDbEntry.Id} at: ({x:F2}, {y: F2}, {z: F2})");

        return bag.ItemContainerDbEntry;
    }

    private bool RemoveIfEmpty ()
    {
        if (Contents.Count != 0)
        {
            return false;
        }

        if (ParentNodeId != null)
        {
            ActiveNodes.Get(ParentNodeId.Value)?.QueueFree();
        }

        return true;
    }

    public bool RemoveItemByIdAndDestroyContainerIfEmpty (int itemGlobalId)
    {
        if (Contents.ContainsValue(itemGlobalId))
        {
            var key = Contents.First(x => x.Value == itemGlobalId).Key;
            Contents.Remove(key);
            Console.WriteLine($"Removed at {key} ID {itemGlobalId} from container {Id}");
        }

        DbConnection.ItemContainers.Update(Id, this);

        return RemoveIfEmpty();
    }

    public bool RemoveItemBySlotIdAndDestroyContainerIfEmpty (int slotId)
    {
        if (Contents.TryGetValue(slotId, out var value))
        {
            Console.WriteLine($"Removed at {slotId} ID {value} from container {Id}");
            Contents.Remove(slotId);
        }

        DbConnection.ItemContainers.Update(Id, this);

        return RemoveIfEmpty();
    }

    public void ShowForEveryClientInRadius ()
    {
        // foreach (var client in SphereServer.ActiveClients.Values)
        // {
        //     // && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
        //     // MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
        //     ShowForClient(client);
        // }
    }

    public void UpdatePositionForEveryClientInRadius ()
    {
        // foreach (var client in SphereServer.ActiveClients.Values)
        // {
        //     // TODO: proper load/unload for client
        //     // && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
        //     // MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
        //     client.MoveEntity(X, -Y, Z, Angle, client.GetLocalObjectId(Id));
        // }
    }

    public void ShowForClient (SphereClient client)
    {
        // var packetParts = PacketPart.LoadDefinedPartsFromFile(ObjectType.SackMobLoot);
        // PacketPart.UpdateCoordinates(packetParts, X, Y, Z);
        // var localId = client.GetLocalObjectId(Id);
        // PacketPart.UpdateEntityId(packetParts, localId);
        // var lootBagPacket = PacketPart.GetBytesToWrite(packetParts);

        // client.StreamPeer.PutData(lootBagPacket);
    }

    public void ShowItemListForClient (ushort clientId)
    {
        byte[] itemList;
        // 25 and 30 bits should be enough for every item in game, we're not going to use it for now
        // we'll figure out weight for 3-4 slot containers later
        // var weight = 1234;
        // var weight2 = 5678;
        // var weight_1 = (byte) (weight % 2 == 1 ? 0b10000000 : 0);
        // var weight_2 = (byte) ((weight & 0b111111110) >> 1);
        // var weight_3 = (byte) ((weight & 0b11111111000000000) >> 9);
        // var weight_4 = (byte) ((weight & 0b1111111100000000000000000) >> 17);
        //
        // var weight_5 = (byte) ((weight2 & 0b111111) << 2);
        // var weight_6 = (byte) ((weight2 & 0b11111111000000) >> 6);
        // var weight_7 = (byte) ((weight2 & 0b1111111100000000000000) >> 14);
        // var weight_8 = (byte) ((weight2 & 0b111111110000000000000000000000) >> 22);

        // var item_0_id = global::Client.GetLocalObjectId(clientId, Contents.GetValueOrDefault(0, 0));
        // var item_1_id = global::Client.GetLocalObjectId(clientId, Contents.GetValueOrDefault(1, 0));
        // var item_2_id = global::Client.GetLocalObjectId(clientId, Contents.GetValueOrDefault(2, 0));
        // var item_3_id = global::Client.GetLocalObjectId(clientId, Contents.GetValueOrDefault(3, 0));
        //
        // var item0_1 = (byte) ((item_0_id & 0b1111) << 4);
        // var item0_2 = (byte) ((item_0_id >> 4) & 0b11111111);
        // var item0_3 = (byte) ((item_0_id >> 12) & 0b1111);
        //
        // var item1_1 = (byte) ((item_1_id & 0b1) << 7);
        // var item1_2 = (byte) ((item_1_id >> 1) & 0b11111111);
        // var item1_3 = (byte) ((item_1_id >> 9) & 0b1111111);
        //
        // var item2_1 = (byte) ((item_2_id & 0b111111) << 2);
        // var item2_2 = (byte) ((item_2_id >> 6) & 0b11111111);
        // var item2_3 = (byte) ((item_2_id >> 14) & 0b11);
        //
        // var item3_1 = (byte) ((item_3_id & 0b111) << 5);
        // var item3_2 = (byte) ((item_3_id >> 3) & 0b11111111);
        // var item3_3 = (byte) ((item_3_id >> 11) & 0b11111);
        // var localId = global::Client.GetLocalObjectId(clientId, Id);
        //
        // switch (Contents.Count)
        // {
        //     case 1:
        //         itemList =
        //         [
        //             0x19, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(localId), MajorByte(localId), 0x5C,
        //             0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, 0x70, 0x0D, 0x00, 0x00, 0x00
        //         ];
        //
        //         break;
        //     case 2:
        //         itemList =
        //         [
        //             0x23, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(localId), MajorByte(localId), 0x5C,
        //             0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, /*weight*/ 0xC0, 0x00, 0x00,
        //             0x00, 0x50, 0x10, 0x84, item1_1, item1_2, item1_3, /*weight*/ 0x00, 0x4B, 0x00, 0x00, 0x00
        //         ];
        //
        //         break;
        //     case 3:
        //         itemList =
        //         [
        //             0x2E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(localId), MajorByte(localId), 0x5C,
        //             0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, 0x30, 0x00, 0x00, 0x00, 0x50,
        //             0x10, 0x84, item1_1, item1_2, item1_3, 0x00, 0x08, 0x00, 0x00, 0x80, 0x82, 0x20, 0x08, item2_1,
        //             item2_2, item2_3, 0x2C, 0x00, 0x00, 0x00, 0x00
        //         ];
        //
        //         break;
        //     case 4:
        //         itemList =
        //         [
        //             0x38, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(localId), MajorByte(localId), 0x5C,
        //             0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, 0x30, 0x00, 0x00, 0x00, 0x50,
        //             0x10, 0x84, item1_1, item1_2, item1_3, 0x00, 0x08, 0x00, 0x00, 0x80, 0x82, 0x20, 0x08, item2_1,
        //             item2_2, item2_3, 0x2C, 0x00, 0x00, 0x00, 0x14, 0x04, 0x61, item3_1, item3_2, item3_3, 0x80, 0x19,
        //             0x00, 0x00, 0x00
        //         ];
        //
        //         break;
        //     default:
        //         Console.WriteLine($"Item list for count {Contents.Count} not implemented");
        //
        //         return;
        // }
        //
        // global::Client.TryFindClientByIdAndSendData(clientId, itemList);
    }

    public byte[] GetContentsPacket (ushort clientId)
    {
        var items = Contents.Select(x => DbConnection.Items.FindById(x.Value)).ToList();
        return Packet.ItemsToPacket(clientId, Id, items);
    }
}