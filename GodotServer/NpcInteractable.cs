using System;
using System.Collections.Generic;
using System.Linq;
using BitStreams;
using Godot;
using SphServer;
using SphServer.DataModels;
using SphServer.Helpers;
using SphServer.Packets;

public enum NpcType
{
    TradeMagic,
    TradeAlchemy,
    TradeWeapon,
    TradeJewelry,
    TradeArmor,
    TradeTravelGeneric,
    TradeTravelTokens,
    TradeTavernkeeper,
    QuestTitle,
    QuestDegree,
    QuestKarma,
    Guilder,
    Banker,
    Prefix,
    Tournament
}

public enum VendorLocationType
{
    City,
    Outside,
    Castle
}

public static class NpcInteractableMappings
{
    public static int NpcTypeToNpcTradeTypeSph (NpcType npcType)
    {
        return npcType switch
        {
            NpcType.TradeMagic => 9,
            NpcType.TradeAlchemy => 6,
            NpcType.TradeWeapon => 11,
            NpcType.TradeJewelry => 8,
            NpcType.TradeArmor => 7,
            NpcType.TradeTavernkeeper => 5,
            NpcType.TradeTravelGeneric => 10,
            NpcType.TradeTravelTokens => 10,
            NpcType.QuestTitle => 4,
            NpcType.QuestDegree => 2,
            NpcType.QuestKarma => 3,
            NpcType.Guilder => 1,
            NpcType.Banker => 0,
            NpcType.Prefix => 12,
            NpcType.Tournament => 13,
            _ => 0
        };
    }
}

public partial class NpcInteractable : WorldObject
{
    [Export] public int NameID { get; set; } = 4016;
    [Export] public string ModelName { get; set; } = string.Empty;
    public string ModelNameSph => ModelName + "\0";
    [Export] public string IconName { get; set; } = string.Empty;
    public string IconNameSph => IconName + "\0";
    public int IconNameLength => IconNameSph.Length;

    [Export] public NpcType NpcType { get; set; }
    [Export] public int VendorItemTierMin { get; set; }
    [Export] public int VendorItemTierMax { get; set; }
    [Export] public VendorLocationType VendorLocationType { get; set; }
    private readonly List<Item> ItemsOnSale = [];

    public override void _Ready ()
    {
        base._Ready();
        GenerateItemsForSale();
    }

    public override List<PacketPart> GetPacketParts ()
    {
        return PacketPart.LoadDefinedPartsFromFile(NpcType);
    }

    public override List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        PacketPart.UpdateValue(packetParts, "name_id", NameID - 4000, 11);
        var modelName = ModelNameSph;
        if (NpcType is NpcType.Guilder)
        {
            modelName = modelName.PadRight(16, '\0');
        }

