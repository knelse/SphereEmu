using System;
using System.Linq;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

/// <summary>
/// 08 40 03 at bytes 13-15. Vendor id is a guid at bit 141
/// </summary>
public class BuyItemFromTargetHandler (Guid entityId, ClientConnection clientConnection, SphereClient client)
    : ISphereClientNetworkingHandler
{
    private const int VendorIdBitOffset = 141;
    private const int SlotBitOffset = 165 + 112;
    private const int CostPerOneBitOffset = 189 + 112;
    private const int QuantityBitOffset = 221 + 112;

    public async Task Handle (byte[] frame, double delta)
    {
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (entityId));

        var vendorId = ReadGuid (frame, VendorIdBitOffset);
        var slot = (int) ReadBits (frame, SlotBitOffset, 8);
        var costPerOne = (int) ReadBits (frame, CostPerOneBitOffset, 32);
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
            SphLogger.Warning ($"Buy: no vendor {vendorId}. Client ID: {entityId}");
            return;
        }

        var onSale = slot >= 1 && slot <= vendor.ItemsOnSale.Count ? vendor.ItemsOnSale[slot - 1] : null;
        if (onSale is null || onSale.VendorCost != costPerOne)
        {
            onSale = vendor.ItemsOnSale.FirstOrDefault (x => x.VendorCost == costPerOne) ?? onSale;
        }

        if (onSale is null)
        {
            SphLogger.Warning ($"Buy: vendor {vendorId} has nothing in slot {slot}. Client ID: {entityId}");
            return;
        }

        quantity = Math.Clamp (quantity, 1, 10000);
        var totalCost = (long) Math.Max (onSale.VendorCost, 0) * quantity;
        var name = onSale.Localization.GetValueOrDefault (Locale.Russian, onSale.GameObjectType.ToString ());
        if (character.Money < totalCost)
        {
            SphLogger.Info ($"Buy: {name} x{quantity} costs {totalCost}, has {character.Money}. Client ID: {entityId}");
            return;
        }

        var item = ItemDbEntry.Clone (onSale, insertIntoItemCollection: false);
        item.Id = Guid.NewGuid ();
        item.ItemCount = quantity;
        item.ParentContainerId = null;
        DbConnection.Items.Insert (item.Id, item);

        var emptySlot = character.FindEmptyInventorySlot ();
        if (emptySlot is null)
        {
            DbConnection.Items.Delete (item.Id);
            SphLogger.Info ($"Buy: inventory full for {name} x{quantity}. Client ID: {entityId}");
            return;
        }

        character.Items[emptySlot.Value] = item.Id;
        character.Money -= (int) totalCost;
        client.SendMoney ();
        client.SaveCharacter ();
        var reserve = ItemSlotReserve.Build (character.EntityId, emptySlot.Value, item.Id, item.ItemCount);
        if (reserve is not null)
        {
            client.MaybeQueueNetworkPacketSend (reserve);
        }

        client.MaybeQueueNetworkPacketSend (ItemRecordEncoder.Encode (item, client.EntityID));
        SphLogger.Info (
            $"Buy: {name} x{quantity} for {totalCost} from vendor {vendorId} into {emptySlot}, money left {character.Money}. Client ID: {entityId}");
        await Task.CompletedTask;
    }

    private static uint ReadBits (byte[] frame, int bitOffset, int count)
    {
        var stream = new BitStream (frame);
        stream.ReadBits (bitOffset);
        return stream.ReadUInt32 (count);
    }

    private static Guid ReadGuid (byte[] frame, int bitOffset)
    {
        var stream = new BitStream (frame);
        stream.ReadBits (bitOffset);
        return stream.ReadGuid ();
    }
}
