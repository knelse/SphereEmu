using System;
using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.OutdoorNav;

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

public static class OutdoorPathQuery
{
    private static readonly (int Dx, int Dz)[] NeighborOffsets =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    ];

    private static int _requestsThisTick;
    private static ulong _lastBudgetFrame;

    public static NavPathResult FindPath(NavPathRequest request)
    {
        if (!TryBeginPathRequest())
        {
            return Fail(NavPathFailReason.SearchBudgetExceeded);
        }

        if (!OutdoorNavCache.HasAnyNavFiles())
        {
            if (WalkSurfaceCache.HasWalkableField)
            {
                return DirectPathFallback(request);
            }

            return Fail(NavPathFailReason.NavDataMissing);
        }

        var spacing = OutdoorNavCache.SampleSpacingMeters;
        OutdoorNavCache.PreloadForRadius(
            request.LeashCenterWorld.X,
            request.LeashCenterWorld.Z,
            request.LeashRadiusMeters + spacing);

        if (!IsInsideLeash(request.GoalWorld, request.LeashCenterWorld, request.LeashRadiusMeters))
        {
            return Fail(NavPathFailReason.GoalOutsideLeash);
        }

        if (!TrySnapToWalkable(request.StartWorld, request.LeashCenterWorld, request.LeashRadiusMeters, spacing, out var startCell))
        {
            return Fail(NavPathFailReason.StartUnwalkable);
        }

        if (!TrySnapToWalkable(request.GoalWorld, request.LeashCenterWorld, request.LeashRadiusMeters, spacing, out var goalCell))
        {
            return Fail(NavPathFailReason.GoalUnwalkable);
        }

        if (startCell == goalCell)
        {
            return Success(SingleWaypoint(goalCell, spacing));
        }

        var open = new PriorityQueue<(int Gx, int Gz), float>();
        var cameFrom = new Dictionary<(int Gx, int Gz), (int Gx, int Gz)>();
        var gScore = new Dictionary<(int Gx, int Gz), float> { [startCell] = 0f };
        open.Enqueue(startCell, Heuristic(startCell, goalCell, spacing));
        var expanded = 0;

        while (open.Count > 0)
        {
            if (++expanded > OutdoorFieldConfig.AStarMaxExpandedNodes)
            {
                return DirectPathFallback(request);
            }

            var current = open.Dequeue();
            if (current == goalCell)
            {
                return Success(ReconstructPath(cameFrom, current, startCell, spacing));
            }

            foreach (var (dx, dz) in NeighborOffsets)
            {
                var neighbor = (current.Gx + dx, current.Gz + dz);
                var neighborWorld = CellToWorld(neighbor, spacing);
                if (!IsInsideLeash(neighborWorld, request.LeashCenterWorld, request.LeashRadiusMeters))
                {
                    continue;
                }

                if (!OutdoorNavCache.IsWalkable(neighborWorld.X, neighborWorld.Z))
                {
                    continue;
                }

                var step = dx == 0 || dz == 0 ? spacing : spacing * 1.4142135f;
                var tentative = gScore[current] + step;
                if (gScore.TryGetValue(neighbor, out var existing) && tentative >= existing)
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentative;
                open.Enqueue(neighbor, tentative + Heuristic(neighbor, goalCell, spacing));
            }
        }

        return Fail(NavPathFailReason.NoPath);
    }

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
    ///     Cheap atlas-only ground sample for runtime nav. Avoids physics raycasts.
    ///     When <paramref name="atlasVerticalDelta" /> is non-zero, converts atlas Y to Godot world Y.
    /// </summary>
    public static bool TrySampleOutdoorGroundY(
        float worldX,
        float worldZ,
        out float worldY,
        float atlasVerticalDelta = 0f)
    {
        worldY = default;
        if (!TrySampleRawAtlasGroundY(worldX, worldZ, out var atlasY))
        {
            return false;
        }

        worldY = !Mathf.IsZeroApprox(atlasVerticalDelta) ? atlasY - atlasVerticalDelta : atlasY;
        return true;
    }

    private static bool TrySampleRawAtlasGroundY(float worldX, float worldZ, out float atlasY)
    {
        if (WalkSurfaceCache.TrySampleWalkableGround(worldX, worldZ, out atlasY))
        {
            return true;
        }

        return OutdoorNavCache.TrySampleTerrainY(worldX, worldZ, out atlasY);
    }

    private static bool TryBeginPathRequest()
    {
        var frame = Engine.GetProcessFrames();
        if (frame != _lastBudgetFrame)
        {
            _lastBudgetFrame = frame;
            _requestsThisTick = 0;
        }

        if (_requestsThisTick >= OutdoorFieldConfig.PathRequestsPerTick)
        {
            return false;
        }

        _requestsThisTick++;
        return true;
    }

    private static NavPathResult DirectPathFallback(NavPathRequest request)
    {
        var goal = ClampGoalToLeash(request.GoalWorld, request.LeashCenterWorld, request.LeashRadiusMeters);
        if (WalkSurfaceCache.TrySampleWalkableGround(goal.X, goal.Z, out var goalY))
        {
            goal.Y = goalY;
        }

        return Success([goal]);
    }

    private static bool TrySnapToWalkable(
        Vector3 worldPosition,
        Vector3 leashCenterWorld,
        float leashRadiusMeters,
        float spacing,
        out (int Gx, int Gz) cell)
    {
        cell = WorldToCell(worldPosition, spacing);
        var world = CellToWorld(cell, spacing);
        if (OutdoorNavCache.IsWalkable(world.X, world.Z)
            && IsInsideLeash(world, leashCenterWorld, leashRadiusMeters))
        {
            return true;
        }

        for (var ring = 1; ring <= 4; ring++)
        {
            var radius = ring * spacing;
            var samples = Math.Max(8, ring * 8);
            for (var sample = 0; sample < samples; sample++)
            {
                var angle = (float)(sample * Math.Tau / samples);
                var probe = new Vector3(
                    worldPosition.X + Mathf.Cos(angle) * radius,
                    worldPosition.Y,
                    worldPosition.Z + Mathf.Sin(angle) * radius);
                if (!IsInsideLeash(probe, leashCenterWorld, leashRadiusMeters))
                {
                    continue;
                }

                if (!OutdoorNavCache.IsWalkable(probe.X, probe.Z))
                {
                    continue;
                }

                cell = WorldToCell(probe, spacing);
                return true;
            }
        }

        return false;
    }

    private static List<Vector3> ReconstructPath(
        Dictionary<(int Gx, int Gz), (int Gx, int Gz)> cameFrom,
        (int Gx, int Gz) current,
        (int Gx, int Gz) startCell,
        float spacing)
    {
        var cells = new List<(int Gx, int Gz)> { current };
        while (current != startCell)
        {
            current = cameFrom[current];
            cells.Add(current);
        }

        cells.Reverse();
        var waypoints = new List<Vector3>(cells.Count);
        foreach (var cell in cells)
        {
            var world = CellToWorld(cell, spacing);
            if (OutdoorNavCache.TrySampleTerrainY(world.X, world.Z, out var y))
            {
                waypoints.Add(new Vector3(world.X, y, world.Z));
            }
            else if (WalkSurfaceCache.TrySampleWalkableGround(world.X, world.Z, out y))
            {
                waypoints.Add(new Vector3(world.X, y, world.Z));
            }
            else
            {
                waypoints.Add(world);
            }
        }

        return waypoints;
    }

    private static List<Vector3> SingleWaypoint((int Gx, int Gz) cell, float spacing)
    {
        var world = CellToWorld(cell, spacing);
        if (OutdoorNavCache.TrySampleTerrainY(world.X, world.Z, out var y))
        {
            world.Y = y;
        }
        else if (WalkSurfaceCache.TrySampleWalkableGround(world.X, world.Z, out y))
        {
            world.Y = y;
        }

        return [world];
    }

    private static (int Gx, int Gz) WorldToCell(Vector3 world, float spacing)
    {
        return ((int)Mathf.Round(world.X / spacing), (int)Mathf.Round(world.Z / spacing));
    }

    private static Vector3 CellToWorld((int Gx, int Gz) cell, float spacing)
    {
        return new Vector3(cell.Gx * spacing, 0f, cell.Gz * spacing);
    }

    private static float Heuristic((int Gx, int Gz) from, (int Gx, int Gz) to, float spacing)
    {
        var dx = (from.Gx - to.Gx) * spacing;
        var dz = (from.Gz - to.Gz) * spacing;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static NavPathResult Fail(NavPathFailReason reason)
    {
        return new NavPathResult { Success = false, Reason = reason };
    }

    private static NavPathResult Success(List<Vector3> waypoints)
    {
        return new NavPathResult { Success = true, Reason = NavPathFailReason.None, Waypoints = waypoints };
    }
}
