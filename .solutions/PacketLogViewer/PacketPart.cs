using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitStreams;
using LiteDB;
using PacketLogViewer;
using SphereHelpers.Extensions;
using SphServer.Helpers;
using SphServer.Helpers.Enums;

namespace SpherePacketVisualEditor;

public record PacketPartDisplayText (
    string bitsStr,
    string bytesStr,
    string textStr,
    string longStr,
    string ulongStr,
    string? enumValueStr,
    string? coordsClientStr,
    string? coordsServerStr)
{
    public string Bits { get; set; } = bitsStr;
    public string Bytes { get; set; } = bytesStr;
    public string? CoordsClient { get; set; } = coordsClientStr;
    public string? CoordsServer { get; set; } = coordsServerStr;
    public string? EnumValue { get; set; } = enumValueStr;
    public string Long { get; set; } = longStr;
    public string Text { get; set; } = textStr;
    public string Ulong { get; set; } = ulongStr;
}

public class PacketPart
{
    public const string UndefinedFieldValue = "__undef";
    public const string LengthFromPreviousFieldValue = "__fromPrevious";
    public const string ShiftTestValue = "__shiftTest";
    public const string CountTestValue = "__countTest";
    public const string HasGameIdValue = "__hasGameId";
    public const string HasSuffixValue = "__hasSuffix";
    public const string HasItemNameValue = "__hasStringName";
    public const string MaybeDelimiterValue = "__maybeDelimiter";
    public string? EnumName { get; set; }
    public byte HighlightColorR { get; set; }
    public byte HighlightColorG { get; set; }
    public byte HighlightColorB { get; set; }
    public byte HighlightColorA { get; set; }
    public bool LengthFromPreviousField { get; set; }
    public PacketPartType PacketPartType { get; set; }
    public int BitOffset { get; set; }
    public long BitLength { get; set; }
    [BsonIgnore] public PacketPartDisplayText DisplayText;
    [BsonIgnore] public Bit[] Value { get; set; }
    public string Comment { get; set; }
    public long? ActualLongValue { get; set; }
    public int SubpacketIndex { get; set; }
    public int BitOffsetEnd => (int) (BitOffset + BitLength);

    public PacketPart (int length, string name, string? enumName, bool lengthFromPreviousField,
        PacketPartType packetPartType, int bitOffset, Bit[] value, byte r, byte g, byte b, byte a, string comment = "")
    {
        BitLength = length;
        LengthFromPreviousField = lengthFromPreviousField;
        HighlightColorR = r;
        HighlightColorG = g;
        HighlightColorB = b;
        HighlightColorA = a;
        Name = name;
        EnumName = enumName;
        BitOffset = bitOffset;
        Value = value;
        PacketPartType = packetPartType;
        Comment = comment;
        UpdateValueDisplayText();
    }

    public PacketPart ()
    {
        // required for litedb
    }

    [BsonIgnore] public string PartListDisplayText { get; set; }

    public string Name { get; set; }

    public bool Overlaps (PacketPart other)
    {
        return BitOffset <= other.BitOffset && BitOffsetEnd >= other.BitOffsetEnd;
    }

    public bool ContainedWithin (PacketPart other)
    {
        return BitOffset > other.BitOffset && BitOffsetEnd < other.BitOffsetEnd;
    }

    public PacketPart GetPiece (int newOffset, int newLength, string? name = null)
    {
        if (newOffset < BitOffset || newOffset + newLength > BitOffsetEnd)
        {
            return this;
        }

        var skipStart = newOffset - BitOffset;
        var skipEnd = BitOffsetEnd - (newOffset + newLength);

        var newValue = Value.Skip(skipEnd > 0 ? skipEnd : 0).SkipLast(skipStart > 0 ? skipStart : 0).ToArray();

        return new PacketPart(newLength, name ?? Name, EnumName, LengthFromPreviousField, PacketPartType,
            newOffset, newValue, HighlightColorR, HighlightColorG, HighlightColorB, HighlightColorA);
    }

