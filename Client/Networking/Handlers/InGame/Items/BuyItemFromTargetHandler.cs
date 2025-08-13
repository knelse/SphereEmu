using System.Threading.Tasks;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class BuyItemFromTargetHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        // TODO: remove kaitai
        // var packet = new BuyItemRequest(kaitaiStream);
        // var slotId = (byte) packet.SlotId;
        // var clientId = packet.Header.ClientId;
        //
        // var vendorGlobalId = GetGlobalObjectId((ushort) packet.VendorId);
        // var vendor = DbConnectionProvider.VendorCollection.FindById(vendorGlobalId);
        //
        // if (vendor is null)
        // {
        //     Console.WriteLine($"Unknown vendor [{vendorGlobalId}]");
        //     return;
        // }
        //
        // var item = vendor.ItemsOnSale.Count > slotId ? vendor.ItemsOnSale[slotId] : null;
        //
        // if (item is null)
        // {
        //     Console.WriteLine($"Vendor [{vendorGlobalId}] has nothing in slot [{slotId}]");
        //     return;
        // }
        //
        // var localization = item.ObjectType is GameObjectType.FoodApple ? "Apple" : item.Localisation[Locale.Russian];
        //
        // Console.WriteLine($"Buy request: [{clientId}] slot [{slotId}] {localization} " +
        //                   $"({packet.Quantity}) {packet.CostPerOne}t ea " +
        //                   $"from {vendor.Name} {vendor.FamilyName} {vendorGlobalId}");
        //
        // var clientSlotId = CurrentCharacter.FindEmptyInventorySlot();
        //
        // if (clientSlotId is null)
        // {
        //     Console.WriteLine("No empty slots!");
        //     return;
        // }
        //
        // var totalCost = (int) (packet.Quantity * packet.CostPerOne);
        // if (CurrentCharacter.Money < totalCost)
        // {
        //     Console.WriteLine("Not enough money!");
        //     return;
        // }
        //
        // var clone = Item.Clone(item);
        // clone.ItemCount = (int) packet.Quantity;
        // clone.ParentContainerId = LocalId;
        //
        // var characterUpdateStream = new BitStream(new MemoryStream())
        // {
        //     AutoIncreaseStream = true
        // };
        //
        // CurrentCharacter.Money -= totalCost;
        // CurrentCharacter.Items[clientSlotId.Value] = clone.Id;
        //
        // characterUpdateStream.WriteBytes(
        //     new byte[]
        //     {
        //         0x2B, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(LocalId), MinorByte(LocalId), 0x08, 0x40, 0x41, 0x10
        //     }, 13, true);
        // characterUpdateStream.WriteBit(0);
        // characterUpdateStream.WriteByte((byte) clientSlotId);
        // characterUpdateStream.WriteUInt16(GetLocalObjectId(clone.Id));
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteUInt16((ushort) packet.Quantity);
        // characterUpdateStream.WriteByte(0, 7);
        // characterUpdateStream.WriteBytes(new byte[] { 0x0, 0x1A, 0x38, 0x04 }, 4, true);
        // characterUpdateStream.WriteInt32(CurrentCharacter.Money);
        // characterUpdateStream.WriteByte(0x0D);
        // characterUpdateStream.WriteByte(0x04);
        // characterUpdateStream.WriteByte(0b110, 7);
        // characterUpdateStream.WriteUInt16(GetLocalObjectId(clone.Id));
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteByte(0, 1);
        // characterUpdateStream.WriteUInt16((ushort) packet.Quantity);
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteByte(0, 7);
        // characterUpdateStream.WriteUInt16((ushort) packet.Quantity);
        // characterUpdateStream.WriteByte(0, 1);
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteByte(0);
        // characterUpdateStream.WriteByte(0x32);
        //
        // var characterUpdateResult = characterUpdateStream.GetStreamData();
        // clientConnection.MaybeQueueNetworkPacketSend(characterUpdateResult);
        //
        // var buyResult = Packet.ItemsToPacket(LocalId, clientId, new List<Item> { clone });
        // // buyResult[^1] = 0;
        // clientConnection.MaybeQueueNetworkPacketSend(buyResult);
    }
}