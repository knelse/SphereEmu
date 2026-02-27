using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BitStreams;
using LiteDB;
using Microsoft.Extensions.Configuration;
using PacketLogViewer.Models;
using PacketLogViewer.Models.PacketAnalyzeData;
using SphereHelpers.Extensions;
using SpherePacketVisualEditor;
using SphServer.Helpers;
using static SphServer.Helpers.PacketPartMapping;

namespace PacketLogViewer;

public enum PacketTypes
{
    /*Originating from client*/
    CLIENT_LOGIN_DATA,
    CLIENT_SELECT_CHARACTER,
    CLIENT_DELETE_CHARACTER,
    CLIENT_CREATE_CHARACTER,
    CLIENT_PING,
    CLIENT_ATTACK_TARGET,
    CLIENT_SEND_CHAT_MESSAGE,
    CLIENT_MOVE_ITEM,

    /*Originating from server*/
    SERVER_CONNECTION_ACCEPTED,
    SERVER_RECONNECT_ATTEMPT,
    SERVER_TRANSMISSION_END,
    SERVER_CREDENTIALS,
    SERVER_CHARACTER_SELECT_SCREEN_INIT,
    SERVER_CHARACTER_SELECT_SCREEN_CONTENTS,
    SERVER_ENTER_GAME_WORLD_INIT,
    SERVER_ENTER_GAME_WORLD_CONTENTS,
    SERVER_CREATE_NEW_CHARACTER,
    SERVER_NAME_CHECK_OK,
    SERVER_ERROR_ACCOUNT_OUTDATED,
    SERVER_ERROR_NAME_EXISTS,
    SERVER_ERROR_ACCOUNT_IN_USE,
    SERVER_MOVE_INVENTORY_ITEM,
    SERVER_NEW_OBJECT,
    SERVER_SET_PLAYER_INVULNERABLE,
    SERVER_PING_6_SEC,
    SERVER_PING_15_SEC,
    SERVER_MOVE_ENTITY,
    SERVER_DESPAWN_ENTITY,
    SERVER_NEW_INSTANCED_ZONE,
    SERVER_TELEPORT_PLAYER,

    UNKNOWN
}

public enum PacketAnalyzeState
{
    UNDEF,
    NONE,
    PARTIAL,
    UNDEF_TYPE,
    FULL
}

public static class PacketPartNames
{
    public const string ID = "entity_id";
    public const string EntityType = "entity_type";
    public const string ObjectType = "object_type";
    public const string MobType = "mob_type";
    public const string ActionType = "action_type";
    public const string CoordX = "x";
    public const string CoordY = "y";
    public const string CoordZ = "z";
    public const string Angle = "angle";
    public const string Delimiter = "delimiter";
    public const string CurrentHP = "current_hp";
    public const string MaxHP = "max_hp";
    public const string Level = "level";
    public const string NameID = "name_id";
    public const string TypeNameLength = "entity_type_name_length";
    public const string TypeName = "entity_type_name";
    public const string IconNameLength = "icon_name_length";
    public const string IconName = "icon_name";
    public const string Skip = "skip";
    public const string HasGameId = "__hasGameId";
    public const string GameObjectId = "game_object_id";
    public const string ContainerId = "container_id";
    public const string Count = "count";
    public const string SubtypeId = "subtype_id";
    public const string ItemNameLength = "item_name_length";
    public const string ItemName = "item_name";
    public const string PALevel = "pa_level";
    public const string RemainingUses = "remaining_uses";
    public const string OwnerName = "owner_name";
    public const string SuffixLength = "suffix_length";
    public const string Suffix = "suffix";
    public const string HpSizeType = "hp_size_type";
    public const string NpcTradeType = "npc_trade_type";
    public const string TargetX = "target_x";
    public const string TargetY = "target_y";
    public const string TargetZ = "target_z";
}

internal class SubpacketBytesWithOffset
{
    public readonly byte[] Content;
    public readonly int ByteOffsetFromFullContentStart;
    public readonly byte[]? Header;

