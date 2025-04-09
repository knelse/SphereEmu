using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LiteDB;
using SphServer;
using SphServer.DataModels;
using SphServer.Packets;
using static SphServer.Helpers.BitHelper;

public class ItemContainer
{
    [BsonId] public int Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Angle { get; set; }
    public int TitleMinusOne { get; set; }
    public int DegreeMinusOne { get; set; }

    [BsonIgnore]
    private static readonly PackedScene LootBagScene = (PackedScene) ResourceLoader.Load("res://LootBag.tscn");

    public Dictionary<int, int> Contents { get; set; } = new ();
    public ulong? ParentNodeId { get; set; }

    public static ItemContainer Create (double x, double y, double z, int level, //int sourceTypeId,
        LootRatity ratity, int count = -1)
    {
        var bag = LootBagScene.Instantiate<LootBagNode>();
        MainServer.ActiveNodes[bag.GetInstanceId()] = bag;
        var levelOverride = MainServer.Rng.Next(0, 61);
        bag.ItemContainer = new ItemContainer
        {
            TitleMinusOne = (byte) level,
            X = x,
            Y = y,
            Z = z,
            ParentNodeId = bag.GetInstanceId()
        };
        bag.ItemContainer.Id = MainServer.ItemContainerCollection.Insert(bag.ItemContainer);

        var itemCount = count == -1 ? MainServer.Rng.Next(1, 5) : count;

        for (var i = 0; i < itemCount; i++)
        {
            var randomObj = LootHelper.GetRandomObjectData(levelOverride > 0 ? levelOverride : level);
            var item = Item.CreateFromGameObject(randomObj);
            item.ParentContainerId = bag.ItemContainer.Id;
            MainServer.ItemCollection.Insert(item);
            bag.ItemContainer.Contents[i] = item.Id;
        }

        bag.Transform = bag.Transform.Translated(new Vector3((float) x, (float) y, (float) z));
        MainServer.MainServerNode.CallDeferred("add_child", bag);
        MainServer.ItemContainerCollection.Update(bag.ItemContainer);

        return bag.ItemContainer;
    }

    private bool RemoveIfEmpty ()
    {
        if (Contents.Any())
        {
            return false;
        }

        if (ParentNodeId != null)
        {
            MainServer.ActiveNodes[ParentNodeId.Value].QueueFree();
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

        MainServer.ItemContainerCollection.Update(Id, this);

        return RemoveIfEmpty();
    }

    public bool RemoveItemBySlotIdAndDestroyContainerIfEmpty (int slotId)
    {
        if (Contents.ContainsKey(slotId))
        {
            Console.WriteLine($"Removed at {slotId} ID {Contents[slotId]} from container {Id}");
            Contents.Remove(slotId);
        }

        MainServer.ItemContainerCollection.Update(Id, this);

        return RemoveIfEmpty();
    }

    public void ShowForEveryClientInRadius ()
    {
        foreach (var client in MainServer.ActiveClients.Values)
        {
            // && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
            // MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
            ShowForClient(client);
        }
    }

    public void UpdatePositionForEveryClientInRadius ()
    {
        foreach (var client in MainServer.ActiveClients.Values)
        {
            // TODO: proper load/unload for client
            // && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
            // MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
            client.MoveEntity(X, -Y, Z, Angle, client.GetLocalObjectId(Id));
        }
    }

    public void ShowForClient (Client client)
    {
        var packetParts = PacketPart.LoadDefinedPartsFromFile(ObjectType.SackMobLoot);
        PacketPart.UpdateCoordinates(packetParts, X, Y, Z);
        var localId = client.GetLocalObjectId(Id);
        PacketPart.UpdateEntityId(packetParts, localId);
        var lootBagPacket = PacketPart.GetBytesToWrite(packetParts);

        client.StreamPeer.PutData(lootBagPacket);
    }

    public void ShowFourSlotBagDropitemListForClient (ushort clientId)
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

        var item_0_id = Client.GetLocalObjectId(clientId, Contents.ContainsKey(0) ? Contents[0] : 0);
        var item_1_id = Client.GetLocalObjectId(clientId, Contents.ContainsKey(1) ? Contents[1] : 0);
        var item_2_id = Client.GetLocalObjectId(clientId, Contents.ContainsKey(2) ? Contents[2] : 0);
        var item_3_id = Client.GetLocalObjectId(clientId, Contents.ContainsKey(3) ? Contents[3] : 0);

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
        var localId = Client.GetLocalObjectId(clientId, Id);

        switch (Contents.Count)
        {
            case 1:
                itemList = new byte[]
                {
                    0x19, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(localId), MajorByte(localId), 0x5C,
                    0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, 0x70, 0x0D, 0x00, 0x00, 0x00
                };

                break;
            case 2:
                itemList = new byte[]
                {
                    0x23, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(localId), MajorByte(localId), 0x5C,
                    0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, /*weight*/ 0xC0, 0x00, 0x00,
                    0x00, 0x50, 0x10, 0x84, item1_1, item1_2, item1_3, /*weight*/ 0x00, 0x4B, 0x00, 0x00, 0x00
                };

                break;
            case 3:
                itemList = new byte[]
                {
                    0x2E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(localId), MajorByte(localId), 0x5C,
                    0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, 0x30, 0x00, 0x00, 0x00, 0x50,
                    0x10, 0x84, item1_1, item1_2, item1_3, 0x00, 0x08, 0x00, 0x00, 0x80, 0x82, 0x20, 0x08, item2_1,
                    item2_2, item2_3, 0x2C, 0x00, 0x00, 0x00, 0x00
                };

                break;
            case 4:
                itemList = new byte[]
                {
                    0x38, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(localId), MajorByte(localId), 0x5C,
                    0x46, 0x61, 0x02, 0x00, 0x0A, 0x82, 0x00, item0_1, item0_2, item0_3, 0x30, 0x00, 0x00, 0x00, 0x50,
                    0x10, 0x84, item1_1, item1_2, item1_3, 0x00, 0x08, 0x00, 0x00, 0x80, 0x82, 0x20, 0x08, item2_1,
                    item2_2, item2_3, 0x2C, 0x00, 0x00, 0x00, 0x14, 0x04, 0x61, item3_1, item3_2, item3_3, 0x80, 0x19,
                    0x00, 0x00, 0x00
                };

                break;
            default:
                Console.WriteLine($"Item list for count {Contents.Count} not implemented");

                return;
        }

        Client.TryFindClientByIdAndSendData(clientId, itemList);
    }

    public byte[] GetContentsPacket (ushort clientId)
    {
        var items = Contents.Select(x => MainServer.ItemCollection.FindById(x.Value)).ToList();
        return Packet.ItemsToPacket(clientId, Id, items);
    }
}