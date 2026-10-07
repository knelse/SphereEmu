using System.IO;

namespace SphServer.Shared.BitStream;

// Hides GetStreamData (not virtual) and zero-fills the trailing partial byte: 1-padding reads as a
// 7-bit "keep going" tag
public class SphWriteStream : BitStreams.BitStream
{
    public SphWriteStream () : base (new MemoryStream ())
    {
        AutoIncreaseStream = true;
    }

    public new byte[] GetStreamData ()
    {
        while (Bit != 0)
        {
            WriteBit (0);
        }

        return base.GetStreamData ();
    }
}