    public SubpacketBytesWithOffset (byte[] content, int byteOffsetFromFullContentStart, byte[]? header = null)
    {
        Content = content;
        ByteOffsetFromFullContentStart = byteOffsetFromFullContentStart;
        Header = header;
    }
}

internal static class PacketAnalyzer
{
    public static readonly byte[] packet_04_00_4F_01 = { 0x04, 0x00, 0xF4, 0x01 };
    public static readonly byte[] ok_mark = { 0x2c, 0x01, 0x00 };

    public static readonly ILiteCollection<MobPacket> MobCollection =
        PacketLogViewerMainWindow.PacketDatabase.GetCollection<MobPacket>("MobData");

    public static readonly ILiteCollection<NpcTradePacket> NpcTradeCollection =
        PacketLogViewerMainWindow.PacketDatabase.GetCollection<NpcTradePacket>("NpcTradeData");

    public static readonly List<Func<byte[], bool>> ServerPacketHideRules = new ()
    {
        c => c.HasEqualElementsAs(packet_04_00_4F_01),
        c => c[0] == 0x08 && (c.Length < 8 || (c[6] == 0xF4 && c[7] == 0x01)),
        c => c[0] == 0x0C && (c.Length < 12 || (c[10] == 0x0D && c[11] == 0xE2)),
        c => c[0] == 0x12 && (c.Length < 17 || (c[14] == 0x1B && c[15] == 0x01 && c[16] == 0x60)),
        c => c[0] == 0x10 && (c.Length < 16 || (c[14] == 0x52 && c[15] == 0x09)),
        c => c[0] == 0x17 || c[0] == 0x1D || c[0] == 0x2D || c[0] == 0x22 || c[0] == 0x12 || c[0] == 0x0D,
        c => c[0] == 0x11 && (c.Length < 12 || (c[9] == 0x08 && c[10] == 0x40 && c[11] == 0x63)),
        c => c[0] == 0x0F && (c.Length < 14 || (c[12] == 0x84 && c[13] == 0x20)),
        c => c[0] == 0x10 && (c.Length < 9 || (c[7] == 0x00 && c[8] == 0x00)),
        c => c[0] == 0x76 && (c.Length < 12 || (c[9] == 0x08 && c[10] == 0x40 && c[11] == 0x63)) // file check
    };

    public static readonly List<Func<byte[], bool>> ClientPacketHideRules = new ()
    {
        c => true,
        c => c[0] == 0x26 || c[0] == 0x08 || c[0] == 0x0C || c[0] == 0x12,
        c => c[0] == 0x69 && c[13] == 0x08 && c[14] == 0x40 && c[15] == 0x63
    };

    internal static bool ShouldBeHiddenByDefault (StoredPacket storedPacket)
    {
        return storedPacket.Source switch
        {
            PacketSource.CLIENT => ShouldBeHiddenByDefaultClient(storedPacket),
            PacketSource.SERVER => ShouldBeHiddenByDefaultServer(storedPacket),
            _ => false
        };
    }

    private static bool ShouldBeHiddenByDefaultServer (StoredPacket storedPacket)
    {
        var content = storedPacket.ContentBytes;

        return ServerPacketHideRules.Any(ruleFunc => ruleFunc(content));
    }

    private static bool ShouldBeHiddenByDefaultClient (StoredPacket storedPacket)
    {
        var content = storedPacket.ContentBytes;

        return ClientPacketHideRules.Any(ruleFunc => ruleFunc(content));
    }

    public static bool IsClientPingPacket (StoredPacket storedPacket)
    {
        return storedPacket.Source == PacketSource.CLIENT && storedPacket.ContentBytes[0] == 0x26;
    }

