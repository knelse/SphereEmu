using System.Collections.Generic;
using System.Linq;
using BitStreams;
using SpherePacketVisualEditor;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class CharListEntryPacket : PacketAnalyzeData
{
    public string Name { get; set; } = string.Empty;
    public int LookType { get; set; }
    public int Title { get; set; }
    public int Degree { get; set; }

    public override string DisplayValue
    {
        get
        {
            var slot = string.IsNullOrEmpty(Name) ? "(empty)" : Name;
            var deleted = LookType == 0x19 ? " queued-delete" : string.Empty;
            return $"{Id:X4} (CharList) {slot} T{Title}/D{Degree}{deleted}";
        }
    }

    public CharListEntryPacket(List<PacketPart> parts) : base(parts)
    {
        LookType = GetIntValue(PacketPartNames.LookType);
        Title = GetIntValue(PacketPartNames.Title);
        Degree = GetIntValue(PacketPartNames.Degree);
        Name = DecodePackedName(parts);
    }

    private static string DecodePackedName(List<PacketPart> parts)
    {
        var namePart = parts.FirstOrDefault(x => x.Name == PacketPartNames.CharListName);
        if (namePart?.Value is not { Length: > 0 })
        {
            return string.Empty;
        }

        var packed = BitStream.BitArrayToBytes(namePart.Value.Reverse().ToArray());
        var unpacked = new byte[packed.Length];
        for (var i = 0; i < packed.Length; i++)
        {
            var nextLow = i + 1 < packed.Length ? packed[i + 1] : (byte)0;
            unpacked[i] = (byte)((packed[i] >> 2) | ((nextLow & 0b11) << 6));
        }

        return PacketLogViewerMainWindow.Win1251.GetString(unpacked).TrimEnd('\0');
    }
}
