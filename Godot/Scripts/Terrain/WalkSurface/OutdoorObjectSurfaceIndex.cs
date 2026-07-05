using System;
using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Spatial index of terrain object meshes near a bake region. Used to reject outdoor spawn samples
///     where a building roof or prop sits above the GridMap sand height (MultiMesh objects have no physics).
/// </summary>
public sealed class OutdoorObjectSurfaceIndex
{
    public const string DefaultObjectDataDirectory = "res://Godot/Terrain/ObjectDataJson/";
    public const string DefaultModelsDirectory = "res://Godot/Models/";
    private const float MaxObjectSearchPaddingMeters = 48f;

    private readonly string _modelsDirectory;
    private readonly List<IndexedPlacement> _placements;

    private OutdoorObjectSurfaceIndex(string modelsDirectory, List<IndexedPlacement> placements)
    {
        _modelsDirectory = modelsDirectory;
        _placements = placements;
    }

    public int PlacementCount => _placements.Count;

    public static OutdoorObjectSurfaceIndex? TryBuild(
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        string objectDataDirectory = DefaultObjectDataDirectory,
        string modelsDirectory = DefaultModelsDirectory)
    {
        var allPlacements = PlacementCache.GetOrLoad(objectDataDirectory);
        if (allPlacements.Count == 0)
        {
            return null;
        }

        var searchRadius = radiusMeters + MaxObjectSearchPaddingMeters;
        var searchRadiusSq = searchRadius * searchRadius;
        var nearby = new List<IndexedPlacement>();
        foreach (var placement in allPlacements)
        {
            if (!TryCreateIndexedPlacement(placement, modelsDirectory, out var indexed))
            {
                continue;
            }

            var dx = indexed.CenterX - centerWorldX;
            var dz = indexed.CenterZ - centerWorldZ;
            if (dx * dx + dz * dz > (searchRadius + indexed.BoundRadiusMeters) * (searchRadius + indexed.BoundRadiusMeters))
            {
                continue;
            }

            if (!IntersectsSearchCircle(
                    centerWorldX,
                    centerWorldZ,
                    searchRadiusSq,
                    indexed.MinWorldX,
                    indexed.MaxWorldX,
                    indexed.MinWorldZ,
                    indexed.MaxWorldZ))
            {
                continue;
            }

            nearby.Add(indexed);
        }

        return nearby.Count == 0 ? null : new OutdoorObjectSurfaceIndex(modelsDirectory, nearby);
    }

    public bool HasWalkSurfaceAbove(float worldX, float worldZ, float terrainMeshY, float minClearanceMeters)
    {
        return TryGetTopWalkSurfaceY(worldX, worldZ, out var topY)
               && topY - terrainMeshY > minClearanceMeters;
    }

    public bool TryGetTopWalkSurfaceY(float worldX, float worldZ, out float topY)
    {
        topY = float.MinValue;
        var found = false;
        var fromWorld = new Vector3(worldX, TerrainWalkMeshRaycast.RayTopY, worldZ);
        var toWorld = new Vector3(worldX, TerrainWalkMeshRaycast.RayBottomY, worldZ);

        foreach (var indexed in _placements)
        {
            if (!IsInsideFootprint(indexed, worldX, worldZ))
            {
                continue;
            }

            if (!WalkSurfaceObjectMeshCache.TryGetMeshParts(indexed.ObjectName, _modelsDirectory, out var meshParts)
                || meshParts is null)
            {
                continue;
            }

            var placementTransform = indexed.WorldTransform;
            foreach (var part in meshParts)
            {
                var meshGlobal = placementTransform * part.MeshLocalToRoot;
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

                if (hitWorld.Y <= topY)
                {
                    continue;
                }

                topY = hitWorld.Y;
                found = true;
            }
        }

        return found;
    }

