using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Server.Config;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Debug.Parser;

/// <summary>
///     Item GM commands, registered in InitCommands (main parser file).
/// </summary>
public partial class ConsoleCommandParser
{
    /// <summary>How far in front of the character (world Z units) /give drops the item.</summary>
    private const double GiveGroundDropOffset = 1.0;

    /// <summary>
    ///     The parent an item lying in the world carries. A 2022 retail capture of a ground item
    ///     sends exactly this: Sphere.PacketDefinitions/alchemy_resource_ground.spdp, container_id
    ///     at bit 209, 16 bits, value 1111111100000000.
    /// </summary>
    private const ushort GroundContainerId = 0xFF00;

    /// <summary>
    ///     The one capture we have of an item lying in the world, so the only definition shaped like
    ///     what the client expects there: it carries an angle, container 0xFF00, and a game object id.
    ///     The definitions tried on 2026-07-31 that drew nothing — item_sword, item_with_gameid,
    ///     item_with_gameid_pa, item_amulet — are all records of an item inside a container, which is
    ///     the likelier reason they failed than the game id they have in common.
    ///     Override with the third argument to try another definition.
    /// </summary>
    private const string GiveDefaultSpawnDefinition = "alchemy_resource_ground";

    /// <summary>
    ///     A real retail item record, captured live in 2022 (SphereTools/itemInHand.txt line 24):
    ///     Кривой меч, entity 50B4, game object 3251, suffix 81, held by character C9F1. Decodes
    ///     exactly under the item grammar — every field matches knelse's own labels and the record
    ///     consumes all 288 body bits with nothing left over.
    /// </summary>
    private const string RetailInventoryItemRecord =
        "2B002C0100280AB450D0870F80842E090000000000000000409145E62C131560203E19A0900500FFFFFFFF";

    // Record-local bit offsets, from the start of the frame. The record body begins at wire byte 7.
    private const int RecordStartBit = 56;
    private const int RecordEntityIdBit = RecordStartBit;
    private const int RecordContainerIdBit = RecordStartBit + 205;

    /// <summary>The sword from the retail capture, used when /giveinv is given no game object.</summary>
    private const int DefaultInventoryGameObjectId = 3251;

    /// <summary>
    ///     /giveinv [game object id] [ground] — put an item straight into the character's inventory
    ///     and declare it, or drop it at their feet with "ground".
    ///
    ///     Some useful ids: 1 sword, 2745 jacket, 2790 helmet, 2760 shield, 2835 gloves, 2775 pants,
    ///     2820 boots. Pick armour whose ground model name starts with an "@xy@" wear code, or it
    ///     equips invisibly: the twelve low ids per kind (301, 313, 325, …) have no code and sit at
    ///     tier -1, so no vendor stocks them either.
    ///
    ///     Only the last two rows of the inventory take arbitrary items; the character window takes
    ///     armour by kind, so a sword is refused there and a helmet is not.
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

        var item = ItemDbEntry.CreateFromGameObject(gameObject);
        item.ItemCount = 1;
        item.Id = WorldObjectIndex.New();
        DbConnection.Items.Insert(item.Id, item);

        var name = gameObject.Localisation.GetValueOrDefault(Locale.Russian, gameObject.SphereType);

        // "ground" drops the encoder's own record at the player's feet instead of declaring it as
        // carried. Same encoder, same fields, only the owner and position differ — so if the sword
        // appears the record does build a client object, and the inventory problem is the slot link.
        // If it does not, the client is rejecting the record itself and nothing built on it can work.
        if (split.Any(x => x.Equals("ground", StringComparison.OrdinalIgnoreCase)))
        {
            var frame = ItemRecordEncoder.EncodeWithoutGameId((ushort) item.Id, (int) item.ObjectType,
                GroundContainerId,
                (float) currentCharacterDbEntry.X,
                (float) -currentCharacterDbEntry.Y,
                (float) -(currentCharacterDbEntry.Z + GiveGroundDropOffset));
            sphereClient.MaybeQueueNetworkPacketSend(frame);
            SendFeedback($"{name} [item id {item.Id}] sent as a ground record: {Convert.ToHexString(frame)}");
            return;
        }

