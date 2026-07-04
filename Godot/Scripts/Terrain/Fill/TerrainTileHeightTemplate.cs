using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.Fill;

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
        var sampleCount = gridCount * gridCount;
        var cellLocalY = new float[sampleCount];
        Array.Fill(cellLocalY, WalkSurfaceChunk.NoGround);

        var cellTransform = new Transform3D(cellBasis, Vector3.Zero);
        var invCellTransform = cellTransform.AffineInverse();
        var hits = 0;

        Parallel.For(0, sampleCount, index =>
        {
            var iz = index / gridCount;
            var ix = index % gridCount;
            var lz = -halfTile + iz * sampleStep;
            var lx = -halfTile + ix * sampleStep;
            var fromWorld = cellTransform * new Vector3(lx, TerrainWalkMeshRaycast.RayTopY, lz);
            var toWorld = cellTransform * new Vector3(lx, TerrainWalkMeshRaycast.RayBottomY, lz);
            if (!TerrainMeshRaycastCache.TryRaycastMesh(mesh, cellTransform, fromWorld, toWorld, out var hitWorld, out _))
            {
                return;
            }

            var hitLocal = invCellTransform * hitWorld;
            cellLocalY[index] = hitLocal.Y;
            Interlocked.Increment(ref hits);
        });

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

        var meshLibrary = terrain.MeshLibrary!;
        var buildJobs = new List<TileTemplateBuildJob>(itemToBasis.Count);
        foreach (var (itemId, basis) in itemToBasis)
        {
            var mesh = meshLibrary.GetItemMesh(itemId);
            if (mesh is null)
            {
                continue;
            }

            TerrainMeshRaycastCache.PrewarmMesh(mesh);
            buildJobs.Add(new TileTemplateBuildJob(itemId, mesh, basis));
        }

        var results = new ConcurrentDictionary<int, TerrainTileHeightTemplate?>();
        var built = 0;
        var progressLogLock = new object();

        Parallel.ForEach(buildJobs, job =>
        {
            var template = TerrainTileHeightTemplate.Build(job.Mesh, job.Basis, halfTile, sampleStep);
            results[job.ItemId] = template;

            var done = Interlocked.Increment(ref built);
            if (done == 1 || done % 5 == 0 || done == buildJobs.Count)
            {
                lock (progressLogLock)
                {
                    GD.Print($"WalkSurfaceAtlasBuilder: tile template {done}/{buildJobs.Count} (item {job.ItemId}).");
                }
            }
        });

        foreach (var (itemId, _) in itemToBasis)
        {
            if (meshLibrary.GetItemMesh(itemId) is null)
            {
                results.TryAdd(itemId, null);
                GD.Print($"WalkSurfaceAtlasBuilder: tile template skipped — item {itemId} has no mesh.");
            }
        }

        lock (CacheLock)
        {
            foreach (var (itemId, template) in results)
            {
                Templates[itemId] = template;
            }
        }

        var merged = new Dictionary<int, TerrainTileHeightTemplate?>(itemToBasis.Count);
        foreach (var itemId in itemToBasis.Keys)
        {
            merged[itemId] = results.GetValueOrDefault(itemId);
        }

        return merged;
    }

    private readonly record struct TileTemplateBuildJob(int ItemId, Mesh Mesh, Basis Basis);
}
