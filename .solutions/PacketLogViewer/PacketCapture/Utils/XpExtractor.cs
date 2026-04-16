using System;
using System.Collections.Generic;
using System.Linq;
using BitStreams;
using PacketLogViewer;
using SphereHelpers.Extensions;
using SpherePacketVisualEditor;
using SphServer.Helpers;

enum XpToExtract
{
    Degree,
    Title,
}

public static class XpExtractor
{
    const int DEGREE_XP_MARKER = 5387;     // 0b01010100001011
    const int TITLE_XP_MARKER = 5259;      // 0b01010010001011
    const int DEGREE_MISSION_MARKER = 1346;   // 0b010101000010
    const int MISSION_TITLE_MARKER = 1346;    // 0b010100100010

    public static Tuple<bool, bool, int, int> TryExtractAllXpFromPacket(byte[] packetBytes, short clientId)
    {
        if (!IsSuitablePacket(packetBytes, clientId, out var stream))
        {
            return Tuple.Create(false, false, 0, 0);
        }

        var currentPosition = stream.BitOffsetFromStart;

        var degreeXpSuccess = TryExtractXpFromPacket(stream, XpToExtract.Degree, out var newDegreeXp);
        stream.SeekBitOffset(currentPosition);
        var titleXpSuccess = TryExtractXpFromPacket(stream, XpToExtract.Title, out var newTitleXp);

        return Tuple.Create(degreeXpSuccess, titleXpSuccess, newDegreeXp, newTitleXp);
    }

    public static bool TryFindMobKilledByClient(List<PacketPart> packetParts, short clientId, out int killedEntityId)
    {
        killedEntityId = 0;
        if (clientId == 0 || packetParts.Count == 0)
        {
            return false;
        }

        foreach (var group in packetParts
                     .Where(p => p.SubpacketIndex > 0)
                     .GroupBy(p => p.SubpacketIndex)
                     .OrderBy(g => g.Key))
        {
            var parts = group.ToList();

            var objectTypePart = parts.FirstOrDefault(x =>
                x.Name == PacketPartNames.EntityType || x.Name == PacketPartNames.ObjectType);
            var objectTypeVal = (ushort)(objectTypePart?.ActualLongValue ?? ushort.MaxValue);
            var objectType = Enum.IsDefined(typeof(ObjectType), objectTypeVal)
                ? (ObjectType)objectTypeVal
                : ObjectType.Unknown;
            if (objectType is not (ObjectType.Monster or ObjectType.MonsterFlyer))
            {
                continue;
            }

            var actionTypeVal =
                (int)(parts.FirstOrDefault(x => x.Name == PacketPartNames.ActionType)?.ActualLongValue ?? int.MaxValue);
            var actionType = Enum.IsDefined(typeof(EntityActionType), actionTypeVal)
                ? (EntityActionType)actionTypeVal
                : EntityActionType.UNDEF;
            if (actionType != EntityActionType.INTERACT)
            {
                continue;
            }

            var interactionTypeVal = (int)(parts.FirstOrDefault(x => x.Name == "interaction_type")?.ActualLongValue ??
                                           int.MaxValue);
            var interactionType = Enum.IsDefined(typeof(EntityInteractionType), interactionTypeVal)
                ? (EntityInteractionType)interactionTypeVal
                : EntityInteractionType.UNDEF;
            if (interactionType != EntityInteractionType.DEATH)
            {
                continue;
            }

            var killedByVal = (ushort)(parts.FirstOrDefault(x => x.Name == "killed_by_id")?.ActualLongValue ??
                                       ushort.MaxValue);
            if (killedByVal != (ushort)clientId)
            {
                continue;
            }

            killedEntityId = (int)(parts.FirstOrDefault(x => x.Name == PacketPartNames.ID)?.ActualLongValue ?? 0);
            return killedEntityId != 0;
        }

        return false;
    }

    private static bool TryExtractXpFromPacket(BitStream stream, XpToExtract xpToExtract, out int newXp)
    {
        newXp = 0;

        // for mobs, it's a full marker. For missions, there isn't actually a C008, but it's easier
        // to search for, so we'll use that and check for a different marker instead

        var missionMarkerBitCount = 12;
        var missionMarkerValue = xpToExtract == XpToExtract.Degree ? DEGREE_MISSION_MARKER : MISSION_TITLE_MARKER;

        var missionTest = stream.ReadUInt16(missionMarkerBitCount);
        if (!stream.ValidPosition)
        {
            return false;
        }

        if (missionTest == missionMarkerValue)
        {
            var missionLengthType = stream.ReadByte(2);
            if (!stream.ValidPosition)
            {
                return false;
            }

            var missionLengthBits = missionLengthType switch
            {
                1 => 7,
                2 => 14,
                3 => 31,
                _ => 0
            };
            if (missionLengthBits == 0)
            {
                return false;
            }

            var missionXp = (int)stream.ReadUInt32(missionLengthBits);
            if (!stream.ValidPosition)
            {
                return false;
            }

            newXp = missionXp;
            return true;
        }

        stream.SeekBack(missionMarkerBitCount);

        var markerBitCount = 14;
        var markerValue = xpToExtract == XpToExtract.Degree ? DEGREE_XP_MARKER : TITLE_XP_MARKER;

        while (stream.ValidPosition)
        {
            var test = (int)stream.ReadUInt16(markerBitCount);
            if (!stream.ValidPosition)
            {
                return false;
            }

            stream.SeekBack(markerBitCount);
            if (test == markerValue)
            {
                break;
            }

            stream.ReadBit();
        }

        if (!stream.ValidPosition)
        {
            return false;
        }

        _ = stream.ReadUInt16(markerBitCount);
        var lengthType = stream.ReadByte(2);
        if (!stream.ValidPosition)
        {
            return false;
        }

        var lengthBits = lengthType switch
        {
            1 => 7,
            2 => 14,
            3 => 31,
            _ => 0
        };
        if (lengthBits == 0)
        {
            return false;
        }

        var xp = (int)stream.ReadUInt32(lengthBits);
        if (!stream.ValidPosition)
        {
            return false;
        }

        newXp = xp;
        return true;
    }

    private static bool IsSuitablePacket(byte[] packetBytes, short clientId, out BitStream stream)
    {
        stream = new BitStream(packetBytes);

        if (clientId == 0 || packetBytes.Length == 0)
        {
            return false;
        }

        // Skip server packet header if present: len_1 len_2 2c 01 00 sync_1 sync_2
        if (packetBytes.Length >= 7 && packetBytes.HasEqualElementsAs(PacketAnalyzer.ok_mark, 2))
        {
            _ = stream.ReadBytes(7, true);
        }

        // Find bit-aligned pattern: clientId (16 bits) followed by 0xC0 0x08 (8+8 bits).
        var found = false;
        while (stream.ValidPosition)
        {
            var idTest = stream.ReadUInt16();
            var c008Test = stream.ReadUInt16();
            if (!stream.ValidPosition)
            {
                break;
            }

            if (idTest == (ushort)clientId && c008Test == 0xC008)
            {
                found = true;
                break;
            }

            stream.SeekBack(32);
            stream.ReadBit();
        }

        return found;
    }
}