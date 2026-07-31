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
    public const string CharacterName = "character_name";
    public const string ClanName = "clan_name";
    public const string ClanNameLength = "clan_name_length";
    public const string ExitX = "exit_x";
    public const string ExitY = "exit_y";
    public const string ExitZ = "exit_z";
    public const string ExitAngle = "exit_angle";
    public const string CastleId = "castle_id";
}

internal class SubpacketBytesWithOffset
{
    public readonly byte[] Content;
    public readonly int ByteOffsetFromFullContentStart;
    public readonly byte[]? Header;

    public SubpacketBytesWithOffset(byte[] content, int byteOffsetFromFullContentStart, byte[]? header = null)
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

    public static readonly List<Func<byte[], bool>> ServerPacketHideRules = new()
    {
        c => c.HasEqualElementsAs(packet_04_00_4F_01),
        c => c[0] == 0x08 && (c.Length < 8 || (c[6] == 0xF4 && c[7] == 0x01)),
        c => c[0] == 0x0C && (c.Length < 12 || (c[10] == 0x0D && c[11] == 0xE2)),
        // PingHandler keepalive pong + CommonPackets timer pings
        c => IsServerKeepalivePong(c),
        c => IsServerSixSecondPing(c),
        c => IsServerFifteenSecondPing(c),
        c => c[0] == 0x10 && (c.Length < 16 || (c[14] == 0x52 && c[15] == 0x09)),
        c => c[0] == 0x17 || c[0] == 0x1D || c[0] == 0x2D || c[0] == 0x22 || c[0] == 0x12 || c[0] == 0x0D,
        c => c[0] == 0x11 && (c.Length < 12 || (c[9] == 0x08 && c[10] == 0x40 && c[11] == 0x63)),
        c => c[0] == 0x0F && (c.Length < 14 || (c[12] == 0x84 && c[13] == 0x20)),
        c => c[0] == 0x10 && (c.Length < 9 || (c[7] == 0x00 && c[8] == 0x00)),
        c => c[0] == 0x76 && (c.Length < 12 || (c[9] == 0x08 && c[10] == 0x40 && c[11] == 0x63)) // file check
    };

    public static readonly List<Func<byte[], bool>> ClientPacketHideRules = new()
    {
        c => true,
        c => c[0] == 0x26 || c[0] == 0x08 || c[0] == 0x0C || c[0] == 0x12,
        c => c[0] == 0x69 && c[13] == 0x08 && c[14] == 0x40 && c[15] == 0x63
    };

    internal static bool ShouldBeHiddenByDefault(StoredPacket storedPacket)
    {
        return storedPacket.Source switch
        {
            PacketSource.CLIENT => ShouldBeHiddenByDefaultClient(storedPacket),
            PacketSource.SERVER => ShouldBeHiddenByDefaultServer(storedPacket),
            _ => false
        };
    }

    internal static bool ShouldBeHiddenByDefaultServer(StoredPacket storedPacket)
    {
        var content = storedPacket.ContentBytes;

        return ServerPacketHideRules.Any(ruleFunc => ruleFunc(content));
    }

    internal static bool ShouldBeHiddenByDefaultClient(StoredPacket storedPacket)
    {
        var content = storedPacket.ContentBytes;

        return ClientPacketHideRules.Any(ruleFunc => ruleFunc(content));
    }

    internal static void RefreshHiddenByDefaultFlags(StoredPacket storedPacket)
    {
        storedPacket.HiddenByDefaultClient = storedPacket.Source == PacketSource.CLIENT
                                             && ShouldBeHiddenByDefaultClient(storedPacket);
        storedPacket.HiddenByDefaultServer = storedPacket.Source == PacketSource.SERVER
                                             && ShouldBeHiddenByDefaultServer(storedPacket);
        storedPacket.HiddenByDefault = storedPacket.HiddenByDefaultClient || storedPacket.HiddenByDefaultServer;
    }

    public static bool IsClientPingPacket(StoredPacket storedPacket)
    {
        return storedPacket.Source == PacketSource.CLIENT && storedPacket.ContentBytes[0] == 0x26;
    }

