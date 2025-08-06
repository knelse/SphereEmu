namespace SphServer.Shared.Networking.Serializers;

public class SphereDbEntrySerializerBase
{
    public static byte MinorByte (ushort input)
    {
        return (byte) (input & 0xFF);
    }

    public static byte MajorByte (ushort input)
    {
        return (byte) (input >> 8);
    }
}