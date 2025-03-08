using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Models;

namespace Sphere.Common.Interfaces.GameObjects
{
    /// <summary>
    /// Basic interface for any spawnable object in a game.
    /// </summary>
    public interface ISpawnable : IGameObject
    {
        Coordinates Coordinates { get; }

        IServerPacketStream ToServerPacket();
    }
}
