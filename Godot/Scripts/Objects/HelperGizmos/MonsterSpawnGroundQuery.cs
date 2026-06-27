using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Resolves walkable ground height for mob placement. Uses the outdoor height atlas when present,
///     with live GridMap / physics raycasts as fallback (platforms, missing atlas, future indoor areas).
/// </summary>
public static class MonsterSpawnGroundQuery
{
    public const float MaxStandingSurfaceToTerrainGapMeters = 0.5f;
    private const float TerrainHitEpsilonMeters = 0.05f;

    private static GridMap? _cachedTerrainGridMap;

    public readonly struct SurfaceHit
    {
        public SurfaceHit(Vector3 position, bool isTerrain)
        {
            Position = position;
            IsTerrain = isTerrain;
        }

        public Vector3 Position { get; }
        public bool IsTerrain { get; }
    }

    public static void InvalidateTerrainCache()
    {
        _cachedTerrainGridMap = null;
        WalkSurfaceCache.Invalidate();
    }

    public static bool TryFindValidSpawnSurface(
        Node3D contextNode,
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition)
    {
        if (WalkSurfaceCache.HasAnyChunkFiles()
            && WalkSurfaceQuery.TryFindValidSpawnSurface(
                worldProbeOrigin,
                minSeparationMeters,
                occupiedWorldPositions,
                out spawnWorldPosition)
            && !IsAtlasBlocked(spawnWorldPosition.X, spawnWorldPosition.Z))
        {
            return true;
        }

        spawnWorldPosition = default;

        if (!TryFindTopSurface(contextNode, worldProbeOrigin, out var standingSurface))
        {
            return false;
        }

        if (IsAtlasBlocked(standingSurface.Position.X, standingSurface.Position.Z))
        {
            return false;
        }

        if (HasOverlap(standingSurface.Position, minSeparationMeters, occupiedWorldPositions))
        {
            return false;
        }

        if (standingSurface.IsTerrain)
        {
            spawnWorldPosition = standingSurface.Position;
            return true;
        }

        if (!TryFindTerrainBelow(
                contextNode,
                standingSurface.Position + Vector3.Down * TerrainHitEpsilonMeters,
                out var terrainBelow))
        {
            return false;
        }

        if (standingSurface.Position.Y - terrainBelow.Y > MaxStandingSurfaceToTerrainGapMeters)
        {
            return false;
        }

        spawnWorldPosition = standingSurface.Position;
        return true;
    }

    private static bool IsAtlasBlocked(float worldX, float worldZ)
    {
        return WalkSurfaceCache.IsBlocked(worldX, worldZ);
    }

    public static bool TryFindTopSurface(Node3D contextNode, Vector3 worldProbeOrigin, out SurfaceHit surfaceHit)
    {
        surfaceHit = default;

        if (WalkSurfaceCache.HasAnyChunkFiles()
            && WalkSurfaceQuery.TrySampleGround(worldProbeOrigin, out var atlasGround))
        {
            surfaceHit = new SurfaceHit(atlasGround, isTerrain: true);
            return true;
        }

        var rayStart = new Vector3(worldProbeOrigin.X, TerrainWalkMeshRaycast.RayTopY, worldProbeOrigin.Z);
        var rayEnd = new Vector3(worldProbeOrigin.X, TerrainWalkMeshRaycast.RayBottomY, worldProbeOrigin.Z);

        var bestFraction = float.MaxValue;
        var bestPosition = Vector3.Zero;
        var bestIsTerrain = false;
        var found = false;

        var terrain = ResolveTerrainGridMap(contextNode);
        if (terrain is not null
            && TerrainWalkMeshRaycast.TryRaycastTerrainMeshes(terrain, rayStart, rayEnd, out var terrainHit, out var terrainFraction))
        {
            bestFraction = terrainFraction;
            bestPosition = terrainHit;
            bestIsTerrain = true;
            found = true;
        }

        if (TryRaycastPhysics(contextNode, rayStart, rayEnd, out var physicsHit, out var physicsFraction)
            && !IsMonsterCollider(physicsHit.Collider)
            && physicsFraction < bestFraction)
        {
            bestFraction = physicsFraction;
            bestPosition = physicsHit.Position;
            bestIsTerrain = false;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        surfaceHit = new SurfaceHit(bestPosition, bestIsTerrain);
        return true;
    }

    private static bool TryFindTerrainBelow(Node3D contextNode, Vector3 rayStart, out Vector3 terrainPosition)
    {
        terrainPosition = default;
        var rayEnd = new Vector3(rayStart.X, TerrainWalkMeshRaycast.RayBottomY, rayStart.Z);

        var terrain = ResolveTerrainGridMap(contextNode);
        if (terrain is null)
        {
            return false;
        }

        return TerrainWalkMeshRaycast.TryRaycastTerrainMeshes(terrain, rayStart, rayEnd, out terrainPosition, out _);
    }

    private static bool TryRaycastPhysics(
        Node3D contextNode,
        Vector3 fromWorld,
        Vector3 toWorld,
        out PhysicsHit hit,
        out float fraction)
    {
        hit = default;
        fraction = float.MaxValue;

        var world = contextNode.GetWorld3D();
        if (world is null)
        {
            return false;
        }

        var query = PhysicsRayQueryParameters3D.Create(fromWorld, toWorld);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.CollisionMask = uint.MaxValue & ~(1u << 1);

        var rayResult = world.DirectSpaceState.IntersectRay(query);
        if (rayResult.Count == 0)
        {
            return false;
        }

        hit = new PhysicsHit((Vector3)rayResult["position"], rayResult.TryGetValue("collider", out var collider) ? collider.AsGodotObject() : null);
        fraction = fromWorld.DistanceTo(hit.Position) / fromWorld.DistanceTo(toWorld);
        return true;
    }

    private readonly struct PhysicsHit
    {
        public PhysicsHit(Vector3 position, GodotObject? collider)
        {
            Position = position;
            Collider = collider;
        }

        public Vector3 Position { get; }
        public GodotObject? Collider { get; }
    }

    private static bool IsMonsterCollider(GodotObject? collider)
    {
        if (collider is not Node node)
        {
            return false;
        }

        while (node is not null)
        {
            if (node is Monster)
            {
                return true;
            }

            node = node.GetParent();
        }

        return false;
    }

    private static GridMap? ResolveTerrainGridMap(Node3D contextNode)
    {
        if (_cachedTerrainGridMap is not null && GodotObject.IsInstanceValid(_cachedTerrainGridMap))
        {
            return _cachedTerrainGridMap;
        }

        var tree = contextNode.GetTree();
        if (tree is null)
        {
            return null;
        }

        foreach (var node in tree.Root.FindChildren("*", nameof(GridMap), recursive: true, owned: false))
        {
            if (node is not GridMap gridMap)
            {
                continue;
            }

            if (node.Name == TerrainGridFill.TerrainNodeName)
            {
                _cachedTerrainGridMap = gridMap;
                return gridMap;
            }
        }

        return null;
    }

    private static bool HasOverlap(
        Vector3 candidate,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions)
    {
        var minSeparationSq = minSeparationMeters * minSeparationMeters;
        foreach (var occupied in occupiedWorldPositions)
        {
            var dx = candidate.X - occupied.X;
            var dz = candidate.Z - occupied.Z;
            if (dx * dx + dz * dz < minSeparationSq)
            {
                return true;
            }
        }

        return false;
    }
}