    /// <summary>
    /// Server pong built by <c>PingHandler.Handle</c> via <c>Packet.ToByteArray(pong, padZeros: 1)</c>.
    /// Layout: len=0x12 | 2C 01 | pad 00 | 13-byte echo payload ending in 01 60 00.
    /// </summary>
    internal static bool IsServerKeepalivePong(byte[] content)
    {
        return content.Length >= 18
               && content[0] == 0x12
               && content[1] == 0x00
               && content[2] == 0x2C
               && content[3] == 0x01
               && content[4] == 0x00
               && content[15] == 0x01
               && content[16] == 0x60
               && content[17] == 0x00;
    }

    /// <summary>Matches <c>CommonPackets.SixSecondPing</c> (player index at bytes 7-8 varies).</summary>
    internal static bool IsServerSixSecondPing(byte[] content)
    {
        // 13 00 2C 01 00 00 04 <PI_hi> <PI_lo> 08 C0 42 A0 FF D3 90 08 B0 07
        return content.Length >= 19
               && content[0] == 0x13
               && content[1] == 0x00
               && content[2] == 0x2C
               && content[3] == 0x01
               && content[4] == 0x00
               && content[5] == 0x00
               && content[6] == 0x04
               && content[9] == 0x08
               && content[10] == 0xC0
               && content[11] == 0x42
               && content[12] == 0xA0
               && content[13] == 0xFF
               && content[14] == 0xD3
               && content[15] == 0x90
               && content[16] == 0x08
               && content[17] == 0xB0
               && content[18] == 0x07;
    }

    /// <summary>Matches <c>CommonPackets.FifteenSecondPing</c> (player index at bytes 7-8 varies).</summary>
    internal static bool IsServerFifteenSecondPing(byte[] content)
    {
        // 10 00 2C 01 00 00 04 <PI_hi> <PI_lo> 08 40 81 93 EE E4 08
        return content.Length >= 16
               && content[0] == 0x10
               && content[1] == 0x00
               && content[2] == 0x2C
               && content[3] == 0x01
               && content[4] == 0x00
               && content[5] == 0x00
               && content[6] == 0x04
               && content[9] == 0x08
               && content[10] == 0x40
               && content[11] == 0x81
               && content[12] == 0x93
               && content[13] == 0xEE
               && content[14] == 0xE4
               && content[15] == 0x08;
    }

