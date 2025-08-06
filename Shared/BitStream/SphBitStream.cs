using System.IO;

namespace SphServer.Shared.BitStream;

public static class SphBitStream
{
    public static BitStreams.BitStream GetWriteBitStream ()
    {
        return new BitStreams.BitStream(new MemoryStream())
        {
            AutoIncreaseStream = true
        };
    }

    public static ushort ByteSwap (ushort u)
    {
        return (ushort) (((u & 0b11111111) << 8) + ((u & 0b1111111100000000) >> 8));
    }
}