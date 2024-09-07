using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Packets;
using Sphere.Test.Common;

namespace Sphere.Test.Unit
{
    public class BasePacketTests
    {
        [Fact]
        public void Login_packetShouldBeDecodedIntoBasicPacket()
        {
            // Arrange
            var hexString = "210021BED5012C01009A012FC70840403DFCCF8CC988C58401CC8CC988C5840100";
            var bytes = Convert.FromHexString(hexString);

            // Act
            var packet = new LoginPacket(new PacketBase(bytes, 1));

            // Assert
            packet.ValidateLoginPacket(bytes.Length, Convert.FromHexString("CF8CC988C58401CC8CC988C5840100"));
        }
    }
}