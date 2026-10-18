using System;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

// 08 40 03 at bytes 13-15; layout from the SphereTools vendorTrade.txt live captures
public class BuyItemFromTargetHandler (ushort localId, ClientConnection clientConnection, SphereClient client)
    : ISphereClientNetworkingHandler
{
    private const int VendorIdBitOffset = 141;
    private const int SlotBitOffset = 165;
    private const int CostPerOneBitOffset = 189;
    private const int QuantityBitOffset = 221;

    public async Task Handle (byte[] frame, double delta)
    {
        // The trade window arms the use timeout the same way a drag does
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));

        var vendorId = (ushort) ReadBits (frame, VendorIdBitOffset, 16);
        var slot = (int) ReadBits (frame, SlotBitOffset, 8);
        var costPerOne = (int) ReadBits (frame, CostPerOneBitOffset, 32);
        // The 25-byte variant ends before the quantity field and buys one
        var quantity = frame.Length * 8 >= QuantityBitOffset + 32
            ? (int) ReadBits (frame, QuantityBitOffset, 32)
            : 1;

        var character = client.CurrentCharacter;
        if (character is null)
        {
            return;
        }

        if (ActiveWorldObjects.Get (vendorId) is not NpcInteractable vendor)
        {
            SphLogger.Warning ($"Buy: no vendor {vendorId:X4}. Client ID: {localId:X4}");
            return;
        }

        // ShowItemList numbers the shop from 1; the cost check catches a list that changed since
        var onSale = slot >= 1 && slot <= vendor.ItemsOnSale.Count ? vendor.ItemsOnSale[slot - 1] : null;
        if (onSale is null || onSale.VendorCost != costPerOne)
        {
            onSale = vendor.ItemsOnSale.FirstOrDefault (x => x.VendorCost == costPerOne) ?? onSale;
        }

        if (onSale is null)
        {
            SphLogger.Warning ($"Buy: vendor {vendorId:X4} has nothing in slot {slot}. Client ID: {localId:X4}");
            return;
        }
        quantity = Math.Clamp (quantity, 1, 10000);
        // The client's price only picks the row; the vendor's own price is charged
        var totalCost = (long) Math.Max (onSale.VendorCost, 0) * quantity;
        var name = onSale.Localization.GetValueOrDefault (Locale.Russian, onSale.GameObjectType.ToString ());

        if (character.Money < totalCost)
        {
            SphLogger.Info ($"Buy: {name} x{quantity} costs {totalCost}, has {character.Money}. Client ID: {localId:X4}");
            return;
        }

        var item = CreatePurchasedItem (onSale, quantity);

        if (client.PutItemInInventoryAndDeclare (item) is not { } inventorySlot)
        {
            DbConnection.Items.Delete (item.Id);
            WorldObjectIndex.Release ((ushort) item.Id);
            SphLogger.Info ($"Buy: inventory full for {name} x{quantity}. Client ID: {localId:X4}");
            return;
        }

        character.Money -= (int) totalCost;
        client.SendMoney ();
        client.SaveCharacter ();

        SphLogger.Info ($"Buy: {name} x{quantity} for {totalCost} from vendor {vendorId:X4} slot {slot} " +
                       $"into {inventorySlot}, money left {character.Money}. Client ID: {localId:X4}");
    }

    private static uint ReadBits (byte[] frame, int bitOffset, int count)
    {
        var stream = new BitStream (frame);
        stream.ReadBits (bitOffset);
        return stream.ReadUInt32 (count);
    }

    /// <summary>
    /// The stored copy gets the bought amount and no parent before it is inserted, otherwise the
    /// record keeps the vendor's count and container
    /// </summary>
    private static ItemDbEntry CreatePurchasedItem (ItemDbEntry onSale, int quantity)
    {
        var item = ItemDbEntry.Clone (onSale, insertIntoItemCollection: false);
        item.Id = WorldObjectIndex.NewItem ();
        item.ItemCount = quantity;
        item.ParentContainerId = null;
        DbConnection.Items.Insert (item.Id, item);
        return item;
    }
}