    private static bool TryCreateIndexedPlacement(
        TerrainObjectPlacement placement,
        string modelsDirectory,
        out IndexedPlacement indexed)
    {
        indexed = default;
        if (!WalkSurfaceObjectMeshCache.TryGetMeshParts(placement.ObjectName, modelsDirectory, out var meshParts)
            || meshParts is null
            || meshParts.Count == 0)
        {
            return false;
        }

        var origin = placement.WorldTransform.Origin;
        if (UsesOrientedRectFootprint(placement.Category))
        {
            if (!WalkSurfaceModelBoundsCache.TryResolveFootprintHalfExtents(
                    placement,
                    modelsDirectory,
                    out var halfExtentX,
                    out var halfExtentZ))
            {
                return false;
            }

            var yaw = placement.WorldTransform.Basis.GetEuler(EulerOrder.Yxz).Y;
            var cos = Mathf.Cos(yaw);
            var sin = Mathf.Sin(yaw);
            var boundHalfX = Mathf.Abs(cos * halfExtentX) + Mathf.Abs(sin * halfExtentZ);
            var boundHalfZ = Mathf.Abs(sin * halfExtentX) + Mathf.Abs(cos * halfExtentZ);
            indexed = new IndexedPlacement
            {
                ObjectName = placement.ObjectName,
                WorldTransform = placement.WorldTransform,
                CenterX = origin.X,
                CenterZ = origin.Z,
                MinWorldX = origin.X - boundHalfX,
                MaxWorldX = origin.X + boundHalfX,
                MinWorldZ = origin.Z - boundHalfZ,
                MaxWorldZ = origin.Z + boundHalfZ,
                BoundRadiusMeters = Mathf.Max(boundHalfX, boundHalfZ),
                UsesOrientedRect = true,
                HalfExtentX = halfExtentX,
                HalfExtentZ = halfExtentZ,
                DiskRadiusMeters = 0f,
            };
            return true;
        }

        var radius = WalkSurfaceModelBoundsCache.ResolveFootprintRadiusMeters(placement, modelsDirectory);
        indexed = new IndexedPlacement
        {
            ObjectName = placement.ObjectName,
            WorldTransform = placement.WorldTransform,
            CenterX = origin.X,
            CenterZ = origin.Z,
            MinWorldX = origin.X - radius,
            MaxWorldX = origin.X + radius,
            MinWorldZ = origin.Z - radius,
            MaxWorldZ = origin.Z + radius,
            BoundRadiusMeters = radius,
            UsesOrientedRect = false,
            HalfExtentX = 0f,
            HalfExtentZ = 0f,
            DiskRadiusMeters = radius,
        };
        return true;
    }

    private static bool UsesOrientedRectFootprint(TerrainObjectWalkCategory category)
        => category is TerrainObjectWalkCategory.ExtraInstanced or TerrainObjectWalkCategory.Other;

    private static bool IsInsideFootprint(IndexedPlacement indexed, float worldX, float worldZ)
    {
        if (worldX < indexed.MinWorldX || worldX > indexed.MaxWorldX
            || worldZ < indexed.MinWorldZ || worldZ > indexed.MaxWorldZ)
        {
            return false;
        }

        if (!indexed.UsesOrientedRect)
        {
            var dx = worldX - indexed.CenterX;
            var dz = worldZ - indexed.CenterZ;
            return dx * dx + dz * dz <= indexed.DiskRadiusMeters * indexed.DiskRadiusMeters;
        }

        var origin = indexed.WorldTransform.Origin;
        var yaw = indexed.WorldTransform.Basis.GetEuler(EulerOrder.Yxz).Y;
        var cos = Mathf.Cos(yaw);
        var sin = Mathf.Sin(yaw);
        var localX = (worldX - origin.X) * cos + (worldZ - origin.Z) * sin;
        var localZ = -(worldX - origin.X) * sin + (worldZ - origin.Z) * cos;
        return Mathf.Abs(localX) <= indexed.HalfExtentX && Mathf.Abs(localZ) <= indexed.HalfExtentZ;
    }

    private static bool IntersectsSearchCircle(
        float centerX,
        float centerZ,
        float radiusSq,
        float minX,
        float maxX,
        float minZ,
        float maxZ)
    {
        var closestX = Mathf.Clamp(centerX, minX, maxX);
        var closestZ = Mathf.Clamp(centerZ, minZ, maxZ);
        var dx = closestX - centerX;
        var dz = closestZ - centerZ;
        return dx * dx + dz * dz <= radiusSq;
    }

    private struct IndexedPlacement
    {
        public string ObjectName;
        public Transform3D WorldTransform;
        public float CenterX;
        public float CenterZ;
        public float MinWorldX;
        public float MaxWorldX;
        public float MinWorldZ;
        public float MaxWorldZ;
        public float BoundRadiusMeters;
        public bool UsesOrientedRect;
        public float HalfExtentX;
        public float HalfExtentZ;
        public float DiskRadiusMeters;
    }

    private static class PlacementCache
    {
        private static readonly object LoadLock = new();
        private static string? _loadedDirectory;
        private static IReadOnlyList<TerrainObjectPlacement> _placements = [];

        public static IReadOnlyList<TerrainObjectPlacement> GetOrLoad(string objectDataDirectory)
        {
            lock (LoadLock)
            {
                if (_loadedDirectory == objectDataDirectory && _placements.Count > 0)
                {
                    return _placements;
                }

                _placements = TerrainObjectPlacementSource.LoadAll(objectDataDirectory);
                _loadedDirectory = objectDataDirectory;
                return _placements;
            }
        }
    }
}
