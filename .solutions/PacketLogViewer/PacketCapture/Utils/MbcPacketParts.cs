using System;
using System.Collections.Generic;
using System.Linq;
using BitStreams;
using SphereHelpers.Extensions;
using SpherePacketVisualEditor;
using SphServer.Helpers.Enums;
using SphServer.Helpers.Networking;

namespace PacketLogViewer;

internal static class MbcPacketParts
{
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

    public static List<PacketPart> Build(byte[] packetBytes, int bodyBitOffset, MbcDecodeResult decoded,
        ref int subpacketIndex)
    {
        var parts = new List<PacketPart>();
        var stream = new BitStream(packetBytes);
        var totalBits = packetBytes.Length * 8L;
        var headerIndex = subpacketIndex;

        Add(parts, stream, totalBits, bodyBitOffset - 8, 8, PacketPartNames.Control, PacketPartType.UINT64,
            120, 120, 120, headerIndex, $"ctrl {decoded.Control:X2}");
        Add(parts, stream, totalBits, bodyBitOffset, 1, PacketPartNames.HasPosition, PacketPartType.BITS,
            180, 180, 180, headerIndex);
        var bit = bodyBitOffset + 1;
        if (decoded.HasPosition && decoded.BasePosition is { Length: 3 } basePos)
        {
            Add(parts, stream, totalBits, bit, 16, PacketPartNames.CoordX, PacketPartType.INT64,
                94, 148, 171, headerIndex, $"x={basePos[0]}");
            bit += 16;
            Add(parts, stream, totalBits, bit, 13, PacketPartNames.CoordY, PacketPartType.INT64,
                149, 57, 199, headerIndex, $"y={basePos[1]}");
            bit += 13;
            Add(parts, stream, totalBits, bit, 16, PacketPartNames.CoordZ, PacketPartType.INT64,
                4, 154, 2, headerIndex, $"z={basePos[2]}");
            bit += 16;
        }

        Add(parts, stream, totalBits, bit, 15, PacketPartNames.Tick, PacketPartType.UINT64,
            234, 174, 95, headerIndex, $"tick {decoded.Tick}");
        bit += 15;
        Add(parts, stream, totalBits, bit, 18, PacketPartNames.ProcessId, PacketPartType.UINT64,
            255, 255, 0, headerIndex, decoded.Module);
        bit += 18;
        Add(parts, stream, totalBits, bit, 12, PacketPartNames.ModuleTag, PacketPartType.UINT64,
            7, 150, 210, headerIndex, decoded.Module, PacketPartNames.ObjectTypesEnum);

        foreach (var life in decoded.Lifecycle)
        {
            var lifeIndex = ++subpacketIndex;
            Add(parts, stream, totalBits, bit, 12, PacketPartNames.WireRegion, PacketPartType.UINT64,
                205, 30, 19, lifeIndex, $"MBC.{life.Type}");
        }

        foreach (var decodedEvent in decoded.Events)
        {
            var eventIndex = ++subpacketIndex;
            var display = MbcKnownEvents.DisplayName(decodedEvent);
            Add(parts, stream, totalBits, bodyBitOffset + decodedEvent.StartBit, 7, PacketPartNames.WireRegion,
                PacketPartType.UINT64, 226, 177, 5, eventIndex, display, decodedEvent.Schema);
            if (decodedEvent.Command is not null)
            {
                var commandField = decodedEvent.Fields[0];
                Add(parts, stream, totalBits, bodyBitOffset + commandField.BitOffset, commandField.BitLength,
                    PacketPartNames.Command, PacketPartType.UINT64, 205, 30, 19, eventIndex,
                    $"cmd {decodedEvent.Command}");
            }

            var fieldStart = decodedEvent.Command is not null ? 1 : 0;
            for (var i = fieldStart; i < decodedEvent.Fields.Count; i++)
            {
                var field = decodedEvent.Fields[i];
                if (field.BitLength <= 0)
                {
                    continue;
                }

                var (r, g, b) = FieldColors[i % FieldColors.Length];
                var type = field.Kind is "relX" or "relY" or "relZ" or "angle"
                    ? PacketPartType.INT64
                    : field.ArrayValue is not null
                        ? PacketPartType.BYTES
                        : PacketPartType.INT64;
                Add(parts, stream, totalBits, bodyBitOffset + field.BitOffset, field.BitLength,
                    $"{field.Kind}_{i}", type, r, g, b, eventIndex, field.Display);
            }
        }

        return parts;
    }

    private static void Add(List<PacketPart> parts, BitStream stream, long totalBits, int bitOffset, int bitLength,
        string name, PacketPartType type, byte r, byte g, byte b, int subpacket, string comment = "",
        string? enumName = null)
    {
        if (bitOffset < 0 || bitOffset > totalBits)
        {
            return;
        }

        var length = bitLength;
        if (bitOffset + length > totalBits)
        {
            length = (int)(totalBits - bitOffset);
        }

        if (length <= 0)
        {
            return;
        }

        stream.SeekBitOffset(bitOffset);
        long? actual = null;
        if (type is PacketPartType.INT64 or PacketPartType.UINT64)
        {
            actual = stream.ReadInt64(length);
            stream.SeekBitOffset(bitOffset);
        }

        var value = stream.ReadBits(length).Reverse().ToArray();
        var part = new PacketPart(length, name, enumName, false, type, bitOffset, value, r, g, b, 255, comment)
        {
            ActualLongValue = actual,
            SubpacketIndex = subpacket
        };
        part.UpdateValueDisplayText();
        parts.Add(part);
    }
}
