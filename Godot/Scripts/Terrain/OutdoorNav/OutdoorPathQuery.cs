using System;
using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.OutdoorNav;

public enum NavPathFailReason
{
    None,
    NavDataMissing,
    StartUnwalkable,
    GoalUnwalkable,
    GoalOutsideLeash,
    NoPath,
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

    public static NavPathResult FindPath(NavPathRequest request)
    {
        if (!OutdoorNavCache.HasAnyNavFiles())
        {
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

        if (!TrySnapToWalkable(request.StartWorld, request.LeashCenterWorld, request.LeashRadiusMeters, out var startCell))
        {
            return Fail(NavPathFailReason.StartUnwalkable);
        }

        if (!TrySnapToWalkable(request.GoalWorld, request.LeashCenterWorld, request.LeashRadiusMeters, out var goalCell))
        {
            return Fail(NavPathFailReason.GoalUnwalkable);
        }

        if (startCell == goalCell)
        {
            return Success(SingleWaypoint(goalCell));
        }

        var open = new PriorityQueue<(int Gx, int Gz), float>();
        var cameFrom = new Dictionary<(int Gx, int Gz), (int Gx, int Gz)>();
        var gScore = new Dictionary<(int Gx, int Gz), float> { [startCell] = 0f };
        open.Enqueue(startCell, Heuristic(startCell, goalCell));

        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current == goalCell)
            {
                return Success(ReconstructPath(cameFrom, current, startCell));
            }

            foreach (var (dx, dz) in NeighborOffsets)
            {
                var neighbor = (current.Gx + dx, current.Gz + dz);
                var neighborWorld = CellToWorld(neighbor);
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
                open.Enqueue(neighbor, tentative + Heuristic(neighbor, goalCell));
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

    private static bool TrySnapToWalkable(
        Vector3 worldPosition,
        Vector3 leashCenterWorld,
        float leashRadiusMeters,
        out (int Gx, int Gz) cell)
    {
        cell = WorldToCell(worldPosition);
        var world = CellToWorld(cell);
        if (OutdoorNavCache.IsWalkable(world.X, world.Z)
            && IsInsideLeash(world, leashCenterWorld, leashRadiusMeters))
        {
            return true;
        }

        var spacing = OutdoorNavCache.SampleSpacingMeters;
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

                cell = WorldToCell(probe);
                return true;
            }
        }

        return false;
    }

    private static List<Vector3> ReconstructPath(
        Dictionary<(int Gx, int Gz), (int Gx, int Gz)> cameFrom,
        (int Gx, int Gz) current,
        (int Gx, int Gz) startCell)
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
            var world = CellToWorld(cell);
            if (OutdoorNavCache.TrySampleTerrainY(world.X, world.Z, out var y))
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

    private static List<Vector3> SingleWaypoint((int Gx, int Gz) cell)
    {
        var world = CellToWorld(cell);
        if (OutdoorNavCache.TrySampleTerrainY(world.X, world.Z, out var y))
        {
            world.Y = y;
        }

        return [world];
    }

    private static (int Gx, int Gz) WorldToCell(Vector3 world)
    {
        return ((int)Mathf.Round(world.X), (int)Mathf.Round(world.Z));
    }

    private static Vector3 CellToWorld((int Gx, int Gz) cell)
    {
        return new Vector3(cell.Gx, 0f, cell.Gz);
    }

    private static float Heuristic((int Gx, int Gz) from, (int Gx, int Gz) to)
    {
        var dx = from.Gx - to.Gx;
        var dz = from.Gz - to.Gz;
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