    internal static List<byte[]> SplitIntoItemSlots(BitStream stream, int separator, int separatorBitCount)
    {
        var results = new List<byte[]>();
        var previousOffset = (long)0;
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

    internal static string GetTextOutputForPacket(byte[] contents)
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
        var entityTypeName = Enum.GetName(typeof(ObjectType), entityType) ?? "(undef)";
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

    public static StoredPacket UpdatePacketPartsForContent(this StoredPacket storedPacket)
    {
        if (storedPacket.Source == PacketSource.CLIENT)
        {
            return UpdateClientPacketClassification(storedPacket);
        }

        storedPacket.AnalyzeState = PacketAnalyzeState.NONE;
        ClearClassification(storedPacket);

        if (IsServerKeepalivePong(storedPacket.ContentBytes))
        {
            return FinalizeKnownProtocolPacket(storedPacket, "KEEPALIVE PONG",
                PacketEventClassifier.ClassifyServerKeepalivePong());
        }

        if (IsServerSixSecondPing(storedPacket.ContentBytes))
        {
            return FinalizeKnownProtocolPacket(storedPacket, "PING 6S",
                PacketEventClassifier.ClassifyServerSixSecondPing());
        }

        if (IsServerFifteenSecondPing(storedPacket.ContentBytes))
        {
            return FinalizeKnownProtocolPacket(storedPacket, "PING 15S",
                PacketEventClassifier.ClassifyServerFifteenSecondPing());
        }

        var allParts = new List<PacketPart>();
        var undefTypes = false;
        var sawResolvedEntity = false;
        var shouldHidePacket = true;
        var subPacketIndex = 0;
        var falseBoundaryScanBudget = 0;
        const int maxFalseBoundaryScanBits = 4096;
        var fullStream = new BitStream(storedPacket.ContentBytes);
        var totalBits = storedPacket.ContentBytes.Length * 8L;
        PacketEventClassification? bestClassification = null;

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
            if (subPacketIndex > 200)
            {
                break;
            }

            var initialBitOffset = (int)fullStream.BitOffsetFromStart;
            var test1 = fullStream.ReadBytes(4, true);
            if (test1.HasEqualElementsAs(packet_04_00_4F_01))
            {
                var parts = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream, "0x0400F401",
                    subPacketIndex);
                allParts.AddRange(parts);
                ConsiderClassification(ref bestClassification, PacketEventClassifier.ClassifyServerAck());
                falseBoundaryScanBudget = 0;
                if (!fullStream.ValidPosition)
                {
                    break;
                }

                continue;
            }

            fullStream.SeekBitOffset(initialBitOffset);

            // Entity header: id(16) + reserved(2) + object_type(10) + reserved(1) + action_type(8) = 37 bits
            if (fullStream.BitOffsetFromStart + 37 > totalBits)
            {
                break;
            }

            var entId = fullStream.ReadUInt16();
            var reservedLow = fullStream.ReadByte(2);
            var objectTypeVal = fullStream.ReadUInt16(10);
            var reservedBit28 = fullStream.ReadBit().AsBool();
            var actionTypeVal = fullStream.ReadByte();
            var headerValid = reservedLow == 0 && !reservedBit28;

            var objectType = Enum.IsDefined(typeof(ObjectType), objectTypeVal)
                ? (ObjectType)objectTypeVal
                : ObjectType.Unknown;
            var actionType = Enum.IsDefined(typeof(EntityActionType), (int)actionTypeVal)
                ? (EntityActionType)actionTypeVal
                : EntityActionType.UNDEF;

            if (!headerValid)
            {
                // Reject mid-payload false entity starts; keep scanning one bit forward.
                falseBoundaryScanBudget++;
                if (falseBoundaryScanBudget > maxFalseBoundaryScanBits)
                {
                    break;
                }

                ConsiderClassification(ref bestClassification,
                    PacketEventClassifier.ClassifyFalseBoundary(reservedLow, reservedBit28));
                fullStream.SeekBitOffset(initialBitOffset + 1);
                subPacketIndex--;
                continue;
            }

            falseBoundaryScanBudget = 0;
            fullStream.SeekBitOffset(initialBitOffset);

            var currentParts = new List<PacketPart>();
            var typeWithDelimiter = false;
            var actionRecovered = false;

            if (objectType == ObjectType.Despawn)
            {
                var despawn = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream, "despawn",
                    subPacketIndex, $"DESPAWN: {entId:X4}");
                currentParts.AddRange(despawn);
                typeWithDelimiter = true;
                sawResolvedEntity = true;
                storedPacket.ObjectType ??= objectType;
                ConsiderClassification(ref bestClassification,
                    PacketEventClassifier.ClassifyServerEntity(objectType, objectTypeVal, actionType, actionTypeVal,
                        true, false));
            }
            else if (EntityObjectTypes.Contains(objectType) || IsRecoverableEntityAction(actionType))
            {
                // Re-read past the validated header fields for optional spawn payload probing.
                fullStream.ReadUInt16();
                fullStream.ReadByte(2);
                fullStream.ReadUInt16(10);
                fullStream.ReadBit();
                fullStream.ReadByte();

                if (actionType is EntityActionType.INTERACT or EntityActionType.FULL_SPAWN
                    or EntityActionType.FULL_SPAWN_2)
                {
                    shouldHidePacket = false;
                }

                if (objectType == ObjectType.Other && actionType != EntityActionType.FULL_SPAWN)
                {
                    var dividerFound = false;
                    while (fullStream.ValidPosition)
                    {
                        var dividerTest = fullStream.ReadByte();
                        if (!fullStream.ValidPosition)
                        {
                            break;
                        }

                        if (dividerTest == 0x7E)
                        {
                            dividerFound = true;
                            break;
                        }

                        fullStream.SeekBack(7);
                    }

                    if (dividerFound)
                    {
                        continue;
                    }

                    break;
                }

                var interactionType = EntityInteractionType.UNDEF;
                var hasGameId = false;
                var optionalFields = new List<OptionalPacketFields>();
                var canProbeSpawnFields = EntityObjectTypes.Contains(objectType);

                if (canProbeSpawnFields && fullStream.BitOffsetFromStart + 16 + 112 + 1 <= totalBits)
                {
                    var interactionTypeVal = fullStream.ReadUInt16();
                    interactionType = Enum.IsDefined(typeof(EntityInteractionType), (int)interactionTypeVal)
                        ? (EntityInteractionType)interactionTypeVal
                        : EntityInteractionType.UNDEF;
                    fullStream.ReadBits(112);
                    if (fullStream.ValidPosition)
                    {
                        hasGameId = fullStream.ReadBit().AsBool();
                        if (hasGameId && actionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
                        {
                            if (EquippableItemTypes.Contains(objectType))
                            {
                                fullStream.ReadBits(14);
                                var hasSuffix = !fullStream.ReadBit().AsBool();
                                var suffixLengthType = fullStream.ReadByte(2);
                                if (!hasSuffix)
                                {
                                    suffixLengthType = 0;
                                }

                                var suffixLength = suffixLengthType switch
                                {
                                    0 => 3,
                                    1 => 7,
                                    _ => 7
                                };
                                _ = fullStream.ReadByte(suffixLength);
                                fullStream.ReadBits(23);
                                fullStream.ReadBits(55);
                            }
                            else
                            {
                                fullStream.ReadBits(98);
                            }

                            _ = fullStream.ReadInt64(31);
                            optionalFields = GetOptionalFields(fullStream);
                        }
                        else if (actionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
                        {
                            fullStream.ReadBits(87);
                            _ = fullStream.ReadInt64(31);
                            optionalFields = GetOptionalFields(fullStream);
                            shouldHidePacket = false;
                        }
                    }
                }

                fullStream.SeekBitOffset(initialBitOffset);
                var (success, parts) = GetNewEntityPacketParts(fullStream, objectType,
                    entId, actionType, interactionType, subPacketIndex, hasGameId, optionalFields);

                if ((!success || !parts.Any()) && IsRecoverableEntityAction(actionType))
                {
                    fullStream.SeekBitOffset(initialBitOffset);
                    parts = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream,
                        "header_with_action_type", subPacketIndex,
                        $"RECOVERED ACTION 0x{actionTypeVal:X2} -- {objectType} [{entId:X4}]");
                    actionRecovered = parts.Any();
                    success = actionRecovered;
                }

                currentParts.AddRange(parts);
                if (success && parts.Any())
                {
                    if (actionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2
                        or EntityActionType.INTERACT)
                    {
                        shouldHidePacket = false;
                    }

                    typeWithDelimiter = true;
                    sawResolvedEntity = true;
                    storedPacket.ObjectType ??= objectType == ObjectType.Unknown ? null : objectType;
                }
                else if (actionType == EntityActionType.UNDEF)
                {
                    fullStream.SeekBitOffset(initialBitOffset);
                    var unresolved = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream,
                        "header_with_action_type", subPacketIndex,
                        $"UNRESOLVED ACTION 0x{actionTypeVal:X2} -- type {objectTypeVal} [{entId:X4}]");
                    currentParts.AddRange(unresolved);
                    undefTypes = true;
                }
                else
                {
                    undefTypes = true;
                }

                ConsiderClassification(ref bestClassification,
                    PacketEventClassifier.ClassifyServerEntity(objectType, objectTypeVal, actionType, actionTypeVal,
                        success && parts.Any() && !actionRecovered, actionRecovered));
            }
            else if (actionType == EntityActionType.UNDEF)
            {
                var unresolved = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream,
                    "header_with_action_type", subPacketIndex,
                    $"UNRESOLVED ACTION 0x{actionTypeVal:X2} -- type {objectTypeVal} [{entId:X4}]");
                currentParts.AddRange(unresolved);
                undefTypes = true;
                ConsiderClassification(ref bestClassification,
                    PacketEventClassifier.ClassifyUnresolvedAction(actionTypeVal));
            }
            else
            {
                var header = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream, "entity_header",
                    subPacketIndex,
                    $"UNKNOWN TYPE: {objectType} ({objectTypeVal})");
                currentParts.AddRange(header);
                undefTypes = true;
                ConsiderClassification(ref bestClassification,
                    PacketEventClassifier.ClassifyServerEntity(objectType, objectTypeVal, actionType, actionTypeVal,
                        false, false));
            }

            allParts.AddRange(currentParts);

            if (typeWithDelimiter)
            {
                if (objectType is ObjectType.Teleport or ObjectType.TeleportBroken or ObjectType.TeleportWild)
                {
                    fullStream.ReadBit();
                }

                if (!fullStream.ValidPosition)
                {
                    break;
                }

                var delimTest = fullStream.ReadByte();
                if (!fullStream.ValidPosition)
                {
                    break;
                }

                fullStream.SeekBack(8);
                if (delimTest == 0x7E || delimTest == 0x7F)
                {
                    subPacketIndex++;
                    var delimiter = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream, "delimiter",
                        subPacketIndex, PacketPart.UndefinedFieldValue);
                    allParts.AddRange(delimiter);
                    continue;
                }

                if (objectType is ObjectType.DoorEntrance)
                {
                    var delimTestShort = fullStream.ReadByte(7);
                    if (delimTestShort is not (0x7E or 0x7F or 0x3F or 0x3E))
                    {
                        fullStream.SeekBack(7);
                    }
                }

                continue;
            }

            // Valid header but incomplete parse: seek a delimiter instead of aborting the whole packet.
            if (undefTypes && !sawResolvedEntity)
            {
                if (!TrySeekToNextDelimiter(fullStream))
                {
                    break;
                }

                subPacketIndex++;
                var delimiter = FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(fullStream, "delimiter",
                    subPacketIndex, PacketPart.UndefinedFieldValue);
                allParts.AddRange(delimiter);
                undefTypes = false;
                continue;
            }

            if (undefTypes)
            {
                if (!TrySeekToNextDelimiter(fullStream))
                {
                    break;
                }

                continue;
            }

            break;
        }

        storedPacket.PacketParts = allParts;
        if (allParts.Any())
        {
            storedPacket.AnalyzeState = undefTypes && !sawResolvedEntity
                ? PacketAnalyzeState.UNDEF_TYPE
                : PacketAnalyzeState.PARTIAL;
        }

        if (bestClassification is { } classification)
        {
            ApplyClassification(storedPacket, classification);
        }

        if (shouldHidePacket)
        {
            storedPacket.HiddenByDefaultServer = true;
            storedPacket.HiddenByDefault = storedPacket.HiddenByDefaultClient || storedPacket.HiddenByDefaultServer;
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

    private static StoredPacket FinalizeKnownProtocolPacket(StoredPacket storedPacket, string headerComment,
        PacketEventClassification classification)
    {
        var allParts = new List<PacketPart>();
        var stream = new BitStream(storedPacket.ContentBytes);
        if (storedPacket.ContentBytes.HasEqualElementsAs(ok_mark, 2))
        {
            allParts.AddRange(FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(
                stream, "server_packet_header", 0, headerComment));
        }

        storedPacket.PacketParts = allParts;
        storedPacket.AnalyzeState = PacketAnalyzeState.PARTIAL;
        ApplyClassification(storedPacket, classification);
        storedPacket.HiddenByDefaultServer = true;
        storedPacket.HiddenByDefault = true;
        return storedPacket;
    }

    private static StoredPacket UpdateClientPacketClassification(StoredPacket storedPacket)
    {
        ClearClassification(storedPacket);
        storedPacket.AnalyzeState = PacketAnalyzeState.NONE;

        var content = storedPacket.ContentBytes;
        if (content.Length == 0)
        {
            ApplyClassification(storedPacket,
                new PacketEventClassification("client.invalid_or_trailing", 0, "empty packet", false));
            RefreshHiddenByDefaultFlags(storedPacket);
            return storedPacket;
        }

        PacketEventClassification? bestClassification = null;
        var offset = 0;
        var frameIndex = 0;
        var sawValidEvent = false;

        while (offset < content.Length)
        {
            var declaredLength = content[offset];
            if (declaredLength >= 1 && offset + declaredLength <= content.Length)
            {
                var frame = content.AsSpan(offset, declaredLength);
                var classification = PacketEventClassifier.ClassifyClientFrame(frame);
                if (frameIndex == 0 || (classification.IsEvent && !sawValidEvent))
                {
                    ConsiderClassification(ref bestClassification, classification);
                }

                if (classification.IsEvent)
                {
                    sawValidEvent = true;
                }

                offset += declaredLength;
                frameIndex++;
                continue;
            }

            // Length prefix is non-canonical or truncated — classify the remainder as one frame.
            var remainderClassification = PacketEventClassifier.ClassifyClientFrame(content.AsSpan(offset));
            if (!sawValidEvent || remainderClassification.IsEvent)
            {
                ConsiderClassification(ref bestClassification, remainderClassification);
            }

            break;
        }

        if (bestClassification is { } chosen)
        {
            ApplyClassification(storedPacket, chosen);
            storedPacket.AnalyzeState = chosen.IsEvent ? PacketAnalyzeState.PARTIAL : PacketAnalyzeState.UNDEF;
        }

        RefreshHiddenByDefaultFlags(storedPacket);
        return storedPacket;
    }

    private static bool IsRecoverableEntityAction(EntityActionType actionType)
    {
        return actionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2
            or EntityActionType.SET_POSITION or EntityActionType.ATTACK or EntityActionType.INTERACT
            or EntityActionType.UNKNOWN;
    }

    private static bool TrySeekToNextDelimiter(BitStream stream)
    {
        while (stream.ValidPosition)
        {
            var delimiterTest = stream.ReadByte();
            if (!stream.ValidPosition)
            {
                return false;
            }

            if (delimiterTest is 0x7E or 0x7F)
            {
                stream.SeekBack(8);
                return true;
            }

            stream.SeekBack(7);
        }

        return false;
    }

    private static void ClearClassification(StoredPacket storedPacket)
    {
        storedPacket.EventName = null;
        storedPacket.EventReason = null;
        storedPacket.EventConfidence = 0;
        storedPacket.IsClassifiedEvent = false;
    }

    private static void ApplyClassification(StoredPacket storedPacket, PacketEventClassification classification)
    {
        storedPacket.EventName = classification.EventName;
        storedPacket.EventConfidence = classification.Confidence;
        storedPacket.EventReason = classification.Reason;
        storedPacket.IsClassifiedEvent = classification.IsEvent;
        storedPacket.PacketType ??= PacketEventClassifier.ToPacketType(classification.EventName);
    }

    private static void ConsiderClassification(ref PacketEventClassification? current,
        PacketEventClassification candidate)
    {
        if (current is null)
        {
            current = candidate;
            return;
        }

        var currentValue = current.Value;
        if (!currentValue.IsEvent && candidate.IsEvent)
        {
            current = candidate;
            return;
        }

        if (currentValue.IsEvent == candidate.IsEvent && candidate.Confidence > currentValue.Confidence)
        {
            current = candidate;
        }
    }

    private static List<OptionalPacketFields> GetOptionalFields(BitStream stream)
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

            var fieldLength = nextField == (byte)OptionalPacketFields.MADE_BY ? 2 : stream.ReadByte();
            if (!stream.ValidPosition)
            {
                break;
            }

            var fieldName = Enum.IsDefined(typeof(OptionalPacketFields), nextField)
                ? (OptionalPacketFields)nextField
                : OptionalPacketFields.UNKNOWN;

            if (fieldName is not OptionalPacketFields.UNKNOWN)
            {
                result.Add(fieldName);
            }

            stream.ReadBits(8 * fieldLength - 1);
        }

        return result;
    }

    private static Tuple<bool, List<PacketPart>> GetNewEntityPacketParts(BitStream stream, ObjectType objectType,
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

    private static StoredPacket AddPacketPartAnalyzeData(this StoredPacket storedPacket)
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

    private static PacketAnalyzeData GetAnalyzeDataForSubpacket(List<PacketPart> subpacket)
    {
        var result = new PacketAnalyzeData(subpacket);
        var outputPath = PacketLogViewerMainWindow.AppConfig.GetSection("Settings").GetValue<string>("OutputFolder");
        if (result.ObjectType is ObjectType.Monster or ObjectType.MonsterFlyer)
        {
            var mob = new MobPacket(subpacket);
            result = mob;
            if (result.ObjectType is ObjectType.Monster or ObjectType.MonsterFlyer &&
                mob.ActionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{mob.Id:X4}",
                    result.ObjectType,
                    mob.ActionType,
                    mob.X,
                    mob.Y,
                    mob.Z,
                    mob.Angle,
                    mob.CurrentHP,
                    mob.MaxHP,
                    mob.Type,
                    mob.Level) + "\n";
                File.AppendAllText($@"{outputPath}\\mob.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.Despawn)
        {
            result = new DespawnPacket(subpacket);
        }

        else if (result.ObjectType is ObjectType.NpcTrade or ObjectType.NpcQuestTitle or ObjectType.NpcQuestDegree
                 or ObjectType.NpcQuestKarma or ObjectType.NpcGuilder or ObjectType.NpcBanker
                 or ObjectType.NpcTournament)
        {
            var npcTradePacket = new NpcTradePacket(subpacket);
            result = npcTradePacket;
            if (npcTradePacket.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{npcTradePacket.Id:X4}",
                    npcTradePacket.ObjectType,
                    npcTradePacket.ActionType,
                    npcTradePacket.X,
                    npcTradePacket.Y,
                    npcTradePacket.Z,
                    npcTradePacket.Angle,
                    npcTradePacket.NameId,
                    npcTradePacket.TypeNameLength,
                    npcTradePacket.TypeName,
                    npcTradePacket.IconNameLength,
                    npcTradePacket.IconName,
                    npcTradePacket.NpcTradeType) + "\n";
                File.AppendAllText($@"{outputPath}\\npc.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.DoorEntrance)
        {
            var door = new DoorEntrancePacket(subpacket);
            result = door;
            if (door.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{door.Id:X4}",
                    result.ObjectType,
                    door.ActionType,
                    door.X,
                    door.Y,
                    door.Z,
                    door.Angle,
                    door.SubtypeID,
                    door.TargetX,
                    door.TargetY,
                    door.TargetZ) + "\n";
                File.AppendAllText($@"{outputPath}\\doors.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.DoorExit)
        {
            var door = new DoorExitPacket(subpacket);
            result = door;
            if (door.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{door.Id:X4}",
                    result.ObjectType,
                    door.ActionType,
                    door.X,
                    door.Y,
                    door.Z,
                    door.Angle,
                    door.ExitX,
                    door.ExitY,
                    door.ExitZ,
                    door.ExitAngle) + "\n";
                File.AppendAllText($@"{outputPath}\\door_exits.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.DoorEntranceWithKey)
        {
            var door = new DoorEntranceWithKey(subpacket);
            result = door;
            if (door.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{door.Id:X4}",
                    result.ObjectType,
                    door.ActionType,
                    door.X,
                    door.Y,
                    door.Z,
                    door.Angle,
                    door.SubtypeID) + "\n";
                File.AppendAllText($@"{outputPath}\\doors_with_key.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.TeleportWithTarget)
        {
            var tp = new TeleportWithTargetPacket(subpacket);
            result = tp;
            if (tp.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{tp.Id:X4}",
                    result.ObjectType,
                    tp.ActionType,
                    tp.X,
                    tp.Y,
                    tp.Z,
                    tp.Angle,
                    tp.SubtypeID) + "\n";
                File.AppendAllText($@"{outputPath}\\target_tps.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.CastleTablet)
        {
            var castleTablet = new CastleTablet(subpacket);
            result = castleTablet;
            if (castleTablet.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{castleTablet.Id:X4}",
                    result.ObjectType,
                    castleTablet.ActionType,
                    castleTablet.X,
                    castleTablet.Y,
                    castleTablet.Z,
                    castleTablet.Angle,
                    (int)castleTablet.Castle) + "\n";
                File.AppendAllText($@"{outputPath}\\castle_tablets.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.CastleGate)
        {
            var castleGates = new CastleGate(subpacket);
            result = castleGates;
            if (castleGates.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{castleGates.Id:X4}",
                    result.ObjectType,
                    castleGates.ActionType,
                    castleGates.X,
                    castleGates.Y,
                    castleGates.Z,
                    castleGates.Angle,
                    (int)castleGates.Castle) + "\n";
                File.AppendAllText($@"{outputPath}\\castle_gates.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.CastleEntrance)
        {
            var castleEntrance = new CastleEntrance(subpacket);
            result = castleEntrance;
            if (castleEntrance.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{castleEntrance.Id:X4}",
                    result.ObjectType,
                    castleEntrance.ActionType,
                    castleEntrance.X,
                    castleEntrance.Y,
                    castleEntrance.Z,
                    castleEntrance.Angle,
                    (int)castleEntrance.Castle) + "\n";
                File.AppendAllText($@"{outputPath}\\castle_entrances.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.LightCrystal)
        {
            var lightCrystal = new WorldObject(subpacket);
            result = lightCrystal;
            if (lightCrystal.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{lightCrystal.Id:X4}",
                    result.ObjectType,
                    lightCrystal.ActionType,
                    lightCrystal.X,
                    lightCrystal.Y,
                    lightCrystal.Z,
                    lightCrystal.Angle) + "\n";
                File.AppendAllText($@"{outputPath}\\light_crystals.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.LightCrystalYellow)
        {
            var lightCrystal = new WorldObject(subpacket);
            result = lightCrystal;
            if (lightCrystal.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{lightCrystal.Id:X4}",
                    result.ObjectType,
                    lightCrystal.ActionType,
                    lightCrystal.X,
                    lightCrystal.Y,
                    lightCrystal.Z,
                    lightCrystal.Angle) + "\n";
                File.AppendAllText($@"{outputPath}\\light_crystals_yellow.txt", output);
            }
        }

        else if (WorldObjectsToTrack.TryGetValue(result.ObjectType, out var filename))
        {
            var worldObject = new WorldObject(subpacket);
            result = worldObject;

            if (worldObject.ActionType == EntityActionType.FULL_SPAWN)
            {
                var output = FileFormatCulture.JoinFields('\t',
                    $"{worldObject.Id:X4}",
                    worldObject.ObjectType,
                    worldObject.ActionType,
                    worldObject.X,
                    worldObject.Y,
                    worldObject.Z,
                    worldObject.Angle) + "\n";
                File.AppendAllText($@"{outputPath}\\{filename}.txt", output);
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
                var output = FileFormatCulture.JoinFields('\t',
                    $"{item.Id:X4}",
                    result.ObjectType,
                    item.ActionType,
                    item.X,
                    item.Y,
                    item.Z,
                    item.Angle,
                    gameId,
                    item.ContainerId,
                    suffix,
                    item.PALevel,
                    item.Count,
                    item.RemainingUses,
                    item.OwnerName) + "\n";
                File.AppendAllText($@"{outputPath}\\items.txt", output);
            }
        }

        else if (result.ObjectType is ObjectType.Other)
        {
            // assume it's new character for now. This is likely very wrong
            result = new CharacterPacket(subpacket);
        }

        return result;
    }

    private static List<PacketPart> FindPartsByName(BitStream stream, string name, bool isSubpacket)
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

    private static List<PacketPart> FindPartsByNameSkipLastUndefSetCommentUpdateBitOffset(BitStream stream,
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