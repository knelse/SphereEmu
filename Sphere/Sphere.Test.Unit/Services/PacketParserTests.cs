using FluentAssertions;
using Sphere.Client.Services;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Packets;

namespace Sphere.Test.Unit.Services
{
    public class PacketParserTests
    {
        [Theory]
        [InlineData("0800CBBDDE01F401", typeof(ClientPingPacketShort))]
        [InlineData("0C00BDBDDD01BC0206000000", typeof(ClientPingPacketLong))]
        [InlineData("1B0040BCE0012C01005A11000408404025FC87898D0184898D01000800C7BDE201F401", typeof(LoginPacket))]
        public void PacketParser_ShouldParsePacketsCorrectly(string packet, Type expectedType)
        {
            // Arrange
            var parser = new PacketParser();
            var packetBase = new PacketBase(Convert.FromHexString(packet), 1);

            // Act
            var parsed = parser.Parse(packetBase);

            // Assert
            parsed.Should().BeOfType(expectedType);
        }
    }
}
