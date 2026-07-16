namespace SphServer.Shared.BitStream;

public static class SphBitStream
{
    public static SphWriteStream GetWriteBitStream ()
    {
        return new SphWriteStream();
    }

    public static ushort ByteSwap (ushort u)
    {
        return (ushort) (((u & 0b11111111) << 8) + ((u & 0b1111111100000000) >> 8));
    }
}