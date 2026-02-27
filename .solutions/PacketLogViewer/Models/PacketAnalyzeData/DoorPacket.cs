using System;
using System.Collections.Generic;
using System.Linq;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class DoorPacket : WorldObject
{
    public int SubtypeID { get; set; }
    public string OverrideType { get; set; }
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double TargetZ { get; set; }

    public bool HasTeleportTarget { get; set; }

    public string TeleportTarget => HasTeleportTarget ? $" to [{TargetX:F2}, {TargetY:F2}, {TargetZ:F2}]" : "";
    public string SubtypeStr => HasTeleportTarget ? "" : $" #{SubtypeID}";

    public override string DisplayValue =>
        $"{Id:X4} ({Enum.GetName(ObjectType) ?? string.Empty}){OverrideType}{SubtypeStr} at [{X:F2}, {Y:F2}, {Z:F2}]{TeleportTarget}";

    public DoorPacket (List<PacketPart> parts) : base(parts)
    {
        if (ActionType is not (EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN))
        {
            return;
        }

        SubtypeID = GetIntValue(PacketPartNames.SubtypeId);

        if (SubtypeID != 0x7FFF)
        {
            var keyLocales = SphObjectDb.LocalisationContent["st_key"][Locale.Russian];
            var subtypeStr = $"{SubtypeID}";
            var text = keyLocales.FirstOrDefault(x => x.StartsWith(subtypeStr));
            if (!string.IsNullOrEmpty(text))
            {
                OverrideType = " " + text[(subtypeStr.Length + 1)..];
            }
        }
        else
        {
            HasTeleportTarget = true;
            TargetX = GetClientCoordValue(PacketPartNames.TargetX);
            TargetY = GetClientCoordValue(PacketPartNames.TargetY);
            TargetZ = GetClientCoordValue(PacketPartNames.TargetZ);
        }
    }
}