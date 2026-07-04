using System;
using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.OutdoorNav;

public static class OutdoorNavReachability
{
    private static readonly (int Dx, int Dz)[] NeighborOffsets =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    ];

    public static bool IsReachable(
        Vector3 spawnerOrigin,
        Vector3 targetWorld,
        float leashRadiusMeters)
    {
        if (!OutdoorNavCache.HasAnyNavFiles())
        {
            return WalkSurfaceCache.HasWalkableField
                && WalkSurfaceCache.IsWalkableAt(targetWorld.X, targetWorld.Z);
        }

        if (!OutdoorPathQuery.IsInsideLeash(targetWorld, spawnerOrigin, leashRadiusMeters))
        {
            return false;
        }

        var spacing = OutdoorNavCache.SampleSpacingMeters;
        if (!OutdoorNavCache.IsWalkable(spawnerOrigin.X, spawnerOrigin.Z))
        {
            return OutdoorNavCache.IsWalkable(targetWorld.X, targetWorld.Z);
        }

        OutdoorNavCache.PreloadForRadius(spawnerOrigin.X, spawnerOrigin.Z, leashRadiusMeters);
        var start = WorldToCell(spawnerOrigin, spacing);
        var goal = WorldToCell(targetWorld, spacing);
        if (start == goal)
        {
            return OutdoorNavCache.IsWalkable(targetWorld.X, targetWorld.Z);
        }

        var visited = new HashSet<(int, int)> { start };
        var queue = new Queue<(int Gx, int Gz)>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var (dx, dz) in NeighborOffsets)
            {
                var neighbor = (Gx: current.Gx + dx, Gz: current.Gz + dz);
                if (!visited.Add(neighbor))
                {
                    continue;
                }

                var world = CellToWorld(neighbor, spacing);
                if (!OutdoorPathQuery.IsInsideLeash(world, spawnerOrigin, leashRadiusMeters))
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

    private static (int Gx, int Gz) WorldToCell(Vector3 world, float spacing)
    {
        return ((int)Mathf.Round(world.X / spacing), (int)Mathf.Round(world.Z / spacing));
    }

    private static Vector3 CellToWorld((int Gx, int Gz) cell, float spacing)
    {
        return new Vector3(cell.Gx * spacing, 0f, cell.Gz * spacing);
    }
}
