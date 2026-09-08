using System.Collections.Generic;
using System.Linq;
using BitStreams;
using SphereHelpers.Extensions;
using SpherePacketVisualEditor;
using SphServer.Helpers.Enums;

namespace PacketLogViewer;

internal static class StatUpdateParser
{
    private const int Divider = 0b0001011;
    private const string MarkerEnumName = "stat_field_markers";

    // Same markers NetworkedStatsUpdater writes (and stat_field_markers.sphenum).
    private static readonly Dictionary<int, string> FallbackFieldNames = new()
    {
        [0] = "hp_current",
        [1] = "hp_max",
        [2] = "mp_current",
        [3] = "mp_max",
        [4] = "satiety_current",
        [5] = "satiety_max",
        [6] = "strength",
        [7] = "agility",
        [8] = "accuracy",
        [9] = "endurance",
        [10] = "earth",
        [11] = "air",
        [12] = "water",
        [13] = "fire",
        [16] = "pd",
        [17] = "md",
        [18] = "pa",
        [19] = "ma",
        [20] = "is_invisible",
        [21] = "block_equip",
        [37] = "title_level",
        [38] = "degree_level",
        [39] = "karma_type",
        [40] = "karma",
        [41] = "title_xp",
        [42] = "degree_xp",
        [44] = "title_stats_available",
        [45] = "degree_stats_available",
        [46] = "gender",
        [47] = "clan_rank_type",
        [52] = "title_rebirth",
        [53] = "degree_rebirth",
        [57] = "money",
    };

    private static readonly (byte r, byte g, byte b)[] FieldColors =
    [
        (94, 148, 171),
        (149, 57, 199),
        (4, 154, 2),
        (234, 174, 95),
        (226, 177, 5),
        (205, 30, 19),
        (7, 150, 210),
        (29, 75, 22),
        (99, 142, 161),
        (42, 224, 129),
        (180, 33, 159),
        (60, 254, 22),
    ];

    public static bool LooksLikeStatUpdate(byte[] packetBytes, long bitOffset)
    {
        if (bitOffset % 8 != 0)
        {
            return false;
        }

        var byteOffset = (int)(bitOffset / 8);
        if (byteOffset + 4 > packetBytes.Length)
        {
            return false;
        }

        if (packetBytes[byteOffset + 2] != 0x08 || packetBytes[byteOffset + 3] != 0xC0)
        {
            return false;
        }

        // Six-second ping is also CI + 08 C0, then 42 A0.
        return byteOffset + 5 >= packetBytes.Length
               || packetBytes[byteOffset + 4] != 0x42
               || packetBytes[byteOffset + 5] != 0xA0;
    }

    public static List<PacketPart> Consume(BitStream stream, int subpacketIndex, long totalBits)
    {
        var parts = new List<PacketPart>();
        var usedNames = new Dictionary<string, int>();

        AddPart(parts, stream, PacketPartNames.ID, 16, PacketPartType.UINT64,
            255, 255, 0, comment: "STAT UPDATE");
        AddPart(parts, stream, PacketPartNames.Opcode, 16, PacketPartType.BYTES, 4, 255, 23);

        var peek = PeekUInt16(stream, totalBits);
        if (peek is not null && (peek.Value & 0x7F) != Divider)
        {
            var tagOffset = stream.BitOffsetFromStart;
            var tag = stream.ReadUInt16(14);
            stream.SeekBitOffset(tagOffset);
            var marker = (tag >> 5) & 0x3F;
            var name = UniqueName(FieldName(marker), usedNames);
            AddPart(parts, stream, name + "_tag", 14, PacketPartType.UINT64, 200, 141, 97);
            AddPart(parts, stream, name, 14, PacketPartType.INT64, 200, 141, 97);
        }

        var fieldIndex = 0;
        while (true)
        {
            var sep = PeekUInt16(stream, totalBits);
            if (sep is null || (sep.Value & 0x7F) != Divider)
            {
                break;
            }

            var lenBits = ((int)(sep.Value >> 14)) switch
            {
                0 => 3,
                1 => 7,
                2 => 14,
                3 => 31,
                _ => 0
            };
            if (lenBits == 0 || stream.BitOffsetFromStart + 16 + lenBits > totalBits)
            {
                break;
            }

            var neg = ((sep.Value >> 13) & 1) == 1;
            var marker = (int)((sep.Value >> 7) & 0x3F);
            var name = UniqueName(FieldName(marker), usedNames);
            var (r, g, b) = FieldColors[fieldIndex++ % FieldColors.Length];

            AddPart(parts, stream, name + "_divider", 7, PacketPartType.UINT64, r, g, b);
            AddPart(parts, stream, name + "_marker", 6, PacketPartType.UINT64, r, g, b, MarkerEnumName);
            AddPart(parts, stream, name + "_neg", 1, PacketPartType.BITS, r, g, b);
            AddPart(parts, stream, name + "_len", 2, PacketPartType.UINT64, r, g, b);
            var valuePart = AddPart(parts, stream, name, lenBits, PacketPartType.INT64, r, g, b);
            if (neg)
            {
                valuePart.ActualLongValue = -(valuePart.ActualLongValue ?? 0);
                valuePart.UpdateValueDisplayText();
            }

            if (fieldIndex > 80)
            {
                break;
            }
        }

        foreach (var part in parts)
        {
            part.SubpacketIndex = subpacketIndex;
        }

        return parts;
    }

    public static string FieldName(int marker)
    {
        if (PacketLogViewerMainWindow.DefinedEnums.TryGetValue(MarkerEnumName, out var names)
            && names.TryGetValue(marker, out var name))
        {
            return name;
        }

        return FallbackFieldNames.TryGetValue(marker, out var fallback) ? fallback : $"unk{marker}";
    }

    public static bool IsStatUpdateParts(List<PacketPart> parts) =>
        parts.Any(x => x.Comment == "STAT UPDATE" || x.Name == PacketPartNames.Opcode);

    private static PacketPart AddPart(List<PacketPart> parts, BitStream stream, string name, int bitLength,
        PacketPartType type, byte r, byte g, byte b, string? enumName = null, string comment = "")
    {
        var offset = (int)stream.BitOffsetFromStart;
        long? actual = null;
        if (type is PacketPartType.INT64 or PacketPartType.UINT64)
        {
            actual = stream.ReadInt64(bitLength);
            stream.SeekBitOffset(offset);
        }

        var value = stream.ReadBits(bitLength).Reverse().ToArray();
        var part = new PacketPart(bitLength, name, enumName, false, type, offset, value, r, g, b, 255, comment)
        {
            ActualLongValue = actual
        };
        part.UpdateValueDisplayText();
        parts.Add(part);
        return part;
    }

    private static ushort? PeekUInt16(BitStream stream, long totalBits)
    {
        if (!stream.ValidPosition || stream.BitOffsetFromStart + 16 > totalBits)
        {
            return null;
        }

        var offset = stream.BitOffsetFromStart;
        var value = stream.ReadUInt16();
        stream.SeekBitOffset(offset);
        return value;
    }

    private static string UniqueName(string name, Dictionary<string, int> used)
    {
        if (!used.TryGetValue(name, out var count))
        {
            used[name] = 1;
            return name;
        }

        used[name] = count + 1;
        return $"{name}_{count + 1}";
    }
}
