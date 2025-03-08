using System.Collections.ObjectModel;

namespace Sphere.Common.Packets
{
    /// <summary>
    /// Statically holds an original library of Packet definitions.
    /// </summary>
    public static class PacketsLibrary
    {
        private static ReadOnlyDictionary<string, PacketDefinition> Library { get; set; }

        public static void Create(ReadOnlyDictionary<string, PacketDefinition> library)
        {
            Library = library;
        }

        /// <summary>
        /// Create a clone of original packet definition.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static PacketDefinition GetPacketDefinition(string name)
        {
            if (!Library.TryGetValue(name, out var packetDefinition))
            {
                throw new ArgumentException($"Unknown packet {name}");
            }

            return (PacketDefinition)packetDefinition.Clone();
        }
    }
}
