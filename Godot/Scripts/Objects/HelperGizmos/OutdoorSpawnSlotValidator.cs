using Godot;
using SphServer.Godot.Scripts.Navigation;
using SphServer.Godot.Scripts.Terrain;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
/// Walkability is one navmesh disc: outdoor GeneratedNavMeshes plus indoor
/// GeneratedIndoorNavMeshes. Dungeon probes map SOURCE_BASIS into the nav frame inside the runtime
/// </summary>
public static class OutdoorSpawnSlotValidator
{
    public enum FailReason
    {
        None,
        NotWalkable,
        OutsideLeash,
        OutsideSpawnRadius,
        WrongLevel,
    }

    public static bool TryValidateCandidate (
        MonsterSpawner spawner,
        Vector3 candidate,
        Vector3 spawnerOrigin,
        out Vector3 refinedCandidate,
        out FailReason reason)
        => TryValidateCandidate (
            spawnerOrigin,
            spawner.SpawnRadiusMeters,
            spawner.LeashRadiusMeters,
            candidate,
            fastBake: false,
            out refinedCandidate,
            out reason);

    /// <summary>
    /// candidate.Y is only a seed. The probe uses spawnerOrigin.Y. refinedCandidate.Y is navmesh
    /// ground. fastBake rejects on one point, then a 4-point disc
    /// </summary>
    public static bool TryValidateCandidate (
        Vector3 spawnerOrigin,
        float spawnRadiusMeters,
        float leashRadiusMeters,
        Vector3 candidate,
        out Vector3 refinedCandidate,
        out FailReason reason)
        => TryValidateCandidate (
            spawnerOrigin,
            spawnRadiusMeters,
            leashRadiusMeters,
            candidate,
            fastBake: false,
            out refinedCandidate,
            out reason);

    public static bool TryValidateCandidate (
        Vector3 spawnerOrigin,
        float spawnRadiusMeters,
        float leashRadiusMeters,
        Vector3 candidate,
        bool fastBake,
        out Vector3 refinedCandidate,
        out FailReason reason)
    {
        reason = FailReason.None;
        refinedCandidate = candidate;

        var probePoint = new Vector3 (candidate.X, spawnerOrigin.Y, candidate.Z);

        // Outdoor markers often float above nav. refineY:false rejects those probes before the disc
        // check
        if (fastBake && !TerrainNavMeshRuntime.IsPointOnNavMesh (probePoint, out _, refineY: true))
        {
            reason = FailReason.NotWalkable;
            return false;
        }

        var discMode = fastBake
            ? TerrainNavMeshRuntime.DiscQueryMode.BakeFast
            : TerrainNavMeshRuntime.DiscQueryMode.Full;
        if (!TerrainNavMeshRuntime.IsDiscWalkable (
                probePoint,
                OutdoorFieldConfig.MobBodyRadiusMeters,
                discMode,
                out var snapped))
        {
            reason = FailReason.NotWalkable;
            return false;
        }

        // The disc check is XZ only, so a closer polygon on another floor can win. The spawner's
        // own Y is the anchor
        var maxVerticalDrift = Mathf.Max (
            OutdoorFieldConfig.MinSpawnSlotVerticalDriftMeters,
            spawnRadiusMeters * OutdoorFieldConfig.MaxSpawnSlotVerticalDriftRadiusMultiplier);
        if (Mathf.Abs (snapped.Y - spawnerOrigin.Y) > maxVerticalDrift)
        {
            reason = FailReason.WrongLevel;
            return false;
        }

        refinedCandidate = new Vector3 (candidate.X, snapped.Y, candidate.Z);

        if (!NavPathQuery.IsInsideLeash (refinedCandidate, spawnerOrigin, spawnRadiusMeters))
        {
            reason = FailReason.OutsideSpawnRadius;
            return false;
        }

        if (!NavPathQuery.IsInsideLeash (refinedCandidate, spawnerOrigin, leashRadiusMeters))
        {
            reason = FailReason.OutsideLeash;
            return false;
        }

        return true;
    }
}
