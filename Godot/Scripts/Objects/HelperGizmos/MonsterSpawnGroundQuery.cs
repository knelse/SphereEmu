using Godot;
using SphServer.Godot.Scripts.Terrain.Fill;
using SphServer.Godot.Scripts.Terrain.WalkSurface;
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
    private const float MinWalkableSurfaceNormalY = 0.55f;
    private const float NearbySearchStepMeters = 2f;
    private const float NearbySearchMaxRadiusMeters = 6f;

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
        if (TryFindValidSpawnSurfaceAt(
                contextNode,
                worldProbeOrigin,
                minSeparationMeters,
                occupiedWorldPositions,
                out spawnWorldPosition))
        {
            return true;
        }

        return TrySearchNearbySpawnSurfaces(
            contextNode,
            worldProbeOrigin,
            minSeparationMeters,
            occupiedWorldPositions,
            out spawnWorldPosition);
    }

    public static bool TryFindTopSurface(Node3D contextNode, Vector3 worldProbeOrigin, out SurfaceHit surfaceHit)
    {
        surfaceHit = default;

        if (TrySampleSceneGroundY(contextNode, worldProbeOrigin.X, worldProbeOrigin.Z, out var sceneY))
        {
            surfaceHit = new SurfaceHit(new Vector3(worldProbeOrigin.X, sceneY, worldProbeOrigin.Z), isTerrain: false);
            return true;
        }

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
            && physicsHit.Normal.Y >= MinWalkableSurfaceNormalY
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

    /// <summary>
    ///     Resolves Godot world Y for mob feet at (worldX, worldZ). Independent of spawner placement height.
    /// </summary>
    public static bool TryResolveSpawnGroundY(Node3D contextNode, float worldX, float worldZ, out float worldY)
    {
        if (TrySampleSceneGroundY(contextNode, worldX, worldZ, out worldY))
        {
            return true;
        }

        var terrain = ResolveTerrainGridMap(contextNode);
        if (terrain is not null
            && TerrainWalkMeshRaycast.TrySampleTerrainTopY(terrain, worldX, worldZ, out worldY))
        {
            return true;
        }

        if (TryConvertAtlasGroundY(contextNode, worldX, worldZ, out worldY))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Resolves outdoor ground height from scene physics for baked-slot placement.
    /// </summary>
    public static bool TrySampleSceneGroundY(Node3D contextNode, float worldX, float worldZ, out float worldY)
    {
        worldY = default;
        var rayStart = new Vector3(worldX, TerrainWalkMeshRaycast.RayTopY, worldZ);
        var rayEnd = new Vector3(worldX, TerrainWalkMeshRaycast.RayBottomY, worldZ);

        if (!TryRaycastPhysics(contextNode, rayStart, rayEnd, out var physicsHit, out _)
            || physicsHit.Normal.Y < MinWalkableSurfaceNormalY
            || IsMonsterCollider(physicsHit.Collider))
        {
            return false;
        }

        worldY = physicsHit.Position.Y;
        return true;
    }

    /// <summary>
    ///     Atlas terrain Y uses tile mesh space that does not match Godot world space.
    ///     Returns atlasY - terrainMeshY at the same horizontal sample (never spawner node Y).
    /// </summary>
    public static float ComputeAtlasVerticalOffset(
        MonsterSpawner spawner,
        IReadOnlyList<(float X, float Z, float Y)>? atlasCandidates = null)
    {
        WalkSurfaceCache.PreloadChunksForRadius(
            spawner.GlobalPosition.X,
            spawner.GlobalPosition.Z,
            spawner.SpawnRadiusMeters + 1f);

        if (TryComputeAtlasVerticalDelta(spawner, spawner.GlobalPosition.X, spawner.GlobalPosition.Z, out var delta))
        {
            return delta;
        }

        if (atlasCandidates is { Count: > 0 })
        {
            var origin = spawner.GlobalPosition;
            var nearest = atlasCandidates[0];
            var nearestDistSq = float.MaxValue;
            foreach (var candidate in atlasCandidates)
            {
                var dx = candidate.X - origin.X;
                var dz = candidate.Z - origin.Z;
                var distSq = dx * dx + dz * dz;
                if (distSq >= nearestDistSq)
                {
                    continue;
                }

                nearestDistSq = distSq;
                nearest = candidate;
            }

            if (TryComputeAtlasVerticalDelta(spawner, nearest.X, nearest.Z, out delta))
            {
                return delta;
            }
        }

        return 0f;
    }

    private static bool TryConvertAtlasGroundY(Node3D contextNode, float worldX, float worldZ, out float worldY)
    {
        worldY = default;
        if (!WalkSurfaceCache.TrySampleWalkableGround(worldX, worldZ, out var atlasY)
            && !WalkSurfaceCache.TrySampleGround(worldX, worldZ, out atlasY))
        {
            return false;
        }

        if (float.IsNaN(atlasY))
        {
            return false;
        }

        if (TryComputeAtlasVerticalDelta(contextNode, worldX, worldZ, out var delta))
        {
            worldY = atlasY - delta;
            return true;
        }

        worldY = atlasY;
        return true;
    }

    private static bool TryComputeAtlasVerticalDelta(
        Node3D contextNode,
        float worldX,
        float worldZ,
        out float delta)
    {
        if (TryComputeAtlasVerticalDeltaAt(contextNode, worldX, worldZ, out delta))
        {
            return true;
        }

        const float stepMeters = 2f;
        const float maxRadiusMeters = 8f;
        var maxRings = Mathf.CeilToInt(maxRadiusMeters / stepMeters);
        for (var ring = 1; ring <= maxRings; ring++)
        {
            var ringRadius = ring * stepMeters;
            var sampleCount = Math.Max(8, ring * 8);
            for (var sample = 0; sample < sampleCount; sample++)
            {
                var angle = (float)(sample * Math.Tau / sampleCount);
                var probeX = worldX + Mathf.Cos(angle) * ringRadius;
                var probeZ = worldZ + Mathf.Sin(angle) * ringRadius;
                if (TryComputeAtlasVerticalDeltaAt(contextNode, probeX, probeZ, out delta))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryComputeAtlasVerticalDeltaAt(
        Node3D contextNode,
        float worldX,
        float worldZ,
        out float delta)
    {
        delta = 0f;
        if (!WalkSurfaceCache.TrySampleWalkableGround(worldX, worldZ, out var atlasY)
            && !WalkSurfaceCache.TrySampleGround(worldX, worldZ, out atlasY))
        {
            return false;
        }

        if (float.IsNaN(atlasY))
        {
            return false;
        }

        var terrain = ResolveTerrainGridMap(contextNode);
        if (terrain is null
            || !TerrainWalkMeshRaycast.TrySampleTerrainTopY(terrain, worldX, worldZ, out var terrainY))
        {
            return false;
        }

        delta = atlasY - terrainY;
        return true;
    }

    private static bool TryFindValidSpawnSurfaceAt(
        Node3D contextNode,
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition)
    {
        spawnWorldPosition = default;

        if (WalkSurfaceCache.HasAnyChunkFiles()
            && WalkSurfaceQuery.TryFindValidSpawnSurface(
                worldProbeOrigin,
                minSeparationMeters,
                occupiedWorldPositions,
                out spawnWorldPosition))
        {
            return true;
        }

        if (WalkSurfaceCache.HasAnyChunkFiles()
            && WalkSurfaceCache.HasChunkCoverageAt(worldProbeOrigin.X, worldProbeOrigin.Z)
            && WalkSurfaceCache.HasWalkableField)
        {
            return false;
        }

        if (!TryFindTopSurface(contextNode, worldProbeOrigin, out var standingSurface))
        {
            return false;
        }

        if (HasOverlap(standingSurface.Position, minSeparationMeters, occupiedWorldPositions))
        {
            return false;
        }

        if (!IsSpawnPositionAllowed(standingSurface.Position))
        {
            return false;
        }

        if (!IsSpawnFootprintAcceptableAt(standingSurface.Position))
        {
            return false;
        }

        if (standingSurface.IsTerrain)
        {
            spawnWorldPosition = standingSurface.Position;
            return true;
        }

        if (ShouldEnforceTerrainGap(standingSurface.Position)
            && !IsWithinTerrainGap(contextNode, standingSurface.Position))
        {
            return false;
        }

        spawnWorldPosition = standingSurface.Position;
        return true;
    }

    /// <summary>
    ///     Rejects positions stamped as terrain-object footprints in the walk atlas.
    ///     Only the nearest atlas sample is checked so nearby object footprints do not
    ///     block open ground the way the old four-corner test did.
    /// </summary>
    private static bool IsSpawnPositionAllowed(Vector3 spawnWorldPosition)
    {
        if (!WalkSurfaceCache.HasAnyChunkFiles())
        {
            return true;
        }

        return !WalkSurfaceCache.IsBlocked(spawnWorldPosition.X, spawnWorldPosition.Z);
    }

    private static bool IsSpawnFootprintAcceptableAt(Vector3 spawnWorldPosition)
    {
        if (!WalkSurfaceCache.HasAnyChunkFiles())
        {
            return true;
        }

        return WalkSurfaceCache.IsSpawnFootprintAcceptable(spawnWorldPosition.X, spawnWorldPosition.Z);
    }

    private static bool TrySearchNearbySpawnSurfaces(
        Node3D contextNode,
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition)
    {
        spawnWorldPosition = default;
        var maxRings = Mathf.CeilToInt(NearbySearchMaxRadiusMeters / NearbySearchStepMeters);

        for (var ring = 1; ring <= maxRings; ring++)
        {
            var ringRadius = ring * NearbySearchStepMeters;
            var sampleCount = Math.Max(8, ring * 8);
            for (var sample = 0; sample < sampleCount; sample++)
            {
                var angle = (float)(sample * Math.Tau / sampleCount);
                var offset = new Vector3(Mathf.Cos(angle) * ringRadius, 0f, Mathf.Sin(angle) * ringRadius);
                var probe = worldProbeOrigin + offset;
                if (TryFindValidSpawnSurfaceAt(
                        contextNode,
                        probe,
                        minSeparationMeters,
                        occupiedWorldPositions,
                        out spawnWorldPosition))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ShouldEnforceTerrainGap(Vector3 standingWorldPosition)
    {
        return WalkSurfaceCache.TrySampleGround(standingWorldPosition.X, standingWorldPosition.Z, out _);
    }

    private static bool IsWithinTerrainGap(Node3D contextNode, Vector3 standingWorldPosition)
    {
        if (!TryFindTerrainBelow(
                contextNode,
                standingWorldPosition + Vector3.Down * TerrainHitEpsilonMeters,
                out var terrainBelow))
        {
            return false;
        }

        return standingWorldPosition.Y - terrainBelow.Y <= MaxStandingSurfaceToTerrainGapMeters;
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

        var normal = rayResult.TryGetValue("normal", out var normalVariant)
            ? (Vector3)normalVariant
            : Vector3.Up;
        hit = new PhysicsHit(
            (Vector3)rayResult["position"],
            normal,
            rayResult.TryGetValue("collider", out var collider) ? collider.AsGodotObject() : null);
        fraction = fromWorld.DistanceTo(hit.Position) / fromWorld.DistanceTo(toWorld);
        return true;
    }

    private readonly struct PhysicsHit
    {
        public PhysicsHit(Vector3 position, Vector3 normal, GodotObject? collider)
        {
            Position = position;
            Normal = normal;
            Collider = collider;
        }

        public Vector3 Position { get; }
        public Vector3 Normal { get; }
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
