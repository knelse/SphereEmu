using System;
using System.Linq;
using System.Threading.Tasks;
using BitStreams;
using SphServer.Sphere.Game.WorldObject;
using SphereHelpers.Extensions;
using SphServer.Packets;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using static SphServer.Shared.BitStream.SphBitStream;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class PickupItemHandler (ushort localId, ClientConnection clientConnection, SphereClient client)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
    }

    public async Task HandlePickupToNextAvailableEmptySlot (byte[] frame, double delta)
    {
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));
        if (TakeNewPlayerDungeonObject (ReadPickupItemId (frame)))
        {
            return;
        }

        var stream = new BitStream (frame);
        stream.SeekBitOffset (141);
        var item = DbConnection.Items.FindById ((int) stream.ReadUInt16 (16));
        if (item?.DroppedOnGroundBy is not null)
        {
            PickUpFromGround (client, clientConnection, item);
        }
    }

    private static ushort ReadPickupItemId (byte[] frame)
    {
        var stream = new BitStream (frame);
        stream.SeekBitOffset (141);
        return stream.ReadUInt16 (16);
    }

    /// <summary>
    /// The newbie dungeon sword and token are not DB items: no 0x2E reply, the dungeon swaps them
    /// for real items itself
    /// </summary>
    private bool TakeNewPlayerDungeonObject (ushort itemId)
    {
        if (NewPlayerDungeon.StarterSwordIdFor (client) == itemId)
        {
            NewPlayerDungeon.TakeStarterSword (client);
            return true;
        }

        if (NewPlayerDungeon.TokenIdFor (client) == itemId)
        {
            NewPlayerDungeon.TakeToken (client);
            return true;
        }

        return false;
    }

    public async Task HandlePickupToTargetSlot (byte[] frame, double delta)
    {
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));
        var clientItemID_1 = frame[21] >> 1;
        var clientItemID_2 = frame[22];
        var clientItemID_3 = frame[23] % 2;
        var clientItemID = (clientItemID_3 << 15) + (clientItemID_2 << 7) + clientItemID_1;

        var globalItemId = (ushort) clientItemID;
        if (TakeNewPlayerDungeonObject (globalItemId))
        {
            return;
        }

        var item = DbConnection.Items.Find (x => x.Id == globalItemId).FirstOrDefault ();

        if (item is null)
        {
            SphLogger.Warning ($"Unable to pickup item. Missing ID: {globalItemId:X4}. Client ID: {localId:X4}");
            return;
        }

        var character = clientConnection.GetSelectedCharacter ()!;

        var clientSlot_raw = frame[24];
        var targetSlotId = clientSlot_raw >> 1;
        var targetSlot = ItemSlotReserve.SlotForWireId (targetSlotId) ?? BelongingSlot.Unknown;
        if (item.DroppedOnGroundBy is not null)
        {
            PickUpFromGround (client, clientConnection, item, targetSlot);
            return;
        }

        // Anything else must still lie in a sack: an id from someone's inventory or a vendor's list
        // would otherwise be copied into the cell
        var oldContainer = item.ParentContainerId is null
            ? null
            : DbConnection.ItemContainers.FindById (item.ParentContainerId);
        if (oldContainer is null || !oldContainer.Contents.ContainsValue (item.Id))
        {
            SphLogger.Warning ($"Pickup to slot: item {item.Id:X4} is not in a sack. Client ID: {localId:X4}");
            return;
        }

        // Requirements gate wearing, not carrying: anything may sit in a cell.
        if (targetSlot is BelongingSlot.Unknown || !character.IsItemSlotEmpty (targetSlot) ||
            !item.IsValidForSlot (targetSlot) ||
            (!ItemDbEntry.IsInventorySlot (targetSlot) && !character.CanUseItem (item)))
        {
            SphLogger.Info (
                $"Item {item.Localization[Locale.Russian]} [{globalItemId}] couldn't be used in slot [{Enum.GetName (targetSlot)}]");
            return;
        }

        SphLogger.Info (
            $"CLI: Move item {item.Localization[Locale.Russian]} ({item.ItemCount}) [{clientItemID}] " +
            $"to slot raw [{clientSlot_raw}] [{Enum.GetName (targetSlot)}]");

        character.PlaceItemInSlot (targetSlot, globalItemId);
        SphLogger.Info ($"{Enum.GetName ((BelongingSlot) targetSlotId)} now has " +
                       $"{item.Localization.GetValueOrDefault (Locale.Russian, "?")} " +
                       $"({item.ItemCount}) [{globalItemId}]");
        clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.EncodePutInto (item, ByteSwap (localId)));

        // The grant above does not fill the slot array, so the cell stays empty without these.
        var reserve = ItemSlotReserve.Build (localId, targetSlot, item.Id, item.ItemCount);
        if (reserve is not null)
        {
            clientConnection.MaybeScheduleNetworkPacketSend (reserve);
        }

        // TODO: check in next process in node instead of this
        if (oldContainer.RemoveItemByIdAndDestroyContainerIfEmpty (globalItemId))
        {
            clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.DespawnEntity ((ushort) oldContainer.Id));
        }

        item.ParentContainerId = null;
        DbConnection.Items.Update (item);

        if (!ItemDbEntry.IsInventorySlot (targetSlot))
        {
            if (character.RecalcCurrentStats ())
            {
                NetworkedStatsUpdater.Update (character);
            }
        }

        // Recalc writes worn look and item stats, so a save before it would miss them
        clientConnection.SaveSelectedCharacter ();
    }

    /// <summary>
    /// A dropped item into the picker's inventory: the wanted cell if it is a free inventory cell,
    /// otherwise the first free one. The reply is the live sack pickup without the sack: the item
    /// takes the player as parent and the player's cell gets the item
    /// </summary>
    public static bool PickUpFromGround (SphereClient client, ClientConnection clientConnection, ItemDbEntry item,
        BelongingSlot? wanted = null)
    {
        var localId = clientConnection.LocalId;
        if (item.DroppedOnGroundBy is null || client.CurrentCharacter is not { } character ||
            GroundItem.Find (item.Id) is not { } groundItem ||
            groundItem.GlobalPosition.DistanceTo (character.Origin) > 10f)
        {
            return false;
        }

        var slot = wanted is { } cell && ItemDbEntry.IsInventorySlot (cell) && character.IsItemSlotEmpty (cell)
            ? cell
            : character.FindEmptyInventorySlot ();
        if (slot is null)
        {
            SphLogger.Info ($"Pickup off the ground: inventory full. Client ID: {localId:X4}");
            return false;
        }

        character.Items[slot.Value] = item.Id;
        clientConnection.SaveSelectedCharacter ();

        clientConnection.MaybeScheduleNetworkPacketSend (ItemRecordEncoder.EncodePutInto (item, ByteSwap (localId)));
        if (ItemSlotReserve.Build (localId, slot.Value, item.Id, item.ItemCount) is { } reserve)
        {
            clientConnection.MaybeScheduleNetworkPacketSend (reserve);
        }

        item.DroppedOnGroundBy = null;
        DbConnection.Items.Update (item);
        groundItem.PickedUp (client);

        SphLogger.Info ($"Picked up {item.Localization.GetValueOrDefault (Locale.Russian, "?")} [{item.Id:X4}] " +
                        $"off the ground into {Enum.GetName (slot.Value)}. Client ID: {localId:X4}");
        return true;
    }

}
