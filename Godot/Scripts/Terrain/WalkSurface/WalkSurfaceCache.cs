using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Godot;
using SphServer.Godot.Scripts.Terrain;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Lazy loader for outdoor walk-surface height atlas chunks.
/// </summary>
public static class WalkSurfaceCache
{
    private static readonly ConcurrentDictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunk?> Loaded = new();

    public static string DirectoryResourcePath { get; set; } = WalkSurfaceAtlasBuilder.DefaultOutputDirectory;

    public static float ChunkSizeMeters => WalkSurfaceAtlasBuilder.ChunkSizeMeters;

    public static float SampleSpacingMeters => WalkSurfaceAtlasBuilder.SampleSpacingMeters;

    public static float MobSpawnBodyRadiusMeters => OutdoorFieldConfig.MobBodyRadiusMeters;

    /// <summary>True when loaded chunks contain a pre-baked walkable field (format v4).</summary>
    public static bool HasWalkableField { get; private set; }

    /// <summary>Deprecated alias for <see cref="HasWalkableField" />.</summary>
    public static bool HasOutdoorSpawnChannel => HasWalkableField;

    public static void Invalidate()
    {
        Loaded.Clear();
        HasWalkableField = false;
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

    public static bool IsWalkableAt(float worldX, float worldZ)
    {
        if (!HasWalkableField)
        {
            return !IsBlocked(worldX, worldZ) && TrySampleGround(worldX, worldZ, out _);
        }

        var chunk = GetOrLoadChunk(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
        return chunk is not null && chunk.IsWalkableAt(worldX, worldZ);
    }

    public static bool TrySampleWalkableGround(float worldX, float worldZ, out float worldY)
    {
        worldY = WalkSurfaceChunk.NoGround;
        if (!HasWalkableField)
        {
            return TrySampleSpawnGround(worldX, worldZ, out worldY);
        }

        var chunk = GetOrLoadChunk(FloorChunkIndex(worldX), FloorChunkIndex(worldZ));
        return chunk is not null && chunk.TrySampleWalkableGround(worldX, worldZ, out worldY) && !float.IsNaN(worldY);
    }

    public static bool TrySampleOutdoorSpawn(float worldX, float worldZ, out float worldY)
    {
        return TrySampleWalkableGround(worldX, worldZ, out worldY);
    }

    public static bool IsSpawnFootprintAcceptable(float worldX, float worldZ)
    {
        if (!HasAnyChunkFiles())
        {
            return true;
        }

        if (!IsWalkableAt(worldX, worldZ))
        {
            return false;
        }

        var radius = MobSpawnBodyRadiusMeters;
        foreach (var (offsetX, offsetZ) in WalkSurfaceMobBodyDisk.Offsets)
        {
            if (!IsWalkableAt(worldX + offsetX * radius, worldZ + offsetZ * radius))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsOutdoorSpawnFootprintAcceptable(float worldX, float worldZ)
    {
        return IsSpawnFootprintAcceptable(worldX, worldZ);
    }

    public static bool TrySampleSpawnGround(float worldX, float worldZ, out float worldY)
    {
        if (HasWalkableField && TrySampleWalkableGround(worldX, worldZ, out worldY))
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
        return IsWalkableAt(worldX, worldZ);
    }

    public static void CollectWalkableCandidates(
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        List<(float X, float Z, float Y)> candidates)
    {
        if (!HasWalkableField)
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
                chunk?.CollectWalkableCandidates(centerWorldX, centerWorldZ, radiusMeters, candidates);
            }
        }
    }

    public static void CollectOutdoorSpawnCandidates(
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        List<(float X, float Z, float Y)> candidates)
    {
        CollectWalkableCandidates(centerWorldX, centerWorldZ, radiusMeters, candidates);
    }

    public static float MeasureLocalOpenness(float worldX, float worldZ, float radiusMeters)
    {
        if (!HasAnyChunkFiles())
        {
            return 1f;
        }

        var spacing = SampleSpacingMeters;
        var ringSteps = Mathf.Max(1, Mathf.CeilToInt(radiusMeters / spacing));
        var total = 0;
        var walkable = 0;
        for (var z = -ringSteps; z <= ringSteps; z++)
        {
            for (var x = -ringSteps; x <= ringSteps; x++)
            {
                var probeX = worldX + x * spacing;
                var probeZ = worldZ + z * spacing;
                var dx = probeX - worldX;
                var dz = probeZ - worldZ;
                if (dx * dx + dz * dz > radiusMeters * radiusMeters)
                {
                    continue;
                }

                total++;
                if (IsWalkableAt(probeX, probeZ))
                {
                    walkable++;
                }
            }
        }

        return total == 0 ? 0f : (float)walkable / total;
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

        if (chunk!.HasWalkableField)
        {
            HasWalkableField = true;
        }

        Loaded[key] = chunk;
        return chunk;
    }

    private static int FloorChunkIndex(float worldCoordinate)
    {
        return (int)Mathf.Floor(worldCoordinate / ChunkSizeMeters);
    }
}
