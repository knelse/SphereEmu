using Sphere.Common.Models;
using System.Numerics;

namespace Sphere.Common.Interfaces.Packets
{
    /// <summary>
    /// Interface to get data behind server packet.
    /// </summary>
    public interface IServerPacketStream
    {
        byte[] GetBytes();

        Stream GetStream();
    }

    /// <summary>
    /// Server packet builder interface. 
    /// </summary>
    public interface IServerPacket
    {
        IServerPacket AddValue<T>(string part, T value) where T : IBinaryInteger<T>;

        IServerPacket AddValue(Coordinates coordinates);

        IServerPacketStream Build();
    }
}
