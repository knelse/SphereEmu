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

        return true;
    }
}
