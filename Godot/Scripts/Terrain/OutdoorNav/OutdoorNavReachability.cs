using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.OutdoorNav;

public static class OutdoorNavReachability
{
    private const int MaxPathExpandedNodes = 8192;

    private static readonly (int Dx, int Dz)[] NeighborOffsets =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    ];

    public static bool IsReachable(
        Vector3 spawnerOrigin,
        Vector3 targetWorld,
        float leashRadiusMeters,
        float anchorSearchRadiusMeters)
    {
        if (!OutdoorPathQuery.IsInsideLeash(targetWorld, spawnerOrigin, leashRadiusMeters))
        {
            return false;
        }

        var anchorRadius = Mathf.Min(anchorSearchRadiusMeters, leashRadiusMeters);
        if (!TryResolveWalkAnchor(spawnerOrigin, anchorRadius, out var anchor))
        {
            return false;
        }

        return IsReachableFromAnchor(anchor, targetWorld, spawnerOrigin, anchorSearchRadiusMeters, useLooseWalkConnectivity: false);
    }

    public static bool IsReachableFromAnchor(
        Vector3 anchorWorld,
        Vector3 targetWorld,
        Vector3 spawnerOrigin,
        float pathSearchRadiusMeters,
        bool useLooseWalkConnectivity = false)
    {
        if (!OutdoorPathQuery.IsInsideLeash(targetWorld, spawnerOrigin, pathSearchRadiusMeters))
        {
            return false;
        }

        if (useLooseWalkConnectivity || !OutdoorNavCache.HasAnyNavFiles())
        {
            return IsLooseWalkReachableFromAnchor(anchorWorld, targetWorld, spawnerOrigin, pathSearchRadiusMeters);
        }

        if (!OutdoorNavCache.IsWalkable(targetWorld.X, targetWorld.Z))
        {
            return false;
        }

        OutdoorNavCache.PreloadForRadius(spawnerOrigin.X, spawnerOrigin.Z, pathSearchRadiusMeters + 1f);
        return BreadthFirstNavReachable(anchorWorld, targetWorld, spawnerOrigin, pathSearchRadiusMeters);
    }

    public static bool IsLooseWalkReachableFromAnchor(
        Vector3 anchorWorld,
        Vector3 targetWorld,
        Vector3 spawnerOrigin,
        float pathSearchRadiusMeters)
    {
        if (!OutdoorPathQuery.IsInsideLeash(targetWorld, spawnerOrigin, pathSearchRadiusMeters))
        {
            return false;
        }

        if (!WalkSurfaceCache.IsLooseOutdoorWalkCandidate(targetWorld.X, targetWorld.Z))
        {
            return false;
        }

        return BreadthFirstLooseReachable(anchorWorld, targetWorld, spawnerOrigin, pathSearchRadiusMeters);
    }

    private static bool BreadthFirstNavReachable(
        Vector3 anchorWorld,
        Vector3 targetWorld,
        Vector3 spawnerOrigin,
        float pathSearchRadiusMeters)
    {
        var spacing = OutdoorNavCache.SampleSpacingMeters;
        var start = WorldToCell(anchorWorld, spacing);
        var goal = WorldToCell(targetWorld, spacing);
        if (start == goal)
        {
            return OutdoorNavCache.IsWalkable(targetWorld.X, targetWorld.Z);
        }

        var visited = new HashSet<(int, int)> { start };
        var queue = new Queue<(int Gx, int Gz)>();
        queue.Enqueue(start);
        var expanded = 0;
        while (queue.Count > 0 && expanded < MaxPathExpandedNodes)
        {
            expanded++;
            var current = queue.Dequeue();
            foreach (var (dx, dz) in NeighborOffsets)
            {
                var neighbor = (Gx: current.Gx + dx, Gz: current.Gz + dz);
                if (!visited.Add(neighbor))
                {
                    continue;
                }

                var world = CellToWorld(neighbor, spacing);
                if (!OutdoorPathQuery.IsInsideLeash(world, spawnerOrigin, pathSearchRadiusMeters))
                {
                    continue;
                }

                if (!OutdoorNavCache.IsWalkable(world.X, world.Z))
                {
                    continue;
                }

                if (neighbor == goal)
                {
                    return true;
                }

                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    private static bool BreadthFirstLooseReachable(
        Vector3 anchorWorld,
        Vector3 targetWorld,
        Vector3 spawnerOrigin,
        float pathSearchRadiusMeters)
    {
        var spacing = OutdoorFieldConfig.MinSlotSeparationMeters;
        var start = WorldToCell(anchorWorld, spacing);
        var goal = WorldToCell(targetWorld, spacing);
        if (start == goal)
        {
            return WalkSurfaceCache.IsLooseOutdoorWalkCandidate(targetWorld.X, targetWorld.Z);
        }

        var visited = new HashSet<(int, int)> { start };
        var queue = new Queue<(int Gx, int Gz)>();
        queue.Enqueue(start);
        var expanded = 0;
        while (queue.Count > 0 && expanded < MaxPathExpandedNodes)
        {
            expanded++;
            var current = queue.Dequeue();
            foreach (var (dx, dz) in NeighborOffsets)
            {
                var neighbor = (Gx: current.Gx + dx, Gz: current.Gz + dz);
                if (!visited.Add(neighbor))
                {
                    continue;
                }

                var world = CellToWorld(neighbor, spacing);
                if (!OutdoorPathQuery.IsInsideLeash(world, spawnerOrigin, pathSearchRadiusMeters))
                {
                    continue;
                }

                if (!WalkSurfaceCache.IsLooseOutdoorWalkCandidate(world.X, world.Z))
                {
                    continue;
                }

                if (neighbor == goal)
                {
                    return true;
                }

                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    private static bool TryResolveWalkAnchor(Vector3 spawnerOrigin, float anchorSearchRadiusMeters, out Vector3 anchor)
    {
        if (OutdoorNavCache.HasAnyNavFiles()
            && OutdoorNavCache.IsWalkable(spawnerOrigin.X, spawnerOrigin.Z)
            && WalkSurfaceCache.TrySampleWalkableGround(spawnerOrigin.X, spawnerOrigin.Z, out var navY))
        {
            anchor = new Vector3(spawnerOrigin.X, navY, spawnerOrigin.Z);
            return true;
        }

        return WalkSurfaceCache.TryFindNearestWalkAnchor(
            spawnerOrigin.X,
            spawnerOrigin.Z,
            anchorSearchRadiusMeters,
            out anchor);
    }

    private static (int Gx, int Gz) WorldToCell(Vector3 world, float spacing)
    {
        return ((int)Mathf.Round(world.X / spacing), (int)Mathf.Round(world.Z / spacing));
    }

    private static Vector3 CellToWorld((int Gx, int Gz) cell, float spacing)
    {
        return new Vector3(cell.Gx * spacing, 0f, cell.Gz * spacing);
    }
}
