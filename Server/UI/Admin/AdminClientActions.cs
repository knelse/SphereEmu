using SphServer.Client;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Server.Config;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Shared admin UI actions: kick / ban / teleport / reset / inventory move.
/// </summary>
public static class AdminClientActions
{
    public static bool Kick(ushort clientId)
    {
        var client = ActiveClients.Get(clientId);
        if (client is null)
        {
            return false;
        }

        AdminActionLog.Info(client, "kicked");
        client.RemoveClient();
        return true;
    }

    public static bool Ban(ushort clientId)
    {
        var client = ActiveClients.Get(clientId);
        if (client is null)
        {
            return false;
        }

        var login = client.GetLogin();
        var ipAddress = client.GetIpAddressWithoutPort();
        if (string.IsNullOrEmpty(login))
        {
            AdminActionLog.Warning(client, "ban failed (login is null)");
            return false;
        }

        AdminActionLog.Info(client, $"banned (login {login})");
        BannedClients.BanClient(login, ipAddress);
        client.RemoveClient();
        return true;
    }

    public static bool Teleport(ushort clientId, WorldCoords worldCoords, string destinationLabel)
    {
        var client = ActiveClients.Get(clientId);
        if (client is null || client.CurrentCharacter is null)
        {
            return false;
        }

        AdminActionLog.Info(client, $"teleported to [{worldCoords}] via \"{destinationLabel}\"");
        var teleportPacket =
            new CharacterDbEntrySerializer(client.CurrentCharacter).GetTeleportByteArray(worldCoords);
        client.MaybeQueueNetworkPacketSend(teleportPacket);
        return true;
    }

    public static bool ResetCharacter(ushort clientId)
    {
        var client = ActiveClients.Get(clientId);
        var character = client?.CurrentCharacter;
        if (client is null || character is null)
        {
            return false;
        }

        var occupied = character.Items.Keys.ToList();
        character.ResetToNewCharacterDefaults();

        foreach (var slot in occupied)
        {
            var reserve = ItemSlotReserve.Build(character.ClientIndex, slot, ItemSlotReserve.NoItem);
            if (reserve is not null)
            {
                client.MaybeQueueNetworkPacketSend(reserve);
            }
        }

        NetworkedStatsUpdater.Update(character);
        client.SaveCharacter();
        AdminActionLog.Info(client, "reset character to new-character defaults");
        return true;
    }

    public static bool SetMoney(ushort clientId, int money)
    {
        var client = ActiveClients.Get(clientId);
        var character = client?.CurrentCharacter;
        if (client is null || character is null)
        {
            return false;
        }

        if (character.Money == money)
        {
            return true;
        }

        var old = character.Money;
        character.Money = money;
        NetworkedStatsUpdater.Update(character);
        client.SaveCharacter();
        AdminActionLog.Info(client, $"set money from {old} to {money}");
        return true;
    }

    public static bool SetKarmaCount(ushort clientId, int karmaCount)
    {
        var client = ActiveClients.Get(clientId);
        var character = client?.CurrentCharacter;
        if (client is null || character is null)
        {
            return false;
        }

        var old = character.KarmaCount;
        character.SetKarmaCount(karmaCount);
        if (character.KarmaCount == old)
        {
            return true;
        }

        NetworkedStatsUpdater.Update(character);
        client.SaveCharacter();
        AdminActionLog.Info(client, $"set karma from {old} to {character.KarmaCount}");
        return true;
    }

    public static bool SetGuild(ushort clientId, Guild guild, int rankMinusOne)
    {
        var client = ActiveClients.Get(clientId);
        var character = client?.CurrentCharacter;
        if (client is null || character is null)
        {
            return false;
        }

        if (guild == Guild.None)
        {
            rankMinusOne = 0;
        }
        else
        {
            rankMinusOne = Math.Clamp(rankMinusOne, 0, (int)GuildRank.Expert);
        }

        var oldGuild = character.Guild;
        var oldRank = character.GuildLevelMinusOne;
        if (oldGuild == guild && oldRank == rankMinusOne)
        {
            return true;
        }

        character.Guild = guild;
        character.GuildLevelMinusOne = rankMinusOne;
        if (character.RecalcCurrentStats())
        {
            NetworkedStatsUpdater.Update(character);
        }

        client.SaveCharacter();
        AdminActionLog.Info(client,
            $"set guild from {oldGuild} rank {oldRank} to {guild} rank {character.GuildLevelMinusOne}");
        return true;
    }

    public static bool ClearSlotItem(ushort clientId, BelongingSlot slot)
    {
        var client = ActiveClients.Get(clientId);
        var character = client?.CurrentCharacter;
        if (client is null || character is null || !character.Items.TryGetValue(slot, out var itemId))
        {
            return false;
        }

        character.Items.Remove(slot);
        DbConnection.Items.Delete(itemId);
        SendSlotBinding(client, character.ClientIndex, slot, item: null);
        if (character.RecalcCurrentStats())
        {
            NetworkedStatsUpdater.Update(character);
        }

        client.SaveCharacter();
        AdminActionLog.Info(client, $"cleared [{slot}] (item {itemId})");
        return true;
    }

