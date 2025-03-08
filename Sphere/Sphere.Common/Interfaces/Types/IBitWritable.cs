using BitStreams;

namespace Sphere.Common.Interfaces.Types
{
    /// <summary>
    /// Defines a bit writable object such as vByte.
    /// </summary>
    /// <typeparam name="TSelf"></typeparam>
    public interface IBitWritable<TSelf> where TSelf : IBitWritable<TSelf>
    {
        abstract static BitStream WriteVBytes(BitStream stream, TSelf value);
    }
}
