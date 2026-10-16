using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.NpcTrade.ItemsOnSale;

namespace SphServer.Sphere.Game.WorldObject;

public partial class NpcInteractable
{
    protected override void ClientInteract (Guid clientID,
        ClientInteractionType interactionType = ClientInteractionType.Unknown)
    {
        SphLogger.Info ($"FROM NPC: Client [{clientID}] interacts with [{EntityID}] {ObjectType} -- {interactionType}");
        switch (interactionType)
        {
            case ClientInteractionType.OpenTrade:
                EnsureShopStock ();
                ShowItemList (clientID);
                ShowItemContents (clientID);
                break;
            default:
                break;
        }
    }

    private void EnsureShopStock ()
    {
        if (ItemsOnSale.Count > 0 || !IsTradeNpc ())
        {
            return;
        }

        if (VendorItemTierMax == 0 || VendorItemTierMin == 0)
        {
            return;
        }

        GenerateItemsForSale ();
    }

    private void GenerateItemsForSale ()
    {
        List<ItemDbEntry> itemsOnSale = NpcType switch
        {
            NpcType.TradeJewelry => ItemsOnSaleGenerator.Jewelry (VendorItemTierMin, VendorItemTierMax),
            NpcType.TradeTravelGeneric => ItemsOnSaleGenerator.TravelGeneric (VendorItemTierMin, VendorItemTierMax),
            NpcType.TradeWeapon => ItemsOnSaleGenerator.Weapons (VendorItemTierMin, VendorItemTierMax),
            NpcType.TradeArmor => ItemsOnSaleGenerator.Armor (VendorItemTierMin, VendorItemTierMax),
            NpcType.TradeAlchemy => ItemsOnSaleGenerator.Alchemy (VendorItemTierMin, VendorItemTierMax),
            NpcType.TradeMagic => ItemsOnSaleGenerator.Magic (VendorItemTierMin, VendorItemTierMax),
            _ => []
        };

        if (itemsOnSale.Count == 0)
        {
            for (var i = 0; i < 20; i++)
            {
                itemsOnSale.Add (ItemDbEntry.CreateFromGameObject (SphObjectDb.GameObjectDataDb[3400 + i]));
            }
        }

        foreach (var item in itemsOnSale.Take (MaxDisplayedShopItems))
        {
            item.ParentContainerId = EntityID;
            item.Id = Guid.NewGuid ();
            ItemsOnSale.Add (item);
        }
    }

    public int GetMaxItemsOnSale ()
    {
        return Math.Min (ItemsOnSale.Count, MaxDisplayedShopItems);
    }

    private void ShowItemList (Guid clientId)
    {
        var output = serializer!.ShowItemList (clientId);
        FindClientAndScheduleSend (output, clientId);
    }

    private void FindClientAndScheduleSend (byte[] packet, Guid clientId)
    {
        var client = ActiveClients.Get (clientId);
        if (client is null)
        {
            SphLogger.Warning ($"Unable to find client with ID: {clientId} when trading with NPC ID: {EntityID}");
            return;
        }

        client.MaybeQueueNetworkPacketSend (packet);
    }

    private void ShowItemContents (Guid clientId)
    {
        var output = serializer!.ShowItemContents (clientId);
        FindClientAndScheduleSend (output, clientId);
    }
}
