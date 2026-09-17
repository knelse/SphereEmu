using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.NpcTrade.ItemsOnSale;

namespace SphServer.Sphere.Game.WorldObject;

public partial class NpcInteractable
{
	protected override void ClientInteract(ushort clientID,
		ClientInteractionType interactionType = ClientInteractionType.Unknown)
	{
		SphLogger.Info($"FROM NPC: Client [{clientID:X4}] interacts with [{ID}] {ObjectType} -- {interactionType}");
		switch (interactionType)
		{
			case ClientInteractionType.OpenTrade:
				EnsureShopStock();
				ShowItemList(clientID);
				ShowItemContents(clientID);
				break;
			default:
				break;
		}
	}

	private void EnsureShopStock()
	{
		if (ItemsOnSale.Count > 0 || !IsTradeNpc())
		{
			return;
		}

		if (VendorItemTierMax == 0 || VendorItemTierMin == 0)
		{
			return;
		}

		GenerateItemsForSale();
	}

	private void GenerateItemsForSale()
	{
		List<ItemDbEntry> itemsOnSale = NpcType switch
		{
			NpcType.TradeJewelry => ItemsOnSaleGenerator.Jewelry(VendorItemTierMin, VendorItemTierMax),
			NpcType.TradeTravelGeneric => ItemsOnSaleGenerator.TravelGeneric(VendorItemTierMin, VendorItemTierMax),
			NpcType.TradeWeapon => ItemsOnSaleGenerator.Weapons(VendorItemTierMin, VendorItemTierMax),
			NpcType.TradeArmor => ItemsOnSaleGenerator.Armor(VendorItemTierMin, VendorItemTierMax),
			NpcType.TradeAlchemy => ItemsOnSaleGenerator.Alchemy(VendorItemTierMin, VendorItemTierMax),
			NpcType.TradeMagic => ItemsOnSaleGenerator.Magic(VendorItemTierMin, VendorItemTierMax),
			_ => []
		};

		if (itemsOnSale.Count == 0)
		{
			for (var i = 0; i < 20; i++)
			{
				itemsOnSale.Add(ItemDbEntry.CreateFromGameObject(SphObjectDb.GameObjectDataDb[3400 + i]));
			}
		}

		foreach (var item in itemsOnSale.Take(MaxDisplayedShopItems))
		{
			item.ParentContainerId = ID;
			item.Id = WorldObjectIndex.NewItem();
			ItemsOnSale.Add(item);
		}
	}

	public int GetMaxItemsOnSale()
	{
		return Math.Min(ItemsOnSale.Count, MaxDisplayedShopItems);
	}

	private void ShowItemList(ushort clientId)
	{
		var output = serializer!.ShowItemList(clientId);
		FindClientAndScheduleSend(output, clientId);
	}

	private void FindClientAndScheduleSend(byte[] packet, ushort clientId)
	{
		var client = ActiveClients.Get(clientId);
		if (client is null)
		{
			SphLogger.Warning($"Unable to find client with ID: {clientId:X4} when trading with NPC ID: {ID:X4}");
			return;
		}

		client.MaybeQueueNetworkPacketSend(packet);
	}

	private void ShowItemContents(ushort clientId)
	{
		var output = serializer!.ShowItemContents(clientId);
		FindClientAndScheduleSend(output, clientId);
	}
}
