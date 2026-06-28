using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Top-down mesh raster of blocked XZ cells for one terrain object in object-root local space.
/// </summary>
internal sealed class WalkSurfaceObjectBlockedTemplate
{
    private readonly float _halfExtentX;
    private readonly float _halfExtentZ;
    private readonly float _sampleStep;
    private readonly int _gridCountX;
    private readonly int _gridCountZ;
    private readonly byte[] _blocked;

    private WalkSurfaceObjectBlockedTemplate(
        float halfExtentX,
        float halfExtentZ,
        float sampleStep,
        int gridCountX,
        int gridCountZ,
        byte[] blocked)
    {
        _halfExtentX = halfExtentX;
        _halfExtentZ = halfExtentZ;
        _sampleStep = sampleStep;
        _gridCountX = gridCountX;
        _gridCountZ = gridCountZ;
        _blocked = blocked;
    }

    public static WalkSurfaceObjectBlockedTemplate? Build(
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
        var blocked = new byte[sampleCount];
        var rootTransform = Transform3D.Identity;
        var hits = 0;

        Parallel.For(0, sampleCount, index =>
        {
            var iz = index / gridCountX;
            var ix = index % gridCountX;
            var lz = -halfExtentZ + iz * sampleStep;
            var lx = -halfExtentX + ix * sampleStep;
            if (!TryRaycastAnyMesh(meshParts, rootTransform, lx, lz))
            {
                return;
            }

            blocked[index] = 1;
            Interlocked.Increment(ref hits);
        });

        return hits == 0
            ? null
            : new WalkSurfaceObjectBlockedTemplate(halfExtentX, halfExtentZ, sampleStep, gridCountX, gridCountZ, blocked);
    }

    public int WritePlacementBlocked(
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
        var stamped = 0;

        Parallel.For(0, sampleCount, () => 0, (index, _, localStamped) =>
        {
            if (_blocked[index] == 0)
            {
                return localStamped;
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
                return localStamped;
            }

            var builder = WalkSurfaceAtlasBuilder.GetOrCreateBuilder(builders, horizontalWorld.X, horizontalWorld.Z);
            return builder.TryStampBlockedWorld(horizontalWorld.X, horizontalWorld.Z)
                ? localStamped + 1
                : localStamped;
        }, localStamped => Interlocked.Add(ref stamped, localStamped));

        return stamped;
    }

    private static bool TryRaycastAnyMesh(
        IReadOnlyList<WalkSurfaceObjectMeshPart> meshParts,
        Transform3D placementWorldTransform,
        float localX,
        float localZ)
    {
        var fromWorld = placementWorldTransform * new Vector3(localX, TerrainWalkMeshRaycast.RayTopY, localZ);
        var toWorld = placementWorldTransform * new Vector3(localX, TerrainWalkMeshRaycast.RayBottomY, localZ);

        foreach (var part in meshParts)
        {
            var meshGlobal = placementWorldTransform * part.MeshLocalToRoot;
            if (TerrainMeshRaycastCache.TryRaycastMesh(
                    part.Mesh,
                    meshGlobal,
                    fromWorld,
                    toWorld,
                    out _,
                    out _))
            {
                return true;
            }
        }

        return false;
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

internal static class WalkSurfaceObjectBlockedTemplateCache
{
    private static readonly Dictionary<string, WalkSurfaceObjectBlockedTemplate?> Templates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static void Clear()
    {
        lock (CacheLock)
        {
            Templates.Clear();
        }
    }

    public static Dictionary<string, WalkSurfaceObjectBlockedTemplate?> WarmAll(
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

        var buildJobs = new List<BlockedTemplateBuildJob>(uniqueNames.Count);
        foreach (var (objectName, category) in uniqueNames)
        {
            if (!WalkSurfaceObjectMeshCache.TryGetMeshParts(objectName, modelsDirectory, out var meshParts)
                || !WalkSurfaceModelBoundsCache.TryGetBuildingRasterHalfExtents(
                    objectName,
                    modelsDirectory,
                    category,
                    out var halfExtentX,
                    out var halfExtentZ))
            {
                continue;
            }

            foreach (var part in meshParts)
            {
                TerrainMeshRaycastCache.PrewarmMesh(part.Mesh);
            }
            buildJobs.Add(new BlockedTemplateBuildJob(objectName, meshParts, halfExtentX, halfExtentZ));
        }

        var results = new ConcurrentDictionary<string, WalkSurfaceObjectBlockedTemplate?>(StringComparer.OrdinalIgnoreCase);
        var built = 0;
        var progressLogLock = new object();

        Parallel.ForEach(buildJobs, job =>
        {
            results[job.ObjectName] = WalkSurfaceObjectBlockedTemplate.Build(
                job.MeshParts,
                job.HalfExtentX,
                job.HalfExtentZ,
                sampleSpacingMeters);

            var done = Interlocked.Increment(ref built);
            if (done == 1 || done % 25 == 0 || done == buildJobs.Count)
            {
                lock (progressLogLock)
                {
                    GD.Print($"WalkSurfaceObjectBlockedRaster: template {done}/{buildJobs.Count} ({job.ObjectName}).");
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

        return new Dictionary<string, WalkSurfaceObjectBlockedTemplate?>(results, StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct BlockedTemplateBuildJob(
        string ObjectName,
        IReadOnlyList<WalkSurfaceObjectMeshPart> MeshParts,
        float HalfExtentX,
        float HalfExtentZ);
}

/// <summary>
///     Ray-rasterizes building/town meshes into blocked walk atlas cells at bake time.
/// </summary>
public static class WalkSurfaceObjectBlockedRasterStamper
{
    public static int StampPlacements(
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders,
        IReadOnlyList<TerrainObjectPlacement> placements,
        string modelsDirectory,
        float sampleSpacingMeters)
    {
        WalkSurfaceObjectMeshCache.Clear();
        WalkSurfaceObjectBlockedTemplateCache.Clear();

        var buildingPlacements = new List<TerrainObjectPlacement>();
        foreach (var placement in placements)
        {
            if (WalkSurfaceObjectHeightBaker.UsesHeightTemplate(placement.Category))
            {
                buildingPlacements.Add(placement);
            }
        }

        if (buildingPlacements.Count == 0)
        {
            return 0;
        }

        var templates = WalkSurfaceObjectBlockedTemplateCache.WarmAll(
            buildingPlacements,
            modelsDirectory,
            sampleSpacingMeters);

        var stampJobs = new List<BlockedStampJob>(buildingPlacements.Count);
        foreach (var placement in buildingPlacements)
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

            stampJobs.Add(new BlockedStampJob(placement, template, halfExtentX, halfExtentZ));
        }

        var cellsStamped = 0;
        Parallel.ForEach(stampJobs, job =>
        {
            Interlocked.Add(
                ref cellsStamped,
                job.Template.WritePlacementBlocked(
                    job.Placement.WorldTransform,
                    job.HalfExtentX,
                    job.HalfExtentZ,
                    builders));
        });

        GD.Print(
            $"WalkSurfaceObjectBlockedRaster: stamped {cellsStamped} blocked cell(s) from {stampJobs.Count} building placement(s).");
        return cellsStamped;
    }

    private readonly record struct BlockedStampJob(
        TerrainObjectPlacement Placement,
        WalkSurfaceObjectBlockedTemplate Template,
        float HalfExtentX,
        float HalfExtentZ);
}
