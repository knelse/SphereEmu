using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Server.Config;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Activates <see cref="AlchemyMaterialSpawner" /> instances when an in-game client enters
///     <see cref="ServerConfig.AppConfig.ObjectVisibilityDistance" />. Once activated, a spawner
///     stays active for the rest of the session.
/// </summary>
public static class AlchemyMaterialSpawnerActivationManager
{
    private static readonly object GridLock = new();
    private static readonly Dictionary<(int CellX, int CellZ), List<AlchemyMaterialSpawner>> Grid = new();

    public static float ActivationDistanceMeters => ServerConfig.AppConfig.ObjectVisibilityDistance;

    public static void Register(AlchemyMaterialSpawner spawner)
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

    public static void Unregister(AlchemyMaterialSpawner spawner)
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

        var clientPosition = ClientWorldPosition.GetGodotWorldPosition(client);
        var activationRadiusSq = ActivationDistanceMeters * ActivationDistanceMeters;
        var centerCell = WorldToCell(clientPosition);

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                var cell = (centerCell.CellX + dx, centerCell.CellZ + dz);
                List<AlchemyMaterialSpawner> spawners;
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

    private static (int CellX, int CellZ) WorldToCell(Vector3 worldPosition)
    {
        var cellSize = ActivationDistanceMeters;
        return (
            (int)Math.Floor(worldPosition.X / cellSize),
            (int)Math.Floor(worldPosition.Z / cellSize));
    }
}
