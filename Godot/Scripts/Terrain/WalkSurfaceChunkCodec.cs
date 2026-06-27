using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Encodes walk atlas chunks for compact on-disk storage (format v3).
///     Heights are quantized to uint16; blocked flags are bit-packed; payload is Deflate-compressed.
/// </summary>
internal static class WalkSurfaceChunkCodec
{
    public const ushort FormatVersion = 3;
    private const ushort HeightNoGround = ushort.MaxValue;
    private const float MinQuantStepMeters = 0.001f;

    public static void WriteV3(Stream stream, float sampleSpacing, float originX, float originZ, int width, int height, float[] heights, byte[] blocked)
    {
        EncodeHeights(heights, out var heightBase, out var quantStep, out var encodedHeights);
        var packedBlocked = PackBlocked(blocked);

        using var payloadStream = new MemoryStream(encodedHeights.Length * 2 + packedBlocked.Length);
        WriteUInt16Array(payloadStream, encodedHeights);
        payloadStream.Write(packedBlocked, 0, packedBlocked.Length);
        var payload = payloadStream.ToArray();
        var compressedPayload = Compress(payload);

        using var writer = new BinaryWriter(stream, global::System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(WalkSurfaceChunkFile.Magic);
        writer.Write(FormatVersion);
        writer.Write(sampleSpacing);
        writer.Write(originX);
        writer.Write(originZ);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write(heightBase);
        writer.Write(quantStep);
        writer.Write(compressedPayload.Length);
        writer.Write(compressedPayload);
    }

    public static bool TryReadV3(
        BinaryReader reader,
        float sampleSpacing,
        float originX,
        float originZ,
        int width,
        int height,
        out float[]? heights,
        out byte[]? blocked)
    {
        heights = null;
        blocked = null;

        var count = width * height;
        if (count <= 0)
        {
            return false;
        }

        var heightBase = reader.ReadSingle();
        var quantStep = reader.ReadSingle();
        var compressedLength = reader.ReadInt32();
        if (compressedLength <= 0 || compressedLength > 256 * 1024 * 1024)
        {
            return false;
        }

        var compressedPayload = reader.ReadBytes(compressedLength);
        if (compressedPayload.Length != compressedLength)
        {
            return false;
        }

        byte[] payload;
        try
        {
            payload = Decompress(compressedPayload);
        }
        catch
        {
            return false;
        }

        var blockedBytes = (count + 7) / 8;
        var heightsBytes = count * sizeof(ushort);
        if (payload.Length < heightsBytes + blockedBytes)
        {
            return false;
        }

        var encodedHeights = ReadUInt16Array(payload, 0, count);
        heights = DecodeHeights(encodedHeights, heightBase, quantStep);
        blocked = UnpackBlocked(payload, heightsBytes, count);
        return true;
    }

    private static void EncodeHeights(float[] heights, out float heightBase, out float quantStep, out ushort[] encoded)
    {
        encoded = new ushort[heights.Length];
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;

        foreach (var height in heights)
        {
            if (float.IsNaN(height))
            {
                continue;
            }

            if (height < minY)
            {
                minY = height;
            }

            if (height > maxY)
            {
                maxY = height;
            }
        }

        if (float.IsPositiveInfinity(minY))
        {
            heightBase = 0f;
            quantStep = MinQuantStepMeters;
            Array.Fill(encoded, HeightNoGround);
            return;
        }

        heightBase = minY;
        var range = maxY - minY;
        quantStep = range <= 0f ? MinQuantStepMeters : Math.Max(MinQuantStepMeters, range / 65534f);

        for (var i = 0; i < heights.Length; i++)
        {
            var height = heights[i];
            if (float.IsNaN(height))
            {
                encoded[i] = HeightNoGround;
                continue;
            }

            var quantized = (int)Math.Round((height - heightBase) / quantStep);
            encoded[i] = (ushort)Math.Clamp(quantized, 0, 65534);
        }
    }

    private static float[] DecodeHeights(ushort[] encoded, float heightBase, float quantStep)
    {
        var heights = new float[encoded.Length];
        for (var i = 0; i < encoded.Length; i++)
        {
            var value = encoded[i];
            heights[i] = value == HeightNoGround ? WalkSurfaceChunk.NoGround : heightBase + value * quantStep;
        }

        return heights;
    }

    private static byte[] PackBlocked(byte[] blocked)
    {
        var packed = new byte[(blocked.Length + 7) / 8];
        for (var i = 0; i < blocked.Length; i++)
        {
            if (blocked[i] == 0)
            {
                continue;
            }

            packed[i >> 3] |= (byte)(1 << (i & 7));
        }

        return packed;
    }

    private static byte[] UnpackBlocked(byte[] payload, int offset, int count)
    {
        var blocked = new byte[count];
        for (var i = 0; i < count; i++)
        {
            var packedIndex = offset + (i >> 3);
            blocked[i] = (payload[packedIndex] & (1 << (i & 7))) != 0 ? (byte)1 : (byte)0;
        }

        return blocked;
    }

    private static void WriteUInt16Array(Stream stream, ushort[] values)
    {
        var bytes = new byte[values.Length * sizeof(ushort)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * sizeof(ushort), sizeof(ushort)), values[i]);
        }

        stream.Write(bytes, 0, bytes.Length);
    }

    private static ushort[] ReadUInt16Array(byte[] payload, int offset, int count)
    {
        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + i * sizeof(ushort), sizeof(ushort)));
        }

        return values;
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}

internal static class WalkSurfaceChunkFile
{
    public const uint Magic = 0x4B485357; // 'WSHK'
}
