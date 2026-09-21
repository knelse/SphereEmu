using System.Collections.Generic;
using System.Linq;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Shared.Db;

/// <summary>
///     At launch: reserve every persisted wire id, then split duplicate slot maps and remap
///     items that share a ushort with a container or a stored client index.
/// </summary>
public static class WorldObjectIdCleanup
{
    public static void SeedAndRepair()
    {
        WorldObjectIndex.IsPersistedInUse = IsPersistedWireId;

        var seed = CollectPersistedWireIds();
        WorldObjectIndex.Seed(seed);
        SphLogger.Info($"WorldObjectIndex: reserved {seed.Count} existing wire id(s) from the database");

        var conflicts = Repair();
        if (conflicts.Count == 0)
        {
            SphLogger.Info("WorldObjectIndex: no persisted id conflicts");
            return;
        }

        SphLogger.Warning($"WorldObjectIndex: repaired {conflicts.Count} id conflict(s):");
        foreach (var line in conflicts)
        {
            SphLogger.Warning($"  {line}");
        }
    }

    private static bool IsPersistedWireId(ushort id)
    {
        if (DbConnection.Db is null)
        {
            return false;
        }

        return DbConnection.Items.FindById((int)id) is not null
               || DbConnection.ItemContainers.FindById((int)id) is not null;
    }

    private static HashSet<ushort> CollectPersistedWireIds()
    {
        var ids = new HashSet<ushort>();
        foreach (var item in DbConnection.Items.FindAll().ToList())
        {
            AddWire(ids, item.Id);
        }

        foreach (var bag in DbConnection.ItemContainers.FindAll().ToList())
        {
            AddWire(ids, bag.Id);
            foreach (var itemId in bag.Contents.Values)
            {
                AddWire(ids, itemId);
            }
        }

        foreach (var character in DbConnection.Characters.FindAll().ToList())
        {
            AddWire(ids, character.ClientIndex);
            foreach (var itemId in character.Items.Values)
            {
                AddWire(ids, itemId);
            }
        }

        return ids;
    }

    private static void AddWire(HashSet<ushort> ids, int id)
    {
        if (id is > 0 and <= ushort.MaxValue)
        {
            ids.Add((ushort)id);
        }
    }

    private static List<string> Repair()
    {
        var conflicts = new List<string>();
        RepairMissingAndDuplicateSlots(conflicts);
        RemapItemsCollidingWithContainers(conflicts);
        RemapItemsCollidingWithClientIndex(conflicts);
        DbConnection.Checkpoint();
        return conflicts;
    }

    private static void RepairMissingAndDuplicateSlots(List<string> conflicts)
    {
        foreach (var character in DbConnection.Characters.FindAll().ToList())
        {
            var changed = false;

            foreach (var (slot, itemId) in character.Items.ToList())
            {
                if (DbConnection.Items.FindById(itemId) is not null)
                {
                    continue;
                }

                character.Items.Remove(slot);
                changed = true;
                conflicts.Add(
                    $"{character.Name}: {slot} pointed at missing item {itemId} ({itemId:X4}), cleared");
            }

            var bagSlotsByItem = character.Items
                .Where(kv => kv.Key is not BelongingSlot.MainHand)
                .GroupBy(kv => kv.Value)
                .Where(g => g.Count() > 1);

            foreach (var group in bagSlotsByItem)
            {
                var slots = group.Select(kv => kv.Key).ToList();
                var keep = slots[0];
                var source = DbConnection.Items.FindById(group.Key);
                if (source is null)
                {
                    continue;
                }

                for (var i = 1; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    var clone = ItemDbEntry.Clone(source);
                    character.Items[slot] = clone.Id;
                    changed = true;
                    conflicts.Add(
                        $"{character.Name}: {keep} and {slot} both used item {group.Key} ({group.Key:X4}). " +
                        $"{slot} cloned to {clone.Id} ({clone.Id:X4})");
                }
            }

            if (changed)
            {
                DbConnection.Characters.Update(character);
            }
        }
    }

    private static void RemapItemsCollidingWithContainers(List<string> conflicts)
    {
        var containerIds = DbConnection.ItemContainers.FindAll().ToList()
            .Select(b => b.Id)
            .Where(id => id is > 0 and <= ushort.MaxValue)
            .ToHashSet();

        foreach (var item in DbConnection.Items.FindAll().ToList())
        {
            if (!containerIds.Contains(item.Id))
            {
                continue;
            }

            var oldId = item.Id;
            var newId = RemapItem(item);
            conflicts.Add($"item {oldId} ({oldId:X4}) shared a wire id with a container, remapped to {newId} ({newId:X4})");
        }
    }

    private static void RemapItemsCollidingWithClientIndex(List<string> conflicts)
    {
        var clientIds = DbConnection.Characters.FindAll().ToList()
            .Select(c => (int)c.ClientIndex)
            .Where(id => id > 0)
            .ToHashSet();

        foreach (var item in DbConnection.Items.FindAll().ToList())
        {
            if (!clientIds.Contains(item.Id))
            {
                continue;
            }

            var oldId = item.Id;
            var newId = RemapItem(item);
            conflicts.Add(
                $"item {oldId} ({oldId:X4}) shared a wire id with a character ClientIndex, remapped to {newId} ({newId:X4})");
        }
    }

    private static int RemapItem(ItemDbEntry item)
    {
        var oldId = item.Id;
        var newId = WorldObjectIndex.NewItem();
        DbConnection.Items.Delete(oldId);
        WorldObjectIndex.Release((ushort)oldId);
        item.Id = newId;
        DbConnection.Items.Insert((int)newId, item);

        foreach (var character in DbConnection.Characters.FindAll().ToList())
        {
            var changed = false;
            foreach (var (slot, itemId) in character.Items.ToList())
            {
                if (itemId != oldId)
                {
                    continue;
                }

                character.Items[slot] = newId;
                changed = true;
            }

            if (changed)
            {
                DbConnection.Characters.Update(character);
            }
        }

        foreach (var bag in DbConnection.ItemContainers.FindAll().ToList())
        {
            var changed = false;
            foreach (var (slot, itemId) in bag.Contents.ToList())
            {
                if (itemId != oldId)
                {
                    continue;
                }

                bag.Contents[slot] = newId;
                changed = true;
            }

            if (changed)
            {
                DbConnection.ItemContainers.Update(bag);
            }
        }

        return newId;
    }
}
