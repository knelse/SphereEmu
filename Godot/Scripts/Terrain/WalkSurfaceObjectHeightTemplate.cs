using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Pre-sampled top walkable heights for one terrain object mesh in object-root local space.
/// </summary>
internal sealed class WalkSurfaceObjectHeightTemplate
{
    private readonly float _halfExtentX;
    private readonly float _halfExtentZ;
    private readonly float _sampleStep;
    private readonly int _gridCountX;
    private readonly int _gridCountZ;
    private readonly float[] _localY;

    private WalkSurfaceObjectHeightTemplate(
        float halfExtentX,
        float halfExtentZ,
        float sampleStep,
        int gridCountX,
        int gridCountZ,
        float[] localY)
    {
        _halfExtentX = halfExtentX;
        _halfExtentZ = halfExtentZ;
        _sampleStep = sampleStep;
        _gridCountX = gridCountX;
        _gridCountZ = gridCountZ;
        _localY = localY;
    }

    public static WalkSurfaceObjectHeightTemplate? Build(
        IReadOnlyList<WalkSurfaceObjectMeshPart> meshParts,
        float halfExtentX,
        float halfExtentZ,
        float sampleStep)
    {
        if (halfExtentX <= 0f || halfExtentZ <= 0f || meshParts.Count == 0)
        {
            return null;
        }

        var gridCountX = Mathf.CeilToInt((halfExtentX * 2f) / sampleStep) + 1;
        var gridCountZ = Mathf.CeilToInt((halfExtentZ * 2f) / sampleStep) + 1;
        var sampleCount = gridCountX * gridCountZ;
        var localY = new float[sampleCount];
        Array.Fill(localY, WalkSurfaceChunk.NoGround);

        var rootTransform = Transform3D.Identity;
        var hits = 0;

        Parallel.For(0, sampleCount, index =>
        {
            var iz = index / gridCountX;
            var ix = index % gridCountX;
            var lz = -halfExtentZ + iz * sampleStep;
            var lx = -halfExtentX + ix * sampleStep;
            if (!TrySampleTopWalkableY(meshParts, rootTransform, lx, lz, out var y))
            {
                return;
            }

            localY[index] = y;
            Interlocked.Increment(ref hits);
        });

        return hits == 0
            ? null
            : new WalkSurfaceObjectHeightTemplate(halfExtentX, halfExtentZ, sampleStep, gridCountX, gridCountZ, localY);
    }

    public int WritePlacementSamples(
        Transform3D placementWorldTransform,
        float halfExtentX,
        float halfExtentZ,
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders)
    {
        var origin = placementWorldTransform.Origin;
        var yaw = placementWorldTransform.Basis.GetEuler(EulerOrder.Yxz).Y;
        var cos = Mathf.Cos(yaw);
        var sin = Mathf.Sin(yaw);
        var sampleCount = _gridCountX * _gridCountZ;
        var written = 0;

        Parallel.For(0, sampleCount, () => 0, (index, _, localWritten) =>
        {
            var y = _localY[index];
            if (float.IsNaN(y))
            {
                return localWritten;
            }

            var iz = index / _gridCountX;
            var ix = index % _gridCountX;
            var lz = -_halfExtentZ + iz * _sampleStep;
            var lx = -_halfExtentX + ix * _sampleStep;
            var horizontalWorld = placementWorldTransform * new Vector3(lx, 0f, lz);
            if (!IsInsideOrientedFootprint(
                    horizontalWorld.X,
                    horizontalWorld.Z,
                    origin.X,
                    origin.Z,
                    halfExtentX,
                    halfExtentZ,
                    cos,
                    sin))
            {
                return localWritten;
            }

            var worldPoint = placementWorldTransform * new Vector3(lx, y, lz);
            var builder = WalkSurfaceAtlasBuilder.GetOrCreateBuilder(builders, worldPoint.X, worldPoint.Z);
            if (builder.IsBlockedAtWorld(worldPoint.X, worldPoint.Z))
            {
                return localWritten;
            }

            return builder.TrySetWalkableWorldSample(worldPoint.X, worldPoint.Z, worldPoint.Y)
                ? localWritten + 1
                : localWritten;
        }, localWritten => Interlocked.Add(ref written, localWritten));

        return written;
    }

    private static bool TrySampleTopWalkableY(
        IReadOnlyList<WalkSurfaceObjectMeshPart> meshParts,
        Transform3D placementWorldTransform,
        float localX,
        float localZ,
        out float localY)
    {
        localY = default;
        var fromWorld = placementWorldTransform * new Vector3(localX, TerrainWalkMeshRaycast.RayTopY, localZ);
        var toWorld = placementWorldTransform * new Vector3(localX, TerrainWalkMeshRaycast.RayBottomY, localZ);
        var found = false;
        var bestWorldY = float.MinValue;

        foreach (var part in meshParts)
        {
            var meshGlobal = placementWorldTransform * part.MeshLocalToRoot;
            if (!TerrainMeshRaycastCache.TryRaycastMeshTopWalkableSurface(
                    part.Mesh,
                    meshGlobal,
                    fromWorld,
                    toWorld,
                    TerrainMeshRaycastCache.DefaultMinWalkableNormalY,
                    out var hitWorld))
            {
                continue;
            }

            if (hitWorld.Y <= bestWorldY)
            {
                continue;
            }

            bestWorldY = hitWorld.Y;
            var hitLocal = placementWorldTransform.AffineInverse() * hitWorld;
            localY = hitLocal.Y;
            found = true;
        }

        return found;
    }

