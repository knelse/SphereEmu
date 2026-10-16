using System;
using System.Threading.Tasks;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.Chat.Encoders;
using SphServer.Shared.WorldState;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class MoveItemHandler (Guid entityId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        // ideally we'd support swapping items but client simply doesn't send anything if slot is
        // occupied
        // var clientID_1 = ReceiveBuffer[11];
        // var clientID_2 = ReceiveBuffer[12];
        var newSlotRaw = frame[21];
        var oldSlotRaw = frame[22];
        var oldSlotId = frame[22] >> 1;
        var newSlotId = frame[21] >> 1;

        var character = clientConnection.GetSelectedCharacter ()!;
        SphLogger.Info (
            $"Move to another slot request: from [{Enum.GetName (typeof (BelongingSlot), oldSlotId)}] " +
            $"to [{Enum.GetName (typeof (BelongingSlot), newSlotId)}]");
        var targetSlot = Enum.IsDefined (typeof (BelongingSlot), newSlotId)
            ? (BelongingSlot) newSlotId
            : BelongingSlot.Unknown;
        var oldSlot = Enum.IsDefined (typeof (BelongingSlot), oldSlotId)
            ? (BelongingSlot) oldSlotId
            : BelongingSlot.Unknown;

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
                              $"which is not in the database. Client ID: {entityId}");
            return;
        }

        // Requirements gate wearing, not carrying: anything may sit in a cell.
        if (!item.IsValidForSlot (targetSlot) ||
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

        if (returnToOldSlot)
        {
            newSlotRaw = oldSlotRaw;
        }

        SphLogger.Info ($"Item found: {globalOldItemId}");
        var destination = returnToOldSlot ? oldSlot : targetSlot;
        var moveResult = ItemSlotReserve.BuildMove (entityId, oldSlot, destination, globalOldItemId);
        if (!returnToOldSlot)
        {
            var lookBefore = CharacterWornLook.Capture (character);
            character.Items[targetSlot] = globalOldItemId;
            character.Items.Remove (oldSlot);
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
                ActiveClients.Get (entityId)?.BroadcastAppearanceRefreshToVisibleClients ();
            }
        }

        clientConnection.MaybeScheduleNetworkPacketSend (moveResult);
    }
}
