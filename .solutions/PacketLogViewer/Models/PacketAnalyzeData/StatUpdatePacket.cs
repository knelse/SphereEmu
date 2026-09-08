using System.Collections.Generic;
using System.Linq;
using SpherePacketVisualEditor;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class StatUpdatePacket : PacketAnalyzeData
{
    public List<(string Name, long Value, int Marker)> Fields { get; } = new();

    public override string DisplayValue
    {
        get
        {
            var fields = Fields.Count == 0
                ? "(no fields)"
                : string.Join(", ", Fields.Select(f => $"{f.Name}={f.Value}"));
            return $"{Id:X4} (Stats) {fields}";
        }
    }

    public StatUpdatePacket(List<PacketPart> parts) : base(parts)
    {
        if (Id == 0)
        {
            Id = GetIntValue(PacketPartNames.ClientIndex);
        }

        foreach (var part in parts)
        {
            if (part.Name is PacketPartNames.ID or PacketPartNames.ClientIndex or PacketPartNames.Opcode
                or PacketPartNames.StatUpdate)
            {
                continue;
            }

            if (part.Name.EndsWith("_divider") || part.Name.EndsWith("_marker") || part.Name.EndsWith("_neg")
                || part.Name.EndsWith("_len") || part.Name.EndsWith("_tag"))
            {
                continue;
            }

            var markerPart = parts.FirstOrDefault(x => x.Name == part.Name + "_marker");
            var marker = (int)(markerPart?.ActualLongValue ?? 0);
            Fields.Add((part.Name, part.ActualLongValue ?? 0, marker));
        }
    }
}
