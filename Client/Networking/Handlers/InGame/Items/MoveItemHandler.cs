using System;
using System.Threading.Tasks;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking.Chat.Encoders;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class MoveItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        // ideally we'd support swapping items but client simply doesn't send anything if slot is occupied
        // var clientID_1 = ReceiveBuffer[11];
        // var clientID_2 = ReceiveBuffer[12];
        var newSlotRaw = frame[21];
        var oldSlotRaw = frame[22];
        var oldSlotId = frame[22] >> 1;
        var newSlotId = frame[21] >> 1;

        var character = clientConnection.GetSelectedCharacter()!;
        SphLogger.Info(
            $"Move to another slot request: from [{Enum.GetName(typeof (BelongingSlot), oldSlotId)}] " +
            $"to [{Enum.GetName(typeof (BelongingSlot), newSlotId)}]");
        var targetSlot = Enum.IsDefined(typeof (BelongingSlot), newSlotId)
            ? (BelongingSlot) newSlotId
            : BelongingSlot.Unknown;
        var oldSlot = Enum.IsDefined(typeof (BelongingSlot), oldSlotId)
            ? (BelongingSlot) oldSlotId
            : BelongingSlot.Unknown;

        var returnToOldSlot = false;

        // Only an empty source slot is unanswerable. An unknown target still has somewhere to go
        // back to, and the slot check below sends it there.
        if (oldSlot is BelongingSlot.Unknown || !character.Items.ContainsKey(oldSlot))
        {
            SphLogger.Warning($"Item not found in slot [{Enum.GetName(oldSlot)}]");
            return;
        }

        var globalOldItemId = character.Items[oldSlot];

        var item = DbConnection.Items.FindById(globalOldItemId);

        if (item is null)
        {
            SphLogger.Warning($"Move: slot [{Enum.GetName(oldSlot)}] points at item {globalOldItemId}, " +
                              $"which is not in the database. Client ID: {localId:X4}");
            return;
        }

        // Requirements gate wearing, not carrying: anything may sit in a cell.
        if (!item.IsValidForSlot(targetSlot) ||
            (!ItemDbEntry.IsInventorySlot(targetSlot) && !character.CanUseItem(item)))
        {
            SphLogger.Warning($"Item [{globalOldItemId}] couldn't be used in slot [{Enum.GetName(targetSlot)}]");
            returnToOldSlot = true;

            // The client checks requirements when an item is used but not when it is dragged, so it
            // says nothing here. Word it the way it words its own refusal.
            if (character.UnmetRequirement(item) is { } unmet)
            {
                var name = item.Localization.GetValueOrDefault(Locale.Russian, "?");
                clientConnection.MaybeScheduleNetworkPacketSend(
                    MessageEncoder.EncodeToSendFromServer($"Нельзя использовать {name}, {unmet}", "GM",
                        (int) PublicChatType.GM_Outgoing));
            }
        }

        if (returnToOldSlot)
        {
            newSlotRaw = oldSlotRaw;
        }

        SphLogger.Info($"Item found: {globalOldItemId}");
        var newSlot_1 = (byte) ((newSlotRaw & 0b11111) << 3);
        var newSlot_2 = (byte) (((globalOldItemId & 0b1111) << 4) + (newSlotRaw >> 5));
        var oldItem_1 = (byte) ((globalOldItemId >> 4) & 0b11111111);
        var oldItem_2 = (byte) (globalOldItemId >> 12);

        var moveResult = new byte[]
        {
            0x20, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(localId), MinorByte(localId), 0x08, 0x40, 0x41, 0x10,
            oldSlotRaw, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x82, newSlot_1, newSlot_2, oldItem_1,
            oldItem_2, 0xC0, 0x44, 0x00, 0x00, 0x00
        };
        if (!returnToOldSlot)
        {
            character.Items[targetSlot] = globalOldItemId;
            character.Items.Remove(oldSlot);

            if (character.RecalcCurrentStats())
            {
                NetworkedStatsUpdater.Update(character);
            }

            // Moving an item is the one way to equip, and it persisted nothing: the slot and the
            // appearance the recalculation just worked out both lived only in memory, so they
            // survived a restart only when some later action happened to save.
            clientConnection.SaveSelectedCharacter();
        }

        clientConnection.MaybeScheduleNetworkPacketSend(moveResult);
    }
}