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
}
