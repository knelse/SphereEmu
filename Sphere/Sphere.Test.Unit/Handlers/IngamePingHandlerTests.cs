using Microsoft.Extensions.Logging;
using Moq;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets;
using Sphere.Common.Packets.Client;
using Sphere.Services.Services.Handlers;

namespace Sphere.Test.Unit.Handlers
{
    public class IngamePingHandlerTests
    {
        private readonly Mock<ILogger<IngamePingPacketHandler>> _loggerMock;
        private readonly Mock<IClientAccessor> _clientAccessorMock;
        private readonly Mock<ITcpClient> _tcpClientMock;

        private readonly IPacketHandler<IngamePingPacket> _packetHandler;

        public IngamePingHandlerTests()
        {
            _loggerMock = new Mock<ILogger<IngamePingPacketHandler>>();
            _clientAccessorMock = new Mock<IClientAccessor>();
            _tcpClientMock = new Mock<ITcpClient>();

            _clientAccessorMock.Setup(x => x.Client).Returns(_tcpClientMock.Object);
            _clientAccessorMock.Setup(x => x.Character).Returns(new Sphere.Common.Entities.CharacterEntity());

            _packetHandler = new IngamePingPacketHandler(_loggerMock.Object, _clientAccessorMock.Object);
        }

        [Theory]
        [InlineData("260092BD08032C0100DD029F4AADDEB10040000160D0DFC3ED1097F9C7D066582A3153950910", "12002C0100DD029F4AADDE00004000016000")]
        public async Task Test(string hexInput, string expectedHexOutput)
        {
            // Arrange
            var inputBytes = Convert.FromHexString(hexInput);
            var expectedBytes = Convert.FromHexString(expectedHexOutput);

            var basePacket = new PacketBase(inputBytes, 1);
            var ingamePingPacket = new IngamePingPacket(basePacket);

            // Act
            await _packetHandler.Handle(ingamePingPacket, default);

            // Arrange
            _tcpClientMock.Verify(x => x.WriteAsync(expectedBytes), Times.Once);
            _tcpClientMock.Verify(x => x.WriteAsync(CommonPackets.TransmissionEndPacket), Times.Once);
        }
    }
}
