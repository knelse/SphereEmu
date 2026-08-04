using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using SphServer.Client;
using SphServer.Godot.Scripts.Terrain.Fill;
using SphServer.Godot.Scripts.World;
using SphServer.Server.Config;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Streams individual ground tiles (MeshInstance + collision) near clients / editor camera.
///     Leaves TerrainObjects MultiMeshes untouched. Expects the Terrain GridMap MeshLibrary to be stripped
///     so opening MainServer does not pull TerrainMeshLibrary.tres.
/// </summary>
[Tool]
public partial class TerrainGroundStreamer : Node
{
	public const string StreamedRootName = "TerrainGroundStreamed";
	public const string ChunksDirectory = "res://Godot/Terrain/GroundChunks";

	private readonly HashSet<(int Gx, int Gz)> loaded = [];
	private TerrainGroundIndex? index;
	private GridMap? terrainGridMap;
	private Node3D? streamedRoot;

	[Export]
	public float LoadRadiusMeters { get; set; }

	[Export]
	public bool AutoLoadAroundClients { get; set; } = true;

	public int LoadedCellCount => loaded.Count;

	public override void _Ready()
	{
		if (LoadRadiusMeters <= 0f)
		{
			LoadRadiusMeters = 200f;
			try
			{
				if (!Engine.IsEditorHint())
				{
					LoadRadiusMeters = ServerConfig.AppConfig.ObjectVisibilityDistance * 2f;
				}
			}
			catch
			{
				// ignore
			}
		}

		var watch = Stopwatch.StartNew();
		index = TerrainGroundIndex.GetOrLoad();
		ResolveTerrainGridMap();
		EnsureStreamedRoot();
		StartupTiming.MarkSpan(
			$"TerrainGroundStreamer._Ready (cells={index.Count})",
			watch.ElapsedMilliseconds);
	}

	public void EnsureAroundWorldPosition(Vector3 worldPosition)
	{
		if (index is null)
		{
			return;
		}

		ResolveTerrainGridMap();
		EnsureStreamedRoot();
		if (terrainGridMap is null)
		{
			return;
		}

		var local = terrainGridMap.ToLocal(worldPosition);
		var center = terrainGridMap.LocalToMap(local);
		var cellRadius = Mathf.Max(1, Mathf.CeilToInt(LoadRadiusMeters / Mathf.Max(1f, terrainGridMap.CellSize.X)));

		for (var gz = center.Z - cellRadius; gz <= center.Z + cellRadius; gz++)
		{
			for (var gx = center.X - cellRadius; gx <= center.X + cellRadius; gx++)
			{
				EnsureCellLoaded(gx, gz);
			}
		}
	}

	public void EnsureCellLoaded(int gx, int gz)
	{
		var key = (gx, gz);
		if (loaded.Contains(key))
		{
			return;
		}

		if (index is null || !index.TryGetMasterName(gx, gz, out var masterName))
		{
			loaded.Add(key);
			return;
		}

		ResolveTerrainGridMap();
		EnsureStreamedRoot();
		if (terrainGridMap is null || streamedRoot is null)
		{
			return;
		}

		var chunkPath = $"{ChunksDirectory}/{gx}_{gz}.tscn";
		if (ResourceLoader.Exists(chunkPath))
		{
			var packed = ResourceLoader.Load<PackedScene>(chunkPath);
			if (packed is not null)
			{
				var instance = packed.Instantiate<Node3D>();
				ClearOwnerRecursive(instance);
				streamedRoot.AddChild(instance);
				loaded.Add(key);
				return;
			}
		}

		// Runtime fallback: build from tile GLB without a pre-packed chunk.
		var mesh = TerrainTileMeshFactory.GetOrBuildMesh(masterName);
		var shape = TerrainTileMeshFactory.GetOrBuildShape(masterName);
		if (mesh is null)
		{
			loaded.Add(key);
			return;
		}

		var cellNode = new Node3D
		{
			Name = $"Ground_{gx}_{gz}",
			Position = terrainGridMap.MapToLocal(new Vector3I(gx, 0, gz)),
		};
		var meshInstance = new MeshInstance3D { Mesh = mesh };
		cellNode.AddChild(meshInstance);
		if (shape is not null)
		{
			var body = new StaticBody3D();
			var collision = new CollisionShape3D { Shape = shape };
			body.AddChild(collision);
			cellNode.AddChild(body);
		}

		streamedRoot.AddChild(cellNode);
		loaded.Add(key);
	}

	public int LoadAll()
	{
		index ??= TerrainGroundIndex.GetOrLoad();
		var count = 0;
		foreach (var (gx, gz, _) in index.EnumerateCells())
		{
			var before = loaded.Count;
			EnsureCellLoaded(gx, gz);
			if (loaded.Count > before)
			{
				count++;
			}
		}

		GD.Print($"TerrainGroundStreamer: LoadAll loaded {count} cell(s), total={loaded.Count}");
		return count;
	}

	public void UnloadAll()
	{
		if (streamedRoot is not null)
		{
			foreach (var child in streamedRoot.GetChildren())
			{
				streamedRoot.RemoveChild(child);
				child.Free();
			}
		}

		loaded.Clear();
	}

	public void NotifyClientPosition(SphereClient client)
	{
		if (!AutoLoadAroundClients || Engine.IsEditorHint())
		{
			return;
		}

		if (client.CurrentCharacter is null)
		{
			return;
		}

		EnsureAroundWorldPosition(ClientWorldPosition.GetGodotWorldPosition(client));
	}

	public void CheckAllClients()
	{
		if (!AutoLoadAroundClients || Engine.IsEditorHint())
		{
			return;
		}

		foreach (var client in ActiveClients.GetAll().Values)
		{
			NotifyClientPosition(client);
		}
	}

	private void ResolveTerrainGridMap()
	{
		if (GodotObject.IsInstanceValid(terrainGridMap))
		{
			return;
		}

		terrainGridMap = null;
		Node? walk = this;
		while (walk is not null)
		{
			if (walk.FindChild(TerrainGridFill.TerrainNodeName, recursive: true, owned: false) is GridMap grid)
			{
				terrainGridMap = grid;
				return;
			}

			walk = walk.GetParent();
		}
	}

	private void EnsureStreamedRoot()
	{
		if (GodotObject.IsInstanceValid(streamedRoot))
		{
			return;
		}

		streamedRoot = null;
		if (terrainGridMap is null)
		{
			return;
		}

		if (terrainGridMap.GetNodeOrNull(StreamedRootName) is Node3D existing)
		{
			streamedRoot = existing;
			return;
		}

		streamedRoot = new Node3D { Name = StreamedRootName };
		terrainGridMap.AddChild(streamedRoot);
		if (Engine.IsEditorHint())
		{
			streamedRoot.Owner = null;
		}
	}

	private static void ClearOwnerRecursive(Node node)
	{
		node.Owner = null;
		foreach (var child in node.GetChildren())
		{
			ClearOwnerRecursive(child);
		}
	}
}