    private static bool IsInsideOrientedFootprint(
        float worldX,
        float worldZ,
        float originX,
        float originZ,
        float halfExtentX,
        float halfExtentZ,
        float cos,
        float sin)
    {
        var dx = worldX - originX;
        var dz = worldZ - originZ;
        var localX = dx * cos + dz * sin;
        var localZ = -dx * sin + dz * cos;
        return Mathf.Abs(localX) <= halfExtentX && Mathf.Abs(localZ) <= halfExtentZ;
    }
}

internal static class WalkSurfaceObjectHeightTemplateCache
{
    private static readonly Dictionary<string, WalkSurfaceObjectHeightTemplate?> Templates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static void Clear()
    {
        lock (CacheLock)
        {
            Templates.Clear();
        }
    }

    public static WalkSurfaceObjectHeightTemplate? GetOrBuild(
        string objectName,
        string modelsDirectory,
        TerrainObjectWalkCategory category,
        float sampleSpacingMeters)
    {
        lock (CacheLock)
        {
            if (Templates.TryGetValue(objectName, out var cached))
            {
                return cached;
            }
        }

        WalkSurfaceObjectHeightTemplate? template = null;
        if (WalkSurfaceObjectMeshCache.TryGetMeshParts(objectName, modelsDirectory, out var meshParts)
            && WalkSurfaceModelBoundsCache.TryGetUnscaledFootprintHalfExtents(
                objectName,
                modelsDirectory,
                category,
                out var halfExtentX,
                out var halfExtentZ))
        {
            PrewarmMeshParts(meshParts);
            template = WalkSurfaceObjectHeightTemplate.Build(meshParts, halfExtentX, halfExtentZ, sampleSpacingMeters);
        }

        lock (CacheLock)
        {
            if (!Templates.TryGetValue(objectName, out var existing))
            {
                Templates[objectName] = template;
                return template;
            }

            return existing;
        }
    }

    public static Dictionary<string, WalkSurfaceObjectHeightTemplate?> WarmAll(
        IEnumerable<TerrainObjectPlacement> placements,
        string modelsDirectory,
        float sampleSpacingMeters)
    {
        var uniqueNames = new Dictionary<string, TerrainObjectWalkCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in placements)
        {
            if (!WalkSurfaceObjectHeightBaker.UsesHeightTemplate(placement.Category))
            {
                continue;
            }

            uniqueNames.TryAdd(placement.ObjectName, placement.Category);
        }

        var buildJobs = new List<ObjectTemplateBuildJob>(uniqueNames.Count);
        foreach (var (objectName, category) in uniqueNames)
        {
            if (!WalkSurfaceObjectMeshCache.TryGetMeshParts(objectName, modelsDirectory, out var meshParts)
                || !WalkSurfaceModelBoundsCache.TryGetUnscaledFootprintHalfExtents(
                    objectName,
                    modelsDirectory,
                    category,
                    out var halfExtentX,
                    out var halfExtentZ))
            {
                continue;
            }

            PrewarmMeshParts(meshParts);
            buildJobs.Add(new ObjectTemplateBuildJob(objectName, meshParts, halfExtentX, halfExtentZ));
        }

        var results = new ConcurrentDictionary<string, WalkSurfaceObjectHeightTemplate?>(StringComparer.OrdinalIgnoreCase);
        var built = 0;
        var progressLogLock = new object();

        Parallel.ForEach(buildJobs, job =>
        {
            results[job.ObjectName] = WalkSurfaceObjectHeightTemplate.Build(
                job.MeshParts,
                job.HalfExtentX,
                job.HalfExtentZ,
                sampleSpacingMeters);

            var done = Interlocked.Increment(ref built);
            if (done == 1 || done % 25 == 0 || done == buildJobs.Count)
            {
                lock (progressLogLock)
                {
                    GD.Print($"WalkSurfaceObjectHeightBaker: object template {done}/{buildJobs.Count} ({job.ObjectName}).");
                }
            }
        });

        foreach (var objectName in uniqueNames.Keys)
        {
            results.TryAdd(objectName, null);
        }

        lock (CacheLock)
        {
            foreach (var (objectName, template) in results)
            {
                Templates[objectName] = template;
            }
        }

        return new Dictionary<string, WalkSurfaceObjectHeightTemplate?>(results, StringComparer.OrdinalIgnoreCase);
    }

    private static void PrewarmMeshParts(IReadOnlyList<WalkSurfaceObjectMeshPart> meshParts)
    {
        foreach (var part in meshParts)
        {
            TerrainMeshRaycastCache.PrewarmMesh(part.Mesh);
        }
    }

    private readonly record struct ObjectTemplateBuildJob(
        string ObjectName,
        IReadOnlyList<WalkSurfaceObjectMeshPart> MeshParts,
        float HalfExtentX,
        float HalfExtentZ);
}
