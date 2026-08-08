using System;
using System.Linq;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Shared.BitStream;
using SphServer.Packets;
using SphServer.Server.Config;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Debug.Parser;

/// <summary>Item GM commands, registered in InitCommands (main parser file).</summary>
public partial class ConsoleCommandParser
{
    /// <summary>How far in front of the character (world Z units) /give drops the item.</summary>
    private const double GiveGroundDropOffset = 1.0;

    /// <summary>
    ///     The one capture we have of an item lying in the world, so the only definition shaped the
    ///     way the client expects there: it carries an angle, container 0xFF00, and a game object id.
    ///     The definitions that drew nothing are all records of an item inside a container, which is
    ///     a likelier reason than the game object id they happen to share.
    ///     Override with the third argument.
    /// </summary>
    private const string GiveDefaultSpawnDefinition = "alchemy_resource_ground";

    private void Give (string args)
    {
        var split = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = 1;
        int? objectTypeOverride = null;
        if (split.Length is < 1 or > 4 || !int.TryParse(split[0], out var gameId) ||
            (split.Length >= 2 && (!int.TryParse(split[1], out count) || count < 1)) ||
            (split.Length == 4 && (!int.TryParse(split[3], out var typeArg) || (objectTypeOverride = typeArg) < 0)))
        {
            SendFeedback("Usage: /give <game_object_id> [count] [packet definition] [object_type]");
            return;
        }

        var definition = split.Length >= 3 ? split[2] : GiveDefaultSpawnDefinition;

        if (sphereClient is null)
        {
            SendFeedback("/give needs a connected client.");
            return;
        }

        if (!ServerConfig.AppConfig.DebugMode)
        {
            // The spawn goes through the packet definition pipeline, which is gated by DebugMode;
            // refuse instead of silently inserting a DB row the client never hears about.
            SendFeedback("/give requires DebugMode=true in appsettings.json.");
            return;
        }

        var gameObject = DbConnection.GameObjects.FindById(gameId);
        if (gameObject is null)
        {
            SendFeedback($"Unknown game object id: {gameId}");
            return;
        }

        var item = ItemDbEntry.CreateFromGameObject(gameObject);
        item.ItemCount = count;
        item.X = currentCharacterDbEntry.X;
        item.Y = currentCharacterDbEntry.Y;
        item.Z = currentCharacterDbEntry.Z + GiveGroundDropOffset;
        item.ParentContainerId = null;

        // The entity id sent to the client must equal the row id so a later pickup request
        // resolves, and it must come from the world index: LiteDB's auto-id counts from 1 and
        // would collide with the ids of entities already on screen.
        item.Id = WorldObjectIndex.New();
        DbConnection.Items.Insert(item.Id, item);

        DebugConsole.SendSpherePacket($"/packet {definition}",
            bytes => sphereClient.MaybeQueueNetworkPacketSend(bytes),
            false,
            parts =>
            {
                PacketPart.UpdateEntityId(parts, (ushort) item.Id);

                // 0xFF00 = lying on the ground. The definitions carry whatever container was in
                // the frame they were captured from, which does not exist on this server.
                PacketPart.UpdateValue(parts, "container_id", item.ParentContainerId ?? 0xFF00, 16);

                // Unconditionally: UpdateValue no-ops on a name the definition does not carry, and
                // the default one has object_type but no game_object_id — so guarding this on the
                // latter left every spawn wearing the identity of the frame it was captured from.
                PacketPart.UpdateValue(parts, "object_type",
                    (objectTypeOverride ?? (int) item.ObjectType) & 0x3FF, 10);
                PacketPart.UpdateValue(parts, "game_object_id", gameObject.GameId & 0x3FFF, 14);

                // DB world coords -> client coords: Y and Z are negated.
                PacketPart.UpdateCoordinates(parts, item.X, -item.Y, -item.Z);
            });

        var name = gameObject.Localisation.GetValueOrDefault(Locale.Russian, gameObject.SphereType);
        var countSuffix = count > 1 ? $" x{count} (count is server-side only; the ground shows one item)" : "";
        var typeSuffix = objectTypeOverride is { } t ? $", object_type forced to {t}" : "";
        SendFeedback($"Spawned {name} [game id {gameObject.GameId}, item id {item.Id}]{countSuffix} " +
                     $"on the ground via {definition}{typeSuffix}.");
    }

