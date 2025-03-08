using BitStreams;
using Sphere.Common.Extensions;
using Sphere.Common.Helpers.Extensions;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Models;
using Sphere.Common.Types;
using System.Numerics;

namespace Sphere.Common.Packets.Server
{
    public class ServerPacketBase : IServerPacket, IServerPacketStream
    {
        private static readonly ushort PacketValidationCodeOK = 0x2C01;

        protected virtual string PacketName { get; } = "empty";
        
        protected virtual ushort Size => 4; // empty packet

        protected vByte EmptyPacket = new vByte([0x04, 0x00, 0xF4, 0x01]);

        protected virtual vByte Padding => new vByte(0, 24);

        /// <summary>
        /// Loads new packet definition once per instantiated ServerPacker.
        /// </summary>
        private Lazy<PacketDefinition> PacketDefinition => new Lazy<PacketDefinition>(() => GetPacketDefinition(PacketName));

        /// <summary>
        /// Builds a basic set of vBytes that contains packet size and validation indicator.
        /// </summary>
        /// <returns></returns>
        protected vByte GetBaseBytes()
        {
            var size = (ushort)Size;
            return new vByte([
                BitHelper.MinorByte(size),
                BitHelper.MajorByte(size),
                BitHelper.MajorByte(PacketValidationCodeOK),
                BitHelper.MinorByte(PacketValidationCodeOK),
            ]);
        }

        /// <summary>
        /// Builds bit stream from filled packet definition and provides it as byte array.
        /// </summary>
        /// <returns></returns>
        public byte[] GetBytes()
        {
            return BuildBitStream().GetStreamData();
        }

        /// <summary>
        /// Builds bit stream from filled packet definition and provides it as stream.
        /// </summary>
        /// <returns></returns>
        public Stream GetStream()
        {
            return BuildBitStream().GetStream();
        }        

        /// <summary>
        /// Adds (replaces) value in packet definition
        /// </summary>
        /// <typeparam name="T">An integer type that implements <see cref="IBinaryInteger{TSelf}"/>IBinaryInteger</typeparam> interface
        /// <param name="part">The name of packet part</param>
        /// <param name="value">Value to add into packet</param>
        /// <returns></returns>
        public IServerPacket AddValue<T>(string part, T value) where T : IBinaryInteger<T>
        {
            PacketDefinition.Value.ReplacePart(part, value);
            return this;
        }

        /// <summary>
        /// Adds coordinates into packet replacing X,Y,Z and angle parts in a definition.
        /// </summary>
        /// <param name="coordinates">Coordinates value</param>
        /// <returns></returns>
        public IServerPacket AddValue(Coordinates coordinates)
        {
            var bytes = coordinates.GetBytes();

            PacketDefinition.Value.ReplacePart("x", bytes[0..4]);
            PacketDefinition.Value.ReplacePart("y", bytes[4..8]);
            PacketDefinition.Value.ReplacePart("z", bytes[8..12]);
            PacketDefinition.Value.ReplacePart("angle", bytes[12..13]);

            return this;
        }

        /// <summary>
        /// Finishes adding values into packet.
        /// For now no specific purpose, maybe will be used for logging or some additional logic.
        /// </summary>
        /// <returns></returns>
        public IServerPacketStream Build() => this;

        private BitStream BuildBitStream()
        {
            var buffer = new byte[Size];

            var bitStream = new BitStream(buffer);

            bitStream.WriteVBytes(GetBaseBytes());
            bitStream.WriteVBytes(this.Padding);

            PacketDefinition.Value.AppendToStream(bitStream);

            return bitStream;
        }
    }
}
