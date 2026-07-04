using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.OutdoorNav;

/// <summary>
///     One coarse outdoor navigation chunk aligned with walk atlas chunks.
/// </summary>
public sealed class OutdoorNavChunk
{
    public const float NoGround = float.NaN;

    private readonly byte[] _walkable;
    private readonly float[] _terrainY;

    public OutdoorNavChunk(
        float originX,
        float originZ,
        float sampleSpacing,
        int width,
        int height,
        byte[] walkable,
        float[] terrainY)
    {
        OriginX = originX;
        OriginZ = originZ;
        SampleSpacing = sampleSpacing;
        Width = width;
        Height = height;
        _walkable = walkable;
        _terrainY = terrainY;
    }

    public float OriginX { get; }
    public float OriginZ { get; }
    public float SampleSpacing { get; }
    public int Width { get; }
    public int Height { get; }

    public bool IsWalkableWorld(float worldX, float worldZ)
    {
        return TryGetSampleIndex(worldX, worldZ, out var index) && _walkable[index] != 0;
    }

    public bool TrySampleTerrainY(float worldX, float worldZ, out float worldY)
    {
        worldY = NoGround;
        if (!TryGetSampleIndex(worldX, worldZ, out var index) || _walkable[index] == 0)
        {
            return false;
        }

        var terrainY = _terrainY[index];
        if (float.IsNaN(terrainY))
        {
            return false;
        }

        worldY = terrainY;
        return true;
    }

    internal bool TryGetSampleIndex(float worldX, float worldZ, out int index)
    {
        index = 0;
        var x = Mathf.RoundToInt((worldX - OriginX) / SampleSpacing);
        var z = Mathf.RoundToInt((worldZ - OriginZ) / SampleSpacing);
        if (x < 0 || x >= Width || z < 0 || z >= Height)
        {
            return false;
        }

        index = z * Width + x;
        return true;
    }

    internal void CollectWalkableInRadius(
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        List<(float X, float Z, float Y, int Index)> cells)
    {
        var radiusSq = radiusMeters * radiusMeters;
        for (var z = 0; z < Height; z++)
        {
            for (var x = 0; x < Width; x++)
            {
                var index = z * Width + x;
                if (_walkable[index] == 0)
                {
                    continue;
                }

                var worldX = OriginX + x * SampleSpacing;
                var worldZ = OriginZ + z * SampleSpacing;
                var dx = worldX - centerWorldX;
                var dz = worldZ - centerWorldZ;
                if (dx * dx + dz * dz > radiusSq)
                {
                    continue;
                }

                var terrainY = _terrainY[index];
                if (float.IsNaN(terrainY))
                {
                    continue;
                }

                cells.Add((worldX, worldZ, terrainY, index));
            }
        }
    }

    public static string BuildResourcePath(int chunkX, int chunkZ, string directoryResourcePath)
    {
        var trimmed = directoryResourcePath.TrimEnd('/');
        return $"{trimmed}/nav_chunk_{chunkX}_{chunkZ}.bin";
    }

    public static string BuildAbsolutePath(int chunkX, int chunkZ, string directoryResourcePath)
    {
        return ProjectSettings.GlobalizePath(BuildResourcePath(chunkX, chunkZ, directoryResourcePath));
    }

    public void SaveAtomic(string absolutePath)
    {
        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = absolutePath + ".tmp";
        using (var stream = File.Open(tempPath, FileMode.Create, global::System.IO.FileAccess.Write, FileShare.None))
        {
            OutdoorNavChunkCodec.WriteV1(stream, SampleSpacing, OriginX, OriginZ, Width, Height, _walkable, _terrainY);
        }

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        File.Move(tempPath, absolutePath);
    }

    public static bool TryLoad(string absolutePath, out OutdoorNavChunk? chunk)
    {
        chunk = null;
        if (!File.Exists(absolutePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(absolutePath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != OutdoorNavChunkFile.Magic)
            {
                return false;
            }

            var version = reader.ReadUInt16();
            if (version != OutdoorNavChunkCodec.FormatVersion)
            {
                return false;
            }

            var sampleSpacing = reader.ReadSingle();
            var originX = reader.ReadSingle();
            var originZ = reader.ReadSingle();
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            if (!OutdoorNavChunkCodec.TryReadV1(reader, width, height, out var walkable, out var terrainY)
                || walkable is null
                || terrainY is null)
            {
                return false;
            }

            chunk = new OutdoorNavChunk(originX, originZ, sampleSpacing, width, height, walkable, terrainY);
            return true;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"OutdoorNavChunk: failed to load '{absolutePath}': {ex.Message}");
            return false;
        }
    }
}

internal static class OutdoorNavChunkFile
{
    public const uint Magic = 0x3156414E; // 'NAV1'
}
