using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Lazy loader for outdoor walk-surface height atlas chunks.
/// </summary>
public static class WalkSurfaceCache
{
    public const float MobSpawnBodyRadiusMeters = 0.55f;

    private static readonly (float X, float Z)[] MobBodyDiskOffsets =
    [
        (1f, 0f),
        (-1f, 0f),
        (0f, 1f),
        (0f, -1f),
        (0.70710677f, 0.70710677f),
        (-0.70710677f, 0.70710677f),
        (0.70710677f, -0.70710677f),
        (-0.70710677f, -0.70710677f),
    ];

    private static readonly ConcurrentDictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunk?> Loaded = new();

    public static string DirectoryResourcePath { get; set; } = WalkSurfaceAtlasBuilder.DefaultOutputDirectory;

    public static float ChunkSizeMeters => WalkSurfaceAtlasBuilder.ChunkSizeMeters;

    public static bool HasOutdoorSpawnChannel { get; private set; }

    public static void Invalidate()
    {
        Loaded.Clear();
        HasOutdoorSpawnChannel = false;
    }

    public static bool IsChunkFilePresent(int chunkX, int chunkZ)
    {
        return File.Exists(WalkSurfaceChunk.BuildAbsolutePath(chunkX, chunkZ, DirectoryResourcePath));
    }

    public static bool HasAnyChunkFiles()
    {
        var absoluteDirectory = ProjectSettings.GlobalizePath(DirectoryResourcePath);
        return Directory.Exists(absoluteDirectory) && Directory.GetFiles(absoluteDirectory, "chunk_*.bin").Length > 0;
    }

    public static bool HasChunkCoverageAt(float worldX, float worldZ)
    {
        if (!HasAnyChunkFiles())
        {
            return false;
        }

        return IsChunkFilePresent(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
    }

    public static void PreloadChunksForRadius(float worldX, float worldZ, float radiusMeters)
    {
        if (!HasAnyChunkFiles())
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

    public static bool TrySampleGround(float worldX, float worldZ, out float worldY)
    {
        worldY = WalkSurfaceChunk.NoGround;
        var chunk = GetOrLoadChunk(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
        if (chunk is null)
        {
            return false;
        }

        return chunk.TrySampleBilinear(worldX, worldZ, out worldY) && !float.IsNaN(worldY);
    }

    public static bool TrySampleOutdoorSpawn(float worldX, float worldZ, out float worldY)
    {
        worldY = WalkSurfaceChunk.NoGround;
        if (!HasOutdoorSpawnChannel)
        {
            return false;
        }

        var chunk = GetOrLoadChunk(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
        return chunk is not null && chunk.TrySampleOutdoorSpawn(worldX, worldZ, out worldY) && !float.IsNaN(worldY);
    }

    public static bool IsSpawnFootprintAcceptable(float worldX, float worldZ)
    {
        if (!HasAnyChunkFiles())
        {
            return true;
        }

        if (HasOutdoorSpawnChannel)
        {
            return IsOutdoorSpawnFootprintAcceptable(worldX, worldZ);
        }

        if (IsBlocked(worldX, worldZ))
        {
            return false;
        }

        var radius = MobSpawnBodyRadiusMeters;
        foreach (var (offsetX, offsetZ) in MobBodyDiskOffsets)
        {
            if (IsBlocked(worldX + offsetX * radius, worldZ + offsetZ * radius))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsOutdoorSpawnFootprintAcceptable(float worldX, float worldZ)
    {
        if (!IsOutdoorSpawnAllowed(worldX, worldZ))
        {
            return false;
        }

        var radius = MobSpawnBodyRadiusMeters;
        foreach (var (offsetX, offsetZ) in MobBodyDiskOffsets)
        {
            if (!IsOutdoorSpawnAllowed(worldX + offsetX * radius, worldZ + offsetZ * radius))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TrySampleSpawnGround(float worldX, float worldZ, out float worldY)
    {
        if (HasOutdoorSpawnChannel && TrySampleOutdoorSpawn(worldX, worldZ, out worldY))
        {
            return true;
        }

        worldY = WalkSurfaceChunk.NoGround;
        var chunk = GetOrLoadChunk(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
        if (chunk is null)
        {
            return false;
        }

        return chunk.TrySampleBilinearForSpawn(worldX, worldZ, out worldY) && !float.IsNaN(worldY);
    }

    public static bool IsBlocked(float worldX, float worldZ)
    {
        if (!HasAnyChunkFiles())
        {
            return false;
        }

        var chunk = GetOrLoadChunk(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
        return chunk is not null && chunk.IsBlockedForPlacement(worldX, worldZ);
    }

    public static bool IsOutdoorSpawnAllowed(float worldX, float worldZ)
    {
        if (!HasOutdoorSpawnChannel)
        {
            return false;
        }

        var chunk = GetOrLoadChunk(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
        return chunk is not null && chunk.IsOutdoorSpawnAllowed(worldX, worldZ);
    }

    public static void CollectOutdoorSpawnCandidates(
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        List<(float X, float Z, float Y)> candidates)
    {
        if (!HasOutdoorSpawnChannel)
        {
            return;
        }

        var minChunkX = FloorChunkIndex(centerWorldX - radiusMeters);
        var maxChunkX = FloorChunkIndex(centerWorldX + radiusMeters);
        var minChunkZ = FloorChunkIndex(centerWorldZ - radiusMeters);
        var maxChunkZ = FloorChunkIndex(centerWorldZ + radiusMeters);
        for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
        {
            for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
            {
                var chunk = GetOrLoadChunk(chunkX, chunkZ);
                chunk?.CollectOutdoorSpawnCandidates(centerWorldX, centerWorldZ, radiusMeters, candidates);
            }
        }
    }

    private static WalkSurfaceChunk? GetOrLoadChunk(int chunkX, int chunkZ)
    {
        var key = (chunkX, chunkZ);
        if (Loaded.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var path = WalkSurfaceChunk.BuildAbsolutePath(chunkX, chunkZ, DirectoryResourcePath);
        if (!WalkSurfaceChunk.TryLoad(path, out var chunk))
        {
            Loaded[key] = null;
            return null;
        }

        if (chunk!.HasOutdoorSpawnChannel)
        {
            HasOutdoorSpawnChannel = true;
        }

        Loaded[key] = chunk;
        return chunk;
    }

    private static int FloorChunkIndex(float worldCoordinate)
    {
        return (int)Mathf.Floor(worldCoordinate / ChunkSizeMeters);
    }
}