    internal static List<byte[]> SplitIntoItemSlots (BitStream stream, int separator, int separatorBitCount)
    {
        var results = new List<byte[]>();
        var previousOffset = (long) 0;
        var previousBit = 0;
        stream.Seek(0, 0);

        while (stream.ValidPosition)
        {
            var test = stream.ReadUInt32(separatorBitCount);
            if (!stream.ValidPosition)
            {
                break;
            }

            if (test != separator)
            {
                stream.SeekBack(separatorBitCount - 1);
                continue;
            }

            if (previousOffset == 0)
            {
                previousOffset = stream.Offset;
                previousBit = stream.Bit;
                continue;
            }

            stream.SeekBack(separatorBitCount);
            results.Add(stream.GetStreamDataBetween(previousOffset, previousBit, stream.Offset, stream.Bit));
            stream.ReadBytes(separatorBitCount);
            previousOffset = stream.Offset;
            previousBit = stream.Bit;
        }

        if (results.Any())
        {
            // last item won't be added
            stream.Seek(previousOffset, previousBit);
            var bitCount = separator == 0x600A ? 96 : 64;
            results.Add(stream.ReadBytes(bitCount));
        }

        return results;
    }

    internal static string GetTextOutputForPacket (byte[] contents)
    {
        if (contents.Length < 5)
        {
            return string.Empty;
        }

        if (contents.HasEqualElementsAs(ok_mark, 2))
        {
            // len_1 len_2 2c 01 00 sync_1 sync_2
            contents = contents[7..];
        }

        var stream = new BitStream(contents);
        var analyzeResult = new List<Dictionary<string, object>>();

        var entityId = stream.ReadUInt16();
        stream.ReadByte(2);
        var entityType = stream.ReadUInt16(10);
        var entityTypeName = Enum.GetName(typeof (ObjectType), entityType) ?? "(undef)";
        var tradeEntities = new HashSet<int>
        {
            (int) ObjectType.NpcTrade
        };
        var containerEntities = new HashSet<int>
        {
            (int) ObjectType.Chest,
            (int) ObjectType.Sack,
            (int) ObjectType.SackMobLoot,
            (int) ObjectType.MantraBookSmall,
            (int) ObjectType.MantraBookLarge,
            (int) ObjectType.MantraBookGreat,
            (int) ObjectType.AlchemyPot,
            (int) ObjectType.BackpackLarge,
            (int) ObjectType.BackpackSmall,
            (int) ObjectType.MapBook,
            (int) ObjectType.RecipeBook
        };
        stream.ReadByte(2);
        var output = new StringBuilder("\n");
        if (tradeEntities.Contains(entityType))
        {
            var splittedBySeparator = SplitIntoItemSlots(stream, 0x600A, 15);
            if (!splittedBySeparator.Any())
            {
                output.AppendLine("[EMPTY]");
            }

            foreach (var splitted in splittedBySeparator)
            {
                var splitStream = new BitStream(splitted);
                var itemSlot = splitStream.ReadByte();
                var itemId = splitStream.ReadUInt16();
                var skip = splitStream.ReadByte();
                var weight = splitStream.ReadUInt32();
                var cost = splitStream.ReadUInt32();
                analyzeResult.Add(new Dictionary<string, object>
                {
                    ["ItemId"] = itemId,
                    ["ItemSlot"] = itemSlot,
                    ["Weight"] = weight,
                    ["Skip"] = skip,
                    ["Cost"] = cost
                });
                output.AppendLine($"{itemSlot:0#}: {itemId:X4} ({cost}t), {weight} u");
            }
        }
        else if (containerEntities.Contains(entityType))
        {
            var splittedBySeparator = SplitIntoItemSlots(stream, 0x40105, 23);
            if (!splittedBySeparator.Any())
            {
                output.AppendLine("[EMPTY]");
            }

            foreach (var splitted in splittedBySeparator)
            {
                var splitStream = new BitStream(splitted);
                var itemSlot = splitStream.ReadByte();
                var itemId = splitStream.ReadUInt16();
                var skip = splitStream.ReadByte();
                var weight = splitStream.ReadUInt32();
                analyzeResult.Add(new Dictionary<string, object>
                {
                    ["ItemId"] = itemId,
                    ["ItemSlot"] = itemSlot,
                    ["Weight"] = weight,
                    ["Skip"] = skip
                });
                output.AppendLine($"{itemSlot:0#}: {itemId:X4}, {weight} u");
            }
        }

        if (string.IsNullOrWhiteSpace(output.ToString()))
        {
            output.Clear();
        }

        return $"ID: {entityId:X4} ({entityType}, {entityTypeName})\n{output}";
    }

