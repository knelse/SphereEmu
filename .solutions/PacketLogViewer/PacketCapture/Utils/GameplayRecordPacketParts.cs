using System;
using System.Collections.Generic;
using System.Linq;
using BitStreams;
using SphereHelpers.Extensions;
using SpherePacketVisualEditor;
using SphServer.Helpers;
using SphServer.Helpers.Enums;
using SphServer.Helpers.Networking;

namespace PacketLogViewer;

internal static class GameplayRecordPacketParts
{
    public static List<PacketPart> Build(byte[] packetBytes)
    {
        var parts = new List<PacketPart>();
        if (packetBytes.Length < 2)
        {
            return parts;
        }

        var stream = new BitStream(packetBytes);
        var totalBits = packetBytes.Length * 8L;
        var offset = 0;
        var sub = 0;
        while (offset + 2 <= packetBytes.Length)
        {
            var size = packetBytes[offset] | (packetBytes[offset + 1] << 8);
            if (size < 2 || offset + size > packetBytes.Length)
            {
                break;
            }

            AddFrame(parts, stream, totalBits, packetBytes.AsSpan(offset, size), offset * 8, ++sub);
            offset += size;
        }

        return parts;
    }

    private static void AddFrame(List<PacketPart> parts, BitStream stream, long totalBits,
        ReadOnlySpan<byte> frame, int frameBit, int sub)
    {
        var bit = frameBit;
        Add(parts, stream, totalBits, bit, 16, PacketPartNames.Length, PacketPartType.UINT64,
            120, 120, 120, sub);
        bit += 16;
        if (frame.Length < ClientFrame.HeaderLength)
        {
            AddRest(parts, stream, totalBits, bit, frameBit + frame.Length * 8, sub);
            return;
        }

        Add(parts, stream, totalBits, bit, 16, PacketPartNames.Checksum, PacketPartType.UINT64,
            180, 180, 180, sub);
        bit += 16;
        Add(parts, stream, totalBits, bit, 16, PacketPartNames.Sequence, PacketPartType.UINT64,
            94, 148, 171, sub);
        bit += 16;
        var channel = (WireChannel)(frame[6] | (frame[7] << 8));
        Add(parts, stream, totalBits, bit, 16, PacketPartNames.Channel, PacketPartType.UINT64,
            149, 57, 199, sub, channel.ToString());
        bit += 16;

        var frameEnd = frameBit + frame.Length * 8;
        if (bit >= frameEnd)
        {
            return;
        }

        Add(parts, stream, totalBits, bit, 8, PacketPartNames.Pad, PacketPartType.BITS,
            160, 160, 160, sub);
        bit += 8;
        if (channel != WireChannel.Gameplay || frame.Length < 16)
        {
            AddRest(parts, stream, totalBits, bit, frameEnd, sub);
            return;
        }

        var frameBytes = frame.ToArray();
        var identity = GameplayRecord.ReadIdentity(frameBytes);
        Add(parts, stream, totalBits, bit, 1, PacketPartNames.HasPosition, PacketPartType.BITS,
            180, 180, 180, sub, identity.PositionFlag ? "1" : "0");
        bit += 1;
        if (identity.PositionFlag)
        {
            var record = new GameplayRecord(frameBytes);
            Add(parts, stream, totalBits, bit, 16, PacketPartNames.CoordX, PacketPartType.INT64,
                94, 148, 171, sub, $"x={record.CoarseX}");
            bit += 16;
            Add(parts, stream, totalBits, bit, 13, PacketPartNames.CoordY, PacketPartType.INT64,
                149, 57, 199, sub, $"y={record.CoarseY}");
            bit += 13;
            Add(parts, stream, totalBits, bit, 16, PacketPartNames.CoordZ, PacketPartType.INT64,
                4, 154, 2, sub, $"z={record.CoarseZ}");
            bit += 16;
        }

        Add(parts, stream, totalBits, bit, 15, PacketPartNames.Tick, PacketPartType.UINT64,
            234, 174, 95, sub, $"tick {identity.ClientClock}");
        bit += 15;
        Add(parts, stream, totalBits, bit, 18, PacketPartNames.ID, PacketPartType.UINT64,
            255, 255, 0, sub, $"id {identity.EntityId:X}");
        bit += 18;
        var objectTypeName = Enum.IsDefined(typeof(ObjectType), identity.SubjectType)
            ? Enum.GetName((ObjectType)identity.SubjectType) ?? identity.SubjectType.ToString()
            : identity.SubjectType.ToString();
        Add(parts, stream, totalBits, bit, 12, PacketPartNames.ObjectType, PacketPartType.UINT64,
            7, 150, 210, sub, objectTypeName, PacketPartNames.ObjectTypesEnum);
        bit += 12;
        Add(parts, stream, totalBits, bit, 7, PacketPartNames.WireRegion, PacketPartType.UINT64,
            180, 180, 180, sub, TagName(identity.Tag));
        bit += 7;

        if (!identity.PositionFlag && identity.Tag == GameplayRecord.TagPlayerAction)
        {
            Add(parts, stream, totalBits, bit, 5, PacketPartNames.ActionCode, PacketPartType.UINT64,
                226, 177, 5, sub, GameplayRecord.ActionOf(identity).ToString());
            bit += 5;
            Add(parts, stream, totalBits, bit, 1, PacketPartNames.ActionFlag, PacketPartType.BITS,
                60, 254, 22, sub, identity.ActionFlag ? "1" : "0");
            bit += 1;
        }
        else if (identity.PositionFlag)
        {
            Add(parts, stream, totalBits, bit, 4, PacketPartNames.Skip, PacketPartType.BITS,
                100, 100, 100, sub);
            bit += 4;
            Add(parts, stream, totalBits, bit, 32, "x_float", PacketPartType.UINT64,
                94, 148, 171, sub, "float x");
            bit += 32;
            Add(parts, stream, totalBits, bit, 32, "y_float", PacketPartType.UINT64,
                149, 57, 199, sub, "float y");
            bit += 32;
            Add(parts, stream, totalBits, bit, 32, "z_float", PacketPartType.UINT64,
                4, 154, 2, sub, "float z");
            bit += 32;
            Add(parts, stream, totalBits, bit, 32, "t_float", PacketPartType.UINT64,
                7, 150, 210, sub, "float t");
            bit += 32;
        }

        AddEventPayload(parts, stream, totalBits, frameBytes, frameBit, ref bit, frameEnd, sub);
        AddRest(parts, stream, totalBits, bit, frameEnd, sub);
    }

