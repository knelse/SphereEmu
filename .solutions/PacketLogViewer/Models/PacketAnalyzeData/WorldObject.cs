using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class WorldObject : PacketAnalyzeData
{
    [BsonId] public int DbId { get; set; }
    public EntityActionType ActionType { get; set; } = EntityActionType.UNDEF;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int Angle { get; set; }

    public override string DisplayValue =>
        $"{Id:X4} ({Enum.GetName(ObjectType) ?? string.Empty}) at [{X:F2}, {Y:F2}, {Z:F2}]";

    public WorldObject (List<PacketPart> parts) : base(parts)
    {
        var actionTypePart = Parts.FirstOrDefault(x => x.Name == PacketPartNames.ActionType);
        if (actionTypePart is not null)
        {
            var actionTypeVal = (int) (actionTypePart.ActualLongValue ?? int.MaxValue);
            ActionType = Enum.IsDefined(typeof (EntityActionType), actionTypeVal)
                ? (EntityActionType) actionTypeVal
                : EntityActionType.UNDEF;
        }

        if (ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
        {
            return;
        }

        X = GetClientCoordValue(PacketPartNames.CoordX);
        Y = GetClientCoordValue(PacketPartNames.CoordY);
        Z = GetClientCoordValue(PacketPartNames.CoordZ);
        Angle = GetIntValue(PacketPartNames.Angle);
    }
}