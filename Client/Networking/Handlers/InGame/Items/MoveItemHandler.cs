using System;
using System.Threading.Tasks;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Server;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.Chat.Encoders;
using SphServer.Shared.WorldState;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class MoveItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    /// <summary>
    /// Payload of player ContMan cmd 5 len 1: the cell number, as in the slot-set message
    /// </summary>
    private const int LeftCellBitOffset = 137;

    /// <summary>
    /// Long enough for a real move's own request to land first
    /// </summary>
    private const double RestoreCellAfterSeconds = 0.5;

    /// <summary>
    /// The inventory window empties a cell on its own when a drag from it ends elsewhere (_inv
    /// DnDRes 1), e.g. on a hotkey, which only keeps a reference: the item never left the server's
    /// slot, so the cell is put back unless a real move took the item away meanwhile
    /// </summary>
    public Task HandleLeftCell (byte[] frame, double delta)
    {
        var stream = new BitStreams.BitStream (frame);
        stream.ReadBits (LeftCellBitOffset);
        var slot = ItemSlotReserve.SlotForWireId (stream.ReadByte (8)) ?? BelongingSlot.Unknown;
        var character = clientConnection.GetSelectedCharacter ();
        if (character is null || !character.Items.TryGetValue (slot, out var itemId))
        {
            SphLogger.Info ($"Cell {Enum.GetName (slot) ?? slot.ToString ()} let go of nothing. Client ID: {localId:X4}");
            return Task.CompletedTask;
        }

        SphereServer.ServerNode.GetTree ().CreateTimer (RestoreCellAfterSeconds).Timeout += () =>
        {
            if (!ReferenceEquals (clientConnection.GetSelectedCharacter (), character))
            {
                return;
            }

            if (!character.Items.TryGetValue (slot, out var stillThere) || stillThere != itemId)
            {
                return;
            }

            var item = DbConnection.Items.FindById (itemId);
            if (ItemSlotReserve.Build (localId, slot, itemId, item?.ItemCount ?? 1) is { } reserve)
            {
                clientConnection.MaybeScheduleNetworkPacketSend (reserve);
            }

            SphLogger.Info ($"Cell {Enum.GetName (slot)} restored with {itemId:X4} after the client let go of it. " +
                            $"Client ID: {localId:X4}");
        };
        return Task.CompletedTask;
    }

    public async Task Handle (byte[] frame, double delta)
    {
        var oldSlotId = frame[22] >> 1;
        var newSlotId = frame[21] >> 1;

        var character = clientConnection.GetSelectedCharacter ()!;
        SphLogger.Info (
            $"Move to another slot request: from [{Enum.GetName (typeof (BelongingSlot), oldSlotId)}] " +
            $"to [{Enum.GetName (typeof (BelongingSlot), newSlotId)}]");
        var targetSlot = ItemSlotReserve.SlotForWireId (newSlotId) ?? BelongingSlot.Unknown;
        var oldSlot = ItemSlotReserve.SlotForWireId (oldSlotId) ?? BelongingSlot.Unknown;

        var returnToOldSlot = false;

        // An unknown target can still be sent back; only a missing source slot is unanswerable
        if (oldSlot is BelongingSlot.Unknown || !character.Items.ContainsKey (oldSlot))
        {
            SphLogger.Warning ($"Item not found in slot [{Enum.GetName (oldSlot)}]");
            return;
        }

        var globalOldItemId = character.Items[oldSlot];

        var item = DbConnection.Items.FindById (globalOldItemId);

        if (item is null)
        {
            SphLogger.Warning ($"Move: slot [{Enum.GetName (oldSlot)}] points at item {globalOldItemId}, " +
                              $"which is not in the database. Client ID: {localId:X4}");
            return;
        }

        // Requirements gate wearing, not carrying: anything may sit in a cell.
        if (targetSlot == BelongingSlot.Unknown ||
            (targetSlot != oldSlot && !character.IsItemSlotEmpty (targetSlot)) ||
            !item.IsValidForSlot (targetSlot) ||
            (!ItemDbEntry.IsInventorySlot (targetSlot) && !character.CanUseItem (item)))
        {
            SphLogger.Warning ($"Item [{globalOldItemId}] couldn't be used in slot [{Enum.GetName (targetSlot)}]");
            returnToOldSlot = true;

            // Drag does not check requirements, so the refusal uses the client's own wording
            if (character.UnmetRequirement (item) is { } unmet)
            {
                var name = item.Localization.GetValueOrDefault (Locale.Russian, "?");
                clientConnection.MaybeScheduleNetworkPacketSend (
                    MessageEncoder.EncodeToSendFromServer ($"Нельзя использовать {name}, {unmet}", "GM",
                        (int) PublicChatType.GM_Outgoing));
            }
        }

        SphLogger.Info ($"Item found: {globalOldItemId}");
        if (!returnToOldSlot)
        {
            var lookBefore = CharacterWornLook.Capture (character);
            character.Items.Remove (oldSlot);
            character.Items[targetSlot] = globalOldItemId;
            if (oldSlot == BelongingSlot.Guild || targetSlot == BelongingSlot.Guild)
            {
                character.SyncGuildFromWornEmblem ();
                GuildAbilityLoadout.Sync (character, clientConnection.MaybeScheduleNetworkPacketSend);
            }

            if (character.RecalcCurrentStats ())
            {
                NetworkedStatsUpdater.Update (character);
            }

            // The worn slot and look exist only in memory until this save
            clientConnection.SaveSelectedCharacter ();

            if (lookBefore != CharacterWornLook.Capture (character))
            {
                ActiveClients.Get (localId)?.BroadcastAppearanceRefreshToVisibleClients ();
            }
        }

        var moveResult = returnToOldSlot
            ? ItemSlotReserve.Build (localId, oldSlot, item.Id, item.ItemCount)
            : ItemSlotReserve.BuildMove (localId, oldSlot, targetSlot, item.Id, item.ItemCount);
        if (moveResult is not null)
        {
            clientConnection.MaybeScheduleNetworkPacketSend (moveResult);
        }
    }
}
