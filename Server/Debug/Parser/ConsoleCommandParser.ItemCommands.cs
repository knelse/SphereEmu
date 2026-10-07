using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Server.Config;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game;

namespace SphServer.Server.Debug.Parser;

/// <summary>
/// Registered from InitCommands in the main parser file
/// </summary>
public partial class ConsoleCommandParser
{
    /// <summary>
    /// How far in front of the character (world Z units) /give drops the item.
    /// </summary>
    private const double GiveGroundDropOffset = 1.0;

    /// <summary>
    /// Ground container_id is 16 bits at bit 209, value 0xFF00, from alchemy_resource_ground.spdp
    /// </summary>
    private const ushort GroundContainerId = 0xFF00;

    /// <summary>
    /// Ground shape is angle, container 0xFF00, and a game object id; container records such as
    /// item_sword and item_with_gameid draw nothing
    /// </summary>
    private const string GiveDefaultSpawnDefinition = "alchemy_resource_ground";

    /// <summary>
    /// In-hand capture from SphereTools/itemInHand.txt: entity 50B4, game object 3251, suffix 81,
    /// 288 body bits
    /// </summary>
    private const string RetailInventoryItemRecord =
        "2B002C0100280AB450D0870F80842E090000000000000000409145E62C131560203E19A0900500FFFFFFFF";

    // Offsets are from the start of the frame; the record body begins at wire byte 7
    private const int RecordStartBit = 56;
    private const int RecordEntityIdBit = RecordStartBit;
    private const int RecordContainerIdBit = RecordStartBit + 205;

    /// <summary>
    /// The sword from the retail capture, used when /giveinv is given no game object.
    /// </summary>
    private const int DefaultInventoryGameObjectId = 3251;