    public string GetDisplayTextForValueType ()
    {
        switch (PacketPartType)
        {
            case PacketPartType.BITS:
                return DisplayText.Bits;
            case PacketPartType.BYTES:
                return DisplayText.Bytes;
            case PacketPartType.INT64:
                return DisplayText.Long;
            case PacketPartType.UINT64:
                return DisplayText.Ulong;
            case PacketPartType.STRING:
                return DisplayText.Text;
            case PacketPartType.COORDS_CLIENT:
                return DisplayText.CoordsClient ?? string.Empty;
            case PacketPartType.COORDS_SERVER:
                return DisplayText.CoordsServer ?? string.Empty;
            default:
                return string.Empty;
        }
    }

    public void UpdateValueDisplayText ()
    {
        var bits = new List<Bit>(Value);
        bits.Reverse();
        DisplayText = GetValueDisplayText(bits, EnumName);
        PartListDisplayText =
            $"{Name} ({BitOffset / 8}, {BitOffset % 8}) to ({BitOffsetEnd / 8}, {BitOffsetEnd % 8})";
    }

    public static PacketPartDisplayText GetValueDisplayText (List<Bit> bits, string? enumName)
    {
        var remainingBitsToFullByte = bits.Count % 8;
        var bitsPadding = remainingBitsToFullByte == 0 ? 0 : 8 - remainingBitsToFullByte;

        if (bitsPadding != 0)
        {
            for (var i = 0; i < bitsPadding; i++)
            {
                bits.Add(0);
            }
        }

        var bytes = BitStream.BitArrayToBytes(bits.ToArray());
        var stream = new BitStream(bytes);
        var textChars = PacketLogViewerMainWindow.Win1251.GetString(bytes).ToCharArray();
        var visibleChars = textChars.Select(PacketLogViewerMainWindow.GetVisibleChar).ToArray();
        var textString = new string(visibleChars);
        Array.Reverse(bytes);
        var bytesString = Convert.ToHexString(bytes);
        var actualBits = bits.ToArray()[..^bitsPadding];
        Array.Reverse(actualBits);
        var bitsString = string.Join("", actualBits.Select(x => (int) x));
        var longValue = stream.ReadInt64();
        var longValueStr = bytes.Length > 8 ? "(too large)" : $"0x{bytesString} = {longValue}";
        stream.Seek(0, 0);
        var ulongValue = stream.ReadUInt64();
        var ulongValueStr = bytes.Length > 8 ? "(too large)" : $"0x{bytesString} = {ulongValue}";
        stream.Seek(0, 0);
        var coordsServer = bytes.Length >= 4 ? CoordsHelper.DecodeServerCoordinate(bytes) : (double?) null;
        var coordsServerStr = coordsServer is null ? null : $"{coordsServer:F2}";
        var coordsClient = bytes.Length >= 4 ? CoordsHelper.DecodeClientCoordinateWithoutShift(bytes) : (double?) null;
        var coordsClientStr = coordsClient is null ? null : $"{coordsClient:F2}";

        var enumValueStr = enumName is null ? null : "(undef)";
        if (bytes.Length <= 8 && enumName is not null && PacketLogViewerMainWindow.DefinedEnums.ContainsKey(enumName) &&
            PacketLogViewerMainWindow.DefinedEnums[enumName].ContainsKey((int) ulongValue))
        {
            enumValueStr = PacketLogViewerMainWindow.DefinedEnums[enumName][(int) ulongValue];
        }
        else if (bytes.Length <= 8 && enumName is not null &&
                 PacketLogViewerMainWindow.DefinedEnums.ContainsKey(enumName) && enumName == "localizables" &&
                 ulongValue != 2560)
        {
            // try to find nearest defined value (armor would be put as 1000:something 1002:something_else, while
            // quest armor would have 1001 as index)
            var localizables = PacketLogViewerMainWindow.DefinedEnums[enumName];
            var currentIndex = (int) ulongValue;
            if (SphObjectDb.GameObjectDataDb.ContainsKey(currentIndex))
            {
                enumValueStr = SphObjectDb.GameObjectDataDb[currentIndex].Localisation[Locale.Russian];
            }

            else
            {
                while (currentIndex >= 0)
                {
                    if (localizables.ContainsKey(currentIndex))
                    {
                        enumValueStr = localizables[currentIndex];
                        break;
                    }

                    currentIndex--;
                }
            }
        }
        else if (bytes.Length <= 8 && enumName is not null &&
                 PacketLogViewerMainWindow.DefinedEnums.ContainsKey(enumName) && enumName == "localizables" &&
                 ulongValue == 2560)
        {
            enumValueStr = "NO_GAME_OBJECT";
        }

        return new PacketPartDisplayText(bitsString, bytesString, textString, longValueStr, ulongValueStr,
            enumValueStr, coordsClientStr, coordsServerStr);
    }

