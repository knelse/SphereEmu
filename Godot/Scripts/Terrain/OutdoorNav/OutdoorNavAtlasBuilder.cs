using System.IO;
using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.OutdoorNav;

/// <summary>
///     Builds coarse outdoor nav chunks from v4 walk-surface chunks.
/// </summary>
public static class OutdoorNavAtlasBuilder
{
    public const string DefaultOutputDirectory = "res://Godot/Terrain/NavData";
    public const float SampleSpacingMeters = 1f;

    public static int BuildFromWalkDirectory(
        string walkDirectoryResourcePath = WalkSurfaceAtlasBuilder.DefaultOutputDirectory,
        string navDirectoryResourcePath = DefaultOutputDirectory)
    {
        var walkDirectory = ProjectSettings.GlobalizePath(walkDirectoryResourcePath);
        if (!Directory.Exists(walkDirectory))
        {
            GD.PushWarning($"OutdoorNavAtlasBuilder: walk directory not found: {walkDirectoryResourcePath}");
            return 0;
        }

        EnsureOutputDirectory(navDirectoryResourcePath);
        var saved = 0;
        foreach (var file in Directory.GetFiles(walkDirectory, "chunk_*.bin"))
        {
            if (!WalkSurfaceChunk.TryLoad(file, out var walkChunk) || walkChunk is null)
            {
                continue;
            }

            if (!walkChunk.HasOutdoorSpawnChannel)
            {
                GD.PushWarning($"OutdoorNavAtlasBuilder: skipping '{file}' — no outdoor spawn channel (rebake walk v4 first).");
                continue;
            }

            var navChunk = BuildFromWalkChunk(walkChunk);
            var chunkX = (int)Mathf.Floor(walkChunk.OriginX / WalkSurfaceAtlasBuilder.ChunkSizeMeters);
            var chunkZ = (int)Mathf.Floor(walkChunk.OriginZ / WalkSurfaceAtlasBuilder.ChunkSizeMeters);
            var path = OutdoorNavChunk.BuildAbsolutePath(chunkX, chunkZ, navDirectoryResourcePath);
            navChunk.SaveAtomic(path);
            saved++;
        }

        OutdoorNavCache.Invalidate();
        GD.Print($"OutdoorNavAtlasBuilder: wrote {saved} nav chunk(s) to '{navDirectoryResourcePath}'.");
        return saved;
    }

    public static OutdoorNavChunk BuildFromWalkChunk(WalkSurfaceChunk walkChunk)
    {
        var spacing = SampleSpacingMeters;
        var width = Mathf.CeilToInt(WalkSurfaceAtlasBuilder.ChunkSizeMeters / spacing) + 1;
        var height = width;
        var walkable = new byte[width * height];
        var terrainY = new float[width * height];
        for (var i = 0; i < terrainY.Length; i++)
        {
            terrainY[i] = OutdoorNavChunk.NoGround;
        }

        for (var z = 0; z < height; z++)
        {
            for (var x = 0; x < width; x++)
            {
                var worldX = walkChunk.OriginX + x * spacing;
                var worldZ = walkChunk.OriginZ + z * spacing;
                var index = z * width + x;
                if (!IsOutdoorWalkableAt(walkChunk, worldX, worldZ, out var y))
                {
                    continue;
                }

                walkable[index] = 1;
                terrainY[index] = y;
            }
        }

        return new OutdoorNavChunk(walkChunk.OriginX, walkChunk.OriginZ, spacing, width, height, walkable, terrainY);
    }

    private static bool IsOutdoorWalkableAt(WalkSurfaceChunk walkChunk, float worldX, float worldZ, out float terrainY)
    {
        terrainY = OutdoorNavChunk.NoGround;
        if (!walkChunk.IsOutdoorSpawnAllowed(worldX, worldZ))
        {
            return false;
        }

        return walkChunk.TrySampleOutdoorSpawn(worldX, worldZ, out terrainY);
    }

    private static void EnsureOutputDirectory(string outputDirectoryResourcePath)
    {
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(outputDirectoryResourcePath));
    }
}
