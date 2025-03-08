using Sphere.Common.Enums;
using Sphere.Common.Models;

namespace Sphere.Test.Unit.ServerPacketTests
{
    public class SpawnMobPacketTests
    {
        [Fact]
        public void Serialization()
        {
            // Arrange
            var packet = new SpawnMobModel
            {
                EntityId = 4269,
                CurrentHP = 100,
                MaxHP = 100,
                Type = MonsterType.Assassin,
                Level = 1,
                Coordinates = new Coordinates
                {
                    Angle = 25,
                    X = 425.9232f,
                    Y = 153.42842f,
                    Z = -1301.6813f
                }
            };

            // Act
            var serverPacket = packet.ToServerPacket();
            var bytes = serverPacket.GetBytes();

            // Assert
            Console.WriteLine(BitConverter.ToString(bytes));
        }
    }
}