    private static void AddEventPayload(List<PacketPart> parts, BitStream stream, long totalBits,
        byte[] frame, int frameBit, ref int bit, int frameEnd, int sub)
    {
        var classified = ClientPacketClassifier.ClassifyFrame(frame);
        switch (classified.Event)
        {
            case ClientPacketEvent.CharacterSelect:
                {
                    var slotBit = frameBit + 17 * 8;
                    if (slotBit >= bit && slotBit + 8 <= frameEnd)
                    {
                        AddRest(parts, stream, totalBits, bit, slotBit, sub);
                        var slot = frame[17] / 4 - 1;
                        Add(parts, stream, totalBits, slotBit, 8, PacketPartNames.Slot, PacketPartType.UINT64,
                            226, 177, 5, sub, $"slot {slot}");
                        bit = slotBit + 8;
                    }

                    break;
                }
            case ClientPacketEvent.StatsUpdateRequest:
                {
                    var deltaBit = frameBit + 141;
                    if (deltaBit >= bit && deltaBit + 32 * 8 <= frameEnd)
                    {
                        AddRest(parts, stream, totalBits, bit, deltaBit, sub);
                        bit = deltaBit;
                        string[] names = ["str", "agi", "acc", "end", "earth", "air", "water", "fire"];
                        foreach (var name in names)
                        {
                            Add(parts, stream, totalBits, bit, 32, name, PacketPartType.INT64,
                                7, 150, 210, sub);
                            bit += 32;
                        }
                    }

                    break;
                }
            case ClientPacketEvent.CombatDamageTarget:
                {
                    var targetBit = frameBit + GameplayRecord.TargetIdBit;
                    if (targetBit >= bit && targetBit + 16 <= frameEnd)
                    {
                        AddRest(parts, stream, totalBits, bit, targetBit, sub);
                        Add(parts, stream, totalBits, targetBit, 16, PacketPartNames.TargetId, PacketPartType.UINT64,
                            205, 30, 19, sub);
                        bit = targetBit + 16;
                    }

                    break;
                }
        }
    }

    private static string TagName(byte tag) =>
        tag switch
        {
            GameplayRecord.TagTelemetry => "telemetry",
            GameplayRecord.TagObjectInteract => "object_interact",
            GameplayRecord.TagPosition => "position",
            GameplayRecord.TagPlayerAction => "player_action",
            _ => $"tag {tag}"
        };

    private static void AddRest(List<PacketPart> parts, BitStream stream, long totalBits, int bit, int end, int sub)
    {
        if (bit < end)
        {
            Add(parts, stream, totalBits, bit, end - bit, PacketPartNames.Skip, PacketPartType.BITS,
                100, 100, 100, sub);
        }
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
