using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Stamps precomputed walkable heights from terrain object meshes into walk atlas builders.
///     Plants and rocks use blocked footprints only; buildings and town objects use height templates.
/// </summary>
public static class WalkSurfaceObjectHeightBaker
{
    public static int BakePlacements(
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders,
        IReadOnlyList<TerrainObjectPlacement> placements,
        string modelsDirectory,
        float sampleSpacingMeters)
    {
        WalkSurfaceObjectMeshCache.Clear();
        WalkSurfaceObjectHeightTemplateCache.Clear();

        var heightPlacements = new List<TerrainObjectPlacement>();
        foreach (var placement in placements)
        {
            if (UsesHeightTemplate(placement.Category))
            {
                heightPlacements.Add(placement);
            }
        }

        if (heightPlacements.Count == 0)
        {
            GD.Print("WalkSurfaceObjectHeightBaker: no building/town placements require height templates.");
            return 0;
        }

        var templates = WalkSurfaceObjectHeightTemplateCache.WarmAll(
            heightPlacements,
            modelsDirectory,
            sampleSpacingMeters);

        var stampJobs = new List<HeightStampJob>(heightPlacements.Count);
        foreach (var placement in heightPlacements)
        {
            if (!templates.TryGetValue(placement.ObjectName, out var template) || template is null)
            {
                continue;
            }

            if (!WalkSurfaceModelBoundsCache.TryResolveFootprintHalfExtents(
                    placement,
                    modelsDirectory,
                    out var halfExtentX,
                    out var halfExtentZ))
            {
                continue;
            }

            stampJobs.Add(new HeightStampJob(placement, template, halfExtentX, halfExtentZ));
        }

        var samplesWritten = 0;
        var processed = 0;
        var progressLogLock = new object();

        Parallel.ForEach(stampJobs, job =>
        {
            var written = job.Template.WritePlacementSamples(
                job.Placement.WorldTransform,
                job.HalfExtentX,
                job.HalfExtentZ,
                builders);
            Interlocked.Add(ref samplesWritten, written);

            var done = Interlocked.Increment(ref processed);
            if (done % 500 == 0 || done == stampJobs.Count)
            {
                lock (progressLogLock)
                {
                    GD.Print($"WalkSurfaceObjectHeightBaker: {done}/{stampJobs.Count} height placement(s)...");
                }
            }
        });

        return samplesWritten;
    }

    internal static bool UsesHeightTemplate(TerrainObjectWalkCategory category)
    {
        return category is TerrainObjectWalkCategory.ExtraInstanced or TerrainObjectWalkCategory.Other;
    }

    private readonly record struct HeightStampJob(
        TerrainObjectPlacement Placement,
        WalkSurfaceObjectHeightTemplate Template,
        float HalfExtentX,
        float HalfExtentZ);
}
