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
        OutsideSpawnRadius,
        Unreachable,
    }

    public enum ValidationMode
    {
        AtlasFootprint,
        LooseTerrain,
    }

    public static bool TryValidateCandidate(
        MonsterSpawner spawner,
        Vector3 candidate,
        Vector3 spawnerOrigin,
        out FailReason reason,
        ValidationMode mode = ValidationMode.AtlasFootprint,
        SpawnSlotBakeContext bakeContext = default)
        => TryValidateCandidate(
            spawnerOrigin,
            spawner.SpawnRadiusMeters,
            spawner.LeashRadiusMeters,
            candidate,
            out reason,
            mode,
            bakeContext);

    public static bool TryValidateCandidate(
        Vector3 spawnerOrigin,
        float spawnRadiusMeters,
        float leashRadiusMeters,
        Vector3 candidate,
        out FailReason reason,
        ValidationMode mode = ValidationMode.AtlasFootprint,
        SpawnSlotBakeContext bakeContext = default)
    {
        reason = FailReason.None;

        if (mode == ValidationMode.AtlasFootprint)
        {
            if (!WalkSurfaceCache.IsSpawnFootprintAcceptable(candidate.X, candidate.Z))
            {
                reason = FailReason.NotWalkable;
                return false;
            }
        }
        else if (!WalkSurfaceCache.IsLooseOutdoorWalkCandidate(candidate.X, candidate.Z))
        {
            reason = FailReason.NotWalkable;
            return false;
        }

        if (!OutdoorPathQuery.IsInsideLeash(candidate, spawnerOrigin, spawnRadiusMeters))
        {
            reason = FailReason.OutsideSpawnRadius;
            return false;
        }

        if (!OutdoorPathQuery.IsInsideLeash(candidate, spawnerOrigin, leashRadiusMeters))
        {
            reason = FailReason.OutsideLeash;
            return false;
        }

        if (mode == ValidationMode.LooseTerrain)
        {
            return true;
        }

        if (bakeContext.HasWalkAnchor)
        {
            if (!OutdoorNavReachability.IsReachableFromAnchor(
                    bakeContext.WalkAnchor,
                    candidate,
                    spawnerOrigin,
                    spawnRadiusMeters,
                    useLooseWalkConnectivity: false))
            {
                reason = FailReason.Unreachable;
                return false;
            }
        }
        else if (!OutdoorNavReachability.IsReachable(
                     spawnerOrigin,
                     candidate,
                     leashRadiusMeters,
                     spawnRadiusMeters))
        {
            reason = FailReason.Unreachable;
            return false;
        }

        return true;
    }
}
