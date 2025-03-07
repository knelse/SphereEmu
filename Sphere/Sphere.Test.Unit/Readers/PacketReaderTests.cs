using FluentAssertions;
using Moq;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets.Client;
using Sphere.Services.Readers;
using Sphere.Test.Common;

namespace Sphere.Test.Unit.Readers
{
    public class PacketReaderTests
    {
        private readonly Mock<IClientAccessor> _tcpClientAccessor = new Mock<IClientAccessor>();
        private readonly Mock<ITcpClient> _tcpClientMock = new Mock<ITcpClient>();
        private readonly Stream _stream = new MemoryStream();
        public PacketReaderTests()
        {
            _tcpClientMock.Setup(x => x.ReadAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>())).Returns((byte[] buff, int offset, int count) => 
                _stream.ReadAsync(buff, offset, count));
        }

        [Fact]
        public async Task Reader_shouldReadOnePacketAtATime()
        {
            // Arrange
            // 2 packets at once
            const string data = "210021BED5012C01009A012FC70840403DFCCF8CC988C58401CC8CC988C58401002A00D442E2012C010026024F6F0840A06108000000C4000000000000000000000000000000000000C000";
            
            _stream.Write(Convert.FromHexString(data));
            _stream.Seek(0, SeekOrigin.Begin);

            _tcpClientMock.Setup(x => x.GetStream()).Returns(_stream);
            _tcpClientAccessor.Setup(x => x.Client).Returns(_tcpClientMock.Object);
            _tcpClientAccessor.Setup(x => x.ClientId).Returns(1);

            var reader = new SpherePacketReader(_tcpClientAccessor.Object);

            // Act
            await reader.MoveNextAsync();
            var currentPacket = new LoginPacket(reader.Current);

            // Arrange
            _stream.Position.Should().Be(currentPacket!.Size);
            currentPacket.ValidateLoginPacket(33, Convert.FromHexString("CF8CC988C58401CC8CC988C5840100"));
        }

        [Fact]
        public async Task Reader_shouldReturnFalseIfStreamIsEmpty()
        {
            // Arrange
            var stream = new MemoryStream();

            var tcpClientAccessor = new Mock<IClientAccessor>();
            var tcpClient = new Mock<ITcpClient>();
            tcpClient.Setup(x => x.GetStream()).Returns(stream);
            tcpClientAccessor.Setup(x => x.Client).Returns(tcpClient.Object);
            tcpClientAccessor.Setup(x => x.ClientId).Returns(1);

            var reader = new SpherePacketReader(tcpClientAccessor.Object);

            // Act
            var result = await reader.MoveNextAsync();

            // Arrange
            result.Should().BeFalse();
        }

        [Fact]
        public async Task Reader_shouldNotThrowOnInvalidPacket()
        {
            // Arrange
            const string data = "21";
            var stream = new MemoryStream();
            stream.Write(Convert.FromHexString(data));
            stream.Seek(0, SeekOrigin.Begin);

            var tcpClientAccessor = new Mock<IClientAccessor>();
            var tcpClient = new Mock<ITcpClient>();
            tcpClientAccessor.Setup(x => x.Client).Returns(tcpClient.Object);
            tcpClientAccessor.Setup(x => x.ClientId).Returns(1);

            var reader = new SpherePacketReader(tcpClientAccessor.Object);

            // Act
            var result = await reader.MoveNextAsync();

            // Arrange
            result.Should().BeFalse();
        }
    }
}
