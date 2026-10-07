namespace SphServer.Helpers.Networking;

/// Channel word at bytes 6-7 of every client frame
public enum WireChannel : ushort
{
    Unknown = 0,

    /// Server connect greeting
    Handshake = 0x00C8,

    /// Player actions and position updates
    Gameplay = 0x012C,

    /// Three-second heartbeat; Echo HUD is elapsed/3 on the matching server frame
    Keepalive = 0x01F4,

    /// Client reply to the connect greeting
    HandshakeReply = 0x0190,

    /// Running count of frames the client has sent
    SentCount = 0x02BC
}

/// C2S: LE u16 length, checksum (low = sum of bytes 4..end xor per-connection key), seq (+1..4),
/// channel, pad 00, body from byte 9; S2C is length then channel
public readonly struct ClientFrame (byte[] raw)
{
    public const int HeaderLength = 8;
    public const int BodyOffset = 9;

    public byte[] Raw { get; } = raw;

    public int DeclaredLength => Raw.Length >= 2 ? Raw[0] | (Raw[1] << 8) : 0;

    public ushort Checksum => (ushort) (Raw[2] | (Raw[3] << 8));

    public ushort Sequence => (ushort) (Raw[4] | (Raw[5] << 8));

    public WireChannel Channel =>
        Raw.Length >= 8 ? (WireChannel) (Raw[6] | (Raw[7] << 8)) : WireChannel.Unknown;

    public bool HasBody => Raw.Length > BodyOffset;

    /// Low byte of the frame length
    public byte LegacyCaseByte => Raw.Length > 0 ? Raw[0] : (byte) 0;

    /// A non-empty remainder is bytes that no longer start on a frame
    public static List<ClientFrame> Split (byte[] data, out int remainder)
    {
        var frames = new List<ClientFrame> ();
        var offset = 0;

        while (offset + 2 <= data.Length)
        {
            var length = data[offset] | (data[offset + 1] << 8);
            if (length < 2 || offset + length > data.Length)
            {
                break;
            }

            frames.Add (new ClientFrame (data[offset..(offset + length)]));
            offset += length;
        }

        remainder = data.Length - offset;
        return frames;
    }

    /// Low byte is the sum of bytes from offset 4, xored with the per-connection key
    public bool ChecksumLowByteMatches (byte keyLow)
    {
        if (Raw.Length < 5)
        {
            return false;
        }

        var sum = 0;
        for (var i = 4; i < Raw.Length; i++)
        {
            sum += Raw[i];
        }

        return Raw[2] == ((sum & 0xFF) ^ keyLow);
    }

    /// Key implied by a frame already known intact
    public byte DeriveChecksumKeyLow ()
    {
        var sum = 0;
        for (var i = 4; i < Raw.Length; i++)
        {
            sum += Raw[i];
        }

        return (byte) (Raw[2] ^ (sum & 0xFF));
    }
}
