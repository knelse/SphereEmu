using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Server.Config;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Creates deferred <see cref="WorldObject" /> client visibility areas when a player enters range.
///     Avoids thousands of <see cref="Area3D" /> nodes at server startup.
/// </summary>
public static class WorldObjectVisibilityManager
{
	private static readonly object GridLock = new();
	private static readonly Dictionary<(int CellX, int CellZ), List<WorldObject>> Grid = new();

	public static float VisibilityDistanceMeters => ServerConfig.AppConfig.ObjectVisibilityDistance;

	public static void Register(WorldObject worldObject)
	{
		if (Engine.IsEditorHint())
		{
			return;
		}

		var cell = WorldToCell(worldObject.GlobalPosition);
		lock (GridLock)
		{
			if (!Grid.TryGetValue(cell, out var worldObjects))
			{
				worldObjects = [];
				Grid[cell] = worldObjects;
			}

			if (!worldObjects.Contains(worldObject))
			{
				worldObjects.Add(worldObject);
			}
		}
	}

	public static void Unregister(WorldObject worldObject)
	{
		if (Engine.IsEditorHint())
		{
			return;
		}

		lock (GridLock)
		{
			foreach (var worldObjects in Grid.Values)
			{
				worldObjects.Remove(worldObject);
			}
		}
	}

	public static void NotifyClientPosition(SphereClient client)
	{
		if (Engine.IsEditorHint())
		{
			return;
		}

		EnsureAreasNearClient(client);
	}

	public static void CheckAllClients()
	{
		if (Engine.IsEditorHint())
		{
			return;
		}

		foreach (var client in ActiveClients.GetAll().Values)
		{
			EnsureAreasNearClient(client);
		}
	}

	private static void EnsureAreasNearClient(SphereClient client)
	{
		if (client.CurrentCharacter is null)
		{
			return;
		}

		var clientPosition = ClientWorldPosition.GetGodotWorldPosition(client);
		var visibilityRadiusSq = VisibilityDistanceMeters * VisibilityDistanceMeters;
		var centerCell = WorldToCell(clientPosition);

		for (var dx = -1; dx <= 1; dx++)
		{
			for (var dz = -1; dz <= 1; dz++)
			{
				var cell = (centerCell.CellX + dx, centerCell.CellZ + dz);
				List<WorldObject> worldObjects;
				lock (GridLock)
				{
					if (!Grid.TryGetValue(cell, out worldObjects!) || worldObjects.Count == 0)
					{
						continue;
					}

					worldObjects = [.. worldObjects];
				}

				foreach (var worldObject in worldObjects)
				{
					if (!GodotObject.IsInstanceValid(worldObject))
					{
						continue;
					}

					if (worldObject.GlobalPosition.DistanceSquaredTo(clientPosition) > visibilityRadiusSq)
					{
						continue;
					}

					worldObject.EnsureVisibilityArea();
					worldObject.EnsureVisibleToClient(client);
				}
			}
		}
	}

	private static (int CellX, int CellZ) WorldToCell(Vector3 worldPosition)
	{
		var cellSize = VisibilityDistanceMeters;
		return (
			(int)Math.Floor(worldPosition.X / cellSize),
			(int)Math.Floor(worldPosition.Z / cellSize));
	}
}
