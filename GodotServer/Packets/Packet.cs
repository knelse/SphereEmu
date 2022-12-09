using static SphServer.Helpers.BitHelper;

namespace SphServer.Packets
{

    public partial class Packet
    {
        protected static readonly ushort PacketValidationCodeOK = 0x2C01;

        private static readonly byte[] EmptyPacketByteArray = { 0x04, 0x00, 0xF4, 0x01 };

        public static byte[] ToByteArray(byte[]? content = null, int padZeros = 2)
        {
            if (content is null)
            {
                return EmptyPacketByteArray;
            }

            var packetSize = (ushort)(content.Length + 4 + padZeros);

            var result = new byte [content.Length + 4 + padZeros];

            result[0] = MinorByte(packetSize);
            result[1] = MajorByte(packetSize);
            result[2] = MajorByte(PacketValidationCodeOK);
            result[3] = MinorByte(PacketValidationCodeOK);

            for (var i = 0; i < padZeros; i++)
            {
                result[4 + i] = 0x00;
            }

            content.CopyTo(result, 4 + padZeros);

            return result;
        }
    }
}