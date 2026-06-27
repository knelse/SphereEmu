using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Shared vertical mesh raycasts against the outdoor <see cref="GridMap" /> terrain.
/// </summary>
public static class TerrainWalkMeshRaycast
{
    public const float RayTopY = 4096f;
    public const float RayBottomY = -1024f;

    public static bool TrySampleTerrainTopY(GridMap terrain, float worldX, float worldZ, out float worldY)
    {
        worldY = default;
        var fromWorld = new Vector3(worldX, RayTopY, worldZ);
        var toWorld = new Vector3(worldX, RayBottomY, worldZ);
        return TryRaycastTerrainMeshes(terrain, fromWorld, toWorld, out var hit, out _)
            && (worldY = hit.Y) == hit.Y;
    }

    public static bool TryRaycastTerrainMeshes(
        GridMap terrain,
        Vector3 fromWorld,
        Vector3 toWorld,
        out Vector3 hitPosition,
        out float fraction)
    {
        hitPosition = default;
        fraction = float.MaxValue;

        if (terrain.MeshLibrary is null)
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

        var mapCell = ResolveHorizontalTerrainCell(terrain, fromWorld, toWorld);
        var found = false;
        var bestHit = Vector3.Zero;
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
                bestHit = hitWorld;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        hitPosition = bestHit;
        fraction = bestFraction;
        return true;
    }

    public static bool TryRaycastSingleCellTopY(
        GridMap terrain,
        Vector3I cell,
        float worldX,
        float worldZ,
        out float worldY)
    {
        worldY = default;
        var itemId = terrain.GetCellItem(cell);
        if (itemId < 0 || terrain.MeshLibrary is null)
        {
            return false;
        }

        var mesh = terrain.MeshLibrary.GetItemMesh(itemId);
        if (mesh is null)
        {
            return false;
        }

        var fromWorld = new Vector3(worldX, RayTopY, worldZ);
        var toWorld = new Vector3(worldX, RayBottomY, worldZ);
        var cellTransform = terrain.GlobalTransform * new Transform3D(
            terrain.GetCellItemBasis(cell),
            terrain.MapToLocal(cell));

        if (!TryRaycastMesh(mesh, cellTransform, fromWorld, toWorld, out var hitWorld, out _))
        {
            return false;
        }

        worldY = hitWorld.Y;
        return true;
    }

    public static Vector3I ResolveHorizontalTerrainCell(GridMap terrain, Vector3 fromWorld, Vector3 toWorld)
    {
        var midWorld = (fromWorld + toWorld) * 0.5f;
        var local = terrain.ToLocal(midWorld);
        var layerLocalY = terrain.MapToLocal(new Vector3I(0, 0, 0)).Y;
        local = new Vector3(local.X, layerLocalY, local.Z);
        var mapCell = terrain.LocalToMap(local);
        return new Vector3I(mapCell.X, 0, mapCell.Z);
    }

    public static bool TryRaycastMesh(
        Mesh mesh,
        Transform3D globalTransform,
        Vector3 fromWorld,
        Vector3 toWorld,
        out Vector3 hitWorld,
        out float fraction)
    {
        return TerrainMeshRaycastCache.TryRaycastMesh(mesh, globalTransform, fromWorld, toWorld, out hitWorld, out fraction);
    }

    internal static bool TryRayTriangle(
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
