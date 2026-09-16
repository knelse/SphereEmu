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
        var headerBytes = decoded.Direction == MbcDirection.Client ? 9 : 5;
        var parts = new List<PacketPart>();
        var stream = new BitStream(packetBytes);
        var totalBits = packetBytes.Length * 8L;
        var headerIndex = subpacketIndex;
        var lengthBit = bodyBitOffset - headerBytes * 8;
        if (lengthBit >= 0)
        {
            Add(parts, stream, totalBits, lengthBit, 16, PacketPartNames.Length, PacketPartType.UINT64,
                120, 120, 120, headerIndex);
        }

        if (bodyBitOffset >= 24)
        {
            Add(parts, stream, totalBits, bodyBitOffset - 24, 16, PacketPartNames.Channel, PacketPartType.UINT64,
                149, 57, 199, headerIndex);
        }

        Add(parts, stream, totalBits, bodyBitOffset - 8, 8, PacketPartNames.Control, PacketPartType.UINT64,
            120, 120, 120, headerIndex);
        Add(parts, stream, totalBits, bodyBitOffset, 1, PacketPartNames.HasPosition, PacketPartType.BITS,
            180, 180, 180, headerIndex);
        var bit = bodyBitOffset + 1;
        if (decoded.HasPosition && decoded.BasePosition is { Length: 3 } basePos)
        {
            // Wire is trunc(world)+bias; BasePosition is already world (client: -32768 / -1200 / -32768).
            AddOrigin(parts, stream, totalBits, bit, 16, "origin_x", basePos[0], 32768, 94, 148, 171, headerIndex);
            bit += 16;
            AddOrigin(parts, stream, totalBits, bit, 13, "origin_y", basePos[1], 1200, 149, 57, 199, headerIndex);
            bit += 13;
            AddOrigin(parts, stream, totalBits, bit, 16, "origin_z", basePos[2], 32768, 4, 154, 2, headerIndex);
            bit += 16;
        }

        Add(parts, stream, totalBits, bit, 15, PacketPartNames.Tick, PacketPartType.UINT64,
            234, 174, 95, headerIndex);
        bit += 15;
        var sectionBanners = BuildProcessSectionBanners(decoded);
        AddProcessId(parts, stream, totalBits, bit, 18, headerIndex, sectionBanners[0]);
        bit += 18;
        Add(parts, stream, totalBits, bit, 12, PacketPartNames.ModuleTag, PacketPartType.UINT64,
            7, 150, 210, headerIndex);
        if (parts.Count > 0)
        {
            var tagPart = parts[^1];
            var tag = decoded.ModuleTag;
            var mod = string.IsNullOrEmpty(decoded.Module) ? $"tag {tag}" : decoded.Module;
            tagPart.ListValuePrimary = mod;
            tagPart.ListValueSecondary = $" = {tag} (0x{tag:X})";
        }

        for (var switchIndex = 0; switchIndex < decoded.Switches.Count; switchIndex++)
        {
            var switchMark = decoded.Switches[switchIndex];
            var partIndex = ++subpacketIndex;
            Add(parts, stream, totalBits, bodyBitOffset + switchMark.StartBit, 7, PacketPartNames.WireRegion,
                PacketPartType.UINT64, 180, 180, 180, partIndex);
            ApplyWireRegionDisplay(parts, MbcWire.Switch, MbcWire.RegionType(MbcWire.Switch));
            var banner = switchIndex + 1 < sectionBanners.Count ? sectionBanners[switchIndex + 1] : "";
            AddProcessId(parts, stream, totalBits, bodyBitOffset + switchMark.StartBit + 7, 18, partIndex, banner);
            Add(parts, stream, totalBits, bodyBitOffset + switchMark.StartBit + 25, 12, PacketPartNames.ModuleTag,
                PacketPartType.UINT64, 7, 150, 210, partIndex);
            if (parts.Count > 0)
            {
                var tagPart = parts[^1];
                var tag = switchMark.ModuleTag;
                var fromEvent = decoded.Events.FirstOrDefault(e =>
                    e.ProcessId == switchMark.ProcessId && e.ModuleTag == tag);
                var mod = !string.IsNullOrEmpty(fromEvent?.Module)
                    ? fromEvent.Module
                    : $"tag {tag}";
                tagPart.ListValuePrimary = mod;
                tagPart.ListValueSecondary = $" = {tag} (0x{tag:X})";
            }
        }

        foreach (var life in decoded.Lifecycle)
        {
            var lifeIndex = ++subpacketIndex;
            Add(parts, stream, totalBits, bit, 12, PacketPartNames.WireRegion, PacketPartType.UINT64,
                180, 180, 180, lifeIndex, $"MBC.{life.Type}");
        }

        foreach (var decodedEvent in decoded.Events)
        {
            var eventIndex = ++subpacketIndex;
            Add(parts, stream, totalBits, bodyBitOffset + decodedEvent.StartBit, 7, PacketPartNames.WireRegion,
                PacketPartType.UINT64, 180, 180, 180, eventIndex);
            ApplyWireRegionDisplay(parts, decodedEvent.WireRegion,
                MbcWire.RegionType(decodedEvent.WireRegion, decodedEvent.Region, decodedEvent.RecoveredName,
                    decodedEvent.Command));
            if (decodedEvent.Command is not null)
            {
                var commandField = decodedEvent.Fields[0];
                var cmdName = decodedEvent.RecoveredName;
                var named = !string.IsNullOrEmpty(cmdName)
                            && !cmdName.StartsWith("cmd", StringComparison.OrdinalIgnoreCase);
                Add(parts, stream, totalBits, bodyBitOffset + commandField.BitOffset, commandField.BitLength,
                    PacketPartNames.Command, PacketPartType.UINT64, 205, 30, 19, eventIndex);
                if (named && parts.Count > 0)
                {
                    var part = parts[^1];
                    part.ListValuePrimary = cmdName;
                    part.ListValueSecondary =
                        $" = {decodedEvent.Command} (0x{decodedEvent.Command:X})";
                }
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
                    : field.ArrayValue is not null && field.StringValue is null
                        ? PacketPartType.BYTES
                        : field.Kind is "text"
                            ? PacketPartType.STRING
                            : PacketPartType.INT64;
                var partName = string.IsNullOrEmpty(field.Name) ? $"{field.Kind}_{i}" : field.Name;
                Add(parts, stream, totalBits, bodyBitOffset + field.BitOffset, field.BitLength,
                    partName, type, r, g, b, eventIndex);
                ApplySemanticPartDisplay(parts, field);
            }
        }

        return parts;
    }

    /// <summary>
    ///     Part list must show the same semantic value as the event banner (varint sign/width,
    ///     signed_m30000, world coords, floats). Raw bit-span integer is secondary only.
    /// </summary>
    private static void ApplySemanticPartDisplay(List<PacketPart> parts, MbcDecodedField field)
    {
        if (parts.Count == 0)
        {
            return;
        }

        var part = parts[^1];
        if (field.Kind == "object_id" && field.IntValue is { } oid)
        {
            part.ListValuePrimary = $"{oid:X4}";
            part.ListValueSecondary = $" = {oid}";
            return;
        }

        if (field.Kind == "signed_m30000" && field.IntValue is { } parsedM30)
        {
            var raw = field.RawWireValue ?? part.ActualLongValue ?? parsedM30 + 30000;
            part.ListValuePrimary = parsedM30.ToString();
            part.ListValueSecondary = $" = 0x{raw:X} ({raw})";
            return;
        }

        if (field.Kind == "float" && field.DoubleValue is { } fval)
        {
            part.ListValuePrimary = fval.ToString("0.###");
            if (field.RawWireValue is { } rawBits)
            {
                part.ListValueSecondary = $" = 0x{rawBits:X8}";
            }

            return;
        }

        if (field.Kind is "relX" or "relY" or "relZ")
        {
            var raw = field.IntValue ?? part.ActualLongValue ?? 0;
            if (field.DoubleValue is { } world)
            {
                part.ListValuePrimary = world.ToString("0.###");
                part.ListValueSecondary = $" = raw 0x{raw:X} ({raw})";
            }
            else
            {
                part.ListValuePrimary = field.Display;
                part.ListValueSecondary = $" = raw 0x{raw:X} ({raw})";
            }

            return;
        }

        // Varint (and any other decoded IntValue): banner uses IntValue; bit-span ActualLongValue
        // includes sign/tier bits and must not be the primary display.
        if (!string.IsNullOrEmpty(field.Display) && field.Display.Contains('='))
        {
            var eq = field.Display.IndexOf('=');
            part.ListValuePrimary = field.Display[(eq + 1)..];
            if (field.IntValue is { } parsedDisp)
            {
                part.ListValueSecondary = $" = {parsedDisp}";
            }

            return;
        }

        if (field.IntValue is { } parsed)
        {
            var wire = part.ActualLongValue;
            part.ListValuePrimary = parsed.ToString();
            if (wire is not null && wire != parsed)
            {
                part.ListValueSecondary = $" = wire 0x{wire:X} ({wire})";
            }
            else
            {
                part.ListValueSecondary = $" = 0x{parsed:X}";
            }
        }
    }

    private static void ApplyWireRegionDisplay(List<PacketPart> parts, int wire, string regionType)
    {
        if (parts.Count == 0)
        {
            return;
        }

        var part = parts[^1];
        part.ListValuePrimary = regionType;
        part.ListValueSecondary = $" = 0x{wire:X2} ({wire})";
    }

    private static List<string> BuildProcessSectionBanners(MbcDecodeResult decoded)
    {
        var sectionCount = 1 + decoded.Switches.Count;
        var sections = Enumerable.Range(0, sectionCount).Select(_ => new List<string>()).ToArray();
        foreach (var decodedEvent in decoded.Events)
        {
            var section = 0;
            for (var i = 0; i < decoded.Switches.Count; i++)
            {
                if (decoded.Switches[i].StartBit < decodedEvent.StartBit)
                {
                    section = i + 1;
                }
            }

            var name = MbcKnownEvents.DisplayName(decodedEvent);
            if (!string.IsNullOrEmpty(name))
            {
                sections[section].Add(name);
            }
        }

        return sections.Select(x => string.Join("; ", x)).ToList();
    }

    private static void AddProcessId(List<PacketPart> parts, BitStream stream, long totalBits, int bitOffset,
        int bitLength, int subpacket, string comment)
    {
        Add(parts, stream, totalBits, bitOffset, bitLength, PacketPartNames.ProcessId, PacketPartType.UINT64,
            255, 255, 0, subpacket, comment);
        if (parts.Count == 0)
        {
            return;
        }

        var part = parts[^1];
        var id = part.ActualLongValue ?? 0;
        part.ListValuePrimary = $"{id:X4}";
        part.ListValueSecondary = $" = {id}";
    }

    private static void AddOrigin(List<PacketPart> parts, BitStream stream, long totalBits, int bitOffset,
        int bitLength, string name, int world, int bias, byte r, byte g, byte b, int subpacket)
    {
        Add(parts, stream, totalBits, bitOffset, bitLength, name, PacketPartType.INT64, r, g, b, subpacket);
        if (parts.Count == 0)
        {
            return;
        }

        var part = parts[^1];
        var wire = part.ActualLongValue ?? world + bias;
        part.ListValuePrimary = world.ToString();
        part.ListValueSecondary = $" = 0x{wire:X} ({wire})";
    }

    public static List<PacketPart> BuildIdentity(byte[] packetBytes, int bodyBitOffset, MbcDirection direction,
        ref int subpacketIndex)
    {
        var decoded = new MbcDecodeResult { Direction = direction };
        if (bodyBitOffset < 8 || bodyBitOffset >= packetBytes.Length * 8)
        {
            return [];
        }

        decoded.Control = (byte)EntityMoveParser.Read(packetBytes, bodyBitOffset - 8, 8);
        decoded.HasPosition = EntityMoveParser.Read(packetBytes, bodyBitOffset, 1) == 1;
        var bit = bodyBitOffset + 1;
        if (decoded.HasPosition && bit + 45 <= packetBytes.Length * 8)
        {
            decoded.BasePosition =
            [
                EntityMoveParser.Read(packetBytes, bit, 16) - 0x8000,
                EntityMoveParser.Read(packetBytes, bit + 16, 13) - 0x4B0,
                EntityMoveParser.Read(packetBytes, bit + 29, 16) - 0x8000
            ];
            bit += 45;
        }

        if (bit + 45 <= packetBytes.Length * 8)
        {
            decoded.Tick = EntityMoveParser.Read(packetBytes, bit, 15);
            decoded.ProcessId = EntityMoveParser.Read(packetBytes, bit + 15, 18);
            decoded.ModuleTag = EntityMoveParser.Read(packetBytes, bit + 33, 12);
        }

        decoded.WireBitLength = Math.Max(0, packetBytes.Length * 8 - bodyBitOffset);
        var parts = Build(packetBytes, bodyBitOffset, decoded, ref subpacketIndex);
        var lastEnd = parts.Count == 0 ? bodyBitOffset : parts.Max(x => x.BitOffsetEnd);
        var totalBits = packetBytes.Length * 8;
        if (lastEnd < totalBits)
        {
            Add(parts, new BitStream(packetBytes), totalBits, lastEnd, totalBits - lastEnd, PacketPartNames.Skip,
                PacketPartType.BITS, 100, 100, 100, subpacketIndex);
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
