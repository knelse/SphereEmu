using FluentAssertions;
using Sphere.Common.Enums;
using Sphere.Services.Services.Readers;
using Xunit.Extensions.AssemblyFixture;

namespace Sphere.Test.Unit.Readers
{
    public class SphereStringReaderTests : IAssemblyFixture<EncodingFixture>
    {
        public SphereStringReaderTests()
        {
        }

        [Fact]
        public void SphereStringReader_shouldThrowFormatException()
        {
            // Arrange
            var invalidHexString = "FFFFFFFFFFFFFFFFFFFFFF";
            var bytes = Convert.FromHexString(invalidHexString);

            // Act
            var action = () => SphereStringReader.Read(bytes, SphereStringType.Login);

            // Assert
            action.Should().Throw<FormatException>();
        }

        [Fact]
        public void SphereStringReader_shouldThrowArgumentNullException_ifBytesNull()
        {
            // Arrange
            var bytes = (byte[])null;

            // Act
            var action = () => SphereStringReader.Read(bytes, SphereStringType.Login);

            // Assert
            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void SphereStringReader_shouldThrowArgumentNullException_ifBytesEmpty()
        {
            // Arrange
            var bytes = (byte[])[];

            // Act
            var action = () => SphereStringReader.Read(bytes, SphereStringType.Login);

            // Assert
            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void SphereStringReader_shouldThrowFormatExcptionException_ifNotWin1251()
        {
            // Arrange
            var bytes = Convert.FromHexString("FF898DC5C8CC8400");

            // Act
            var action = () => SphereStringReader.Read(bytes, SphereStringType.Login);

            // Assert
            action.Should().Throw<FormatException>();
        }

        [Theory]
        [InlineData("87898DC5C8CC8400", "abc123a")]
        [InlineData("87898DC5C8CC8403", "бвг123б")]
        [InlineData("C7C8CC80878B03", "123абв")]
        public void SphereStringReader_shouldReturnCorrectString(string hexInput, string expectedResult)
        {
            // Arrange
            var bytes = Convert.FromHexString(hexInput);

            // Act
            var result = SphereStringReader.Read(bytes, SphereStringType.Login);

            // Assert
            result.Should().Be(expectedResult);
        }
    }
}
