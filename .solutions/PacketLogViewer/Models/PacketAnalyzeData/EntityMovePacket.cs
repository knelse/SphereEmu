using System.Collections.Generic;
using System.Linq;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class EntityMovePacket : PacketAnalyzeData
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int Angle { get; set; }

    public override string DisplayValue =>
        $"{Id:X4} (Move) at [{X:F1}, {Y:F1}, {Z:F1}] ang {Angle}";

    public EntityMovePacket(List<PacketPart> parts) : base(parts)
    {
        ActionType = EntityActionType.SET_POSITION;
        if (Id == 0)
        {
            Id = GetIntValue(PacketPartNames.ID);
        }

        if (Id == 0)
        {
            Id = GetIntValue(PacketPartNames.ProcessId);
        }

        if (TryReadMbcWorldCoords(parts, out var x, out var y, out var z, out var angle))
        {
            X = x;
            Y = y;
            Z = z;
            Angle = angle;
            return;
        }

        if (parts.Any(x => x.Name == EntityMoveParser.XPlus32768))
        {
            X = GetIntValue(EntityMoveParser.XPlus32768) - 32768;
            Y = 1200 - GetIntValue(EntityMoveParser.YPlus1200);
            Z = 32768 - GetIntValue(EntityMoveParser.ZPlus32768);
            Angle = GetIntValue(PacketPartNames.Angle);
            return;
        }

        X = GetClientCoordValue(PacketPartNames.CoordX);
        Y = GetClientCoordValue(PacketPartNames.CoordY);
        Z = GetClientCoordValue(PacketPartNames.CoordZ);
        Angle = GetIntValue(PacketPartNames.Angle);
    }

    public static bool HasMbcWorldCoords(List<PacketPart> parts) =>
        parts.Any(x => x.Name == "x") && parts.Any(x => x.Name == "y") && parts.Any(x => x.Name == "z");

    private static bool TryReadMbcWorldCoords(List<PacketPart> parts, out double x, out double y, out double z,
        out int angle)
    {
        x = y = z = 0;
        angle = 0;
        if (!HasMbcWorldCoords(parts))
        {
            return false;
        }

        if (!TryParseWorld(parts, "x", out x) || !TryParseWorld(parts, "y", out y) ||
            !TryParseWorld(parts, "z", out z))
        {
            return false;
        }

        angle = (int)(parts.FirstOrDefault(p => p.Name == PacketPartNames.Angle)?.ActualLongValue ?? 0);
        return true;
    }

    private static bool TryParseWorld(List<PacketPart> parts, string name, out double value)
    {
        value = 0;
        var part = parts.FirstOrDefault(p => p.Name == name);
        if (part is null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(part.ListValuePrimary) &&
            double.TryParse(part.ListValuePrimary, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        // Fallback: raw 12-bit code is not world space; prefer ListValuePrimary from MbcPacketParts.
        return false;
    }
}
