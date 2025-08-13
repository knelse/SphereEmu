using System;
using System.Threading.Tasks;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class MoveItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        // ideally we'd support swapping items but client simply doesn't send anything if slot is occupied
        // var clientID_1 = ReceiveBuffer[11];
        // var clientID_2 = ReceiveBuffer[12];
        var newSlotRaw = clientConnection.ReceiveBuffer[21];
        var oldSlotRaw = clientConnection.ReceiveBuffer[22];
        var oldSlotId = clientConnection.ReceiveBuffer[22] >> 1;
        var newSlotId = clientConnection.ReceiveBuffer[21] >> 1;

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

        if (targetSlot is BelongingSlot.Unknown || oldSlot is BelongingSlot.Unknown ||
            !character.Items.ContainsKey(oldSlot))
        {
            SphLogger.Warning($"Item not found in slot [{Enum.GetName(oldSlot)}]");
            returnToOldSlot = true;
        }

        var globalOldItemId = character.Items[oldSlot];

        var item = DbConnection.Items.FindById(globalOldItemId);

        if (!item.IsValidForSlot(targetSlot) || !character.CanUseItem(item))
        {
            SphLogger.Warning($"Item [{globalOldItemId}] couldn't be used in slot [{Enum.GetName(targetSlot)}]");
            returnToOldSlot = true;
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
            // TODO: character state shouldn't be stored in starting dungeon
            // DbConnectionProvider.CharacterCollection.Update(CurrentCharacter.Id, CurrentCharacter);
        }

        clientConnection.MaybeQueueNetworkPacketSend(moveResult);
    }
}