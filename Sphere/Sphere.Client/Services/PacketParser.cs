using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Packets;
using System.Reflection.Metadata.Ecma335;

namespace Sphere.Client.Services
{
    public class PacketParser : IPacketParser
    {
        private static readonly Dictionary<PacketType, Func<PacketBase, IPacket>> _packetTypeMap = new()
        {
            [PacketType.Login] = (packet) => new LoginPacket(packet),
            [PacketType.CreateCharacter] = (packet) => new CharacterCreatePacket(packet),
        };

        public PacketParser()
        {
        }

        public IPacket? Parse(PacketBase packet)
        {
            return packet.Size switch
            {
                0x08 => new ClientPingPacketShort(packet),
                0x0C => new ClientPingPacketLong(packet),
                0x26 => new IngamePingPacket(packet),
                0x15 => new CharacterSelectPacket(packet),
                0x13 => new IngameAcknowledgePacket(packet),

                //_ when packet.OriginalMessage[15] == 0x80 => new CharacterCreatePacket(packet),
                _ when packet.OriginalMessage[15] == 0x40 => new LoginPacket(packet),
                _ => null// throw new ArgumentException($"Unknown packet: {packet}")
            };

           
        }
    }
}
