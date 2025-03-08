using BitStreams;
using Sphere.Common.Extensions;
using Sphere.Common.Types;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sphere.Common.Packets
{
    /// <summary>
    /// Describes a sphere packet definition scrapped from the client.
    /// </summary>
    public class PacketDefinition : ICloneable
    {
        /// <summary>
        /// The name of the packet.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// An instance of packet parts initialize during startup.
        /// </summary>
        private Dictionary<string, vByte> PacketParts { get; init; }

        public PacketDefinition(string name, Dictionary<string, vByte> packetParts)
        {
            Name = name;
            PacketParts = packetParts;
        }

        /// <summary>
        /// Replaces packet part's value with provided integer value (short/int/long and unsigned versions)
        /// </summary>
        /// <typeparam name="T">Represents an integer type that implements <see cref="IBinaryInteger"/>IBinaryInteger</typeparam> interface
        /// <param name="name">Part name</param>
        /// <param name="value">Value that packet part will be changed to</param>
        /// <returns></returns>
        public PacketDefinition ReplacePart<T>(string name, T value) where T : IBinaryInteger<T>
        {
            var toReplace = GetPacketPart(name);

            /// magic here. Copied from .net implementation of <see cref="BitConverter">BitConverter</see>
            var byteCount = value.GetByteCount();
            byte[] bytes = new byte[byteCount];
            Unsafe.As<byte, T>(ref bytes[0]) = value;

            PacketParts[name] = new vByte(bytes, toReplace.Length);

            return this;
        }

        /// <summary>
        /// Replaces packet part with provided raw bytes array
        /// </summary>
        /// <param name="name">Packet part name</param>
        /// <param name="value">Raw bytes value</param>
        /// <returns></returns>
        public PacketDefinition ReplacePart(string name, byte[] value)
        {
            var toReplace = GetPacketPart(name);

            PacketParts[name] = new vByte(value);

            return this;
        }

        public object Clone() => new PacketDefinition(this.Name, new Dictionary<string, vByte>(this.PacketParts));

        /// <summary>
        /// Appends packet parts into provided stream
        /// </summary>
        /// <param name="stream">A BitStream to append data to</param>
        internal void AppendToStream(BitStream stream)
        {
            foreach(var part in PacketParts)
            {
                stream.WriteVBytes(part.Value);
            }
        }

        private vByte GetPacketPart(string name)
        {
           if (!PacketParts.TryGetValue(name, out var toReplace))
            {
                throw new ArgumentException($"Unknown packet part {name}");
            }

            return toReplace;
        }
    }
}
