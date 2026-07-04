using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Pre-bakes validated outdoor spawn slots for monster spawners from the walk atlas spawn channel.
/// </summary>
public static class MonsterSpawnSlotBaker
{
    public static int BakeForSpawner(MonsterSpawner spawner)
    {
        var targetCount = spawner.TargetRegularMonsterCount + spawner.TargetNamedMonsterCount;
        if (targetCount <= 0)
        {
            spawner.SetBakedSpawnSlots([]);
            return 0;
        }

        if (!WalkSurfaceCache.HasAnyChunkFiles())
        {
            GD.PushWarning($"MonsterSpawnSlotBaker: no walk atlas for spawner '{spawner.Name}'.");
            spawner.SetBakedSpawnSlots([]);
            return 0;
        }

        if (!WalkSurfaceCache.HasOutdoorSpawnChannel)
        {
            GD.PushWarning(
                $"MonsterSpawnSlotBaker: walk chunks have no outdoor spawn channel (format v4). "
                + "Rebake walk surface with --objects-only or full bake first.");
            spawner.SetBakedSpawnSlots([]);
            return 0;
        }

        if (OutdoorNavCache.HasAnyNavFiles())
        {
            OutdoorNavCache.PreloadForRadius(
                spawner.LeashCenterWorld.X,
                spawner.LeashCenterWorld.Z,
                spawner.LeashRadiusMeters);
        }

        var origin = spawner.GlobalPosition;
        if (!WalkSurfaceOutdoorSpawnQuery.TryPickSpawnSlots(
                origin,
                spawner.SpawnRadiusMeters,
                targetCount,
                MonsterSpawnPlacement.MinMobSeparationMeters,
                existingOccupied: null,
                out var slots))
        {
            GD.PushWarning(
                $"MonsterSpawnSlotBaker: found {slots.Count}/{targetCount} slot(s) for spawner '{spawner.Name}'.");
        }

        var validated = FilterSlots(spawner, slots);
        if (validated.Count < slots.Count)
        {
            GD.PushWarning(
                $"MonsterSpawnSlotBaker: kept {validated.Count}/{slots.Count} slot(s) after leash/nav validation for '{spawner.Name}'.");
        }

        spawner.SetBakedSpawnSlots(validated);
        return validated.Count;
    }

    public static int BakeAllUnder(Node parent)
    {
        var baked = 0;
        var slotCount = 0;
        foreach (var child in parent.GetChildren())
        {
            if (child is not MonsterSpawner spawner || !GodotObject.IsInstanceValid(spawner))
            {
                continue;
            }

            slotCount += BakeForSpawner(spawner);
            baked++;
        }

        GD.Print($"MonsterSpawnSlotBaker: baked {slotCount} slot(s) across {baked} spawner(s).");
        return slotCount;
    }

    private static List<Vector3> FilterSlots(MonsterSpawner spawner, List<Vector3> candidates)
    {
        var origin = spawner.GlobalPosition;
        var validated = new List<Vector3>(candidates.Count);
        foreach (var slot in candidates)
        {
            if (!OutdoorPathQuery.IsInsideLeash(slot, origin, spawner.LeashRadiusMeters))
            {
                continue;
            }

            if (OutdoorNavCache.HasAnyNavFiles()
                && !OutdoorNavReachability.IsReachable(origin, slot, spawner.LeashRadiusMeters))
            {
                continue;
            }

            validated.Add(slot);
        }

        return validated;
    }
}
