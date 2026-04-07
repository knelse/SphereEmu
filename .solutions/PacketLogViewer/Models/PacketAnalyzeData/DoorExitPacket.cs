using System;
using System.Collections.Generic;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class DoorExitPacket : WorldObject
{
    public double ExitX { get; set; }
    public double ExitY { get; set; }
    public double ExitZ { get; set; }
    public double ExitAngle { get; set; }

    public override string DisplayValue =>
        $"{Id:X4} ({Enum.GetName(ObjectType) ?? string.Empty}) at [{X:F2}, {Y:F2}, {Z:F2}] "
        + $"to [{ExitX:F2}, {ExitY:F2}, {ExitZ:F2}]";

    public DoorExitPacket(List<PacketPart> parts) : base(parts)
    {
        if (ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
        {
            return;
        }

        ExitX = GetClientCoordValue(PacketPartNames.ExitX);
        ExitY = GetClientCoordValue(PacketPartNames.ExitY);
        ExitZ = GetClientCoordValue(PacketPartNames.ExitZ);
        ExitAngle = GetIntValue(PacketPartNames.ExitAngle);
    }
}