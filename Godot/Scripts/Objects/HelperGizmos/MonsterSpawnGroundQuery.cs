using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Resolves walkable ground height via GridMap mesh raycasts and optional physics bodies.
///     Terrain tiles have no physics shapes, so GridMap mesh intersection is required.
/// </summary>
public static class MonsterSpawnGroundQuery
{
    public const float MaxStandingSurfaceToTerrainGapMeters = 0.5f;
    private const float TerrainHitEpsilonMeters = 0.05f;
    /// <summary>World-space vertical span for downward spawn rays (probe XZ only; Y comes from terrain hit).</summary>
    private const float WorldRayTopY = 4096f;
    private const float WorldRayBottomY = -1024f;

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
    }

    public static bool TryFindValidSpawnSurface(
        Node3D contextNode,
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition)
    {
        spawnWorldPosition = default;

        if (!TryFindTopSurface(contextNode, worldProbeOrigin, out var standingSurface))
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

    public static bool TryFindTopSurface(Node3D contextNode, Vector3 worldProbeOrigin, out SurfaceHit surfaceHit)
    {
        surfaceHit = default;

        // Do not anchor rays to spawner/probe Y — dump coords can be far from local terrain height.
        var rayStart = new Vector3(worldProbeOrigin.X, WorldRayTopY, worldProbeOrigin.Z);
        var rayEnd = new Vector3(worldProbeOrigin.X, WorldRayBottomY, worldProbeOrigin.Z);

        var bestFraction = float.MaxValue;
        var bestPosition = Vector3.Zero;
        var bestIsTerrain = false;
        var found = false;

        if (TryRaycastTerrainMeshes(contextNode, rayStart, rayEnd, out var terrainHit, out var terrainFraction))
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
        var rayEnd = new Vector3(rayStart.X, WorldRayBottomY, rayStart.Z);

        if (!TryRaycastTerrainMeshes(contextNode, rayStart, rayEnd, out terrainPosition, out _))
        {
            return false;
        }

        return true;
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

    private static bool TryRaycastTerrainMeshes(
        Node3D contextNode,
        Vector3 fromWorld,
        Vector3 toWorld,
        out Vector3 hitPosition,
        out float fraction)
    {
        hitPosition = default;
        fraction = float.MaxValue;

        var terrain = ResolveTerrainGridMap(contextNode);
        if (terrain is null || terrain.MeshLibrary is null)
        {
            return false;
        }

        var localFrom = terrain.ToLocal(fromWorld);
        var localTo = terrain.ToLocal(toWorld);
        var localDir = localTo - localFrom;
        var rayLength = localDir.Length();
        if (rayLength < 0.0001f)
        {
            return false;
        }

        localDir /= rayLength;
        var mapCell = ResolveHorizontalTerrainCell(terrain, fromWorld, toWorld);
        var found = false;
        var bestLocalHit = Vector3.Zero;
        var bestFraction = float.MaxValue;

        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var cell = mapCell + new Vector3I(dx, 0, dz);
                var itemId = terrain.GetCellItem(cell);
                if (itemId < 0)
                {
                    continue;
                }

                var mesh = terrain.MeshLibrary.GetItemMesh(itemId);
                if (mesh is null)
                {
                    continue;
                }

                var cellTransform = terrain.GlobalTransform * new Transform3D(
                    terrain.GetCellItemBasis(cell),
                    terrain.MapToLocal(cell));

                if (!TryRaycastMesh(mesh, cellTransform, fromWorld, toWorld, out var hitWorld, out var hitFraction)
                    || hitFraction >= bestFraction)
                {
                    continue;
                }

                bestFraction = hitFraction;
                bestLocalHit = hitWorld;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        hitPosition = bestLocalHit;
        fraction = bestFraction;
        return true;
    }

    /// <summary>
    ///     GridMap <see cref="GridMap.LocalToMap" /> uses full 3D position; tall spawn rays would map to
    ///     empty vertical layers while terrain lives at map Y = 0.
    /// </summary>
    private static Vector3I ResolveHorizontalTerrainCell(GridMap terrain, Vector3 fromWorld, Vector3 toWorld)
    {
        var midWorld = (fromWorld + toWorld) * 0.5f;
        var local = terrain.ToLocal(midWorld);
        var layerLocalY = terrain.MapToLocal(new Vector3I(0, 0, 0)).Y;
        local = new Vector3(local.X, layerLocalY, local.Z);
        var mapCell = terrain.LocalToMap(local);
        return new Vector3I(mapCell.X, 0, mapCell.Z);
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

    private static bool TryRaycastMesh(
        Mesh mesh,
        Transform3D globalTransform,
        Vector3 fromWorld,
        Vector3 toWorld,
        out Vector3 hitWorld,
        out float fraction)
    {
        hitWorld = default;
        fraction = float.MaxValue;

        var inv = globalTransform.AffineInverse();
        var fromLocal = inv * fromWorld;
        var toLocal = inv * toWorld;
        var dirLocal = toLocal - fromLocal;
        var rayLength = dirLocal.Length();
        if (rayLength < 0.0001f)
        {
            return false;
        }

        dirLocal /= rayLength;
        var found = false;
        var bestT = float.MaxValue;

        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (vertices.Length == 0)
            {
                continue;
            }

            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (indices.Length >= 3)
            {
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    if (!TryRayTriangle(
                            fromLocal,
                            dirLocal,
                            rayLength,
                            vertices[indices[i]],
                            vertices[indices[i + 1]],
                            vertices[indices[i + 2]],
                            out var t)
                        || t >= bestT)
                    {
                        continue;
                    }

                    bestT = t;
                    found = true;
                }

                continue;
            }

            for (var i = 0; i + 2 < vertices.Length; i += 3)
            {
                if (!TryRayTriangle(
                        fromLocal,
                        dirLocal,
                        rayLength,
                        vertices[i],
                        vertices[i + 1],
                        vertices[i + 2],
                        out var t)
                    || t >= bestT)
                {
                    continue;
                }

                bestT = t;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        fraction = bestT / rayLength;
        hitWorld = globalTransform * (fromLocal + dirLocal * bestT);
        return true;
    }

    private static bool TryRayTriangle(
        Vector3 rayOrigin,
        Vector3 rayDir,
        float rayLength,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        out float hitDistance)
    {
        hitDistance = float.MaxValue;

        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var pvec = rayDir.Cross(edge2);
        var det = edge1.Dot(pvec);
        if (Mathf.Abs(det) < 1e-8f)
        {
            return false;
        }

        var invDet = 1f / det;
        var tvec = rayOrigin - v0;
        var u = tvec.Dot(pvec) * invDet;
        if (u is < 0f or > 1f)
        {
            return false;
        }

        var qvec = tvec.Cross(edge1);
        var v = rayDir.Dot(qvec) * invDet;
        if (v < 0f || u + v > 1f)
        {
            return false;
        }

        var t = edge2.Dot(qvec) * invDet;
        if (t < 0f || t > rayLength)
        {
            return false;
        }

        hitDistance = t;
        return true;
    }
}
