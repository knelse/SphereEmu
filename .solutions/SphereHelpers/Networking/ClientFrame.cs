namespace SphServer.Helpers.Networking;

/// <summary>
///     Channel word carried at bytes 6-7 of every client frame. Everything a player does travels on
///     <see cref="Gameplay" />; the rest is transport bookkeeping the dispatch has never looked at.
/// </summary>
public enum WireChannel : ushort
{
    Unknown = 0,

    /// <summary>Server's connect greeting.</summary>
    Handshake = 0x00C8,

    /// <summary>Every player action and position update.</summary>
    Gameplay = 0x012C,

    /// <summary>Three-second heartbeat. Both directions; the server already sends its half.</summary>
    Keepalive = 0x01F4,

    /// <summary>Client's reply to <see cref="Handshake" />.</summary>
    HandshakeReply = 0x0190,

    /// <summary>Client's running total of frames it has sent. An exact loss detector.</summary>
    SentCount = 0x02BC
}

/// <summary>
///     One length-prefixed client-to-server frame.
///     <code>
///     [0..1] length    LE u16, counts the whole frame
///     [2..3] checksum  low byte = (sum of bytes 4..end) xor a per-connection key
///     [4..5] seq       increments by a random 1..4 per frame
///     [6..7] channel
///     [8]    0x00      pad
///     [9..]  body      bit-packed, obfuscated from here (Packet.DecodeClientPacket start)
///     </code>
///     Server-to-client frames are shorter: just length then channel, with no checksum or seq.
/// </summary>
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

    /// <summary>
    ///     The low byte of the length — what the old dispatch switched on. Kept only so the
    ///     transitional routing can still reach handlers that have not been ported yet.
    /// </summary>
    public byte LegacyCaseByte => Raw.Length > 0 ? Raw[0] : (byte) 0;

    /// <summary>
    ///     Splits one read into frames using the length prefix. A non-empty <paramref name="remainder" />
    ///     means the split desynced and nothing past that point can be trusted.
    /// </summary>
    public static List<ClientFrame> Split (byte[] data, out int remainder)
    {
        var frames = new List<ClientFrame>();
        var offset = 0;

        while (offset + 2 <= data.Length)
        {
            var length = data[offset] | (data[offset + 1] << 8);
            if (length < 2 || offset + length > data.Length)
            {
                break;
            }

            frames.Add(new ClientFrame(data[offset..(offset + length)]));
            offset += length;
        }

        remainder = data.Length - offset;
        return frames;
    }

    /// <summary>
    ///     Verified against 5137 captured frames from three sessions, every length from 8 to 273
    ///     bytes: the checksum's low byte is the byte sum from offset 4 to the end, xored with a key
    ///     the client keeps per connection. The high byte does not follow this and is not understood,
    ///     so only the low byte is checked.
    /// </summary>
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

    /// <summary>The key this connection is using, recovered from a frame known to be intact.</summary>
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
