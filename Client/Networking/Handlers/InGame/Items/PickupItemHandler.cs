using System;
using System.Linq;
using System.Threading.Tasks;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class PickupItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        return;
    }

    public async Task HandlePickupToNextAvailableEmptySlot (double delta)
    {
        // TODO: remove kaitai
        // var packet = new PickupItemRequest(kaitaiStream);
        // var clientItemID = (ushort) packet.ItemId;
        // Console.WriteLine($"Pickup request: {clientItemID}");
        //
        // var itemId = GetGlobalObjectId(clientItemID);
        //
        // var item = DbConnectionProvider.ItemCollection.FindById(itemId);
        // var parentId = item?.ParentContainerId;
        // if (parentId is null)
        // {
        //     Console.WriteLine($"Item local [{clientItemID}] global [{itemId}] not found or has no parent container");
        //     return;
        // }
        //
        // var container = DbConnectionProvider.ItemContainerCollection.FindById(parentId);
        //
        // if (container is null)
        // {
        //     Console.WriteLine($"Container [{parentId}] not found");
        //     return;
        // }
        //
        // var slotId = container.Contents.First(x => x.Value == itemId).Key << 1;
        // var packetObjectType = (int) item.ObjectType.GetPacketObjectType();
        // var type_1 = (byte) ((packetObjectType & 0b111111) << 2);
        // var type_2 = (byte) (0b11000000 + (packetObjectType >> 6));
        //
        // var targetSlot = CurrentCharacter.FindEmptyInventorySlot();
        //
        // var targetSlot_1 = (byte) (((int) targetSlot & 0b111111) << 2);
        // var targetSlot_2 = (byte) ((((int) targetSlot >> 6) & 0b11) + ((clientItemID & 0b111111) << 2));
        // var itemId_1 = (byte) ((clientItemID >> 6) & 0b11111111);
        // var itemId_2 = (byte) ((clientItemID >> 14) & 0b11);
        //
        // var clientId_1 = (byte) ((ByteSwap(LocalId) & 0b1111111) << 1);
        // var clientId_2 = (byte) ((ByteSwap(LocalId) >> 7) & 0b11111111);
        // var clientId_3 = (byte) (((ByteSwap(LocalId) >> 15) & 0b1) + 0b00010000);
        //
        // var bagId_1 = (byte) ((ByteSwap(LocalId) & 0b111111) << 2);
        // var bagId_2 = (byte) ((ByteSwap(LocalId) >> 6) & 0b11111111);
        // var bagId_3 = (byte) ((ByteSwap(LocalId) >> 14) & 0b11);
        //
        // var pickupResult = new byte[]
        // {
        //     0x36, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte((ushort) parentId), MajorByte((ushort) parentId), 0x5c,
        //     0x46, 0x41, 0x02, (byte) slotId, 0x7e, MinorByte(clientItemID), MajorByte(clientItemID), type_1, type_2,
        //     0x00, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x02, 0x0C, bagId_1,
        //     bagId_2,
        //     bagId_3, 0xFC, clientId_1, clientId_2, clientId_3, 0x80, 0x82, 0x20, targetSlot_1, targetSlot_2, itemId_1,
        //     itemId_2, 0xC8, 0x00,
        //     0x00, 0x00, 0x00
        // };
        //
        // CurrentCharacter.Items[targetSlot.Value] = itemId;
        //
        // if (container.RemoveItemBySlotIdAndDestroyContainerIfEmpty(slotId >> 1))
        // {
        //     RemoveEntity(GetLocalObjectId(LocalId, container.Id));
        // }
        //
        // StreamPeer.PutData(pickupResult);
    }

    public async Task HandlePickupToTargetSlot (double delta)
    {
        var clientItemID_1 = clientConnection.ReceiveBuffer[21] >> 1;
        var clientItemID_2 = clientConnection.ReceiveBuffer[22];
        var clientItemID_3 = clientConnection.ReceiveBuffer[23] % 2;
        var clientItemID = (clientItemID_3 << 15) + (clientItemID_2 << 7) + clientItemID_1;

        var globalItemId = (ushort) clientItemID;
        var item = DbConnection.Items.Find(x => x.Id == globalItemId).FirstOrDefault();

        if (item is null)
        {
            SphLogger.Warning($"Unable to pickup item. Missing ID: {globalItemId:X4}. Client ID: {localId:X4}");
            return;
        }

        var character = clientConnection.GetSelectedCharacter()!;

        var clientSlot_raw = clientConnection.ReceiveBuffer[24];
        var targetSlotId = clientSlot_raw >> 1;
        var targetSlot = Enum.IsDefined(typeof (BelongingSlot), targetSlotId)
            ? (BelongingSlot) targetSlotId
            : BelongingSlot.Unknown;

        if (targetSlot is BelongingSlot.Unknown || !item.IsValidForSlot(targetSlot) ||
            !character.CanUseItem(item))
        {
            SphLogger.Info(
                $"Item {item.Localization[Locale.Russian]} [{globalItemId}] couldn't be used in slot [{Enum.GetName(targetSlot)}]");
            return;
        }

        var clientSlot = (clientSlot_raw - 0x32) / 2;
        SphLogger.Info(
            $"CLI: Move item {item.Localization[Locale.Russian]} ({item.ItemCount}) [{clientItemID}] to slot raw [{clientSlot_raw}] " +
            $"[{Enum.GetName(typeof (BelongingSlot), clientSlot_raw >> 1)}] actual [{clientSlot}]");

        var clientSync_1 = clientConnection.ReceiveBuffer[17];
        var clientSync_2 = clientConnection.ReceiveBuffer[18];

        var clientSyncOther_1 = (clientConnection.ReceiveBuffer[10] & 0b11000000) >> 4;
        var clientSyncOther_2 = clientConnection.ReceiveBuffer[11];
        var clientSyncOther_3 = clientConnection.ReceiveBuffer[12] & 0b111111;
        var clientSyncOther = (ushort) ((clientSyncOther_3 << 10) + (clientSyncOther_2 << 2) + clientSyncOther_1);

        var serverItemID_1 = (clientItemID & 0b111111) << 2;
        var serverItemID_2 = (clientItemID & 0b11111111000000) >> 6;
        var serverItemID_3 = (clientItemID & 0b1100000000000000) >> 14;

        var moveResult = new byte[]
        {
            0x2E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte((ushort) clientItemID),
            MajorByte((ushort) clientItemID), 0xE8, 0xC7, 0xA0, 0xB0, 0x6E, 0xA6, 0x88, 0x98, 0x95, 0xB1, 0x28, 0x09,
            0xDC, 0x85, 0xC8, 0xDF, 0x02, 0x0C, MinorByte(clientSyncOther), MajorByte(clientSyncOther), 0x01, 0xFC,
            clientSync_1, clientSync_2, 0x10, 0x80, 0x82, 0x20, (byte) (clientSlot_raw << 1), (byte) serverItemID_1,
            (byte) serverItemID_2, (byte) serverItemID_3, 0x20, 0x4E, 0x00, 0x00, 0x00
        };

        character.Items[targetSlot] = globalItemId;
        SphLogger.Info($"{Enum.GetName((BelongingSlot) targetSlotId)} now has {item.Localization[Locale.Russian]} " +
                       $"({item.ItemCount}) [{globalItemId}]");
        clientConnection.MaybeQueueNetworkPacketSend(moveResult);

        var oldContainer = item.ParentContainerId is null
            ? null
            : DbConnection.ItemContainers.FindById(item.ParentContainerId);

        // TODO: check in next process in node instead of this
        if (oldContainer?.RemoveItemByIdAndDestroyContainerIfEmpty(globalItemId) ?? false)
        {
            clientConnection.MaybeQueueNetworkPacketSend(CommonPackets.DespawnEntity((ushort) oldContainer.Id));
        }

        if (!ItemDbEntry.IsInventorySlot(targetSlot))
        {
            if (character.RecalcCurrentStats())
            {
                NetworkedStatsUpdater.Update(character);
            }
        }
    }
}