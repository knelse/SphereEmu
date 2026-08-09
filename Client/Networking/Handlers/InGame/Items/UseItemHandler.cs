using System;
using System.Linq;
using System.Threading.Tasks;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class UseItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        var itemId = (ushort) (frame[11] + frame[12] * 0x100);
        // Attack-wedge fix: item use arms the same client use-lock (g_6008) as an attack, so clear it here
        // too. Without this the client wedges permanently after using an item. See CommonPackets.ClearUseToutAck.
        clientConnection.MaybeScheduleNetworkPacketSend(CommonPackets.ClearUseToutAck(localId));

        var character = clientConnection.GetSelectedCharacter();
        var item = DbConnection.Items.FindById((int) itemId);
        if (character is null || item is null)
        {
            Log(itemId, "?", "no character or no such item");
            return;
        }

        var name = item.Localization.GetValueOrDefault(Locale.Russian, "?");
        if (!character.Items.Any(x => x.Value == item.Id))
        {
            // Using something lying in the world is a pickup, which the client asks for separately.
            Log(itemId, name, "not carried");
            return;
        }

        var from = character.Items.First(x => x.Value == item.Id).Key;
        var wearing = !ItemDbEntry.IsInventorySlot(from);
        var to = wearing ? character.FindEmptyInventorySlot() : WearSlotFor(character, item);
        if (to is null)
        {
            Log(itemId, name, wearing ? "inventory is full" : "nowhere to wear it");
            return;
        }

        // Whatever is already in that slot takes the place this item is leaving, so the two trade
        // squares rather than the displaced one landing in the first free cell it can find.
        var displaced = character.Items.TryGetValue(to.Value, out var occupant) ? occupant : (int?) null;

        character.PlaceItemInSlot(to.Value, item.Id);

        // The square it came from still holds a handle to it either way: filled by the swap, or
        // cleared, or it keeps drawing what is no longer there.
        byte[]? vacated;
        if (displaced is { } displacedId)
        {
            character.PlaceItemInSlot(from, displacedId);
            vacated = ItemSlotReserve.Build(localId, from, displacedId);
        }
        else
        {
            vacated = ItemSlotReserve.Build(localId, from, ItemSlotReserve.NoItem);
        }

        if (vacated is not null)
        {
            clientConnection.MaybeScheduleNetworkPacketSend(vacated);
        }

        var claimed = ItemSlotReserve.Build(localId, to.Value, item.Id, item.ItemCount);
        if (claimed is not null)
        {
            clientConnection.MaybeScheduleNetworkPacketSend(claimed);
        }

        if (character.RecalcCurrentStats())
        {
            NetworkedStatsUpdater.Update(character);
        }

        clientConnection.SaveSelectedCharacter();
        Log(itemId, name, displaced is null
            ? $"{Enum.GetName(from)} -> {Enum.GetName(to.Value)}"
            : $"{Enum.GetName(from)} <-> {Enum.GetName(to.Value)}");
    }

    /// <summary>The first free slot this item may be worn in, or null when there is none.</summary>
    private static BelongingSlot? WearSlotFor (CharacterDbEntry character, ItemDbEntry item)
    {
        BelongingSlot? fallback = null;

        foreach (var slot in Enum.GetValues<BelongingSlot>())
        {
            if (ItemDbEntry.IsInventorySlot(slot) || !item.IsValidForSlot(slot))
            {
                continue;
            }

            // An empty one first; otherwise the first that fits, whose occupant is swapped out.
            if (!character.Items.ContainsKey(slot))
            {
                return slot;
            }

            fallback ??= slot;
        }

        return fallback;
    }

    private void Log (ushort itemId, string name, string outcome)
    {
        SphLogger.Info($"UseItem: Source [{localId:X4}] - Target [{itemId:X4}] - Item [{name}] - [{outcome}]");
    }
}
