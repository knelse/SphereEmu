using FluentAssertions;
using Sphere.Common.Enums;
using Sphere.Common.Packets.Client;

namespace Sphere.Test.Common
{
    public static class TestExtensions
    {
        public static void ValidateLoginPacket(this LoginPacket packet, int size, byte[] payload)
        {
            packet.Size.Should().Be(size);
            packet.SyncBytes1.Should().BeEquivalentTo([0x21, 0xBE, 0xD5, 0x01]);
            packet.ConfirmationBytes.Should().BeEquivalentTo([0x2C, 0x01, 0x00]);
            packet.SyncBytes2.Should().BeEquivalentTo([0x9A, 0x01, 0x2F, 0xC7]);
            packet.BeginPayload.Should().BeEquivalentTo([0x08, 0x40]);
            packet.PacketType.Should().Be((PacketType)0x40);
            packet.Payload.Should().BeEquivalentTo(payload);
        }
    }
}