    public static StoredPacket UpdatePacketPartsForContent (this StoredPacket storedPacket)
    {
        if (storedPacket.Source == PacketSource.CLIENT)
        {
            // TODO
            return storedPacket;
        }

        storedPacket.AnalyzeState = PacketAnalyzeState.NONE;

        var allParts = new List<PacketPart>();
        var undefTypes = false;
        var typesInside = new List<ObjectType>();
        var hpByLevel = new List<KeyValuePair<int, int>>();
        var shouldHidePacket = true;
        var subPacketIndex = 0;
        var fullStream = new BitStream(storedPacket.ContentBytes);

        if (storedPacket.ContentBytes.HasEqualElementsAs(ok_mark, 2))
        {
            var header = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(
                fullStream, "server_packet_header", 0,
                "NEXT PACKET");
            allParts.AddRange(header);
        }

        while (fullStream.ValidPosition)
        {
            subPacketIndex++;
            var initialBitOffset = (int) fullStream.BitOffsetFromStart;
            var test1 = fullStream.ReadBytes(4, true);
            var breakAfterCurrentTry = false;
            if (test1.HasEqualElementsAs(packet_04_00_4F_01))
            {
                var parts = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream, "0x0400F401",
                    subPacketIndex);
                allParts.AddRange(parts);
                if (!fullStream.ValidPosition)
                {
                    break;
                }

                continue;
            }

            fullStream.SeekBitOffset(initialBitOffset);

            if (subPacketIndex > 100)
            {
                // something weird happened
                break;
            }


            // try to find entity id and object type
            var entId = fullStream.ReadUInt16();
            if (!fullStream.ValidPosition)
            {
                break;
            }

            fullStream.ReadBits(2);
            if (!fullStream.ValidPosition)
            {
                break;
            }

            var objectTypeVal = fullStream.ReadUInt16(10);
            if (!fullStream.ValidPosition)
            {
                break;
            }

            var objectType = Enum.IsDefined(typeof (ObjectType), objectTypeVal)
                ? (ObjectType) objectTypeVal
                : ObjectType.Unknown;
            if (objectType != ObjectType.Unknown)
            {
                typesInside.Add(objectType);
            }

            var currentParts = new List<PacketPart>();
            var typeWithDelimiter = false;
            if (objectType == ObjectType.Despawn)
            {
                fullStream.SeekBitOffset(initialBitOffset);
                var despawn = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream, "despawn",
                    subPacketIndex, $"DESPAWN: {entId:X4}");
                currentParts.AddRange(despawn);
                typeWithDelimiter = true;
            }
            else if (EntityObjectTypes.Contains(objectType))
            {
                fullStream.ReadBit();
                var actionTypeVal = fullStream.ReadByte();
                var actionType = Enum.IsDefined(typeof (EntityActionType), (int) actionTypeVal)
                    ? (EntityActionType) actionTypeVal
                    : EntityActionType.UNDEF;
                if (actionType == EntityActionType.INTERACT)
                {
                    shouldHidePacket = false;
                }

                var interactionTypeVal = fullStream.ReadUInt16();
                var interactionType = Enum.IsDefined(typeof (EntityInteractionType), (int) interactionTypeVal)
                    ? (EntityInteractionType) interactionTypeVal
                    : EntityInteractionType.UNDEF;
                fullStream.ReadBits(112);
                if (!fullStream.ValidPosition)
                {
                    break;
                }

                var suffixLength = 7;

                var hasGameId = fullStream.ReadBit().AsBool();
                var optionalFields = new List<OptionalPacketFields>();
                if (hasGameId && actionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
                {
                    if (EquippableItemTypes.Contains(objectType))
                    {
                        var gameId = fullStream.ReadBits(14);
                        // has_suffix
                        var hasSuffix = !fullStream.ReadBit().AsBool();
                        var suffixLengthType = fullStream.ReadByte(2);
                        if (!hasSuffix)
                        {
                            suffixLengthType = 0;
                        }

                        suffixLength = suffixLengthType switch
                        {
                            0 => 3,
                            1 => 7,
                            _ => 7
                        };
                        // suffix
                        _ = fullStream.ReadByte(suffixLength);
                        // divider aka 00000011000000000000101 
                        var divider = fullStream.ReadBits(23);

                        fullStream.ReadBits(55);
                    }
                    else
                    {
                        fullStream.ReadBits(98);
                    }

                    // should be equal to 2^32 - 1
                    var t = fullStream.ReadInt64(31);
                    optionalFields = GetOptionalFields(fullStream);
                }
                else if (actionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
                {
                    fullStream.ReadBits(87);
                    // should be equal to 2^32 - 1
                    _ = fullStream.ReadInt64(31);
                    optionalFields = GetOptionalFields(fullStream);
                    shouldHidePacket = false;
                }

                fullStream.SeekBitOffset(initialBitOffset);
                var (success, parts) = GetNewEntityPacketParts(fullStream, objectType,
                    entId, actionType, interactionType, subPacketIndex, hasGameId, optionalFields);
                currentParts.AddRange(parts);
                if (success)
                {
                    if (actionType == EntityActionType.FULL_SPAWN)
                    {
                        shouldHidePacket = false;
                    }

                    typeWithDelimiter = true;
                }

                if (!success || !parts.Any())
                {
                    undefTypes = true;
                    breakAfterCurrentTry = false;
                }
            }
            else
            {
                undefTypes = true;
                breakAfterCurrentTry = false;
            }

            allParts.AddRange(currentParts);
            if (breakAfterCurrentTry)
            {
                break;
            }

            if (typeWithDelimiter)
            {
                if (objectType is ObjectType.Teleport)
                {
                    fullStream.ReadBit();
                }

                var delimTest = fullStream.ReadByte();
                if (!fullStream.ValidPosition)
                {
                    break;
                }

                fullStream.SeekBack(8);
                if (delimTest == 0x7E || delimTest == 0x7F)
                {
                    // fullStream.SeekBitOffset(currentFullStreamPosition);
                    subPacketIndex++;
                    var delimiter = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream, "delimiter",
                        subPacketIndex, PacketPart.UndefinedFieldValue);
                    allParts.AddRange(delimiter);
                    continue;
                }
            }

            if (undefTypes)
            {
                fullStream.SeekBitOffset(initialBitOffset);
                var header = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream, "entity_header",
                    subPacketIndex,
                    $"UNKNOWN TYPE: {objectType} ({objectTypeVal})");
                allParts.AddRange(header);
                break;
            }
        }

        storedPacket.PacketParts = allParts;
        if (allParts.Any())
        {
            storedPacket.AnalyzeState = undefTypes ? PacketAnalyzeState.UNDEF_TYPE : PacketAnalyzeState.PARTIAL;
        }

        if (shouldHidePacket)
        {
            storedPacket.HiddenByDefault = true;
        }

        AddPacketPartAnalyzeData(storedPacket);

        foreach (var mobPacket in storedPacket.AnalyzeResult.Where(x => x is MobPacket))
        {
            MobCollection.Upsert(mobPacket as MobPacket);
        }

        foreach (var npcTradePacket in storedPacket.AnalyzeResult.Where(x => x is NpcTradePacket))
        {
            NpcTradeCollection.Upsert(npcTradePacket as NpcTradePacket);
        }

        return storedPacket;
    }

    private static List<OptionalPacketFields> GetOptionalFields (BitStream stream)
    {
        var currentPosition = stream.BitOffsetFromStart;
        var result = new List<OptionalPacketFields>();
        while (stream.ValidPosition)
        {
            var divider = stream.ReadByte();
            if (!stream.ValidPosition)
            {
                break;
            }

            var isDelimiter = divider is 0x7F or 0x7E;
            if (isDelimiter) // || (divider != 0b0001011 && divider != 0b00010101))
            {
                break;
            }

            var nextField = stream.ReadByte();
            if (!stream.ValidPosition)
            {
                break;
            }

            var fieldLength = nextField == (byte) OptionalPacketFields.MADE_BY ? 2 : stream.ReadByte();
            if (!stream.ValidPosition)
            {
                break;
            }

            var fieldName = Enum.IsDefined(typeof (OptionalPacketFields), nextField)
                ? (OptionalPacketFields) nextField
                : OptionalPacketFields.UNKNOWN;

            if (fieldName is not OptionalPacketFields.UNKNOWN)
            {
                result.Add(fieldName);
            }

            stream.ReadBits(8 * fieldLength - 1);
        }

        return result;
    }

    private static Tuple<bool, List<PacketPart>> GetNewEntityPacketParts (BitStream stream, ObjectType objectType,
        ushort entId, EntityActionType actionType, EntityInteractionType interactionType, int subpacketIndex,
        bool hasGameId, List<OptionalPacketFields> optionalFields)
    {
        var (packetName, comment, success) = GetPacketPartName(objectType, actionType, interactionType, entId,
            hasGameId, optionalFields);

        return packetName == string.Empty
            ? new Tuple<bool, List<PacketPart>>(success, [])
            : new Tuple<bool, List<PacketPart>>(success,
                FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(stream, packetName,
                    subpacketIndex, comment));
    }

    private static StoredPacket AddPacketPartAnalyzeData (this StoredPacket storedPacket)
    {
        storedPacket.AnalyzeResult.Clear();
        var partsBySubpacket = new Dictionary<int, List<PacketPart>>();
        storedPacket.PacketParts.ForEach(part =>
        {
            if (!partsBySubpacket.ContainsKey(part.SubpacketIndex))
            {
                partsBySubpacket.Add(part.SubpacketIndex, new List<PacketPart>());
            }

            partsBySubpacket[part.SubpacketIndex].Add(part);
        });

        foreach (var key in partsBySubpacket.Keys)
        {
            if (partsBySubpacket[key].Count == 1 && partsBySubpacket[key].First().Name == PacketPartNames.Delimiter)
            {
                continue;
            }

            storedPacket.AnalyzeResult.Add(GetAnalyzeDataForSubpacket(partsBySubpacket[key]));
        }

        return storedPacket;
    }

    private static PacketAnalyzeData GetAnalyzeDataForSubpacket (List<PacketPart> subpacket)
    {
        var result = new PacketAnalyzeData(subpacket);
        var outputPath = PacketLogViewerMainWindow.AppConfig.GetSection("Settings").GetValue<string>("OutputFolder");
        if (result.ObjectType is ObjectType.Monster or ObjectType.MonsterFlyer or ObjectType.MobSpawner)
        {
            var mob = new MobPacket(subpacket);
            result = mob;
            if (result.ObjectType is ObjectType.Monster or ObjectType.MonsterFlyer &&
                mob.ActionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
            {
                var output =
                    $"{mob.Id:X4}\t{result.ObjectType}\t{mob.ActionType}\t{mob.X}\t{mob.Y}\t{mob.Z}\t{mob.Angle}\t{mob.CurrentHP}\t{mob.MaxHP}\t{mob.Type}\t{mob.Level}\n";
                File.AppendAllText($@"{outputPath}\\mob.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.Despawn)
        {
            result = new DespawnPacket(subpacket);
        }

        else if (result.ObjectType is ObjectType.NpcTrade or ObjectType.NpcQuestTitle or ObjectType.NpcQuestDegree
                 or ObjectType.NpcQuestKarma or ObjectType.NpcGuilder or ObjectType.NpcBanker)
        {
            var npcTradePacket = new NpcTradePacket(subpacket);
            result = npcTradePacket;
            if (npcTradePacket.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output =
                    $"{npcTradePacket.Id:X4}\t{npcTradePacket.ObjectType}\t{npcTradePacket.ActionType}\t{npcTradePacket.X}\t{npcTradePacket.Y}\t{npcTradePacket.Z}\t{npcTradePacket.Angle}\t{npcTradePacket.NameId}\t{npcTradePacket.TypeNameLength}\t{npcTradePacket.TypeName}\t{npcTradePacket.IconNameLength}\t{npcTradePacket.IconName}\t{npcTradePacket.NpcTradeType}\n";
                File.AppendAllText($@"{outputPath}\\npc.txt", output);
            }
        }

        else if (ItemObjectTypes.Contains(result.ObjectType))
        {
            var item = new ItemPacket(subpacket);
            result = item;
            if (item.ActionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
            {
                var gameId = item.HasGameId ? item.GameObjectId : 0;
                var suffix = item.HasSuffix ? item.Suffix : 0;
                var output =
                    $"{item.Id:X4}\t{result.ObjectType}\t{item.ActionType}\t{item.X}\t{item.Y}\t{item.Z}\t{item.Angle}\t" +
                    $"{gameId}\t{item.ContainerId}\t{suffix}\t{item.PALevel}\t{item.Count}\t{item.RemainingUses}\t{item.OwnerName}\n";
                File.AppendAllText($@"{outputPath}\\items.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.DoorEntrance or ObjectType.DoorExit)
        {
            var door = new DoorPacket(subpacket);
            result = door;
            if (door.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output =
                    $"{door.Id:X4}\t{result.ObjectType}\t{door.ActionType}\t{door.X}\t{door.Y}\t{door.Z}\t{door.Angle}\t{door.SubtypeID}\t{door.TargetX}\t{door.TargetY}\t{door.TargetZ}\n";
                File.AppendAllText($@"{outputPath}\\doors.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.TeleportWithTarget)
        {
            var tp = new TeleportWithTargetPacket(subpacket);
            result = tp;
            if (tp.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output =
                    $"{tp.Id:X4}\t{result.ObjectType}\t{tp.ActionType}\t{tp.X}\t{tp.Y}\t{tp.Z}\t{tp.Angle}\t{tp.SubtypeID}\n";
                File.AppendAllText($@"{outputPath}\\target_tps.txt", output);
            }
        }

        else if (WorldObjectsToTrack.TryGetValue(result.ObjectType, out var filename))
        {
            var worldObject = new WorldObject(subpacket);
            result = worldObject;

            if (worldObject.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output =
                    $"{worldObject.Id:X4}\t{worldObject.ObjectType}\t{worldObject.ActionType}\t{worldObject.X}\t{worldObject.Y}\t{worldObject.Z}\t{worldObject.Angle}\n";
                File.AppendAllText($@"{outputPath}\\{filename}.txt", output);
            }
        }

        return result;
    }

    private static List<PacketPart> FindPartsByName (BitStream stream, string name, bool isSubpacket)
    {
        var isMob = name is "monster_full" or "entity_monster";
        var isItem = name.StartsWith("item");
        if (isSubpacket)
        {
            var subpacket = PacketLogViewerMainWindow.Subpackets.FirstOrDefault(x => x.Name == name);
            if (subpacket is null)
            {
                return new List<PacketPart>();
            }

            return subpacket.LoadFromFile(stream, 0, isMob, isItem);
        }

        var definition = PacketLogViewerMainWindow.PacketDefinitions.FirstOrDefault(x => x.Name == name);
        if (definition is null)
        {
            return new List<PacketPart>();
        }

        return definition.LoadFromFile(stream, 0, isMob, isItem);
    }

    private static List<PacketPart> FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset (BitStream stream,
        string name, int subpacketIndex, string? comment = null, bool isSubpacket = true)
    {
        var parts = FindPartsByName(stream, name, isSubpacket);
        if (!parts.Any())
        {
            return parts;
        }

        comment ??= name;
        parts[0].Comment = comment;
        foreach (var t in parts)
        {
            t.SubpacketIndex = subpacketIndex;
        }

        if (name == "monster_full")
        {
            // hack until I figure this out
            // mob packet should end with 001 and 36 bits of zeroes, so we change stream position accordingly
            var lastSkipPart = parts.Last();
        }

        return parts;
    }
}