    public static bool ReplaceSlotItem(ushort clientId, BelongingSlot slot, int gameId, ItemSuffix suffix)
    {
        var client = ActiveClients.Get(clientId);
        var character = client?.CurrentCharacter;
        if (client is null || character is null
            || !SphObjectDb.GameObjectDataDb.TryGetValue(gameId, out var catalog))
        {
            return false;
        }

        if (character.Items.TryGetValue(slot, out var oldId))
        {
            character.Items.Remove(slot);
            DbConnection.Items.Delete(oldId);
            SendSlotBinding(client, character.ClientIndex, slot, item: null);
        }

        var go = SphGameObject.CreateFromGameObject(catalog);
        go.Suffix = suffix;
        var item = ItemDbEntry.CreateFromGameObject(go);
        item.ItemCount = 1;
        item.Id = WorldObjectIndex.New();
        DbConnection.Items.Insert(item.Id, item);
        character.PlaceItemInSlot(slot, item.Id);

        var reserve = ItemSlotReserve.Build(character.ClientIndex, slot, item.Id, item.ItemCount);
        if (reserve is not null)
        {
            client.MaybeQueueNetworkPacketSend(reserve);
        }

        client.MaybeQueueNetworkPacketSend(ItemRecordEncoder.Encode(
            (ushort)item.Id, (int)item.ObjectType, item.GameId,
            ItemRecordEncoder.SuffixWireFor(item),
            SphBitStream.ByteSwap(character.ClientIndex)));

        if (character.RecalcCurrentStats())
        {
            NetworkedStatsUpdater.Update(character);
        }

        client.SaveCharacter();
        AdminActionLog.Info(client, $"set [{slot}] to game id {gameId} suffix {suffix}");
        return true;
    }

    /// <summary>
    ///     Same gate as client move/swap: the item must fit the slot, and wearing (not carrying)
    ///     also requires <see cref="CharacterDbEntry.CanUseItem"/>. A swap needs both directions.
    /// </summary>
    public static bool CanMoveOrSwapItem(ushort clientId, BelongingSlot from, BelongingSlot to)
    {
        if (from == to)
        {
            return false;
        }

        var character = ActiveClients.Get(clientId)?.CurrentCharacter;
        if (character is null
            || !character.Items.TryGetValue(from, out var fromId)
            || DbConnection.Items.FindById(fromId) is not { } fromItem
            || !MayGoIn(character, fromItem, to))
        {
            return false;
        }

        if (!character.Items.TryGetValue(to, out var toId))
        {
            return true;
        }

        return DbConnection.Items.FindById(toId) is { } toItem && MayGoIn(character, toItem, from);
    }

    public static bool TryMoveOrSwapItem(ushort clientId, BelongingSlot from, BelongingSlot to)
    {
        if (!CanMoveOrSwapItem(clientId, from, to))
        {
            return false;
        }

        var client = ActiveClients.Get(clientId);
        var character = client?.CurrentCharacter;
        if (client is null || character is null
            || !character.Items.TryGetValue(from, out var fromId)
            || DbConnection.Items.FindById(fromId) is not { } fromItem)
        {
            return false;
        }

        if (character.Items.TryGetValue(to, out var toId)
            && DbConnection.Items.FindById(toId) is { } toItem)
        {
            character.Items[from] = toId;
            character.Items[to] = fromId;
            SendSlotBinding(client, character.ClientIndex, from, toItem);
            SendSlotBinding(client, character.ClientIndex, to, fromItem);
            AdminActionLog.Info(client, $"swapped [{from}] <-> [{to}]");
        }
        else
        {
            character.Items[to] = fromId;
            character.Items.Remove(from);
            SendSlotBinding(client, character.ClientIndex, from, item: null);
            SendSlotBinding(client, character.ClientIndex, to, fromItem);
            AdminActionLog.Info(client, $"moved [{from}] -> [{to}]");
        }

        if (character.RecalcCurrentStats())
        {
            NetworkedStatsUpdater.Update(character);
        }

        client.SaveCharacter();
        return true;
    }

    /// <summary>
    ///     Persona: move to the first empty inventory cell (no-op if bags are full).
    ///     Inventory: wear in the first matching persona slot, swapping if it is occupied.
    /// </summary>
    public static bool TryDoubleClickSlot(ushort clientId, BelongingSlot slot)
    {
        var character = ActiveClients.Get(clientId)?.CurrentCharacter;
        if (character is null
            || !character.Items.TryGetValue(slot, out var itemId)
            || DbConnection.Items.FindById(itemId) is not { } item)
        {
            return false;
        }

        var to = ItemDbEntry.IsInventorySlot(slot)
            ? WearSlotFor(character, item)
            : character.FindEmptyInventorySlot();
        return to is not null && TryMoveOrSwapItem(clientId, slot, to.Value);
    }

    /// <summary>First empty wear slot this item fits, else the first occupied one (swap).</summary>
    private static BelongingSlot? WearSlotFor(CharacterDbEntry character, ItemDbEntry item)
    {
        BelongingSlot? fallback = null;
        foreach (var slot in Enum.GetValues<BelongingSlot>())
        {
            if (ItemDbEntry.IsInventorySlot(slot) || !item.IsValidForSlot(slot))
            {
                continue;
            }

            if (!character.Items.ContainsKey(slot))
            {
                return slot;
            }

            fallback ??= slot;
        }

        return fallback;
    }

    private static bool MayGoIn(CharacterDbEntry character, ItemDbEntry item, BelongingSlot slot)
    {
        if (!item.IsValidForSlot(slot))
        {
            return false;
        }

        return ItemDbEntry.IsInventorySlot(slot) || character.CanUseItem(item);
    }

    private static void SendSlotBinding(SphereClient client, ushort clientIndex,
        BelongingSlot slot, ItemDbEntry? item)
    {
        var packet = item is null
            ? ItemSlotReserve.Build(clientIndex, slot, ItemSlotReserve.NoItem)
            : ItemSlotReserve.Build(clientIndex, slot, item.Id, item.ItemCount);
        if (packet is not null)
        {
            client.MaybeQueueNetworkPacketSend(packet);
        }
    }
}
