using emu.Helpers;

namespace emu;

public class Packet
{
    protected ushort PacketSize;
    protected static ushort PacketValidationCodeOK = 0x2c01;
    protected ushort PacketValidationCode;
    
    public static Packet EmptyPacket = new()
    {
        PacketSize = 0x04,
        PacketValidationCode = 0xf401
    };

    private static readonly byte[] EmptyPacketByteArray = { 0x04, 0x00, 0xf4, 0x01 };

    public static byte[] ToByteArray(byte[]? content = null)
    {
        if (content is null)
        {
            return EmptyPacketByteArray;
        }

        var packetSize = (ushort) (content.Length + 6);

        var result = new byte [content.Length + 6];

        result[0] = BitHelper.GetFirstByte(packetSize);
        result[1] = BitHelper.GetSecondByte(packetSize);
        result[2] = BitHelper.GetSecondByte(PacketValidationCodeOK);
        result[3] = BitHelper.GetFirstByte(PacketValidationCodeOK);
        result[4] = 0x00;
        result[5] = 0x00;
        content.CopyTo(result, 6);

        return result;
    }
}