    public static List<PacketPart> LoadFromFile (string filePath, string groupName, BitStream contentStream,
        int bitOffset, bool isMob = false, bool isItem = false, bool optionalPartsIncluded = false)
    {
        var initialOffset = contentStream.BitOffsetFromStart;
        if (groupName == "door_entrance")
        {
            // some doors desperately want to be teleports
            contentStream.ReadBits(198);
            var tpTest = contentStream.ReadUInt16(15);
            if (tpTest == 0x7FFF)
            {
                filePath = filePath.Replace("door_entrance", "door_entrance_tp");
            }

            contentStream.SeekBitOffset(initialOffset);
        }

        if (groupName == "entity_monster")
        {
            // level 1 monsters are special
            contentStream.ReadBits(141);
            var hpSizeType = contentStream.ReadByte(5);
            var hpSize = hpSizeType == 0x11 ? 16 : 8;
            contentStream.ReadBits(hpSize);
            contentStream.ReadBits(hpSize == 8 ? 2 : 1);
            contentStream.ReadBits(hpSize);
            contentStream.ReadBits(hpSize == 8 ? 2 : 1);
            var mobType = contentStream.ReadUInt32(14);
            contentStream.ReadBits(3);
            var lvlValue = contentStream.ReadUInt32(29);
            if (lvlValue == 0x104028)
            {
                filePath = filePath.Replace("entity_monster", "monster_level_1");
            }
            else if (mobType > 2000)
            {
                filePath = filePath.Replace("entity_monster", "monster_full");
            }

            contentStream.SeekBitOffset(initialOffset);
        }

        if (groupName == "new_player_dungeon_start")
        {
            // some of these are longer than others for whatever reason
            contentStream.ReadBits(696);
            var delimTest = contentStream.ReadByte();
            if (delimTest != 0x7E)
            {
                filePath = filePath.Replace("new_player_dungeon_start", "new_player_dungeon_start_long");
            }
            
            contentStream.SeekBitOffset(initialOffset);
        }

        var contents = File.ReadAllLines(filePath);
        var parts = new List<PacketPart>();

        foreach (var line in contents)
        {
            var fieldValues = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);

            if (fieldValues.Length < 9)
            {
                Console.WriteLine($"Missing fields in {groupName}, line: {line}");
            }

            var partName = fieldValues[0];

            var packetPartType = Enum.TryParse(fieldValues[1], out PacketPartType partType)
                ? partType
                : PacketPartType.BITS;
            var start = int.Parse(fieldValues[2]);
            var length = 0;
            var lengthFromPrevious = false;
            if (fieldValues[3] == LengthFromPreviousFieldValue)
            {
                lengthFromPrevious = true;
            }
            else
            {
                length = int.Parse(fieldValues[3]);
            }

            var enumName = fieldValues[4];
            if (enumName == UndefinedFieldValue)
            {
                enumName = null;
            }

            var r = byte.Parse(fieldValues[5]);
            var g = byte.Parse(fieldValues[6]);
            var b = byte.Parse(fieldValues[7]);
            var a = byte.Parse(fieldValues[8]);

            var part = new PacketPart(length, partName, enumName, lengthFromPrevious, packetPartType,
                start, [], r, g, b, a);
            parts.Add(part);
        }

        UpdatePacketPartValues(parts, contentStream, bitOffset, isMob, isItem);