        PacketPart.UpdateValue(packetParts, "entity_type_name_length", modelName.Length, 8);
        PacketPart.UpdateValue(packetParts, "entity_type_name", modelName);
        PacketPart.UpdateValue(packetParts, "icon_name_length", IconNameLength, 8);
        PacketPart.UpdateValue(packetParts, "icon_name", IconNameSph);
        var tradeType = NpcInteractableMappings.NpcTypeToNpcTradeTypeSph(NpcType);
        PacketPart.UpdateValue(packetParts, "npc_trade_type", tradeType, 4);
        return packetParts;
    }

    public override byte[] PostprocesPacketBytes (byte[] packet)
    {
        packet[^1] = 0;
        return packet;
    }

    public override void ClientInteract (ushort clientID,
        ClientInteractionType interactionType = ClientInteractionType.Unknown)
    {
        Console.WriteLine($"FROM NPC: Client [{clientID}] interacts with [{ID}] {ObjectType} -- {interactionType}");
        switch (interactionType)
        {
            case ClientInteractionType.OpenTrade:
                ShowItemList(clientID);
                ShowItemContents(clientID);
                break;
            default:
                break;
        }
    }

    private void ShowItemList (ushort clientId)
    {
        var localId = Client.GetLocalObjectId(clientId, ID);
        var stream = BitHelper.GetWriteBitStream();
        stream.WriteUInt16(localId);
        stream.WriteByte(0, 2);
        stream.WriteUInt16((ushort) ObjectType, 10);
        stream.WriteByte(0, 1);
        // interaction
        stream.WriteByte(0x0A, 8);
        // open container
        stream.WriteUInt16(0x0103, 16);
        stream.WriteByte(0, 8);

        var itemSeparator = (ushort) 0b110000000001010;

        for (var i = 0; i < ItemsOnSale.Count; i++)
        {
            var item = ItemsOnSale[i];
            stream.WriteUInt16(itemSeparator, 15);
            stream.WriteByte((byte) (i + 1), 8);
            var itemLocalId = Client.GetLocalObjectId(clientId, item.Id);
            stream.WriteUInt16(itemLocalId);
            stream.WriteBytes([0x00, 0x00, 0x00, 0x00, 0x00], 5, true);
            stream.WriteUInt32((uint) item.VendorCost);
        }

        stream.WriteByte(0x3F, 7);
        stream.WriteUInt16(clientId);
        stream.WriteBytes([0b00001000, 0b01000000, 0b10100011, 0b01100010], 4, true);
        stream.WriteByte(0x0, 5);
        stream.WriteUInt16(localId);
        stream.WriteByte(0x0, 7);

        var streamData = stream.GetStreamData();
        // I have no idea why but it becomes 0xFC in some cases, because value pops at stream.WriteByte(0x0, 5)
        // and keeps getting shifted back to end of stream
        if (streamData[^1] != 0)
        {
            streamData = streamData[..^1];
        }

        var packet = Packet.ToByteArray(streamData, 3);
        Client.TryFindClientByIdAndSendData(clientId, packet);
    }

    private void ShowItemContents (ushort clientId)
    {
        var stream = BitHelper.GetWriteBitStream();
        for (var i = 0; i < ItemsOnSale.Count; i++)
        {
            var item = ItemsOnSale[i];
            WriteItemPacketToStream(clientId, item, stream);
            if (i > 0 && i % 5 == 0)
            {
                // live splits items into batches of 5 and client seems to break if we send more than 10 at a time
                var packetOfFive = Packet.ToByteArray(stream.GetStreamData(), 3);
                stream.CutStream(0, 0);
                Console.WriteLine(Convert.ToHexString(packetOfFive));
                Client.TryFindClientByIdAndSendData(clientId, packetOfFive);
                continue;
            }

            if (i != ItemsOnSale.Count - 1)
            {
                var delimiter =
                    GameObjectDataHelper.WeaponsAndArmor.Contains(item.GameObjectType) ||
                    item.ObjectType is ObjectType.MantraBookSmall or ObjectType.MantraBookLarge
                        or ObjectType.MantraBookGreat
                        ? 0x7F
                        : 0x7E;
                stream.WriteByte((byte) delimiter);
            }
        }

        if (stream.Bit != 0)
        {
            // 1s would be left at the end if we don't fill
            stream.WriteByte(0, 8 - stream.Bit);
        }

        var packet = Packet.ToByteArray(stream.GetStreamData(), 3);
        Console.WriteLine(Convert.ToHexString(packet));
        Client.TryFindClientByIdAndSendData(clientId, packet);
    }

    private void WriteItemPacketToStream (ushort clientId, Item item, BitStream stream)
    {
        var packetParts = PacketPart.LoadDefinedPartsFromFile(item.ObjectType == ObjectType.Unknown
            ? item.GameObjectType.GetPacketObjectType()
            : item.ObjectType);
        if (item.ObjectType != ObjectType.Ring)
        {
            PacketPart.UpdateCoordinates(packetParts, 1000000, 0, 0, 0);
            var localId = Client.GetLocalObjectId(clientId, item.Id);
            PacketPart.UpdateEntityId(packetParts, localId);
            PacketPart.UpdateValue(packetParts, "game_object_id", item.GameId, 14);
            PacketPart.UpdateValue(packetParts, "container_id", item.ParentContainerId ?? 0xFF00, 16);
            if (item.ItemCount > 1)
            {
                PacketPart.UpdateValue(packetParts, "count", item.ItemCount, 15);
            }

            if (item.Suffix != ItemSuffix.None)
            {
                PacketPart.UpdateValue(packetParts, "suffix",
                    GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual[item.GameObjectType][item.Suffix].value, 7);
            }
        }

        foreach (var part in packetParts)
        {
            stream.WriteBits(part.Value);
        }
    }

    private void GenerateItemsForSale ()
    {
        var itemsOnSale = new List<Item>();

        switch (NpcType)
        {
            case NpcType.TradeJewelry:
                GenerateItemsForSaleJewelry(itemsOnSale);
                break;
        }

        if (itemsOnSale.Count == 0)
        {
            for (var i = 0; i < 20; i++)
            {
                var item = Item.CreateFromGameObject(SphObjectDb.GameObjectDataDb[3400 + i]);
                ItemsOnSale.Add(item);
            }
        }

        foreach (var item in itemsOnSale)
        {
            item.ParentContainerId = ID;
            item.Id = MainServer.GetNewWorldObjectIndex();
            ItemsOnSale.Add(item);
        }
    }

    private void GenerateItemsForSaleJewelry (List<Item> itemsOnSale)
    {
        itemsOnSale.Add(new Item
        {
            ObjectType = ObjectType.MantraBookSmall,
            Weight = 200,
            VendorCost = 350
        });
        var ringGo = SphObjectDb.GameObjectDataDb[GetGameIdForTier(ObjectType.Ring, 1)];
        var ringGo2 = SphObjectDb.GameObjectDataDb[GetGameIdForTier(ObjectType.Ring, 1)];
        var clone = SphGameObject.CreateFromGameObject(ringGo);
        clone.Suffix = ItemSuffix.Absorption;
        var ring = Item.CreateFromGameObject(clone);
        ring.Id = 20000;
        ring.ObjectType = ObjectType.Ring;
        itemsOnSale.Add(ring);
        var clone2 = SphGameObject.CreateFromGameObject(ringGo2);
        clone2.Suffix = ItemSuffix.Absorption;
        var ring2 = Item.CreateFromGameObject(clone2);
        ring2.Id = 20001;
        ring2.ObjectType = ObjectType.Ring;
        itemsOnSale.Add(ring2);
        // itemsOnSale.Add(Item.CreateFromGameObject(SphObjectDb.GameObjectDataDb[GetGameIdForTier(ObjectType.Ring, 1)]));
        // itemsOnSale.Add(Item.CreateFromGameObject(SphObjectDb.GameObjectDataDb[GetGameIdForTier(ObjectType.Ring, 2)]));
        // itemsOnSale.Add(Item.CreateFromGameObject(SphObjectDb.GameObjectDataDb[GetGameIdForTier(ObjectType.Ring, 2)]));
        // itemsOnSale.Add(Item.CreateFromGameObject(SphObjectDb.GameObjectDataDb[GetGameIdForTier(ObjectType.Ring, 3)]));
    }

    private int GetGameIdForTier (ObjectType objectType, int tier)
    {
        var id = 0;
        if (objectType is ObjectType.Ring)
        {
            var ringIdBaseTitle = 4047;
            var ringIdBaseDegree = 4107;
            var idMinTitle = ringIdBaseTitle + (tier - 1) * 5;
            var idMinDegree = ringIdBaseDegree + (tier - 1) * 5;
            var rand = MainServer.Rng.Next(0, 10);
            if (rand >= 5)
            {
                id = idMinDegree + rand - 5;
            }
            else
            {
                id = idMinTitle + rand;
            }
        }

        return id;
    }
}