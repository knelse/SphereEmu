using System.Collections.Generic;
using System.Linq;
using LiteDB;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;

namespace SphServer.Shared.Db;

/// <summary>
/// Rewrites stored int ids to guids. A guid is not reused or reassigned
/// </summary>
public static class WorldObjectIdCleanup
{
    public static void MigrateIntIds ()
    {
        if (DbConnection.Db is null)
        {
            return;
        }

        var itemMap = RemapCollectionIds (DbConnection.Db.GetCollection ("Items"));
        var containerMap = RemapCollectionIds (DbConnection.Db.GetCollection ("ItemContainers"));
        RemapContainerContents (containerMap, itemMap);
        RemapItemParents (containerMap);
        RemapCharacters (itemMap);
    }

    public static void SeedAndRepair ()
    {
        ClearMissingItemSlots ();
    }

    private static Dictionary<int, Guid> RemapCollectionIds (ILiteCollection<BsonDocument> collection)
    {
        var map = new Dictionary<int, Guid> ();
        foreach (var doc in collection.FindAll ().ToList ())
        {
            if (doc["_id"].Type is not (BsonType.Int32 or BsonType.Int64))
            {
                continue;
            }

            var oldId = doc["_id"].AsInt32;
            var id = Guid.NewGuid ();
            map[oldId] = id;
            collection.Delete (oldId);
            doc["_id"] = id;
            doc.Remove ("EntityGuid");
            collection.Insert (doc);
        }

        return map;
    }

    private static void RemapContainerContents (Dictionary<int, Guid> containerMap, Dictionary<int, Guid> itemMap)
    {
        if (containerMap.Count == 0)
        {
            return;
        }

        var containers = DbConnection.Db.GetCollection ("ItemContainers");
        foreach (var doc in containers.FindAll ().ToList ())
        {
            if (!doc.TryGetValue ("Contents", out var contents) || !contents.IsDocument)
            {
                continue;
            }

            var changed = false;
            foreach (var key in contents.AsDocument.Keys.ToList ())
            {
                var value = contents[key];
                if (value.IsNumber && itemMap.TryGetValue (value.AsInt32, out var itemId))
                {
                    contents[key] = itemId;
                    changed = true;
                }
            }

            if (changed)
            {
                containers.Update (doc);
            }
        }
    }

    private static void RemapItemParents (Dictionary<int, Guid> containerMap)
    {
        if (containerMap.Count == 0)
        {
            return;
        }

        var items = DbConnection.Db.GetCollection ("Items");
        foreach (var doc in items.FindAll ().ToList ())
        {
            if (!doc.TryGetValue ("ParentContainerId", out var parent) || !parent.IsNumber)
            {
                continue;
            }

            if (!containerMap.TryGetValue (parent.AsInt32, out var parentId))
            {
                continue;
            }

            doc["ParentContainerId"] = parentId;
            items.Update (doc);
        }
    }

    private static void RemapCharacters (Dictionary<int, Guid> itemMap)
    {
        var characters = DbConnection.Db.GetCollection ("Characters");
        foreach (var doc in characters.FindAll ().ToList ())
        {
            var changed = false;
            if (doc.TryGetValue ("ClientIndex", out var index))
            {
                doc["EntityId"] = index.IsNumber ? new BsonValue (Guid.NewGuid ()) : index;
                doc.Remove ("ClientIndex");
                changed = true;
            }

            if (doc.TryGetValue ("Items", out var slots) && slots.IsDocument)
            {
                foreach (var key in slots.AsDocument.Keys.ToList ())
                {
                    var value = slots[key];
                    if (value.IsNumber && itemMap.TryGetValue (value.AsInt32, out var itemId))
                    {
                        slots[key] = itemId;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                characters.Update (doc);
            }
        }
    }

    private static void ClearMissingItemSlots ()
    {
        foreach (var character in DbConnection.Characters.FindAll ().ToList ())
        {
            var changed = false;
            foreach (var (slot, itemId) in character.Items.ToList ())
            {
                if (itemId == Guid.Empty || DbConnection.Items.FindById (itemId) is not null)
                {
                    continue;
                }

                character.Items.Remove (slot);
                changed = true;
                SphLogger.Warning ($"{character.Name}: {slot} pointed at missing item {itemId}, cleared");
            }

            if (changed)
            {
                DbConnection.Characters.Update (character);
            }
        }
    }
}
