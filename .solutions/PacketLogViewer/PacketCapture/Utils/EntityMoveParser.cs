using System;
using System.Collections.Generic;
using System.Linq;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer;

internal static class EntityMoveParser
{
    public const string XPlus32768 = "x_plus_32768";
    public const string YPlus1200 = "y_plus_1200";
    public const string ZPlus32768 = "z_plus_32768";

    public const int ServerMoveEntityBits = 184;
    public const string ServerMoveEntityDefinition = "server_move_entity";

    public static bool LooksLikeServerMoveEntity(byte[] packetBytes, int bitOffset = 0)
    {
        var totalBits = packetBytes.Length * 8L;
        if (bitOffset + ServerMoveEntityBits > totalBits)
        {
            return false;
        }

        if (bitOffset == 0)
        {
            if (packetBytes.Length < 7 || packetBytes[2] != 0x2C || packetBytes[3] != 0x01)
            {
                return false;
            }

            // 08C0 lives at the same wrapper offset; do not steal those.
            if (StatUpdateParser.LooksLikeStatUpdate(packetBytes, 56))
            {
                return false;
            }

            // Real entity_move / spawn starts with id+type+action after the 56-bit wrapper.
            if (HasStrongEntityHeader(packetBytes, 56, totalBits))
            {
                return false;
            }
        }

        var x = Read(packetBytes, bitOffset + 41, 16) - 32768;
        var y = 1200 - Read(packetBytes, bitOffset + 57, 13);
        var z = 32768 - Read(packetBytes, bitOffset + 70, 16);
        var entityId = Read(packetBytes, bitOffset + 101, 16);
        return entityId != 0
               && Math.Abs(x) <= 10000
               && Math.Abs(z) <= 10000
               && Math.Abs(y) <= 1000;
    }

    public static bool LooksLikeEntityMove(byte[] packetBytes, long bitOffset, long totalBits)
    {
        if (bitOffset + 142 > totalBits)
        {
            return false;
        }

        var reserved = Read(packetBytes, (int)bitOffset + 16, 2);
        var action = Read(packetBytes, (int)bitOffset + 29, 8);
        return reserved == 0 && action == (int)EntityActionType.SET_POSITION;
    }

    public static bool HasStrongEntityHeader(byte[] packetBytes, int bitOffset, long totalBits)
    {
        if (bitOffset + 37 > totalBits)
        {
            return false;
        }

        var reserved = Read(packetBytes, bitOffset + 16, 2);
        var objectTypeVal = Read(packetBytes, bitOffset + 18, 10);
        var bit28 = Read(packetBytes, bitOffset + 28, 1);
        var action = Read(packetBytes, bitOffset + 29, 8);
        if (reserved != 0 || !IsKnownEntityAction((byte)action))
        {
            return false;
        }

        var defined = Enum.IsDefined(typeof(ObjectType), (ushort)objectTypeVal);
        return bit28 == 0 || defined;
    }

    public static bool IsKnownEntityAction(byte actionTypeVal) =>
        Enum.IsDefined(typeof(EntityActionType), (int)actionTypeVal)
        && (EntityActionType)actionTypeVal != EntityActionType.UNDEF;

    public static bool IsServerMoveParts(List<PacketPart> parts) =>
        parts.Any(x => x.Name == XPlus32768);

    public static int Read(byte[] data, int bitOffset, int width)
    {
        var v = 0;
        for (var i = 0; i < width; i++)
        {
            var b = bitOffset + i;
            v |= ((data[b / 8] >> (b % 8)) & 1) << i;
        }

        return v;
    }
}
