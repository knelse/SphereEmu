using System.IO;
using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.OutdoorNav;

/// <summary>
///     Builds outdoor nav chunks from v4 walk-surface chunks at the same 0.25 m resolution.
/// </summary>
public static class OutdoorNavAtlasBuilder
{
    public const string DefaultOutputDirectory = "res://Godot/Terrain/NavData";

    public static float SampleSpacingMeters => WalkSurfaceAtlasBuilder.SampleSpacingMeters;

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

            if (!walkChunk.HasWalkableField)
            {
                GD.PushWarning($"OutdoorNavAtlasBuilder: skipping '{file}' — no walkable field (rebake walk v4 first).");
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
        GD.Print($"OutdoorNavAtlasBuilder: wrote {saved} nav chunk(s) at {SampleSpacingMeters:0.##}m to '{navDirectoryResourcePath}'.");
        return saved;
    }

    public static OutdoorNavChunk BuildFromWalkChunk(WalkSurfaceChunk walkChunk)
    {
        var count = walkChunk.Width * walkChunk.Height;
        var walkable = new byte[count];
        var terrainY = new float[count];
        for (var i = 0; i < terrainY.Length; i++)
        {
            terrainY[i] = OutdoorNavChunk.NoGround;
        }

        for (var z = 0; z < walkChunk.Height; z++)
        {
            for (var x = 0; x < walkChunk.Width; x++)
            {
                var worldX = walkChunk.OriginX + x * walkChunk.SampleSpacing;
                var worldZ = walkChunk.OriginZ + z * walkChunk.SampleSpacing;
                var index = z * walkChunk.Width + x;
                if (!walkChunk.IsWalkableAt(worldX, worldZ)
                    || !walkChunk.TrySampleWalkableGround(worldX, worldZ, out var y))
                {
                    continue;
                }

                walkable[index] = 1;
                terrainY[index] = y;
            }
        }

        return new OutdoorNavChunk(
            walkChunk.OriginX,
            walkChunk.OriginZ,
            walkChunk.SampleSpacing,
            walkChunk.Width,
            walkChunk.Height,
            walkable,
            terrainY);
    }

    private static void EnsureOutputDirectory(string outputDirectoryResourcePath)
    {
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(outputDirectoryResourcePath));
    }
}
