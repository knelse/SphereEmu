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
using Sphere.Common.Packets.Client;
using Sphere.Services.Services.Handlers;
using Sphere.Test.Common;
using static Godot.WebSocketPeer;

namespace Sphere.Test.Unit.Handlers
{
    public class LoginPacketHandlerTests
    {
        const int ClientId = 1;

        private readonly IPacketHandler<LoginPacket> _handler;
        private readonly Mock<ILogger<LoginPacketHandler>> _loggerMock = new Mock<ILogger<LoginPacketHandler>>();
        private readonly Mock<IClientAccessor> _tcpClientAccessorMock = new Mock<IClientAccessor>();
        private readonly Mock<IUnitOfWork> _unitOfWorkMock = new Mock<IUnitOfWork>();
        private readonly Mock<ITcpClient> _tcpClientMock = new Mock<ITcpClient>();
        private readonly Mock<IPlayersRepository> _playersRepositoryMock = new Mock<IPlayersRepository>();

        public LoginPacketHandlerTests()
        {
            _tcpClientAccessorMock.Setup(x => x.ClientId).Returns(ClientId);
            _tcpClientAccessorMock.Setup(x => x.Client).Returns(_tcpClientMock.Object);
            _tcpClientAccessorMock.Setup(x => x.ClientState).Returns(ClientState.INIT_WAITING_FOR_LOGIN_DATA);

            _unitOfWorkMock.Setup(x=> x.PlayersRepository).Returns(_playersRepositoryMock.Object);

            _handler = new LoginPacketHandler(_loggerMock.Object, _tcpClientAccessorMock.Object, _unitOfWorkMock.Object);
        }

        [Fact]
        public async Task Handle_ShouldThrowIfInvalidPacket()
        {
            // Arrange
            var packet = TestData.ShortPing.Packet;

            // Act
            var action = async () => await _handler.Handle(packet, default);

            // Assert
            await action.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Handle_ShouldEndTransmit_ifInvalidLoginOrPassword()
        {
            // Arrange
            var packetRaw = "1B0085BFD5012C0100B201000208404025FC87898D0584898D05000000"; // invalid charset byte
            var packet = new LoginPacket(new PacketBase(Convert.FromHexString(packetRaw), 1));

            // Act
            var action = async () => await _handler.Handle(packet, default);

            // Assert
            await action.Should().NotThrowAsync();

            _tcpClientMock.Verify(x => x.WriteAsync(CommonPackets.TransmissionEndPacket), Times.Once);
            _tcpClientMock.Verify(x => x.WriteAsync(It.IsAny<byte[]>()), Times.Once);
        }

        [Fact]
        public async Task Handle_ShouldSendAccountAlreadyInUse_ifIncorrectPassword()
        {
            // Arrange
            var player = new Sphere.Common.Entities.PlayerEntity()
            {
                Password = "thatsanotherpassword".GetHashedString()
            };
            _playersRepositoryMock.Setup(x => x.FindByLogin(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(player);

            var packet = TestData.Login.Packet;

            // Act
            var action = async () => await _handler.Handle(packet, default);

            // Assert
            await action.Should().NotThrowAsync();
            _tcpClientMock.Verify(x => x.WriteAsync(CommonPackets.AccountAlreadyInUse(ClientId)), Times.Once);
            _tcpClientMock.Verify(x => x.WriteAsync(It.IsAny<byte[]>()), Times.Once);
        }

        [Fact]
        public async Task Handle_ShouldSendCharacterSelectData()
        {
            // Arrange
            var player = new Sphere.Common.Entities.PlayerEntity()
            {
                Password = "abc".GetHashedString()
            };
            _tcpClientAccessorMock.Setup(x => x.ClientState).Returns(ClientState.INIT_WAITING_FOR_LOGIN_DATA);
            _playersRepositoryMock.Setup(x => x.FindByLogin(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(player);

            var packet = TestData.Login.Packet;

            // Act
            var action = async () => await _handler.Handle(packet, default);

            // Assert
            await action.Should().NotThrowAsync();
            _tcpClientMock.Verify(x => x.WriteAsync(CommonPackets.CharacterSelectStartData(ClientId)), Times.Once);
            _tcpClientMock.Verify(x => x.WriteAsync(player.ToInitialDataByteArray(ClientId)), Times.Once);
            _tcpClientMock.Verify(x => x.WriteAsync(It.IsAny<byte[]>()), Times.Exactly(2));
            _tcpClientAccessorMock.VerifySet(x => x.ClientState = ClientState.INIT_WAITING_FOR_CHARACTER_SELECT);
        }

        [Theory]
        [InlineData(ClientState.I_AM_BREAD)]
        [InlineData(ClientState.INIT_READY_FOR_INITIAL_DATA)]
        [InlineData(ClientState.INGAME_DEFAULT)]
        [InlineData(ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY)]
        [InlineData(ClientState.INIT_NEW_DUNGEON_TELEPORT_INITIATED)]
        [InlineData(ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT)]
        [InlineData(ClientState.INIT_WAITING_FOR_CHARACTER_SELECT)]
        [InlineData(ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK)]
        public async Task Handle_ShouldEndTransmit_ifClientInAWrongState(ClientState state)
        {
            // Arrange
            var player = new Sphere.Common.Entities.PlayerEntity()
            {
                Password = "abc".GetHashedString()
            };
            _tcpClientAccessorMock.Setup(x => x.ClientState).Returns(state);

            var packet = TestData.Login.Packet;

            // Act
            var action = async () => await _handler.Handle(packet, default);

            // Assert
            await action.Should().NotThrowAsync();
            _tcpClientMock.Verify(x => x.WriteAsync(CommonPackets.TransmissionEndPacket), Times.Once);
            _tcpClientMock.Verify(x => x.WriteAsync(It.IsAny<byte[]>()), Times.Once);
        }
    }
}
