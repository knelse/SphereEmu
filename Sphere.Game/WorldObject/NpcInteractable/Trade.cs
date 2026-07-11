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
				ShowItemList(clientID);
				ShowItemContents(clientID);
				break;
			default:
				break;
		}
	}

	private void GenerateItemsForSale()
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

	public int GetMaxItemsOnSale()
	{
		return Math.Min(ItemsOnSale.Count, 74);
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
