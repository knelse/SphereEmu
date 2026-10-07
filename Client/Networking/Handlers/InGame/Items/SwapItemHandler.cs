using System;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.Chat.Encoders;
using SphServer.Shared.WorldState;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

// The client waits for an answer and resends the same bytes if the swap is ignored
public class SwapItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    /// <summary>
    /// Both item ids, then both slots, then a terminator, 32 bits each
    /// </summary>
    private const int FirstFieldBit = 141;

    private const int FieldBits = 32;

    private const uint NoFurtherItems = 0xFFFFFFFF;

    public async Task Handle (byte[] frame, double delta)
    {
        // Shares its signature with taking an item in hand, which arms the client's use-lock.
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));

        var character = clientConnection.GetSelectedCharacter ();
        if (character is null)
        {
            return;
        }

        var stream = new BitStream (frame);
        stream.ReadBits (FirstFieldBit);
        var firstItemId = stream.ReadUInt32 (FieldBits);
        var secondItemId = stream.ReadUInt32 (FieldBits);
        var firstSlotId = stream.ReadUInt32 (FieldBits);
        var secondSlotId = stream.ReadUInt32 (FieldBits);
        var terminator = stream.ReadUInt32 (FieldBits);

        if (terminator != NoFurtherItems)
        {
            Log ($"read {terminator:X8} where the list ends - [skip]");
            return;
        }

        // Wire slot ids are not the enum for the five slots BelongingSlot numbers from 1000
        if (ItemSlotReserve.SlotForWireId ((int) firstSlotId) is not { } firstSlot ||
            ItemSlotReserve.SlotForWireId ((int) secondSlotId) is not { } secondSlot)
        {
            Log ($"slots [{firstSlotId}] and [{secondSlotId}], one of which is not a slot - [skip]");
            return;
        }

        // The frame is where the client thinks each item sits; a mismatch would move something else
        if (!character.Items.TryGetValue (firstSlot, out var firstHeld) || firstHeld != firstItemId ||
            !character.Items.TryGetValue (secondSlot, out var secondHeld) || secondHeld != secondItemId)
        {
            Log ($"[{firstSlot}] and [{secondSlot}] do not hold {firstItemId} and {secondItemId} - [skip]");
            return;
        }

        var firstItem = DbConnection.Items.FindById ((int) firstItemId);
        var secondItem = DbConnection.Items.FindById ((int) secondItemId);

        if (firstItem is null || secondItem is null)
        {
            Log ($"{firstItemId} or {secondItemId} is not in the database - [skip]");
            return;
        }

        // Each one has to be wearable where the other one was.
        if (!MayGoIn (character, firstItem, secondSlot) || !MayGoIn (character, secondItem, firstSlot))
        {
            Log ($"[{firstSlot}] {Name (firstItem)} <-> [{secondSlot}] {Name (secondItem)} - [refused]");
            RestateSlot (firstSlot, firstItem.Id);
            RestateSlot (secondSlot, secondItem.Id);
            return;
        }

        var lookBefore = CharacterWornLook.Capture (character);
        character.Items[firstSlot] = secondItem.Id;
        character.Items[secondSlot] = firstItem.Id;
        if (firstSlot == BelongingSlot.Guild || secondSlot == BelongingSlot.Guild)
        {
            character.SyncGuildFromWornEmblem ();
            GuildAbilityLoadout.Sync (character, clientConnection.MaybeScheduleNetworkPacketSend);
        }

        if (character.RecalcCurrentStats ())
        {
            NetworkedStatsUpdater.Update (character);
        }

        clientConnection.SaveSelectedCharacter ();

        if (lookBefore != CharacterWornLook.Capture (character))
        {
            ActiveClients.Get (localId)?.BroadcastAppearanceRefreshToVisibleClients ();
        }

        // A second move would clear the slot the first one filled, so the return leg is a binding
        clientConnection.MaybeScheduleNetworkPacketSend (
            ItemSlotReserve.BuildMove (localId, firstSlot, secondSlot, firstItem.Id));
        clientConnection.MaybeScheduleNetworkPacketSend (
            ItemSlotReserve.BuildSlotBinding (localId, firstSlot, secondItem.Id));

        Log ($"[{firstSlot}] {Name (firstItem)} <-> [{secondSlot}] {Name (secondItem)}");
    }

    private bool MayGoIn (CharacterDbEntry character, ItemDbEntry item, BelongingSlot slot)
    {
        if (!item.IsValidForSlot (slot))
        {
            return false;
        }

        // Requirements gate wearing, not carrying.
        if (ItemDbEntry.IsInventorySlot (slot) || character.CanUseItem (item))
        {
            return true;
        }

        // Drag does not check requirements, so the refusal uses the client's own wording
        if (character.UnmetRequirement (item) is { } unmet)
        {
            clientConnection.MaybeScheduleNetworkPacketSend (
                MessageEncoder.EncodeToSendFromServer ($"Нельзя использовать {Name (item)}, {unmet}", "GM",
                    (int) PublicChatType.GM_Outgoing));
        }

        return false;
    }

    /// <summary>
    /// Tells the client the slot still holds what it held, so its picture matches ours.
    /// </summary>
    private void RestateSlot (BelongingSlot slot, int itemId) =>
        clientConnection.MaybeScheduleNetworkPacketSend (
            ItemSlotReserve.BuildSlotBinding (localId, slot, itemId));

    private static string Name (ItemDbEntry item) =>
        item.Localization.GetValueOrDefault (Locale.Russian, "?");

    private void Log (string what) =>
        SphLogger.Info ($"Swap: Source [{localId:X4}] - {what}");
}
