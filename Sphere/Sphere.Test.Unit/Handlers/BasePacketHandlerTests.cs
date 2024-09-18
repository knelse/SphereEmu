using Microsoft.Extensions.DependencyInjection;
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
    public class BasePacketHandlerTests
    {
        const int ClientId = 1;

        private readonly IPacketHandler _handler;
        private readonly Mock<IServiceProvider> _serviceProviderMock = new Mock<IServiceProvider>();
        private readonly Mock<ILogger<PacketHandlerBase>> _loggerMock = new Mock<ILogger<PacketHandlerBase>>();
        private readonly Mock<IPacketParser> _packetParserMock = new Mock<IPacketParser>();

        private readonly Mock<IPacketHandler<ClientPingPacketLong>> _longPingPacketHandlerMock = new Mock<IPacketHandler<ClientPingPacketLong>>();
        private readonly Mock<IPacketHandler<ClientPingPacketShort>> _shortPingPacketHandlerMock = new Mock<IPacketHandler<ClientPingPacketShort>>();
        private readonly Mock<IPacketHandler<LoginPacket>> _logiPacketHandlerMock = new Mock<IPacketHandler<LoginPacket>>();

        public BasePacketHandlerTests()
        {
            _serviceProviderMock.Setup(x => x.GetService(typeof(IPacketHandler<ClientPingPacketLong>))).Returns(_longPingPacketHandlerMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IPacketHandler<ClientPingPacketShort>))).Returns(_shortPingPacketHandlerMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IPacketHandler<LoginPacket>))).Returns(_logiPacketHandlerMock.Object);

            _handler = new PacketHandlerBase(_loggerMock.Object, _serviceProviderMock.Object, _packetParserMock.Object);
        }

        [Fact]
        public async Task Handle_LongPingShouldCallCorrectHandler()
        {
            // Arrange
            var packet = TestData.LongPing.Packet;

            // Act
            await _handler.Handle(packet, default);

            // Assert
            _longPingPacketHandlerMock.Verify(x=>x.Handle(packet, default), Times.Once());
        }

        [Fact]
        public async Task Handle_ShortPingShouldCallCorrectHandler()
        {
            // Arrange
            var packet = TestData.ShortPing.Packet;

            // Act
            await _handler.Handle(packet, default);

            // Assert
            _shortPingPacketHandlerMock.Verify(x => x.Handle(packet, default), Times.Once());
        }

        [Fact]
        public async Task Handle_LoginPackerShouldCallCorrectHandler()
        {
            // Arrange
            var packet = TestData.Login.Packet;

            // Act
            await _handler.Handle(packet, default);

            // Assert
            _logiPacketHandlerMock.Verify(x => x.Handle(packet, default), Times.Once());
        }

    }
}
