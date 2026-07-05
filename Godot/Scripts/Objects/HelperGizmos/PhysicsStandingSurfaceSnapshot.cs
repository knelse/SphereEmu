using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.Fill;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Captures outdoor standing surfaces from the live physics world during editor/headless bakes.
///     Intended for a dedicated physics bake scene (terrain + StaticBody colliders on all terrain objects).
/// </summary>
public sealed class PhysicsStandingSurfaceSnapshot
{
    private readonly Dictionary<(int X, int Z), float> _heights;
    private readonly float _quantizeScale;

    private PhysicsStandingSurfaceSnapshot(Dictionary<(int X, int Z), float> heights, float quantizeScale)
    {
        _heights = heights;
        _quantizeScale = quantizeScale;
    }

    public int SampleCount => _heights.Count;

    /// <summary>
    ///     Returns null when the scene has no usable physics hits (caller should use mesh fallback).
    /// </summary>
    public static PhysicsStandingSurfaceSnapshot? TryCapture(
        Node3D contextNode,
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        float sampleSpacingMeters = -1f)
    {
        var world = contextNode.GetWorld3D();
        if (world is null)
        {
            return null;
        }

        var spacing = sampleSpacingMeters > 0f
            ? sampleSpacingMeters
            : OutdoorFieldConfig.MinSlotSeparationMeters;
        var quantizeScale = 1f / spacing;
        var heights = new Dictionary<(int X, int Z), float>();
        var extent = Mathf.CeilToInt(radiusMeters / spacing);
        var radiusSq = radiusMeters * radiusMeters;

        var terrain = MonsterSpawnGroundQuery.TryResolveTerrainGridMap(contextNode);

        for (var z = -extent; z <= extent; z++)
        {
            for (var x = -extent; x <= extent; x++)
            {
                var worldX = centerWorldX + x * spacing;
                var worldZ = centerWorldZ + z * spacing;
                var dx = worldX - centerWorldX;
                var dz = worldZ - centerWorldZ;
                if (dx * dx + dz * dz > radiusSq)
                {
                    continue;
                }

                float worldY;
                if (terrain is not null)
                {
                    var sample = PhysicsStandingSurfaceSampler.SampleCell(world, terrain, worldX, worldZ);
                    if (!sample.IsWalkable)
                    {
                        continue;
                    }

                    worldY = sample.StandingY;
                }
                else if (!MonsterSpawnGroundQuery.TrySamplePhysicsStandingY(contextNode, worldX, worldZ, out worldY))
                {
                    continue;
                }

                heights[Quantize(worldX, worldZ, quantizeScale)] = worldY;
            }
        }

        return heights.Count == 0 ? null : new PhysicsStandingSurfaceSnapshot(heights, quantizeScale);
    }

    public TerrainMeshHeightSnapshot? ToTerrainMeshSnapshot()
        => SampleCount == 0 ? null : TerrainMeshHeightSnapshot.FromHeights(_heights, _quantizeScale);

    public bool TrySample(float worldX, float worldZ, out float worldY)
        => _heights.TryGetValue(Quantize(worldX, worldZ, _quantizeScale), out worldY);

    private static (int X, int Z) Quantize(float worldX, float worldZ, float quantizeScale)
        => ((int)Mathf.Round(worldX * quantizeScale), (int)Mathf.Round(worldZ * quantizeScale));
}
