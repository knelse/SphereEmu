using System;
using System.Collections.Generic;
using System.Linq;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class DoorEntranceWithKey : WorldObject
{
    public int SubtypeID { get; set; }

    public override string DisplayValue =>
        $"{Id:X4} ({Enum.GetName(ObjectType) ?? string.Empty}) #{SubtypeID} at [{X:F2}, {Y:F2}, {Z:F2}]";

    public DoorEntranceWithKey(List<PacketPart> parts) : base(parts)
    {
        if (ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
        {
            return;
        }
        SubtypeID = GetIntValue(PacketPartNames.SubtypeId);
    }
}