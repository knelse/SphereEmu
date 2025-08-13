using System.Threading.Tasks;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class MainhandTakeItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        // TODO: remove kaitai
        // dynamic packet = ReceiveBuffer[0] switch
        // {
        //     0x15 => new MainhandEquipPowder(kaitaiStream),
        //     0x19 => new MainhandEquipSword(kaitaiStream),
        //     0x1b => new MainhandReequipPowderPowder(kaitaiStream),
        //     0x1f => new MainhandReequipPowderSword(kaitaiStream),
        //     0x23 => new MainhandReequipSwordSword(kaitaiStream)
        // };
        //
        // var slotState = (MainhandSlotState) packet.MainhandState;
        // var localItemId = (ushort) packet.EquipItemId;
        //
        // Console.WriteLine($"Mainhand: {Enum.GetName(slotState)} [{localItemId}]");
        //
        // if (slotState == MainhandSlotState.Empty)
        // {
        //     var currentItemId = CurrentCharacter.Items[BelongingSlot.MainHand];
        //     var currentItem = DbConnectionProvider.ItemCollection.FindById(currentItemId);
        //     CurrentCharacter.PAtk -= currentItem.PAtkNegative;
        //     CurrentCharacter.MAtk -= currentItem.MAtkNegativeOrHeal;
        //     CurrentCharacter.Items.Remove(BelongingSlot.MainHand);
        // }
        // else if (slotState == MainhandSlotState.Fists)
        // {
        //     CurrentCharacter.Items[BelongingSlot.MainHand] = CurrentCharacter.Fists.Id;
        // }
        // else
        // {
        //     var itemId = GetGlobalObjectId(localItemId);
        //     var item = DbConnectionProvider.ItemCollection.FindById(itemId);
        //     var currentItem = CurrentCharacter.Items.ContainsKey(BelongingSlot.MainHand)
        //         ? DbConnectionProvider.ItemCollection.FindById(CurrentCharacter.Items[BelongingSlot.MainHand])
        //         : null;
        //     if (currentItem is not null)
        //     {
        //         CurrentCharacter.PAtk -= currentItem.PAtkNegative;
        //         CurrentCharacter.MAtk -= currentItem.MAtkNegativeOrHeal;
        //     }
        //
        //     CurrentCharacter.PAtk += item.PAtkNegative;
        //     CurrentCharacter.MAtk += item.MAtkNegativeOrHeal;
        //     CurrentCharacter.Items[BelongingSlot.MainHand] = itemId;
        //     // UpdateStatsForClient();
        // }
    }
}