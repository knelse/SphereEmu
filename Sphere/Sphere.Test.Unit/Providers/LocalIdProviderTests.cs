using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sphere.Services.Providers;

namespace Sphere.Test.Unit.Providers
{
    public class LocalIdProviderTests
    {
        private readonly Mock<ILogger<LocalIdProvider>> _logger = new Mock<ILogger<LocalIdProvider>>();

        [Fact]
        public void LocalIdProvider_shouldAcquireProvidedMaxNumberIds()
        {
            // Arrange
            const ushort maxNumber = 10;
            var localIdProvider = new LocalIdProvider(_logger.Object, maxNumber);

            // Act
            var count = 0;
            for(var i = 0; i < maxNumber; i++)
            {
                localIdProvider.GetIdentifier();
                count++;
            }

            // Assert
            count.Should().Be(maxNumber);    
        }

        [Fact]
        public void LocalIdProvider_shouldThrowOverflowExceptionWhenNoFreeIds()
        {
            // Arrange
            const ushort maxNumber = 10;
            var localIdProvider = new LocalIdProvider(_logger.Object, maxNumber);

            // Act
            for (var i = 0; i < maxNumber; i++)
            {
                var _ = localIdProvider.GetIdentifier();
            }

            var throwAction = () => localIdProvider.GetIdentifier();

            // Assert
            throwAction.Should().Throw<OverflowException>();
        }

        [Fact]
        public void LocalIdProvider_shouldStartWith2()
        {
            // Arrange

            var localIdProvider = new LocalIdProvider(_logger.Object, 10);

            // Act
            var id = localIdProvider.GetIdentifier();

            // Assert
            id.Should().Be(2);
        }

        [Fact]
        public void LocalIdProvider_returnIdShouldBeAddedToPool()
        {
            // Arrange

            var localIdProvider = new LocalIdProvider(_logger.Object, 1);

            // Act
            var id = localIdProvider.GetIdentifier();
            localIdProvider.ReturnId(id);
            var id2 = localIdProvider.GetIdentifier();

            // Assert
            id.Should().Be(id2).And.Be(2);
        }
    }
}
