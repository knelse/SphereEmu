using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sphere.Common.Enums;
using Sphere.Common.Helpers.Extensions;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Repository;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets;
using Sphere.Services.Services.Handlers;
using Sphere.Test.Common;
using static Godot.WebSocketPeer;

namespace Sphere.Test.Unit.Handlers
{
    public class ClientPingShortPacketHandlerTests
    {
        const int ClientId = 1;

        private readonly IPacketHandler<ClientPingPacketShort> _handler;
        private readonly Mock<ILogger<ClientPingPacketShortHandler>> _loggerMock = new Mock<ILogger<ClientPingPacketShortHandler>>();
        private readonly Mock<IClientAccessor> _tcpClientAccessorMock = new Mock<IClientAccessor>();
        private readonly Mock<ITcpClient> _tcpClientMock = new Mock<ITcpClient>();

        public ClientPingShortPacketHandlerTests()
        {
            _tcpClientAccessorMock.Setup(x => x.ClientId).Returns(ClientId);
            _tcpClientAccessorMock.Setup(x => x.Client).Returns(_tcpClientMock.Object);
            _tcpClientAccessorMock.Setup(x => x.ClientState).Returns(ClientState.INIT_WAITING_FOR_LOGIN_DATA);

            _handler = new ClientPingPacketShortHandler(_loggerMock.Object, _tcpClientAccessorMock.Object);
        }

        [Fact]
        public async Task Handle_ShouldSendSixSecondsPingPacket()
        {
            // Arrange
            var packet = TestData.ShortPing.Packet;

            // Act
            await _handler.Handle(packet, default);

            // Assert
            _tcpClientMock.Verify(x => x.WriteAsync(CommonPackets.SixSecondPing(ClientId)), Times.Once);
            _tcpClientMock.Verify(x => x.WriteAsync(It.IsAny<byte[]>()), Times.Once);
        }

        
    }
}