        // Put it in the character's first inventory slot and save, so the login path declares it on
        // the next connect. That path — one item record plus the slot batch — is the one built from
        // the client's own rules, and a relog is the only way to exercise it.
        var emptySlot = currentCharacterDbEntry.FindEmptyInventorySlot();
        if (emptySlot is null)
        {
            // Falling back to the first slot would overwrite it and orphan the item row it named.
            SendFeedback("Inventory is full.");
            return;
        }

        var slot = emptySlot.Value;
        currentCharacterDbEntry.Items[slot] = item.Id;
        sphereClient.SaveCharacter();

        // Declare it straight away rather than waiting for the next login: the same two messages the
        // login path sends, so what you see now is what you would see after a relog.
        var reserve = ItemSlotReserve.Build(currentCharacterDbEntry.ClientIndex, slot, item.Id, item.ItemCount);
        var record = ItemRecordEncoder.Encode((ushort) item.Id, (int) item.ObjectType, item.GameId,
            ItemRecordEncoder.NoSuffix, SphBitStream.ByteSwap(currentCharacterDbEntry.ClientIndex));

        // The reserve is addressed to the player and names an item id. Sent first, that id belongs to
        // nothing yet, so "itemfirst" creates the item before the slot claims it.
        var itemFirst = split.Any(x => x.Equals("itemfirst", StringComparison.OrdinalIgnoreCase));

        if (itemFirst)
        {
            sphereClient.MaybeQueueNetworkPacketSend(record);
        }

        if (reserve is not null)
        {
            sphereClient.MaybeQueueNetworkPacketSend(reserve);
        }

        if (!itemFirst)
        {
            sphereClient.MaybeQueueNetworkPacketSend(record);
        }

        SendFeedback($"{name} [item id {item.Id}] put in {Enum.GetName(slot)} and declared" +
                     (itemFirst ? ", item before slot." : "."));
    }

    /// <summary>
    ///     /clearinv — empty the character's slots and tell the client so. Testing leaves slots
    ///     pointing at rows that exist but are junk, which the login cleanup cannot catch because it
    ///     only drops slots whose item row is missing entirely.
    /// </summary>
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

        // Clear() empties the hand too, so the attack of whatever was held has to go with it.
        if (currentCharacterDbEntry.RecalcCurrentStats())
        {
            NetworkedStatsUpdater.Update(currentCharacterDbEntry);
        }

        SendFeedback($"Cleared {cleared} slot(s). Log out and back in to see the empty grid.");
    }

    /// <summary>/give &lt;game_object_id&gt; [count] [definition] — drop an item on the ground next to the player.</summary>
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

                // Item identity only where the definition has somewhere to put it. Otherwise leave
                // the definition's own object_type alone — which of these two fields decides
                // whether the client draws the item is exactly what the fourth argument tests.
                if (objectTypeOverride is { } forcedType)
                {
                    PacketPart.UpdateValue(parts, "object_type", forcedType, 10);
                }
                else if (parts.Any(x => x.Name == "game_object_id"))
                {
                    PacketPart.UpdateValue(parts, "object_type", (int) item.ObjectType, 10);
                }

                if (parts.Any(x => x.Name == "game_object_id"))
                {
                    PacketPart.UpdateValue(parts, "game_object_id", gameObject.GameId, 14);
                }

                // DB world coords -> client coords: Y and Z are negated.
                PacketPart.UpdateCoordinates(parts, item.X, -item.Y, -item.Z);
            });

        SendGiveFeedback(gameObject, item, count, split.Length >= 3 ? definition : null, objectTypeOverride);
    }

    /// <summary>Names the definition only when one was asked for; the default is the same every time.</summary>
    private void SendGiveFeedback (SphGameObject gameObject, ItemDbEntry item, int count, string? via,
        int? objectTypeOverride)
    {
        var name = gameObject.Localisation.GetValueOrDefault(Locale.Russian, gameObject.SphereType);
        var countSuffix = count > 1 ? $" x{count} (count is server-side only; the ground shows one item)" : "";
        var typeSuffix = objectTypeOverride is { } t ? $", object_type forced to {t}" : "";
        var viaSuffix = via is null ? "" : $" via {via}";
        SendFeedback($"Spawned {name} [game id {gameObject.GameId}, item id {item.Id}]{countSuffix} " +
                     $"on the ground{viaSuffix}{typeSuffix}.");
    }
}
