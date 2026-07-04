using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace SphServer.Godot.Scripts.Terrain.OutdoorNav;

internal static class OutdoorNavChunkCodec
{
    public const ushort FormatVersion = 1;
    private const ushort HeightNoGround = ushort.MaxValue;
    private const float MinQuantStepMeters = 0.001f;

    public static void WriteV1(
        Stream stream,
        float sampleSpacing,
        float originX,
        float originZ,
        int width,
        int height,
        byte[] walkable,
        float[] terrainY)
    {
        EncodeHeights(terrainY, out var heightBase, out var quantStep, out var encodedTerrainY);
        var packedWalkable = PackBits(walkable);

        using var payloadStream = new MemoryStream(encodedTerrainY.Length * sizeof(ushort) + packedWalkable.Length);
        WriteUInt16Array(payloadStream, encodedTerrainY);
        payloadStream.Write(packedWalkable, 0, packedWalkable.Length);
        var payload = payloadStream.ToArray();
        var compressedPayload = Compress(payload);

        using var writer = new BinaryWriter(stream, global::System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(OutdoorNavChunkFile.Magic);
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

    public static bool TryReadV1(
        BinaryReader reader,
        int width,
        int height,
        out byte[]? walkable,
        out float[]? terrainY)
    {
        walkable = null;
        terrainY = null;
        var count = width * height;
        if (count <= 0)
        {
            return false;
        }

        var heightBase = reader.ReadSingle();
        var quantStep = reader.ReadSingle();
        var compressedLength = reader.ReadInt32();
        if (compressedLength <= 0 || compressedLength > 64 * 1024 * 1024)
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

        var walkableBytes = (count + 7) / 8;
        var terrainBytes = count * sizeof(ushort);
        if (payload.Length < terrainBytes + walkableBytes)
        {
            return false;
        }

        var encodedTerrainY = ReadUInt16Array(payload, 0, count);
        terrainY = DecodeHeights(encodedTerrainY, heightBase, quantStep);
        walkable = UnpackBits(payload, terrainBytes, count);
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

            minY = Math.Min(minY, height);
            maxY = Math.Max(maxY, height);
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
            heights[i] = encoded[i] == HeightNoGround ? OutdoorNavChunk.NoGround : heightBase + encoded[i] * quantStep;
        }

        return heights;
    }

    private static byte[] PackBits(byte[] bits)
    {
        var packed = new byte[(bits.Length + 7) / 8];
        for (var i = 0; i < bits.Length; i++)
        {
            if (bits[i] != 0)
            {
                packed[i >> 3] |= (byte)(1 << (i & 7));
            }
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
