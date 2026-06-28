using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;

namespace SphServer.Godot.Scripts.Terrain;

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

        spawner.SetBakedSpawnSlots(slots);
        return slots.Count;
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
}
