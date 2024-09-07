using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Packets;

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

        public IPacket Parse(PacketBase packet)
        {
            if (packet.Size == 0x08)
            {
                return new ClientPingPacketShort(packet);
            }
            else if (packet.Size == 0x0C)
            {
                return new ClientPingPacketLong(packet);
            }

            if (!_packetTypeMap.TryGetValue(packet.PacketType, out var func))
                throw new ArgumentOutOfRangeException($"Packet type [{packet.PacketType}] can not be parsed");

            return func(packet);
        }
    }
}
