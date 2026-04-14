using System.Collections.Generic;
using BitStreams;

namespace SpherePacketVisualEditor;

public class Subpacket
{
    public string Name { get; set; }
    public string FilePath { get; set; }

    public List<PacketPart> LoadFromFile (BitStream stream, int bitOffset, bool isMob = false, bool isItem = false)
    {
        return PacketPart.LoadFromFile(FilePath, Name, stream, bitOffset, isMob, isItem);
    }
}