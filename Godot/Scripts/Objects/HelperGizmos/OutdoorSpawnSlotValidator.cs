using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Bake-time filters for semi-enclosed outdoor spawn slots.
/// </summary>
public static class OutdoorSpawnSlotValidator
{
    public enum FailReason
    {
        None,
        NotWalkable,
        WrongComponent,
        LowOpenness,
        LowOverheadClearance,
        OutsideLeash,
        Unreachable,
    }

    public static bool TryValidateCandidate(
        MonsterSpawner spawner,
        Vector3 candidate,
        Vector3 spawnerOrigin,
        out FailReason reason)
    {
        reason = FailReason.None;

        if (!WalkSurfaceCache.IsSpawnFootprintAcceptable(candidate.X, candidate.Z))
        {
            reason = FailReason.NotWalkable;
            return false;
        }

        if (!OutdoorPathQuery.IsInsideLeash(candidate, spawnerOrigin, spawner.LeashRadiusMeters))
        {
            reason = FailReason.OutsideLeash;
            return false;
        }

        if (OutdoorNavCache.HasAnyNavFiles()
            && !OutdoorNavReachability.IsReachable(spawnerOrigin, candidate, spawner.LeashRadiusMeters))
        {
            reason = FailReason.Unreachable;
            return false;
        }

        var openness = WalkSurfaceCache.MeasureLocalOpenness(
            candidate.X,
            candidate.Z,
            OutdoorFieldConfig.OpennessRadiusMeters);
        if (openness < OutdoorFieldConfig.OpennessThreshold)
        {
            reason = FailReason.LowOpenness;
            return false;
        }

        if (!HasOverheadClearance(spawner, candidate))
        {
            reason = FailReason.LowOverheadClearance;
            return false;
        }

        return true;
    }

    private static bool HasOverheadClearance(MonsterSpawner spawner, Vector3 groundPoint)
    {
        var world = spawner.GetWorld3D();
        if (world is null)
        {
            return true;
        }

        var from = groundPoint + Vector3.Up * 0.05f;
        var to = from + Vector3.Up * OutdoorFieldConfig.OverheadRayHeightMeters;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.CollisionMask = uint.MaxValue & ~(1u << 1);

        var hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return true;
        }

        var hitPosition = (Vector3)hit["position"];
        return hitPosition.Y - groundPoint.Y >= OutdoorFieldConfig.OverheadMinClearanceMeters;
    }
}
