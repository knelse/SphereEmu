using System.IO;

namespace SphServer.Shared.BitStream;

// Write stream that zero-fills the final partial byte on flush: the client reads a 7-bit tag
// after the last frame record, and the library's default 1-padding can make it read as
// "keep going" and crash. GetStreamData isn't virtual, so it's hidden - the factory hands out
// this type so every S->C frame gets the fill.
public class SphWriteStream : BitStreams.BitStream
{
    public SphWriteStream () : base(new MemoryStream())
    {
        AutoIncreaseStream = true;
    }

    public new byte[] GetStreamData ()
    {
        while (Bit != 0)
        {
            WriteBit(0);
        }

        return base.GetStreamData();
    }
}