        if (!optionalPartsIncluded)
        {
            var countTestPart = parts.FirstOrDefault(x => x.Name == CountTestValue);
            if (countTestPart != null && countTestPart.ActualLongValue == 0)
            {
                // should replace this with packet with count
                var nameWithCount = Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(filePath) + "_counted" +
                    Path.GetExtension(filePath));
                contentStream.SeekBitOffset(initialOffset);
                return LoadFromFile(nameWithCount, groupName, contentStream, bitOffset, isMob, isItem, true);
            }
        }

        return parts;
    }

    public static void UpdatePacketPartValues (List<PacketPart> parts, BitStream contentStream, int bitOffset,
        bool isMob = false, bool isItem = false)
    {
        if (bitOffset != 0)
        {
            contentStream.SeekBitOffset(bitOffset);
        }

        var hasGameId = false;
        var hasSuffix = false;
        var suffixLength = 7;
        var levelIsOne = false;

        for (var i = 0; i < parts.Count; i++)
        {
            var packetPart = parts[i];
            var currentOffset = (int) contentStream.BitOffsetFromStart;
            packetPart.BitOffset = currentOffset;
            var length = packetPart.BitLength;

            if (packetPart.LengthFromPreviousField && i > 0)
            {
                var byteValue = BitStream.BitArrayToBytes(parts[i - 1].Value.ToArray().Reverse().ToArray()) ??
                                new byte[4];
                Array.Resize(ref byteValue, 4);
                length = Math.Max(BitConverter.ToInt32(byteValue) * 8, 0);
                for (var j = i + 1; j < parts.Count; j++)
                {
                    parts[j].BitOffset += (int) length;
                }

                packetPart.BitLength = length;
            }

            if (isMob && packetPart.Name == PacketPartNames.Skip && i > 0 && parts[i - 1].Name == PacketPartNames.Angle)
            {
                var val = contentStream.ReadByte((int) length);
                if (val <= 8)
                {
                    length -= 1;
                    packetPart.BitLength = length;
                }

                contentStream.SeekBitOffset(currentOffset);
                // find 36 bit 0 after level and cut
            }

            if (packetPart.Name == PacketPartNames.HpSizeType && contentStream.ValidPosition)
            {
                var val = contentStream.ReadByte(2);
                var hpLength = val switch
                {
                    0 => 8,
                    _ => 16
                };
                if (parts.Count > i + 2 && parts[i + 2].Name == PacketPartNames.CurrentHP)
                {
                    var oldLength = parts[i + 2].BitLength;
                    parts[i + 2].BitLength = hpLength;
                    if (oldLength != hpLength)
                    {
                        var lengthDiff = hpLength - oldLength;
                        for (var j = i + 2; j < parts.Count; j++)
                        {
                            parts[j].BitOffset += (int) lengthDiff;
                        }
                    }
                }

                if (hpLength == 8 && parts.Count > i + 3 && parts[i + 3].Name == "skip_1")
                {
                    parts[i + 3].BitLength = 2;
                    for (var j = i + 3; j < parts.Count; j++)
                    {
                        parts[j].BitOffset += 1;
                    }
                }

                if (parts.Count > i + 4 && parts[i + 4].Name == PacketPartNames.MaxHP)
                {
                    var oldLength = parts[i + 4].BitLength;
                    parts[i + 4].BitLength = hpLength;
                    if (oldLength != hpLength)
                    {
                        var lengthDiff = hpLength - oldLength;
                        for (var j = i + 4; j < parts.Count; j++)
                        {
                            parts[j].BitOffset += (int) lengthDiff;
                        }
                    }
                }

                if (hpLength == 8 && parts.Count > i + 5 && parts[i + 5].Name == "skip_1")
                {
                    parts[i + 5].BitLength = 2;
                    for (var j = i + 5; j < parts.Count; j++)
                    {
                        parts[j].BitOffset += 1;
                    }
                }

                contentStream.SeekBitOffset(currentOffset);
            }

            if (packetPart.Name == HasGameIdValue && contentStream.ValidPosition)
            {
                hasGameId = contentStream.ReadBit().AsBool();
                contentStream.SeekBitOffset(currentOffset);
            }

            if (packetPart.Name == HasSuffixValue && hasGameId && contentStream.ValidPosition)
            {
                hasSuffix = !contentStream.ReadBit().AsBool();

                var suffixLengthType = contentStream.ReadByte(2);
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
                if (parts.Count > i + 2 && parts[i + 2].Name == PacketPartNames.Suffix)
                {
                    var oldLength = parts[i + 2].BitLength;
                    parts[i + 2].BitLength = suffixLength;
                    if (oldLength != suffixLength)
                    {
                        var lengthDiff = suffixLength - oldLength;
                        for (var j = i + 2; j < parts.Count; j++)
                        {
                            parts[j].BitOffset += (int) lengthDiff;
                        }
                    }
                }

                contentStream.SeekBitOffset(currentOffset);
            }

            if (packetPart.Name == "skip_36_bits")
            {
                var zeroCount = 0;
                var bitsRead = 0;
                while (contentStream.ValidPosition && zeroCount < 36)
                {
                    var curr = contentStream.ReadBit().AsInt();
                    if (curr == 0)
                    {
                        zeroCount++;
                    }
                    else
                    {
                        zeroCount = 0;
                    }

                    bitsRead++;
                    var lengthDiff = bitsRead - packetPart.BitLength;
                    if (lengthDiff > 0)
                    {
                        packetPart.BitLength = bitsRead;
                        length = packetPart.BitLength;
                        for (var j = i + 1; j < parts.Count; j++)
                        {
                            parts[j].BitOffset += (int) lengthDiff;
                        }
                    }
                }

                // after this it's either a delimiter or named mob parts
                try
                {
                    var bitTest = contentStream.ReadByte();
                    if (bitTest == 0x14)
                    {
                        // named
                        packetPart.BitLength += 47;
                        length = packetPart.BitLength;
                        for (var j = i + 1; j < parts.Count; j++)
                        {
                            parts[j].BitOffset += 47;
                        }
                    }
                }
                catch (Exception)
                {
                }

                contentStream.SeekBitOffset(currentOffset);
            }

            if (packetPart.PacketPartType is PacketPartType.INT64 or PacketPartType.UINT64)
            {
                // should be good enough
                packetPart.ActualLongValue = contentStream.ReadInt64(packetPart.BitLength);
            }

            contentStream.SeekBitOffset(currentOffset);

            if (packetPart.Name == "level_last_3")
            {
                levelIsOne = true;
            }

            if (packetPart.Name == PacketPartNames.Level && contentStream.ValidPosition)
            {
                var levelVal = contentStream.ReadInt64(packetPart.BitLength);
                var levelVal1 = levelVal & 0b11111;
                var levelVal2 = ((levelVal >> 17) & 0b11111) << 5;
                var level = levelIsOne
                    ? 1
                    : levelVal switch
                    {
                        0x7D080 => 128,
                        0x3E840 => 64,
                        0x1F420 => 32,
                        0xFA10 => 16,
                        _ => (int) (levelVal1 + levelVal2 + 1)
                    };
                packetPart.ActualLongValue = level;
            }

            contentStream.SeekBitOffset(currentOffset);

            packetPart.Value = contentStream.ReadBits(length).Reverse().ToArray();
            packetPart.UpdateValueDisplayText();
        }

        // if (morePartsToAppend.Any())
        // {
        //     parts.AddRange(morePartsToAppend);
        //     delimiterAt = parts.Count;
        // }
        //
        // if (delimiterAt != int.MaxValue)
        // {
        //     var delimiterDefinition =
        //         PacketLogViewerMainWindow.Subpackets.FirstOrDefault(x => x.Name == "delimiter");
        //     var delimiterParts = delimiterDefinition.LoadFromFile(contentStream, 0, isMob, isItem);
        //     var delimiterPart = delimiterParts.First();
        //     if (parts.Count <= delimiterAt)
        //     {
        //         parts.Add(delimiterPart);
        //     }
        //     else
        //     {
        //         parts[delimiterAt] = delimiterPart;
        //     }
        // }
        //
        // parts.RemoveAll(x => x.BitLength == 0);
    }
}