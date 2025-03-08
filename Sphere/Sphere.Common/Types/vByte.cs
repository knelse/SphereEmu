using BitStreams;
using Sphere.Common.Helpers;
using Sphere.Common.Interfaces.Types;
using System.Numerics;

namespace Sphere.Common.Types
{
    /// <summary>
    /// Represents variable length byte in binary format.
    /// E.g. if length is set to 5 and the value is Int16 in binary format it will be cut to 01000 
    /// </summary>
    public struct vByte : IBitWritable<vByte>, IEqualityOperators<vByte, vByte, bool>
    {
        public readonly ushort Length = 32;
        internal readonly byte[] BaseValue;

        internal vByte(int baseValue, ushort length)
        {
            this.Length = length;
            this.BaseValue = BitConverter.GetBytes(baseValue);
        }

        public vByte(byte[] bytes)
        {
            this.Length = (ushort)(bytes.Length * 8);
            this.BaseValue = bytes;
        }

        public vByte(byte[] bytes, ushort length)
        {
            this.Length = length;
            this.BaseValue = bytes;
        }

        public vByte(string str)
        {
            this.BaseValue = EncodingHelper.Win1251.GetBytes(str);
            this.Length = (ushort)(this.BaseValue.Length * 8);
        }

        public static BitStream WriteVBytes(BitStream stream, vByte value)
        {
            stream.WriteBytes(value.BaseValue, value.Length);
            return stream;
        }

        public static bool operator ==(vByte left, vByte right) => left.BaseValue == right.BaseValue && left.Length == right.Length;
        public static bool operator !=(vByte left, vByte right) => left.BaseValue != right.BaseValue || left.Length != right.Length;

        public static implicit operator long(vByte value) => BitConverter.ToInt64(value.BaseValue);
        public static implicit operator int(vByte value) => BitConverter.ToInt32(value.BaseValue);
        public static implicit operator short(vByte value) => BitConverter.ToInt16(value.BaseValue);

        public override bool Equals(object obj)
        {
            return this == (vByte)obj;
        }

        public override int GetHashCode()
        {
            return this.BaseValue.GetHashCode() ^ this.Length.GetHashCode() ^ 12983198;
        }
    }

    
}
