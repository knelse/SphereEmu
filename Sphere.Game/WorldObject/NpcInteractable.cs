// TODO: probably needs refactor and splitting into multiple files

using System;
using System.Collections.Generic;
using Godot;
using SphServer.Packets;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking.WorldObject.Serializers;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.IngameToEmulatorTypeConverters;
using SphServer.Sphere.Game.NpcTrade.ItemsOnSale;

namespace SphServer.Sphere.Game.WorldObject;

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
    [Export] public VendorLocation VendorLocation { get; set; }
    public readonly List<ItemDbEntry> ItemsOnSale = [];

    private NpcInteractableSerializer? serializer;

    public override void _Ready ()
    {
        base._Ready();
        if (VendorItemTierMax == 0 || VendorItemTierMin == 0)
        {
            SphLogger.Warning($"Vendor [{ID}] ({NpcType}) has no item tiers set");
        }
        else
        {
            GenerateItemsForSale();
        }

        serializer = new NpcInteractableSerializer(this);
    }

    protected override List<PacketPart> GetPacketParts ()
    {
        return PacketPart.LoadDefinedPartsFromFile(NpcType);
    }

    protected override List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
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
        var tradeType = NpcTypeToNpcTradeTypeSph.Convert(NpcType);
        PacketPart.UpdateValue(packetParts, "npc_trade_type", tradeType, 4);
        return packetParts;
    }

    protected override byte[] PostprocessPacketBytes (byte[] packet)
    {
        packet[^1] = 0;
        return packet;
    }

    public void ClientInteraction (ushort clientID,
        ClientInteractionType interactionType = ClientInteractionType.Unknown)
    {
        ClientInteract(clientID, interactionType);
    }

    protected override void ClientInteract (ushort clientID,
        ClientInteractionType interactionType = ClientInteractionType.Unknown)
    {
        SphLogger.Info($"FROM NPC: Client [{clientID:X4}] interacts with [{ID}] {ObjectType} -- {interactionType}");
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

    private void GenerateItemsForSale ()
    {
        List<ItemDbEntry> itemsOnSale;

        switch (NpcType)
        {
            case NpcType.TradeJewelry:
                itemsOnSale = ItemsOnSaleGenerator.Jewelry(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeTravelGeneric:
                itemsOnSale = ItemsOnSaleGenerator.TravelGeneric(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeWeapon:
                itemsOnSale = ItemsOnSaleGenerator.Weapons(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeArmor:
                itemsOnSale = ItemsOnSaleGenerator.Armor(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeAlchemy:
                itemsOnSale = ItemsOnSaleGenerator.Alchemy(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeMagic:
                itemsOnSale = ItemsOnSaleGenerator.Magic(VendorItemTierMin, VendorItemTierMax);
                break;
            default:
                itemsOnSale = [];
                break;
        }

        if (itemsOnSale.Count == 0)
        {
            for (var i = 0; i < 20; i++)
            {
                var item = ItemDbEntry.CreateFromGameObject(SphObjectDb.GameObjectDataDb[3400 + i]);
                ItemsOnSale.Add(item);
            }
        }

        foreach (var item in itemsOnSale)
        {
            item.ParentContainerId = ID;
            item.Id = WorldObjectIndex.New();
            ItemsOnSale.Add(item);
        }
    }

    public int GetMaxItemsOnSale ()
    {
        return Math.Min(ItemsOnSale.Count, 74);
    }

    private void ShowItemList (ushort clientId)
    {
        var output = serializer!.ShowItemList(clientId);
        FindClientAndScheduleSend(output, clientId);
    }

    private void FindClientAndScheduleSend (byte[] packet, ushort clientId)
    {
        var client = ActiveClients.Get(clientId);
        if (client is null)
        {
            SphLogger.Warning($"Unable to find client with ID: {clientId:X4} when trading with NPC ID: {ID:X4}");
            return;
        }

        SphLogger.Info(Convert.ToHexString(packet));

        client.MaybeQueueNetworkPacketSend(packet);
    }

    private void ShowItemContents (ushort clientId)
    {
        var output = serializer!.ShowItemContents(clientId);
        FindClientAndScheduleSend(output, clientId);
    }
}