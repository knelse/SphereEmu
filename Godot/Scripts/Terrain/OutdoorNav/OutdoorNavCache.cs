using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.OutdoorNav;

public static class OutdoorNavCache
{
    private static readonly ConcurrentDictionary<(int ChunkX, int ChunkZ), OutdoorNavChunk?> Loaded = new();

    public static string DirectoryResourcePath { get; set; } = OutdoorNavAtlasBuilder.DefaultOutputDirectory;

    public static float ChunkSizeMeters => WalkSurfaceAtlasBuilder.ChunkSizeMeters;

    public static float SampleSpacingMeters => OutdoorNavAtlasBuilder.SampleSpacingMeters;

    public static bool HasAnyNavFiles()
    {
        var absoluteDirectory = ProjectSettings.GlobalizePath(DirectoryResourcePath);
        return Directory.Exists(absoluteDirectory) && Directory.GetFiles(absoluteDirectory, "nav_chunk_*.bin").Length > 0;
    }

    public static void Invalidate()
    {
        Loaded.Clear();
    }

    public static void PreloadForRadius(float worldX, float worldZ, float radiusMeters)
    {
        if (!HasAnyNavFiles())
        {
            return;
        }

        var minChunkX = FloorChunkIndex(worldX - radiusMeters);
        var maxChunkX = FloorChunkIndex(worldX + radiusMeters);
        var minChunkZ = FloorChunkIndex(worldZ - radiusMeters);
        var maxChunkZ = FloorChunkIndex(worldZ + radiusMeters);
        for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
        {
            for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                GetOrLoadChunk(chunkX, chunkZ);
            }
        }
    }

    public static bool IsWalkable(float worldX, float worldZ)
    {
        var chunk = GetOrLoadChunk(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
        return chunk is not null && chunk.IsWalkableWorld(worldX, worldZ);
    }

    public static bool TrySampleTerrainY(float worldX, float worldZ, out float worldY)
    {
        worldY = OutdoorNavChunk.NoGround;
        var chunk = GetOrLoadChunk(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
        return chunk is not null && chunk.TrySampleTerrainY(worldX, worldZ, out worldY);
    }

    public static void CollectWalkableInRadius(
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        List<(float X, float Z, float Y)> cells)
    {
        if (!HasAnyNavFiles())
        {
            return;
        }

        PreloadForRadius(centerWorldX, centerWorldZ, radiusMeters);
        var minChunkX = FloorChunkIndex(centerWorldX - radiusMeters);
        var maxChunkX = FloorChunkIndex(centerWorldX + radiusMeters);
        var minChunkZ = FloorChunkIndex(centerWorldZ - radiusMeters);
        var maxChunkZ = FloorChunkIndex(centerWorldZ + radiusMeters);
        var scratch = new List<(float X, float Z, float Y, int Index)>();
        for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
        {
            for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                var chunk = GetOrLoadChunk(chunkX, chunkZ);
                if (chunk is null)
                {
                    continue;
                }

                scratch.Clear();
                chunk.CollectWalkableInRadius(centerWorldX, centerWorldZ, radiusMeters, scratch);
                foreach (var (x, z, y, _) in scratch)
                {
                    cells.Add((x, z, y));
                }
            }
        }
    }

    internal static OutdoorNavChunk? GetOrLoadChunk(int chunkX, int chunkZ)
    {
        var key = (chunkX, chunkZ);
        if (Loaded.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var path = OutdoorNavChunk.BuildAbsolutePath(chunkX, chunkZ, DirectoryResourcePath);
        if (!OutdoorNavChunk.TryLoad(path, out var chunk))
        {
            Loaded[key] = null;
            return null;
        }

        Loaded[key] = chunk;
        return chunk;
    }

    private static int FloorChunkIndex(float worldCoordinate)
    {
        return (int)Mathf.Floor(worldCoordinate / ChunkSizeMeters);
    }
}
