using System.Collections.Generic;
using Godot;

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
            return true;
        }

        if (!OutdoorPathQuery.IsInsideLeash(targetWorld, spawnerOrigin, leashRadiusMeters))
        {
            return false;
        }

        if (!OutdoorNavCache.IsWalkable(spawnerOrigin.X, spawnerOrigin.Z))
        {
            return OutdoorNavCache.IsWalkable(targetWorld.X, targetWorld.Z);
        }

        OutdoorNavCache.PreloadForRadius(spawnerOrigin.X, spawnerOrigin.Z, leashRadiusMeters);
        var start = (Gx: (int)Mathf.Round(spawnerOrigin.X), Gz: (int)Mathf.Round(spawnerOrigin.Z));
        var goal = (Gx: (int)Mathf.Round(targetWorld.X), Gz: (int)Mathf.Round(targetWorld.Z));
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

                var world = new Vector3(neighbor.Gx, 0f, neighbor.Gz);
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
}
