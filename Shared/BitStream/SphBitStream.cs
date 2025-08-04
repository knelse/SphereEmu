using System.IO;

namespace SphServer.Shared.BitStream;

public class SphBitStream
{
    public static BitStreams.BitStream GetWriteBitStream ()
    {
        return new BitStreams.BitStream(new MemoryStream())
        {
            AutoIncreaseStream = true
        };
    }
}