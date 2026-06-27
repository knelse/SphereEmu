using System.Collections.Generic;
using System.IO;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Lazy loader for outdoor walk-surface height atlas chunks.
/// </summary>
public static class WalkSurfaceCache
{
    private static readonly object LoadLock = new();
    private static readonly Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunk?> Loaded = new();

    public static string DirectoryResourcePath { get; set; } = WalkSurfaceAtlasBuilder.DefaultOutputDirectory;

    public static float ChunkSizeMeters => WalkSurfaceAtlasBuilder.ChunkSizeMeters;

    public static void Invalidate()
    {
        lock (LoadLock)
        {
            Loaded.Clear();
        }
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

    public static bool TrySampleGround(float worldX, float worldZ, out float worldY)
    {
        worldY = WalkSurfaceChunk.NoGround;
        var chunkX = FloorChunkIndex(worldX);
        var chunkZ = FloorChunkIndex(worldZ);
        var chunk = GetOrLoadChunk(chunkX, chunkZ);
        if (chunk is null)
        {
            return false;
        }

        return chunk.TrySampleBilinear(worldX, worldZ, out worldY) && !float.IsNaN(worldY);
    }

    private static WalkSurfaceChunk? GetOrLoadChunk(int chunkX, int chunkZ)
    {
        lock (LoadLock)
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

            Loaded[key] = chunk;
            return chunk;
        }
    }

    private static int FloorChunkIndex(float worldCoordinate)
    {
        return (int)Mathf.Floor(worldCoordinate / ChunkSizeMeters);
    }
}
