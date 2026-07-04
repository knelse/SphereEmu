using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Helpers;
using SphServer.Server.Config;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;
using static SphServer.Helpers.PoiType;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Activates <see cref="MonsterSpawner" /> instances when an in-game client enters
///     <see cref="ServerConfig.AppConfig.ObjectVisibilityDistance" />. Once activated, a spawner
///     stays active for the rest of the session (no deactivation when clients leave).
/// </summary>
public static class MonsterSpawnerActivationManager
{
    private const float DebugTownActivationRadiusMeters = 100f;

    private static readonly object GridLock = new();
    private static readonly Dictionary<(int CellX, int CellZ), List<MonsterSpawner>> Grid = new();

    public static float ActivationDistanceMeters => ServerConfig.AppConfig.ObjectVisibilityDistance;

    public static void Register(MonsterSpawner spawner)
    {
        if (Engine.IsEditorHint())
        {
            return;
        }

        var cell = WorldToCell(spawner.GlobalPosition);
        lock (GridLock)
        {
            if (!Grid.TryGetValue(cell, out var spawners))
            {
                spawners = [];
                Grid[cell] = spawners;
            }

            if (!spawners.Contains(spawner))
            {
                spawners.Add(spawner);
            }
        }
    }

    public static void Unregister(MonsterSpawner spawner)
    {
        if (Engine.IsEditorHint())
        {
            return;
        }

        lock (GridLock)
        {
            foreach (var spawners in Grid.Values)
            {
                spawners.Remove(spawner);
            }
        }
    }

    public static void NotifyClientPosition(SphereClient client)
    {
        if (Engine.IsEditorHint())
        {
            return;
        }

        ActivateSpawnersNearClient(client);
    }

    public static void CheckAllClients()
    {
        if (Engine.IsEditorHint())
        {
            return;
        }

        foreach (var client in ActiveClients.GetAll().Values)
        {
            ActivateSpawnersNearClient(client);
        }
    }

    private static void ActivateSpawnersNearClient(SphereClient client)
    {
        if (client.CurrentCharacter is null)
        {
            return;
        }

        var clientPosition = GetClientWorldPosition(client);
        var activationRadiusSq = ActivationDistanceMeters * ActivationDistanceMeters;
        var centerCell = WorldToCell(clientPosition);

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                var cell = (centerCell.CellX + dx, centerCell.CellZ + dz);
                List<MonsterSpawner> spawners;
                lock (GridLock)
                {
                    if (!Grid.TryGetValue(cell, out spawners!) || spawners.Count == 0)
                    {
                        continue;
                    }

                    spawners = [.. spawners];
                }

                foreach (var spawner in spawners)
                {
                    if (!GodotObject.IsInstanceValid(spawner) || spawner.IsActivated)
                    {
                        continue;
                    }

                    if (spawner.GlobalPosition.DistanceSquaredTo(clientPosition) > activationRadiusSq)
                    {
                        continue;
                    }

                    spawner.ActivateFromProximity();
                }
            }
        }
    }

    private static Vector3 GetClientWorldPosition(SphereClient client)
    {
        var character = client.CurrentCharacter!;
        return new Vector3((float)character.X, (float)-character.Y, (float)-character.Z);
    }

    private static (int CellX, int CellZ) WorldToCell(Vector3 worldPosition)
    {
        var cellSize = ActivationDistanceMeters;
        return (
            (int)Math.Floor(worldPosition.X / cellSize),
            (int)Math.Floor(worldPosition.Z / cellSize));
    }
}

/// <summary>
///     Server tick fallback: clients that have not moved recently still activate nearby spawners
///     and world-object visibility areas.
/// </summary>
public partial class MonsterSpawnerActivationManagerNode : Node
{
    private const double CheckIntervalSeconds = 1.0;
    private double _elapsedSeconds;

    public override void _Process(double delta)
    {
        _elapsedSeconds += delta;
        if (_elapsedSeconds < CheckIntervalSeconds)
        {
            return;
        }

        _elapsedSeconds = 0;
        MonsterSpawnerActivationManager.CheckAllClients();
        WorldObjectVisibilityManager.CheckAllClients();
    }
}
