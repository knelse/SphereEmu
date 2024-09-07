using Microsoft.Extensions.Logging;
using Moq;
using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets;
using Sphere.Services.Services.Handlers;
using Sphere.Test.Common;

namespace Sphere.Test.Unit.Handlers
{
    public class ClientPingLongPacketHandlerTests
    {
        const int ClientId = 1;

        private readonly IPacketHandler<ClientPingPacketLong> _handler;
        private readonly Mock<ILogger<ClientPingPacketLongHandler>> _loggerMock = new Mock<ILogger<ClientPingPacketLongHandler>>();
        private readonly Mock<ITcpClientAccessor> _tcpClientAccessorMock = new Mock<ITcpClientAccessor>();
        private readonly Mock<ITcpClient> _tcpClientMock = new Mock<ITcpClient>();

        public ClientPingLongPacketHandlerTests()
        {
            _tcpClientAccessorMock.Setup(x => x.ClientId).Returns(ClientId);
            _tcpClientAccessorMock.Setup(x => x.Client).Returns(_tcpClientMock.Object);
            _tcpClientAccessorMock.Setup(x => x.ClientState).Returns(ClientState.INIT_WAITING_FOR_LOGIN_DATA);

            _handler = new ClientPingPacketLongHandler(_loggerMock.Object, _tcpClientAccessorMock.Object);
        }

        [Fact]
        public async Task Handle_ShouldSendFifteenSecondPingPacket()
        {
            // Arrange
            var packet = TestData.LongPing.Packet;

            // Act
            await _handler.Handle(packet, default);

            // Assert
            _tcpClientMock.Verify(x => x.WriteAsync(CommonPackets.FifteenSecondPing(ClientId)), Times.Once);
            _tcpClientMock.Verify(x => x.WriteAsync(It.IsAny<byte[]>()), Times.Once);
        }

        
    }
}
