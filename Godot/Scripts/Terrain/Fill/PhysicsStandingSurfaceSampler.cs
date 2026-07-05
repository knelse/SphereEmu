using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
///     Samples walk / spawn surfaces from a live Godot physics world (standing-surface bake scene).
/// </summary>
public static class PhysicsStandingSurfaceSampler
{
    public const float MinWalkableNormalY = 0.55f;

    public readonly struct SampleResult
    {
        public SampleResult(bool hasGround, float standingY, float terrainMeshY, bool blocked, bool isWalkable)
        {
            HasGround = hasGround;
            StandingY = standingY;
            TerrainMeshY = terrainMeshY;
            Blocked = blocked;
            IsWalkable = isWalkable;
        }

        public bool HasGround { get; }
        public float StandingY { get; }
        public float TerrainMeshY { get; }
        public bool Blocked { get; }
        public bool IsWalkable { get; }
    }

    public static SampleResult SampleCell(
        World3D world,
        GridMap terrain,
        float worldX,
        float worldZ)
    {
        var hasTerrainMesh = TerrainWalkMeshRaycast.TrySampleTerrainTopY(terrain, worldX, worldZ, out var terrainMeshY);
        return SampleCellWithTerrainMeshY(
            world,
            worldX,
            worldZ,
            terrainMeshY,
            hasTerrainMesh);
    }

    public static SampleResult SampleCellWithTerrainMeshY(
        World3D world,
        float worldX,
        float worldZ,
        float terrainMeshY,
        bool hasTerrainMesh = true)
    {
        if (!TryRaycastPhysicsTop(world, worldX, worldZ, out var standingY, out var normal))
        {
            if (hasTerrainMesh)
            {
                return new SampleResult(
                    hasGround: true,
                    standingY: terrainMeshY,
                    terrainMeshY,
                    blocked: false,
                    isWalkable: true);
            }

            return new SampleResult(false, default, terrainMeshY, blocked: true, isWalkable: false);
        }

        if (normal.Y < MinWalkableNormalY)
        {
            return new SampleResult(true, standingY, terrainMeshY, blocked: true, isWalkable: false);
        }

        if (hasTerrainMesh
            && standingY - terrainMeshY > OutdoorFieldConfig.MaxOutdoorSpawnAboveTerrainMeters)
        {
            return new SampleResult(true, standingY, terrainMeshY, blocked: true, isWalkable: false);
        }

        var baseline = hasTerrainMesh ? terrainMeshY : standingY;
        return new SampleResult(true, standingY, baseline, blocked: false, isWalkable: true);
    }

    public static bool TryRaycastPhysicsTop(
        World3D world,
        float worldX,
        float worldZ,
        out float standingY,
        out Vector3 normal)
    {
        standingY = default;
        normal = Vector3.Up;
        var from = new Vector3(worldX, TerrainWalkMeshRaycast.RayTopY, worldZ);
        var to = new Vector3(worldX, TerrainWalkMeshRaycast.RayBottomY, worldZ);
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.CollisionMask = uint.MaxValue;

        var hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return false;
        }

        normal = hit.TryGetValue("normal", out var normalVariant)
            ? (Vector3)normalVariant
            : Vector3.Up;
        standingY = ((Vector3)hit["position"]).Y;
        return true;
    }
}
