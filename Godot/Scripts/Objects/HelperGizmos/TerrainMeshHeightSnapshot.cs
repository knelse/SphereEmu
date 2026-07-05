using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.Fill;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Main-thread snapshot of outdoor standing surfaces for spawn-slot bakes when the walk atlas
///     is missing terrain or has false-positive blocked stamps. Uses object mesh raycasts today;
///     prefers physics when a collider bake scene is present.
/// </summary>
public sealed class TerrainMeshHeightSnapshot
{
    private readonly Dictionary<(int X, int Z), float> _heights;
    private readonly float _quantizeScale;

    private TerrainMeshHeightSnapshot(Dictionary<(int X, int Z), float> heights, float quantizeScale)
    {
        _heights = heights;
        _quantizeScale = quantizeScale;
    }

    public int SampleCount => _heights.Count;

    public static TerrainMeshHeightSnapshot? TryCapture(
        Node3D contextNode,
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        float sampleSpacingMeters = -1f)
    {
        if (TerrainPhysicsColliderBuilder.HasColliderRoot(contextNode))
        {
            var physicsSnapshot = PhysicsStandingSurfaceSnapshot.TryCapture(
                contextNode,
                centerWorldX,
                centerWorldZ,
                radiusMeters,
                sampleSpacingMeters);
            if (physicsSnapshot?.ToTerrainMeshSnapshot() is { } fromPhysics)
            {
                return fromPhysics;
            }
        }

        var spacing = sampleSpacingMeters > 0f
            ? sampleSpacingMeters
            : OutdoorFieldConfig.MinSlotSeparationMeters;

        var terrain = MonsterSpawnGroundQuery.TryResolveTerrainGridMap(contextNode);
        if (terrain is null)
        {
            return null;
        }

        var quantizeScale = 1f / spacing;
        var heights = new Dictionary<(int X, int Z), float>();
        var extent = Mathf.CeilToInt(radiusMeters / spacing);
        var radiusSq = radiusMeters * radiusMeters;
        var objectSurfaces = OutdoorObjectSurfaceIndex.TryBuild(
            centerWorldX,
            centerWorldZ,
            radiusMeters);

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

                if (!TerrainWalkMeshRaycast.TrySampleTerrainTopY(terrain, worldX, worldZ, out var worldY))
                {
                    continue;
                }

                if (objectSurfaces is not null
                    && objectSurfaces.HasWalkSurfaceAbove(
                        worldX,
                        worldZ,
                        worldY,
                        OutdoorFieldConfig.MaxOutdoorSpawnAboveTerrainMeters))
                {
                    continue;
                }

                if (MonsterSpawnGroundQuery.IsStandingSurfaceAlignedWithTerrainMesh(
                        contextNode,
                        worldX,
                        worldZ,
                        worldY))
                {
                    heights[Quantize(worldX, worldZ, quantizeScale)] = worldY;
                }
            }
        }

        return heights.Count == 0 ? null : new TerrainMeshHeightSnapshot(heights, quantizeScale);
    }

    internal static TerrainMeshHeightSnapshot FromHeights(
        Dictionary<(int X, int Z), float> heights,
        float quantizeScale)
        => new(heights, quantizeScale);

    public bool TrySample(float worldX, float worldZ, out float worldY)
    {
        return _heights.TryGetValue(Quantize(worldX, worldZ, _quantizeScale), out worldY);
    }

    public void CollectSamplesInRadius(
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        List<(float X, float Z, float Y)> samples)
    {
        var radiusSq = radiusMeters * radiusMeters;
        foreach (var (key, worldY) in _heights)
        {
            var worldX = key.X / _quantizeScale;
            var worldZ = key.Z / _quantizeScale;
            var dx = worldX - centerWorldX;
            var dz = worldZ - centerWorldZ;
            if (dx * dx + dz * dz > radiusSq)
            {
                continue;
            }

            samples.Add((worldX, worldZ, worldY));
        }
    }

    private static (int X, int Z) Quantize(float worldX, float worldZ, float quantizeScale)
        => ((int)Mathf.Round(worldX * quantizeScale), (int)Mathf.Round(worldZ * quantizeScale));
}
