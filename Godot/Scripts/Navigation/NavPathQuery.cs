using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain;

namespace SphServer.Godot.Scripts.Navigation;

public enum NavPathFailReason
{
    None,
    NavDataMissing,
    StartUnwalkable,
    GoalUnwalkable,
    GoalOutsideLeash,
    NoPath,
    SearchBudgetExceeded,

    /// <summary>
    /// The navmesh returned a path that leaves the leash, jumps floors or climbs walls: a bad
    /// bake joining unrelated meshes, so the mob holds its ground instead of following it
    /// </summary>
    PathRejected,
}

public readonly struct NavPathRequest
{
    public NavPathRequest (
        Vector3 startWorld,
        Vector3 goalWorld,
        Vector3 leashCenterWorld,
        float leashRadiusMeters)
    {
        StartWorld = startWorld;
        GoalWorld = goalWorld;
        LeashCenterWorld = leashCenterWorld;
        LeashRadiusMeters = leashRadiusMeters;
    }

    public Vector3 StartWorld { get; }
    public Vector3 GoalWorld { get; }
    public Vector3 LeashCenterWorld { get; }
    public float LeashRadiusMeters { get; }
}

public sealed class NavPathResult
{
    public bool Success { get; init; }
    public NavPathFailReason Reason { get; init; }
    public List<Vector3> Waypoints { get; init; } = [];
}

public static class NavPathQuery
{
    private const float TileLoadMarginMeters = 8f;
    private const float GroundSampleHorizontalToleranceMeters = 1f;

    /// <summary>
    /// The closest point is a 3D nearest neighbour; farther than this above or below the probe it
    /// is another floor (or a ceiling), not the ground under it
    /// </summary>
    private const float GroundSampleVerticalToleranceMeters = 3f;

    /// <summary>
    /// A snap this far off the asked height lands on another storey or mesh
    /// </summary>
    private const float SnapVerticalToleranceMeters = 2.5f;

    /// <summary>
    /// Waypoints may sit just past the leash edge where the mesh corner rounds it
    /// </summary>
    private const float WaypointLeashMarginMeters = 2f;

    /// <summary>
    /// The bake walks slopes up to 70 degrees (rise 2.75 per metre); steeper than 3:1 between
    /// waypoints is a wall or a drop, not a slope or stairs
    /// </summary>
    private const float MaxWaypointSlope = 3f;

    private const float MinWaypointRiseMeters = 1.5f;


    public static Vector3 ClampGoalToLeash (Vector3 goalWorld, Vector3 leashCenterWorld, float leashRadiusMeters)
    {
        var dx = goalWorld.X - leashCenterWorld.X;
        var dz = goalWorld.Z - leashCenterWorld.Z;
        var distSq = dx * dx + dz * dz;
        var radiusSq = leashRadiusMeters * leashRadiusMeters;
        if (distSq <= radiusSq || distSq <= 0.0001f)
        {
            return goalWorld;
        }

        var scale = leashRadiusMeters / Mathf.Sqrt (distSq);
        return new Vector3 (
            leashCenterWorld.X + dx * scale,
            goalWorld.Y,
            leashCenterWorld.Z + dz * scale);
    }

    public static bool IsInsideLeash (Vector3 worldPosition, Vector3 leashCenterWorld, float leashRadiusMeters)
    {
        var dx = worldPosition.X - leashCenterWorld.X;
        var dz = worldPosition.Z - leashCenterWorld.Z;
        return dx * dx + dz * dz <= leashRadiusMeters * leashRadiusMeters + 0.01f;
    }

