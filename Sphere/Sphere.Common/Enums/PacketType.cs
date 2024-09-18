namespace Sphere.Common.Enums
{
    public enum PacketType : byte
    {
        Unknown = 0x00,
        ClientPingShort = 0x01,
        ClientPingLong = 0x02,
        Login = 0x40,
        CreateCharacter = 0x80,
        IngamePingPacket = 0x26,
        IngameAcknowledge = 0x13,
    }
}
