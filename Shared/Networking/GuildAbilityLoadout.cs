using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.WorldState;

namespace SphServer.Shared.Networking;

/// <summary>
///     Guild specials live in <see cref="BelongingSlot.Special_5"/> / 6 / 7, found by emblem
///     game id +10 / +20 / +30. Catalog misses leave that slot empty.
/// </summary>
public static class GuildAbilityLoadout
{
    private static readonly BelongingSlot[] Slots =
    [
        BelongingSlot.Special_5,
        BelongingSlot.Special_6,
        BelongingSlot.Special_7
    ];

    /// <summary>
    ///     Makes Special_5/6/7 match the worn guild (or clears them when there is none).
    ///     When <paramref name="send"/> is null, only the database and slot map are updated.
    /// </summary>
    public static bool Sync(CharacterDbEntry character, Action<byte[]>? send)
    {
        var desired = DesiredGameIds(character);
        var changed = false;
        for (var i = 0; i < Slots.Length; i++)
        {
            changed |= ApplySlot(character, Slots[i], desired[i], send);
        }

        if (changed)
        {
            ClientStateEvents.RaiseCharacterChanged(character.ClientIndex);
        }

        return changed;
    }

    private static int?[] DesiredGameIds(CharacterDbEntry character)
    {
        var desired = new int?[Slots.Length];
        if (!GuildCatalog.TryGetMembershipGameId(character.Guild, character.GuildLevelMinusOne, out var membershipId))
        {
            return desired;
        }

        var candidates = GuildCatalog.AbilityGameIds(membershipId);
        for (var i = 0; i < Slots.Length; i++)
        {
            if (SphObjectDb.GameObjectDataDb.ContainsKey(candidates[i]))
            {
                desired[i] = candidates[i];
            }
        }

        return desired;
    }

    private static bool ApplySlot(CharacterDbEntry character, BelongingSlot slot, int? gameId,
        Action<byte[]>? send)
    {
        character.Items.TryGetValue(slot, out var currentId);
        var current = currentId != 0 ? DbConnection.Items.FindById(currentId) : null;
        if (gameId is null)
        {
            if (!character.Items.ContainsKey(slot))
            {
                return false;
            }

            ClearSlot(character, slot, current?.Id ?? currentId, send);
            return true;
        }

        if (current?.GameId == gameId)
        {
            return false;
        }

        if (current is not null)
        {
            ClearSlot(character, slot, current.Id, send);
        }

        return PlaceCatalogItem(character, slot, gameId.Value, send);
    }

    private static void ClearSlot(CharacterDbEntry character, BelongingSlot slot, int itemId,
        Action<byte[]>? send)
    {
        character.Items.Remove(slot);
        DbConnection.Items.Delete(itemId);
        Send(send, ItemSlotReserve.Build(character.ClientIndex, slot, ItemSlotReserve.NoItem));
    }

    private static bool PlaceCatalogItem(CharacterDbEntry character, BelongingSlot slot, int gameId,
        Action<byte[]>? send)
    {
        if (!SphObjectDb.GameObjectDataDb.TryGetValue(gameId, out var catalog))
        {
            return false;
        }

        var go = SphGameObject.CreateFromGameObject(catalog);
        go.Suffix = ItemSuffix.None;
        var item = ItemDbEntry.CreateFromGameObject(go);
        item.ItemCount = 1;
        item.Id = WorldObjectIndex.New();
        DbConnection.Items.Insert(item.Id, item);
        character.PlaceItemInSlot(slot, item.Id);

        Send(send, ItemSlotReserve.Build(character.ClientIndex, slot, item.Id, item.ItemCount));
        send?.Invoke(ItemRecordEncoder.Encode(
            (ushort)item.Id, (int)item.WireObjectType, item.GameId,
            ItemRecordEncoder.SuffixWireFor(item),
            SphBitStream.ByteSwap(character.ClientIndex)));
        return true;
    }

    private static void Send(Action<byte[]>? send, byte[]? packet)
    {
        if (send is not null && packet is not null)
        {
            send(packet);
        }
    }
}
