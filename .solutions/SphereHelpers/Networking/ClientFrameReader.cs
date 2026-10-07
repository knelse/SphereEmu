using System;

namespace SphServer.Helpers.Networking;

public enum FrameReadResult
{
    Frame,

    /// The rest of the frame has not arrived
    Incomplete,

    /// Bytes no longer start on a frame boundary
    Desynced
}

/// Holds leftover TCP bytes until a length-prefixed frame is complete
public sealed class ClientFrameReader
{
    /// Stall guard on a nonsense length; the checksum decides a real frame
    public const int MaxFrameLength = 4096;

    /// Length, checksum, counter, and channel
    public const int HeaderLength = 8;

    private const int InitialCapacity = 4096;

    /// Checksum byte: sum from byte 4 xor the per-connection key
    private const int ChecksumOffset = 2;

    private const int ChecksumFrom = 4;

    private byte[] pending = new byte[InitialCapacity];
    private int held;
    private int? checksumKey;

    /// Set when the read returns Desynced
    public string? DesyncReason { get; private set; }

    /// Byte count not yet a whole frame
    public int Pending => held;

    public void Append (ReadOnlySpan<byte> bytes)
    {
        if (held + bytes.Length > pending.Length)
        {
            var grown = new byte[Math.Max (pending.Length * 2, held + bytes.Length)];
            pending.AsSpan (0, held).CopyTo (grown);
            pending = grown;
        }

        bytes.CopyTo (pending.AsSpan (held));
        held += bytes.Length;
    }

    /// Desynced is terminal: no resync marker, the caller closes the connection
    public FrameReadResult TryTake (out byte[] frame)
    {
        frame = [];

        if (DesyncReason is not null)
        {
            return FrameReadResult.Desynced;
        }

        if (held < HeaderLength)
        {
            return FrameReadResult.Incomplete;
        }

        var length = pending[0] | (pending[1] << 8);

        // A length outside the frame bounds is not a length field
        if (length < HeaderLength || length > MaxFrameLength)
        {
            DesyncReason = $"frame length {length} outside {HeaderLength}..{MaxFrameLength}";
            return FrameReadResult.Desynced;
        }

        if (held < length)
        {
            return FrameReadResult.Incomplete;
        }

        var candidate = pending.AsSpan (0, length);
        var checksum = ChecksumOf (candidate);

        // Nothing precedes the first frame, so its checksum byte is the connection key
        checksumKey ??= candidate[ChecksumOffset] ^ checksum;

        if ((candidate[ChecksumOffset] ^ checksum) != checksumKey)
        {
            DesyncReason = $"frame checksum {candidate[ChecksumOffset]:X2} does not match the " +
                           $"{(byte) (checksum ^ checksumKey.Value):X2} this frame's bytes give";
            return FrameReadResult.Desynced;
        }

        frame = candidate.ToArray ();
        held -= length;
        pending.AsSpan (length, held).CopyTo (pending);
        return FrameReadResult.Frame;
    }

    private static byte ChecksumOf (ReadOnlySpan<byte> frame)
    {
        var sum = 0;
        for (var i = ChecksumFrom; i < frame.Length; i++)
        {
            sum += frame[i];
        }

        return (byte) sum;
    }
}
