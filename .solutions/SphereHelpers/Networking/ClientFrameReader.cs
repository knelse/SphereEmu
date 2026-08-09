using System;

namespace SphServer.Helpers.Networking;

public enum FrameReadResult
{
    /// <summary>A whole frame was produced.</summary>
    Frame,

    /// <summary>Nothing complete yet. Wait for more bytes; this is not an error.</summary>
    Incomplete,

    /// <summary>The stream no longer starts on a frame boundary. Nothing here is trustworthy.</summary>
    Desynced
}

/// <summary>
///     Turns the TCP byte stream into whole frames. A read can end mid-frame and can hold several
///     frames, so what is left over is kept until the rest arrives.
///     Knows nothing above the length prefix — no engine, socket or handler types.
/// </summary>
public sealed class ClientFrameReader
{
    /// <summary>
    ///     A stall guard, not a protocol bound: the checksum decides whether bytes are a frame,
    ///     this only stops the reader waiting forever on a nonsense length. Generous on purpose —
    ///     too tight would reject a legitimate frame we have not seen yet.
    /// </summary>
    public const int MaxFrameLength = 4096;

    /// <summary>Length, checksum, counter and channel: the fixed part every frame starts with.</summary>
    public const int HeaderLength = 8;

    /// <summary>Starting size of the held bytes. It grows on demand; this only avoids early resizes.</summary>
    private const int InitialCapacity = 4096;

    /// <summary>
    ///     Byte 2: a checksum of everything from byte 4 on, exclusive-ored with a per-connection
    ///     key — a frame proves itself rather than merely looks plausible. It detects a lost
    ///     boundary but cannot find it again, so failing it is terminal for the connection.
    /// </summary>
    private const int ChecksumOffset = 2;

    private const int ChecksumFrom = 4;

    private byte[] pending = new byte[InitialCapacity];
    private int held;
    private int? checksumKey;

    /// <summary>Why <see cref="FrameReadResult.Desynced" /> was returned, for the log.</summary>
    public string? DesyncReason { get; private set; }

    /// <summary>Bytes held that do not yet form a whole frame.</summary>
    public int Pending => held;

    /// <summary>Adds what the socket produced to the held bytes.</summary>
    public void Append (ReadOnlySpan<byte> bytes)
    {
        if (held + bytes.Length > pending.Length)
        {
            var grown = new byte[Math.Max(pending.Length * 2, held + bytes.Length)];
            pending.AsSpan(0, held).CopyTo(grown);
            pending = grown;
        }

        bytes.CopyTo(pending.AsSpan(held));
        held += bytes.Length;
    }

    /// <summary>
    ///     Takes the next whole frame, or reports why it cannot. Desynced is terminal: there is
    ///     no marker to resynchronise on, recovery is the caller closing the connection.
    /// </summary>
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

        // The client is a fixed binary and cannot send a length it did not mean, so a length that
        // cannot be right means this is not a length field.
        if (length < HeaderLength || length > MaxFrameLength)
        {
            DesyncReason = $"frame length {length} outside {HeaderLength}..{MaxFrameLength}";
            return FrameReadResult.Desynced;
        }

        if (held < length)
        {
            return FrameReadResult.Incomplete;
        }

        var candidate = pending.AsSpan(0, length);
        var checksum = ChecksumOf(candidate);

        // The first frame of a connection cannot be misaligned — nothing precedes it — so it is
        // what the key is taken from. Deriving it rather than hardcoding a value means a key that
        // turns out to vary still validates instead of closing a healthy client.
        checksumKey ??= candidate[ChecksumOffset] ^ checksum;

        if ((candidate[ChecksumOffset] ^ checksum) != checksumKey)
        {
            DesyncReason = $"frame checksum {candidate[ChecksumOffset]:X2} does not match the " +
                           $"{(byte) (checksum ^ checksumKey.Value):X2} this frame's bytes give";
            return FrameReadResult.Desynced;
        }

        frame = candidate.ToArray();
        held -= length;
        pending.AsSpan(length, held).CopyTo(pending);
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
