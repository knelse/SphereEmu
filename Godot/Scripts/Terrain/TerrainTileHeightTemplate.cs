using System;
using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Pre-sampled top-surface heights for one GridMap tile mesh in cell-local space (center at origin).
/// </summary>
internal sealed class TerrainTileHeightTemplate
{
    private readonly float _halfTile;
    private readonly float _sampleStep;
    private readonly int _gridCount;
    private readonly float[] _cellLocalY;

    private TerrainTileHeightTemplate(float halfTile, float sampleStep, int gridCount, float[] cellLocalY)
    {
        _halfTile = halfTile;
        _sampleStep = sampleStep;
        _gridCount = gridCount;
        _cellLocalY = cellLocalY;
    }

    public static TerrainTileHeightTemplate? Build(Mesh mesh, Basis cellBasis, float halfTile, float sampleStep)
    {
        var gridCount = Mathf.CeilToInt((halfTile * 2f) / sampleStep) + 1;
        var cellLocalY = new float[gridCount * gridCount];
        Array.Fill(cellLocalY, WalkSurfaceChunk.NoGround);

        var cellTransform = new Transform3D(cellBasis, Vector3.Zero);
        var hits = 0;

        for (var iz = 0; iz < gridCount; iz++)
        {
            var lz = -halfTile + iz * sampleStep;
            for (var ix = 0; ix < gridCount; ix++)
            {
                var lx = -halfTile + ix * sampleStep;
                var fromWorld = cellTransform * new Vector3(lx, TerrainWalkMeshRaycast.RayTopY, lz);
                var toWorld = cellTransform * new Vector3(lx, TerrainWalkMeshRaycast.RayBottomY, lz);
                if (!TerrainMeshRaycastCache.TryRaycastMesh(mesh, cellTransform, fromWorld, toWorld, out var hitWorld, out _))
                {
                    continue;
                }

                var hitLocal = cellTransform.AffineInverse() * hitWorld;
                cellLocalY[iz * gridCount + ix] = hitLocal.Y;
                hits++;
            }
        }

        return hits == 0 ? null : new TerrainTileHeightTemplate(halfTile, sampleStep, gridCount, cellLocalY);
    }

    public int WriteCellSamples(
        Transform3D terrainGlobalTransform,
        Basis cellBasis,
        Vector3 cellCenterLocal,
        Action<float, float, float> writeWorldSample)
    {
        var cellTransform = terrainGlobalTransform * new Transform3D(cellBasis, cellCenterLocal);
        var written = 0;

        for (var iz = 0; iz < _gridCount; iz++)
        {
            var lz = -_halfTile + iz * _sampleStep;
            for (var ix = 0; ix < _gridCount; ix++)
            {
                var y = _cellLocalY[iz * _gridCount + ix];
                if (float.IsNaN(y))
                {
                    continue;
                }

                var lx = -_halfTile + ix * _sampleStep;
                var world = cellTransform * new Vector3(lx, y, lz);
                writeWorldSample(world.X, world.Y, world.Z);
                written++;
            }
        }

        return written;
    }
}

internal static class TerrainTileHeightTemplateCache
{
    private static readonly Dictionary<int, TerrainTileHeightTemplate?> Templates = new();
    private static readonly object CacheLock = new();

    public static void Clear()
    {
        lock (CacheLock)
        {
            Templates.Clear();
        }
    }

    public static TerrainTileHeightTemplate? GetOrBuild(
        Mesh mesh,
        int itemId,
        Basis cellBasis,
        float halfTile,
        float sampleStep)
    {
        lock (CacheLock)
        {
            if (Templates.TryGetValue(itemId, out var cached))
            {
                return cached;
            }
        }

        var template = TerrainTileHeightTemplate.Build(mesh, cellBasis, halfTile, sampleStep);
        lock (CacheLock)
        {
            if (!Templates.TryGetValue(itemId, out var existing))
            {
                Templates[itemId] = template;
                return template;
            }

            return existing;
        }
    }

    public static Dictionary<int, TerrainTileHeightTemplate?> WarmAll(
        GridMap terrain,
        IEnumerable<Vector3I> usedCells,
        float halfTile,
        float sampleStep)
    {
        var itemToBasis = new Dictionary<int, Basis>();
        foreach (var cell in usedCells)
        {
            var itemId = terrain.GetCellItem(cell);
            if (itemId < 0 || itemToBasis.ContainsKey(itemId))
            {
                continue;
            }

            itemToBasis[itemId] = terrain.GetCellItemBasis(cell);
        }

        var results = new Dictionary<int, TerrainTileHeightTemplate?>();
        var meshLibrary = terrain.MeshLibrary!;
        var total = itemToBasis.Count;
        var built = 0;

        foreach (var (itemId, basis) in itemToBasis)
        {
            built++;
            var mesh = meshLibrary.GetItemMesh(itemId);
            if (mesh is null)
            {
                results[itemId] = null;
                GD.Print($"WalkSurfaceAtlasBuilder: tile template {built}/{total} (item {itemId}) skipped — no mesh.");
                continue;
            }

            results[itemId] = GetOrBuild(mesh, itemId, basis, halfTile, sampleStep);
            if (built == 1 || built % 5 == 0 || built == total)
            {
                GD.Print($"WalkSurfaceAtlasBuilder: tile template {built}/{total} (item {itemId}).");
            }
        }

        return results;
    }
}
