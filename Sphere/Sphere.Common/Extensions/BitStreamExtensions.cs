using BitStreams;
using Sphere.Common.Types;

namespace Sphere.Common.Extensions
{
    /// <summary>
    /// Provide BitStream extension to write vBytes into a stream.
    /// </summary>
    public static class BitStreamExtensions
    {
        public static BitStream WriteVBytes(this BitStream bitStream, vByte value)
            => vByte.WriteVBytes(bitStream, value);
    }
}
