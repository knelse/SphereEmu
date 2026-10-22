using System;
using System.Linq;
using System.Threading.Tasks;
using SphereHelpers.Extensions;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class UseItemHandler (ushort localId, ClientConnection clientConnection, SphereClient client)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        var itemId = (ushort) (frame[11] + frame[12] * 0x100);
        // Item use arms g_6008, so this ack clears it or the client stays wedged
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));

        var character = clientConnection.GetSelectedCharacter ();

        // The newbie dungeon sword and token are not DB items
        if (NewPlayerDungeon.StarterSwordIdFor (client) == itemId)
        {
            NewPlayerDungeon.TakeStarterSword (client);
            return;
        }

        if (NewPlayerDungeon.TokenIdFor (client) == itemId)
        {
            NewPlayerDungeon.TakeToken (client);
            return;
        }

        var item = DbConnection.Items.FindById ((int) itemId);
        if (character is null || item is null)
        {
            Log (itemId, "?", "no character or no such item");
            return;
        }

        var name = item.Localization.GetValueOrDefault (Locale.Russian, "?");
        if (!character.Items.Any (x => x.Value == item.Id))
        {
            if (item.DroppedOnGroundBy is not null)
            {
                PickupItemHandler.PickUpFromGround (client, clientConnection, item);
                return;
            }

            Log (itemId, name, "not carried");
            return;
        }

        var from = character.Items.First (x => x.Value == item.Id).Key;
        var wearing = !ItemDbEntry.IsInventorySlot (from);
        var to = wearing ? character.FindEmptyInventorySlot () : WearSlotFor (character, item);

        // tokenst Use sends the telep1 process the player stands on as a u24 after the item id
        if (item.WireObjectType == ObjectType.Token_Tutorial_Torweal)
        {
            var identity = SphServer.Helpers.Networking.GameplayRecord.ReadIdentity (frame);
            var targetBit = 165 + (identity.PositionFlag ? 45 : 0);
            if (frame.Length * 8 >= targetBit + 24)
            {
                var stream = new BitStreams.BitStream (frame);
                stream.ReadBits (targetBit);
                NewPlayerDungeon.UseExitToken (client, item, (ushort) stream.ReadUInt32 (24));
            }

            return;
        }

        if (to is null)
        {
            Log (itemId, name, wearing ? "inventory is full" : "nowhere to wear it");
            return;
        }

        // The occupant of the destination takes the slot this item is leaving
        var displaced = character.Items.TryGetValue (to.Value, out var occupant) ? occupant : (int?) null;

        var lookBefore = CharacterWornLook.Capture (character);
        character.PlaceItemInSlot (to.Value, item.Id);

        // The source slot still has a handle: the swap, or an explicit clear
        byte[]? vacated;
        if (displaced is { } displacedId)
        {
            character.PlaceItemInSlot (from, displacedId);
            vacated = ItemSlotReserve.Build (localId, from, displacedId);
        }
        else
        {
            vacated = ItemSlotReserve.Build (localId, from, ItemSlotReserve.NoItem);
        }

        if (vacated is not null)
        {
            clientConnection.MaybeScheduleNetworkPacketSend (vacated);
        }

        var claimed = ItemSlotReserve.Build (localId, to.Value, item.Id, item.ItemCount);
        if (claimed is not null)
        {
            clientConnection.MaybeScheduleNetworkPacketSend (claimed);
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

        Log (itemId, name, displaced is null
            ? $"{Enum.GetName (from)} -> {Enum.GetName (to.Value)}"
            : $"{Enum.GetName (from)} <-> {Enum.GetName (to.Value)}");
    }

    private static BelongingSlot? WearSlotFor (CharacterDbEntry character, ItemDbEntry item)
    {
        BelongingSlot? fallback = null;

        foreach (var slot in Enum.GetValues<BelongingSlot> ())
        {
            if (ItemDbEntry.IsInventorySlot (slot) || !item.IsValidForSlot (slot))
            {
                continue;
            }

            // Empty legal slot wins; an occupied one is only the fallback
            if (!character.Items.ContainsKey (slot))
            {
                return slot;
            }

            fallback ??= slot;
        }

        return fallback;
    }

    private void Log (ushort itemId, string name, string outcome)
    {
        SphLogger.Info ($"UseItem: Source [{localId:X4}] - Target [{itemId:X4}] - Item [{name}] - [{outcome}]");
    }
}
