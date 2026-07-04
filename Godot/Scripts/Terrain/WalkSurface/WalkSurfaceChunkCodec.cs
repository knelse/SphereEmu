using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Encodes walk atlas chunks for compact on-disk storage (format v3).
///     Heights are quantized to uint16; blocked flags are bit-packed; payload is Deflate-compressed.
/// </summary>
internal static class WalkSurfaceChunkCodec
{
    public const ushort FormatVersion = 3;
    public const ushort FormatVersionV4 = 4;
    private const ushort HeightNoGround = ushort.MaxValue;
    private const float MinQuantStepMeters = 0.001f;

    public static void WriteV3(Stream stream, float sampleSpacing, float originX, float originZ, int width, int height, float[] heights, byte[] blocked)
    {
        WritePayload(stream, FormatVersion, sampleSpacing, originX, originZ, width, height, heights, null, blocked, null);
    }

    public static void WriteV4(
        Stream stream,
        float sampleSpacing,
        float originX,
        float originZ,
        int width,
        int height,
        float[] heights,
        float[] terrainHeights,
        byte[] blocked,
        byte[] spawnAllowed)
    {
        WritePayload(stream, FormatVersionV4, sampleSpacing, originX, originZ, width, height, heights, terrainHeights, blocked, spawnAllowed);
    }

    private static void WritePayload(
        Stream stream,
        ushort formatVersion,
        float sampleSpacing,
        float originX,
        float originZ,
        int width,
        int height,
        float[] heights,
        float[]? terrainHeights,
        byte[] blocked,
        byte[]? spawnAllowed)
    {
        EncodeHeights(heights, out var heightBase, out var quantStep, out var encodedHeights);
        EncodeHeights(terrainHeights ?? heights, out var terrainBase, out var terrainQuantStep, out var encodedTerrainHeights);
        var packedBlocked = PackBits(blocked);
        var packedSpawnAllowed = PackBits(spawnAllowed ?? blocked);

        using var payloadStream = new MemoryStream(encodedHeights.Length * 4 + packedBlocked.Length + packedSpawnAllowed.Length);
        WriteUInt16Array(payloadStream, encodedHeights);
        WriteUInt16Array(payloadStream, encodedTerrainHeights);
        payloadStream.Write(packedBlocked, 0, packedBlocked.Length);
        if (formatVersion >= FormatVersionV4)
        {
            payloadStream.Write(packedSpawnAllowed, 0, packedSpawnAllowed.Length);
        }

        var payload = payloadStream.ToArray();
        var compressedPayload = Compress(payload);

        using var writer = new BinaryWriter(stream, global::System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(WalkSurfaceChunkFile.Magic);
        writer.Write(formatVersion);
        writer.Write(sampleSpacing);
        writer.Write(originX);
        writer.Write(originZ);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write(heightBase);
        writer.Write(quantStep);
        if (formatVersion >= FormatVersionV4)
        {
            writer.Write(terrainBase);
            writer.Write(terrainQuantStep);
        }

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
        blocked = UnpackBits(payload, heightsBytes, count);
        return true;
    }

    public static bool TryReadV4(
        BinaryReader reader,
        float sampleSpacing,
        float originX,
        float originZ,
        int width,
        int height,
        out float[]? heights,
        out float[]? terrainHeights,
        out byte[]? blocked,
        out byte[]? spawnAllowed)
    {
        heights = null;
        terrainHeights = null;
        blocked = null;
        spawnAllowed = null;

        var count = width * height;
        if (count <= 0)
        {
            return false;
        }

        var heightBase = reader.ReadSingle();
        var quantStep = reader.ReadSingle();
        var terrainBase = reader.ReadSingle();
        var terrainQuantStep = reader.ReadSingle();
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
        var terrainBytes = count * sizeof(ushort);
        if (payload.Length < heightsBytes + terrainBytes + blockedBytes + blockedBytes)
        {
            return false;
        }

        var encodedHeights = ReadUInt16Array(payload, 0, count);
        var encodedTerrainHeights = ReadUInt16Array(payload, heightsBytes, count);
        heights = DecodeHeights(encodedHeights, heightBase, quantStep);
        terrainHeights = DecodeHeights(encodedTerrainHeights, terrainBase, terrainQuantStep);
        blocked = UnpackBits(payload, heightsBytes + terrainBytes, count);
        spawnAllowed = UnpackBits(payload, heightsBytes + terrainBytes + blockedBytes, count);
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

    private static byte[] PackBits(byte[] bits)
    {
        var packed = new byte[(bits.Length + 7) / 8];
        for (var i = 0; i < bits.Length; i++)
        {
            if (bits[i] == 0)
            {
                continue;
            }

            packed[i >> 3] |= (byte)(1 << (i & 7));
        }

        return packed;
    }

    private static byte[] UnpackBits(byte[] payload, int offset, int count)
    {
        var bits = new byte[count];
        for (var i = 0; i < count; i++)
        {
            var packedIndex = offset + (i >> 3);
            bits[i] = (payload[packedIndex] & (1 << (i & 7))) != 0 ? (byte)1 : (byte)0;
        }

        return bits;
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