    /// <summary>
    /// Positions are spawner / SOURCE_BASIS space (same as MonsterSpawner.GlobalPosition)
    /// </summary>
    public static NavPathResult FindPath (Node3D context, NavPathRequest request)
    {
        if (!IsInsideLeash (request.GoalWorld, request.LeashCenterWorld, request.LeashRadiusMeters))
        {
            return Fail (NavPathFailReason.GoalOutsideLeash);
        }

        if (!TerrainNavMeshRuntime.HasAnyTileFiles ())
        {
            return Fail (NavPathFailReason.NavDataMissing);
        }

        TerrainNavMeshRuntime.EnsureTilesLoaded (
            context,
            request.LeashCenterWorld,
            request.LeashRadiusMeters + TileLoadMarginMeters);
        TerrainNavMeshRuntime.TrySyncImmediate ();

        if (!TerrainNavMeshRuntime.IsReadyForQueries)
        {
            return Fail (NavPathFailReason.NavDataMissing);
        }

        if (!TerrainNavMeshRuntime.IsPointOnNavMesh (request.StartWorld, out var startSnap)
            || Mathf.Abs (startSnap.Y - request.StartWorld.Y) > SnapVerticalToleranceMeters)
        {
            return Fail (NavPathFailReason.StartUnwalkable);
        }

        if (!TerrainNavMeshRuntime.IsPointOnNavMesh (request.GoalWorld, out var goalSnap)
            || Mathf.Abs (goalSnap.Y - request.GoalWorld.Y) > SnapVerticalToleranceMeters)
        {
            return Fail (NavPathFailReason.GoalUnwalkable);
        }

        // Dungeon clusters are remapped into the same navigation map as the outdoor tiles, so a
        // query crossing between them joins meshes that are far apart in the world
        if (IndoorAreaCriteria.IsIndoorDepth (startSnap.Y) != IndoorAreaCriteria.IsIndoorDepth (goalSnap.Y))
        {
            return Fail (NavPathFailReason.PathRejected);
        }

        var path = TerrainNavMeshRuntime.FindPath (startSnap, goalSnap, optimize: true);
        if (path.Length == 0)
        {
            return Fail (NavPathFailReason.NoPath);
        }

        if (!IsPlausiblePath (path, startSnap, request))
        {
            return Fail (NavPathFailReason.PathRejected);
        }

        var waypoints = new List<Vector3> (path.Length);
        foreach (var point in path)
        {
            waypoints.Add (point);
        }

        return new NavPathResult
        {
            Success = true,
            Reason = NavPathFailReason.None,
            Waypoints = waypoints,
        };
    }

    /// <summary>
    /// probeY only seeds the closest-point search; a hit off to the side is another mesh (or none
    /// loaded here), not the ground under this point
    /// </summary>
    public static bool TrySampleGroundY (float worldX, float worldZ, float probeY, out float groundY)
    {
        groundY = default;
        if (!TerrainNavMeshRuntime.TryClosestPoint (new Vector3 (worldX, probeY, worldZ), out var closest))
        {
            return false;
        }

        var dx = closest.X - worldX;
        var dz = closest.Z - worldZ;
        if (dx * dx + dz * dz > GroundSampleHorizontalToleranceMeters * GroundSampleHorizontalToleranceMeters
            || Mathf.Abs (closest.Y - probeY) > GroundSampleVerticalToleranceMeters)
        {
            return false;
        }

        groundY = closest.Y;
        return true;
    }

    /// <summary>
    /// A bad bake can link polygons that are not walkable neighbours: the path then leaves the
    /// leash, climbs walls or crosses to another dungeon or continent
    /// </summary>
    private static bool IsPlausiblePath (Vector3[] path, Vector3 start, NavPathRequest request)
    {
        var indoor = IndoorAreaCriteria.IsIndoorDepth (start.Y);
        var leashRadius = request.LeashRadiusMeters + WaypointLeashMarginMeters;
        var previous = start;
        foreach (var point in path)
        {
            if (!IsInsideLeash (point, request.LeashCenterWorld, leashRadius)
                || IndoorAreaCriteria.IsIndoorDepth (point.Y) != indoor)
            {
                return false;
            }

            var horizontal = new Vector2 (point.X - previous.X, point.Z - previous.Z).Length ();
            var rise = Mathf.Abs (point.Y - previous.Y);
            if (rise > Mathf.Max (MinWaypointRiseMeters, horizontal * MaxWaypointSlope))
            {
                return false;
            }

            previous = point;
        }

        return true;
    }

    private static NavPathResult Fail (NavPathFailReason reason)
        => new () { Success = false, Reason = reason, Waypoints = [] };
}
