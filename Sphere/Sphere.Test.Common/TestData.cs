using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Packets.Client;

namespace Sphere.Test.Common
{
    public static class TestData
    {
        public static class Login
        {
            public const string PacketRaw = "1B0040BCE0012C01005A11000408404025FC87898D0184898D01000800C7BDE201F401";

            public static byte[] PacketBytes => Convert.FromHexString(PacketRaw);

            public static PacketBase BasePacket => new PacketBase(PacketBytes, 1);

            public static LoginPacket Packet => new LoginPacket(BasePacket);
        }

        public static class ShortPing
        {
            public const string PacketRaw = "0800CBBDDE01F401";

            public static byte[] PacketBytes => Convert.FromHexString(PacketRaw);

            public static PacketBase BasePacket => new PacketBase(PacketBytes, 1);

            public static ClientPingPacketShort Packet => new ClientPingPacketShort(BasePacket);
        }

        public static class LongPing
        {
            public const string PacketRaw = "0C00AEBDE701BC020B000000";

            public static byte[] PacketBytes => Convert.FromHexString(PacketRaw);

            public static PacketBase BasePacket => new PacketBase(PacketBytes, 1);

            public static ClientPingPacketLong Packet => new ClientPingPacketLong(BasePacket);
        }
    }
}