    /// <summary>
    /// Armour without an @xy@ wear code equips invisibly; only the last two inventory rows take an
    /// arbitrary item
    /// </summary>
    private void GiveToInventory (string args)
    {
        if (sphereClient is null)
        {
            SendFeedback ("/giveinv needs a connected client.");
            return;
        }

        var split = args.Split (' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var gameObjectId = split.Length >= 1 && int.TryParse (split[0], out var requested)
            ? requested
            : DefaultInventoryGameObjectId;

        var gameObject = DbConnection.GameObjects.FindById (gameObjectId);
        if (gameObject is null)
        {
            SendFeedback ($"Game object {gameObjectId} is missing from the database.");
            return;
        }

        var item = ItemDbEntry.CreateFromGameObject (gameObject);
        item.ItemCount = 1;
        item.Id = WorldObjectIndex.NewItem ();
        DbConnection.Items.Insert (item.Id, item);

        var name = gameObject.Localisation.GetValueOrDefault (Locale.Russian, gameObject.SphereType);

        // Same record as carried, with owner and position changed; a missing object means the
        // client rejected the record
        if (split.Any (x => x.Equals ("ground", StringComparison.OrdinalIgnoreCase)))
        {
            var frame = ItemRecordEncoder.EncodeWithoutGameId ((ushort) item.Id, (int) item.WireObjectType,
                GroundContainerId,
                (float) currentCharacterDbEntry.X,
                (float) -currentCharacterDbEntry.Y,
                (float) -(currentCharacterDbEntry.Z + GiveGroundDropOffset));
            sphereClient.MaybeQueueNetworkPacketSend (frame);
            SendFeedback ($"{name} [item id {item.Id}] sent as a ground record: {Convert.ToHexString (frame)}");
            return;
        }

        var itemFirst = split.Any (x => x.Equals ("itemfirst", StringComparison.OrdinalIgnoreCase));
        PutItemInInventoryAndDeclare (item, name, itemFirst);
    }

    /// <summary>
    /// Suffix is one word, or omitted when the first word is digits; tier breaks shared names, and
    /// the lowest GameId wins
    /// </summary>
    private void GiveToInventoryByNameWithSuffix (string args)
    {
        if (sphereClient is null)
        {
            SendFeedback ("/giveinvns needs a connected client.");
            return;
        }

        var split = args.Split (' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length < 2)
        {
            SendFeedback ("Usage: /giveinvns [suffix] [tier] [item name...]. Suffix is optional." +
                         "Use tier = -1 for special items.");
            return;
        }

        string? suffixToken = null;
        int rank;
        string itemName;
        // Tier may be the catalog sentinel -1, with an optional leading '-'
        if (IsIntegerToken (split[0]))
        {
            rank = int.Parse (split[0]);
            itemName = string.Join (' ', split.Skip (1));
        }
        else
        {
            if (split.Length < 3 || !IsIntegerToken (split[1]))
            {
                SendFeedback ("Usage: /giveinvns [suffix] [tier] [item name...] -- tier must be an integer.");
                return;
            }

            suffixToken = split[0];
            rank = int.Parse (split[1]);
            itemName = string.Join (' ', split.Skip (2));
        }

        if (string.IsNullOrWhiteSpace (itemName))
        {
            SendFeedback ("Usage: /giveinvns [suffix] [tier] [item name...] -- item name is required.");
            return;
        }

        EnsureGameObjectNameIndex ();
        if (!gameObjectIdsByExactName!.TryGetValue (itemName, out var candidateIds) || candidateIds.Count == 0)
        {
            SendFeedback ($"No game object named \"{itemName}\".");
            return;
        }

        var candidates = new List<SphGameObject> (candidateIds.Count);
        foreach (var id in candidateIds)
        {
            if (GameObjectDb.Db.TryGetValue (id, out var go) ||
                (go = DbConnection.GameObjects.FindById (id)) is not null)
            {
                candidates.Add (go);
            }
        }

        if (candidates.Count == 0)
        {
            SendFeedback ($"No game object named \"{itemName}\".");
            return;
        }

        var atTier = candidates.Where (c => c.Tier == rank).ToList ();
        if (atTier.Count == 0)
        {
            var distinctTiers = candidates.Select (c => c.Tier).Distinct ().OrderBy (t => t).ToList ();
            if (distinctTiers.Count == 1 && distinctTiers[0] == -1)
            {
                var example = string.IsNullOrEmpty (suffixToken)
                    ? $"/giveinvns -1 {itemName}"
                    : $"/giveinvns {suffixToken} -1 {itemName}";
                SendFeedback (
                    $"\"{itemName}\" is very special and only has tier -1. Use that directly. ({example})");
                return;
            }

            var tiers = string.Join (", ", distinctTiers.Where (t => t != -1));
            SendFeedback ($"No \"{itemName}\" at tier {rank}. Available tiers: {tiers}.");
            return;
        }

        // Kept only when that candidate's type accepts the suffix
        var matched = new List<(SphGameObject Go, ItemSuffix Suffix, int SuffixWire)> ();
        string? lastSuffixError = null;
        foreach (var go in atTier)
        {
            if (!TryResolveSuffixForType (go.GameObjectType, suffixToken, out var suffix, out var wire, out var error))
            {
                lastSuffixError = error;
                continue;
            }

            matched.Add ((go, suffix, wire));
        }

        if (matched.Count == 0)
        {
            SendFeedback (lastSuffixError ?? $"Suffix \"{suffixToken}\" does not apply to \"{itemName}\" at tier {rank}.");
            return;
        }

        var chosen = matched.OrderBy (m => m.Go.GameId).First ();
        var goWithSuffix = SphGameObject.CreateFromGameObject (chosen.Go);
        goWithSuffix.Suffix = chosen.Suffix;
        var item = ItemDbEntry.CreateFromGameObject (goWithSuffix);
        item.ItemCount = 1;
        item.Id = WorldObjectIndex.NewItem ();
        DbConnection.Items.Insert (item.Id, item);

        var displayName = chosen.Go.Localisation.GetValueOrDefault (Locale.Russian, chosen.Go.SphereType);
        var suffixLabel = chosen.Suffix == ItemSuffix.None
            ? "None"
            : Enum.GetName (chosen.Suffix) ?? chosen.Suffix.ToString ();
        PutItemInInventoryAndDeclare (item, $"{displayName} [{suffixLabel}]", itemFirst: false);
    }

    private void PutItemInInventoryAndDeclare (ItemDbEntry item, string feedbackName, bool itemFirst)
    {
        var emptySlot = currentCharacterDbEntry.FindEmptyInventorySlot ();
        if (emptySlot is null)
        {
            SendFeedback ("Inventory is full.");
            return;
        }

        var slot = emptySlot.Value;
        currentCharacterDbEntry.Items[slot] = item.Id;
        sphereClient!.SaveCharacter ();

        var reserve = ItemSlotReserve.Build (currentCharacterDbEntry.ClientIndex, slot, item.Id, item.ItemCount);
        var record = ItemRecordEncoder.Encode (item, SphBitStream.ByteSwap (currentCharacterDbEntry.ClientIndex));

        if (itemFirst)
        {
            sphereClient.MaybeQueueNetworkPacketSend (record);
        }

        if (reserve is not null)
        {
            sphereClient.MaybeQueueNetworkPacketSend (reserve);
        }

        if (!itemFirst)
        {
            sphereClient.MaybeQueueNetworkPacketSend (record);
        }

        SendFeedback ($"{feedbackName} [game id {item.GameId}, item id {item.Id}] put in {Enum.GetName (slot)} and declared" +
                     (itemFirst ? ", item before slot." : "."));
    }

    /// <summary>
    /// True for digit-only tokens, optionally with a leading minus (e.g. -1).
    /// </summary>
    private static bool IsIntegerToken (string value)
    {
        if (string.IsNullOrEmpty (value))
        {
            return false;
        }

        var i = value[0] == '-' ? 1 : 0;
        if (i >= value.Length)
        {
            return false;
        }

        for (; i < value.Length; i++)
        {
            if (!char.IsAsciiDigit (value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveSuffixForType (GameObjectType objectType, string? suffixToken,
        out ItemSuffix suffix, out int suffixWire, out string error)
    {
        suffix = ItemSuffix.None;
        suffixWire = ItemRecordEncoder.NoSuffix;
        error = "";

        if (string.IsNullOrEmpty (suffixToken))
        {
            return true;
        }

        if (!GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual.TryGetValue (objectType, out var map))
        {
            error =
                $"Object type {objectType} has no suffix map; only an empty suffix is allowed.";
            return false;
        }

        // 1) Russian locale string in ObjectTypeToSuffixLocaleMapActual
        foreach (var (itemSuffix, entry) in map)
        {
            if (entry.localization.TryGetValue (Locale.Russian, out var ru) &&
                string.Equals (ru, suffixToken, StringComparison.Ordinal))
            {
                suffix = itemSuffix;
                suffixWire = entry.value;
                return true;
            }
        }

        // 2) ItemSuffix enum entry name
        if (!Enum.TryParse<ItemSuffix> (suffixToken, ignoreCase: false, out suffix) || suffix == ItemSuffix.None)
        {
            error =
                $"Unknown suffix \"{suffixToken}\" for {objectType}. Valid: {FormatValidSuffixes (map)}";
            suffix = ItemSuffix.None;
            return false;
        }

        if (!map.TryGetValue (suffix, out var byEnum))
        {
            error =
                $"Suffix {suffix} is not valid for {objectType}. Valid: {FormatValidSuffixes (map)}";
            suffix = ItemSuffix.None;
            return false;
        }

        suffixWire = byEnum.value;
        return true;
    }

    private static string FormatValidSuffixes (Dictionary<ItemSuffix, SuffixValueWithLocale> map)
    {
        var parts = map.Select (kv =>
        {
            var en = Enum.GetName (kv.Key) ?? kv.Key.ToString ();
            var ru = kv.Value.localization.GetValueOrDefault (Locale.Russian, "");
            return string.IsNullOrEmpty (ru) ? en : $"{en}/{ru}";
        }).OrderBy (s => s, StringComparer.Ordinal);
        return string.Join (", ", parts);
    }

    private static Dictionary<string, List<int>>? gameObjectIdsByExactName;
    private static int gameObjectNameIndexCount = -1;

    private static void EnsureGameObjectNameIndex ()
    {
        // In-memory catalog: LiteDB FindAll throws on a bad enum payload
        var count = GameObjectDb.Db.Count;
        if (gameObjectIdsByExactName is not null && gameObjectNameIndexCount == count)
        {
            return;
        }

        var index = new Dictionary<string, List<int>> (StringComparer.OrdinalIgnoreCase);
        foreach (var (id, go) in GameObjectDb.Db)
        {
            foreach (var name in go.Localisation.Values)
            {
                if (string.IsNullOrEmpty (name))
                {
                    continue;
                }

                if (!index.TryGetValue (name, out var list))
                {
                    list = [];
                    index[name] = list;
                }

                if (!list.Contains (id))
                {
                    list.Add (id);
                }
            }
        }

        gameObjectIdsByExactName = index;
        gameObjectNameIndexCount = count;
    }

    /// <summary>
    /// Login cleanup only drops a slot whose item row is missing, so a junk row stays until this
    /// </summary>
    private void ClearInventory (string args)
    {
        if (sphereClient is null)
        {
            SendFeedback ("/clearinv needs a connected client.");
            return;
        }

        var occupied = currentCharacterDbEntry.Items.Keys.ToList ();
        var cleared = occupied.Count;
        currentCharacterDbEntry.Items.Clear ();
        sphereClient.SaveCharacter ();

        foreach (var slot in occupied)
        {
            var reserve = ItemSlotReserve.Build (currentCharacterDbEntry.ClientIndex, slot,
                ItemSlotReserve.NoItem);
            if (reserve is not null)
            {
                sphereClient.MaybeQueueNetworkPacketSend (reserve);
            }
        }

        // Clear empties the hand too, so the held attack goes with it
        if (currentCharacterDbEntry.RecalcCurrentStats ())
        {
            NetworkedStatsUpdater.Update (currentCharacterDbEntry);
        }

        SendFeedback ($"Cleared {cleared} slot(s). Log out and back in to see the empty grid.");
    }

    /// <summary>
    /// Drops game_object_id on the ground next to the player
    /// </summary>
    private void Give (string args)
    {
        var split = args.Split (' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = 1;
        int? objectTypeOverride = null;
        if (split.Length is < 1 or > 4 || !int.TryParse (split[0], out var gameId) ||
            (split.Length >= 2 && (!int.TryParse (split[1], out count) || count < 1)) ||
            (split.Length == 4 && (!int.TryParse (split[3], out var typeArg) || (objectTypeOverride = typeArg) < 0)))
        {
            SendFeedback ("Usage: /give <game_object_id> [count] [packet definition] [object_type]");
            return;
        }

        var definition = split.Length >= 3 ? split[2] : GiveDefaultSpawnDefinition;

        if (sphereClient is null)
        {
            SendFeedback ("/give needs a connected client.");
            return;
        }

        if (!ServerConfig.AppConfig.DebugMode)
        {
            // The spawn goes through the packet definition pipeline, which is gated by DebugMode;
            // refuse instead of silently inserting a DB row the client never hears about.
            SendFeedback ("/give requires DebugMode=true in appsettings.json.");
            return;
        }

        var gameObject = DbConnection.GameObjects.FindById (gameId);
        if (gameObject is null)
        {
            SendFeedback ($"Unknown game object id: {gameId}");
            return;
        }

        var item = ItemDbEntry.CreateFromGameObject (gameObject);
        item.ItemCount = count;
        item.X = currentCharacterDbEntry.X;
        item.Y = currentCharacterDbEntry.Y;
        item.Z = currentCharacterDbEntry.Z + GiveGroundDropOffset;
        item.ParentContainerId = null;

        // The client id is the row id, from the world index: LiteDB auto-id starts at 1 and
        // collides with entities already on screen
        item.Id = WorldObjectIndex.NewItem ();
        DbConnection.Items.Insert (item.Id, item);

        DebugConsole.SendSpherePacket ($"/packet {definition}",
            bytes => sphereClient.MaybeQueueNetworkPacketSend (bytes),
            false,
            parts =>
            {
                PacketPart.UpdateEntityId (parts, (ushort) item.Id);

                // 0xFF00 is on the ground; captured definitions name a container that does not
                // exist here
                PacketPart.UpdateValue (parts, "container_id", item.ParentContainerId ?? 0xFF00, 16);

                // Object type is written only where the definition has a field for it; the fourth
                // argument is which field the client draws from
                if (objectTypeOverride is { } forcedType)
                {
                    PacketPart.UpdateValue (parts, "object_type", forcedType, 10);
                }
                else if (parts.Any (x => x.Name == "game_object_id"))
                {
                    PacketPart.UpdateValue (parts, "object_type", (int) item.WireObjectType, 10);
                }

                if (parts.Any (x => x.Name == "game_object_id"))
                {
                    PacketPart.UpdateValue (parts, "game_object_id", gameObject.GameId, 14);
                }

                // World Y and Z are negated for the client
                PacketPart.UpdateCoordinates (parts, item.X, -item.Y, -item.Z);
            });

        SendGiveFeedback (gameObject, item, count, split.Length >= 3 ? definition : null, objectTypeOverride);
    }

    /// <summary>
    /// Names the definition only when one was asked for; the default is the same every time.
    /// </summary>
    private void SendGiveFeedback (SphGameObject gameObject, ItemDbEntry item, int count, string? via,
        int? objectTypeOverride)
    {
        var name = gameObject.Localisation.GetValueOrDefault (Locale.Russian, gameObject.SphereType);
        var countSuffix = count > 1 ? $" x{count} (count is server-side only; the ground shows one item)" : "";
        var typeSuffix = objectTypeOverride is { } t ? $", object_type forced to {t}" : "";
        var viaSuffix = via is null ? "" : $" via {via}";
        SendFeedback ($"Spawned {name} [game id {gameObject.GameId}, item id {item.Id}]{countSuffix} " +
                     $"on the ground{viaSuffix}{typeSuffix}.");
    }
}
