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
}

public readonly struct NavPathRequest
{
    public NavPathRequest(
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

/// <summary>
///     Runtime pathfinding over <see cref="TerrainNavMeshRuntime" /> (outdoor tiles + indoor clusters).
/// </summary>
public static class NavPathQuery
{
    private const float TileLoadMarginMeters = 8f;

    public static Vector3 ClampGoalToLeash(Vector3 goalWorld, Vector3 leashCenterWorld, float leashRadiusMeters)
    {
        var dx = goalWorld.X - leashCenterWorld.X;
        var dz = goalWorld.Z - leashCenterWorld.Z;
        var distSq = dx * dx + dz * dz;
        var radiusSq = leashRadiusMeters * leashRadiusMeters;
        if (distSq <= radiusSq || distSq <= 0.0001f)
        {
            return goalWorld;
        }

        var scale = leashRadiusMeters / Mathf.Sqrt(distSq);
        return new Vector3(
            leashCenterWorld.X + dx * scale,
            goalWorld.Y,
            leashCenterWorld.Z + dz * scale);
    }

    public static bool IsInsideLeash(Vector3 worldPosition, Vector3 leashCenterWorld, float leashRadiusMeters)
    {
        var dx = worldPosition.X - leashCenterWorld.X;
        var dz = worldPosition.Z - leashCenterWorld.Z;
        return dx * dx + dz * dz <= leashRadiusMeters * leashRadiusMeters + 0.01f;
    }

    /// <summary>
    ///     Loads nearby outdoor/indoor nav, snaps endpoints onto the mesh, then queries Recast path.
    ///     Positions are spawner / SOURCE_BASIS space (same as monster spawner GlobalPosition).
    /// </summary>
    public static NavPathResult FindPath(Node3D context, NavPathRequest request)
    {
        if (!IsInsideLeash(request.GoalWorld, request.LeashCenterWorld, request.LeashRadiusMeters))
        {
            return Fail(NavPathFailReason.GoalOutsideLeash);
        }

        if (!TerrainNavMeshRuntime.HasAnyTileFiles())
        {
            return Fail(NavPathFailReason.NavDataMissing);
        }

        TerrainNavMeshRuntime.EnsureTilesLoaded(
            context,
            request.LeashCenterWorld,
            request.LeashRadiusMeters + TileLoadMarginMeters);
        TerrainNavMeshRuntime.TrySyncImmediate();

        if (!TerrainNavMeshRuntime.IsReadyForQueries)
        {
            return Fail(NavPathFailReason.NavDataMissing);
        }

        if (!TerrainNavMeshRuntime.IsPointOnNavMesh(request.StartWorld, out var startSnap))
        {
            return Fail(NavPathFailReason.StartUnwalkable);
        }

        if (!TerrainNavMeshRuntime.IsPointOnNavMesh(request.GoalWorld, out var goalSnap))
        {
            return Fail(NavPathFailReason.GoalUnwalkable);
        }

        var path = TerrainNavMeshRuntime.FindPath(startSnap, goalSnap, optimize: true);
        if (path.Length == 0)
        {
            return Fail(NavPathFailReason.NoPath);
        }

        var waypoints = new List<Vector3>(path.Length);
        foreach (var point in path)
        {
            waypoints.Add(point);
        }

        return new NavPathResult
        {
            Success = true,
            Reason = NavPathFailReason.None,
            Waypoints = waypoints,
        };
    }

    /// <summary>
    ///     Sample ground Y from the loaded navmesh at XZ (probe Y seeds the closest-point search).
    /// </summary>
    public static bool TrySampleGroundY(float worldX, float worldZ, float probeY, out float groundY)
    {
        groundY = default;
        if (!TerrainNavMeshRuntime.TryClosestPoint(new Vector3(worldX, probeY, worldZ), out var closest))
        {
            return false;
        }

        groundY = closest.Y;
        return true;
    }

    private static NavPathResult Fail(NavPathFailReason reason)
        => new() { Success = false, Reason = reason, Waypoints = [] };
}