    private const int DefaultInventoryGameObjectId = 3251;

    /// <summary>
    ///     /giveinv [game object id] — put an item in the first free inventory slot and declare it.
    ///
    ///     Armour only changes the character's appearance when its ground model name carries an
    ///     "@xy@" wear code; the low ids per kind (301, 313, 325, …) have none and sit at tier -1,
    ///     where no vendor stocks them either. Coded examples: 2745 jacket, 2790 helmet, 2760
    ///     shield, 2835 gloves, 2775 pants, 2820 boots.
    /// </summary>
    private void GiveToInventory (string args)
    {
        if (sphereClient is null)
        {
            SendFeedback("/giveinv needs a connected client.");
            return;
        }

        var split = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var gameObjectId = split.Length >= 1 && int.TryParse(split[0], out var requested)
            ? requested
            : DefaultInventoryGameObjectId;

        var gameObject = DbConnection.GameObjects.FindById(gameObjectId);
        if (gameObject is null)
        {
            SendFeedback($"Game object {gameObjectId} is missing from the database.");
            return;
        }

        var emptySlot = currentCharacterDbEntry.FindEmptyInventorySlot();
        if (emptySlot is null)
        {
            // Falling back to the first slot would overwrite it and orphan the item row it named.
            SendFeedback("Inventory is full.");
            return;
        }

        var item = ItemDbEntry.CreateFromGameObject(gameObject);
        item.ItemCount = 1;
        item.Id = WorldObjectIndex.New();
        DbConnection.Items.Insert(item.Id, item);

        var slot = emptySlot.Value;
        currentCharacterDbEntry.Items[slot] = item.Id;
        sphereClient.SaveCharacter();

        // The same two messages the login path sends, so the grid updates without a relog.
        var reserve = ItemSlotReserve.Build(currentCharacterDbEntry.ClientIndex, slot, item.Id, item.ItemCount);
        if (reserve is not null)
        {
            sphereClient.MaybeQueueNetworkPacketSend(reserve);
        }

        sphereClient.MaybeQueueNetworkPacketSend(ItemRecordEncoder.Encode((ushort) item.Id,
            (int) item.ObjectType, item.GameId, ItemRecordEncoder.NoSuffix,
            SphBitStream.ByteSwap(currentCharacterDbEntry.ClientIndex)));

        var name = gameObject.Localisation.GetValueOrDefault(Locale.Russian, gameObject.SphereType);
        SendFeedback($"{name} [item id {item.Id}] put in {Enum.GetName(slot)} and declared.");
    }

    /// <summary>/clearinv — empty every slot, including the hand.</summary>
    private void ClearInventory (string args)
    {
        if (sphereClient is null)
        {
            SendFeedback("/clearinv needs a connected client.");
            return;
        }

        var occupied = currentCharacterDbEntry.Items.Keys.ToList();
        var cleared = occupied.Count;
        currentCharacterDbEntry.Items.Clear();

        // Clear() empties the hand too, so recalculate before saving — the attack of whatever was
        // held is a persisted field and has to go with it.
        currentCharacterDbEntry.RecalcCurrentStats();
        sphereClient.SaveCharacter();

        foreach (var slot in occupied)
        {
            var reserve = ItemSlotReserve.Build(currentCharacterDbEntry.ClientIndex, slot,
                ItemSlotReserve.NoItem);
            if (reserve is not null)
            {
                sphereClient.MaybeQueueNetworkPacketSend(reserve);
            }
        }

        NetworkedStatsUpdater.Update(currentCharacterDbEntry);

        SendFeedback($"Cleared {cleared} slot(s).");
    }
}
