using Godot;
using SphServer.Godot.Scripts.Navigation;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Bake-time filters for outdoor spawn slots. Walkability is a single navmesh disc check against
///     <see cref="TerrainNavMeshRuntime" /> - the same navmesh written by <c>TerrainNavigationBaker</c>
///     (via <c>bake_and_export_single_nav.gd</c> + checkpoint) and used
///     by <c>MainServer</c>, so a slot that validates here is guaranteed walkable at runtime too.
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

    public static bool TryValidateCandidate(
        MonsterSpawner spawner,
        Vector3 candidate,
        Vector3 spawnerOrigin,
        out Vector3 refinedCandidate,
        out FailReason reason)
        => TryValidateCandidate(
            spawnerOrigin,
            spawner.SpawnRadiusMeters,
            spawner.LeashRadiusMeters,
            candidate,
            fastBake: false,
            out refinedCandidate,
            out reason);

    /// <summary>
    ///     <paramref name="candidate" />'s Y is only a coarse seed. Probing at <paramref name="spawnerOrigin" />'s
    ///     Y is far more reliable; <paramref name="refinedCandidate" /> comes back with the navmesh's own
    ///     snapped ground Y, which is authoritative.
    /// </summary>
    /// <param name="fastBake">
    ///     Batch rebake path: single-point reject first, then a cheaper 4-point disc query.
    /// </param>
    public static bool TryValidateCandidate(
        Vector3 spawnerOrigin,
        float spawnRadiusMeters,
        float leashRadiusMeters,
        Vector3 candidate,
        out Vector3 refinedCandidate,
        out FailReason reason)
        => TryValidateCandidate(
            spawnerOrigin,
            spawnRadiusMeters,
            leashRadiusMeters,
            candidate,
            fastBake: false,
            out refinedCandidate,
            out reason);

    public static bool TryValidateCandidate(
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

        var probePoint = new Vector3(candidate.X, spawnerOrigin.Y, candidate.Z);

        // Cheap reject before the multi-point disc (batch bake especially).
        if (fastBake && !TerrainNavMeshRuntime.IsPointOnNavMesh(probePoint, out _, refineY: false))
        {
            reason = FailReason.NotWalkable;
            return false;
        }

        var discMode = fastBake
            ? TerrainNavMeshRuntime.DiscQueryMode.BakeFast
            : TerrainNavMeshRuntime.DiscQueryMode.Full;
        if (!TerrainNavMeshRuntime.IsDiscWalkable(
                probePoint,
                OutdoorFieldConfig.MobBodyRadiusMeters,
                discMode,
                out var snapped))
        {
            reason = FailReason.NotWalkable;
            return false;
        }

        // The disc check only validates horizontal (XZ) navmesh containment, so near multi-level geometry it
        // can snap onto a polygon on a totally different floor/roof that happens to be closest in 3D. Guard
        // against that here, where we actually have a stable anchor (the spawner's own placement) to compare
        // against.
        var maxVerticalDrift = Mathf.Max(
            OutdoorFieldConfig.MinSpawnSlotVerticalDriftMeters,
            spawnRadiusMeters * OutdoorFieldConfig.MaxSpawnSlotVerticalDriftRadiusMultiplier);
        if (Mathf.Abs(snapped.Y - spawnerOrigin.Y) > maxVerticalDrift)
        {
            reason = FailReason.WrongLevel;
            return false;
        }

        refinedCandidate = new Vector3(candidate.X, snapped.Y, candidate.Z);

        if (!OutdoorPathQuery.IsInsideLeash(refinedCandidate, spawnerOrigin, spawnRadiusMeters))
        {
            reason = FailReason.OutsideSpawnRadius;
            return false;
        }

        if (!OutdoorPathQuery.IsInsideLeash(refinedCandidate, spawnerOrigin, leashRadiusMeters))
        {
            reason = FailReason.OutsideLeash;
            return false;
        }

        return true;
    }
}
