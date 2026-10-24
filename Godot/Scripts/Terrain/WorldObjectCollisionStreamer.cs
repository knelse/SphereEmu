using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Godot.Scripts.Terrain.Fill;
using SphServer.Server.Config;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// StaticBody3D trimeshes for object placements around players, built from ObjectChunks/index.json
/// (ObjectCollisionPackHeadless). Not written into the scene
/// </summary>
public partial class WorldObjectCollisionStreamer : Node
{
	public const string CollisionRootName = "WorldObjectCollision";

	// Marks the object subtree after its authored bodies move to the pick layer
	private const string DemotedMetaKey = "_object_bodies_demoted";

	/// <summary>
	/// Cell node per terrain cell, null once a cell is known to hold no parts
	/// </summary>
	private readonly Dictionary<(int Gx, int Gz), Node3D?> loaded = new ();
	private readonly Dictionary<string, Shape3D?> shapes = new (StringComparer.Ordinal);
	private readonly HashSet<(int Gx, int Gz)> queued = [];
	private readonly Queue<(int Gx, int Gz)> pending = new ();
	private ObjectCollisionChunkIndex? index;
	private GridMap? terrainGridMap;
	private Node3D? collisionRoot;

	[Export]
	public float LoadRadiusMeters { get; set; }

	/// <summary>
	/// Shapes built per EnsureAroundWorldPosition call. One dense cell (max 2782 parts) goes in a
	/// single call, so that cell is one hitch
	/// </summary>
	[Export]
	public int PartsPerCall { get; set; } = 400;

	[Export]
	public bool AutoLoadAroundClients { get; set; } = true;

	/// <summary>
	/// Positions whose cells the per-second CheckAllClients unload must skip, so a viewer that
	/// streams on its own camera does not have its cells dropped under it
	/// </summary>
	public List<Vector3> KeepLoadedPositions { get; } = [];

	public int LoadedCellCount => loaded.Count;

	public override void _Ready ()
	{
		if (LoadRadiusMeters <= 0f)
		{
			LoadRadiusMeters = 200f;
			try
			{
				if (!Engine.IsEditorHint ())
				{
					LoadRadiusMeters = ServerConfig.AppConfig.ObjectVisibilityDistance * 2f;
				}
			}
			catch
			{
				// ignore
			}
		}
	}

	public void NotifyClientPosition (SphereClient client)
	{
		if (!AutoLoadAroundClients || Engine.IsEditorHint ())
		{
			return;
		}

		if (client.CurrentCharacter is null)
		{
			return;
		}

		EnsureAroundWorldPosition (ClientWorldPosition.GetGodotWorldPosition (client));
	}

	public void CheckAllClients ()
	{
		if (!AutoLoadAroundClients || Engine.IsEditorHint () || index is null)
		{
			return;
		}

		if (!TryResolveRoots ())
		{
			return;
		}

		var keep = new HashSet<(int Gx, int Gz)> ();
		foreach (var client in ActiveClients.GetAll ().Values)
		{
			if (client.CurrentCharacter is null)
			{
				continue;
			}

			AddCellsAround (ClientWorldPosition.GetGodotWorldPosition (client), keep);
		}

		foreach (var position in KeepLoadedPositions)
		{
			AddCellsAround (position, keep);
		}

		List<(int Gx, int Gz)>? drop = null;
		foreach (var key in loaded.Keys)
		{
			if (keep.Contains (key))
			{
				continue;
			}

			drop ??= [];
			drop.Add (key);
		}

		if (drop is null)
		{
			return;
		}

		foreach (var key in drop)
		{
			UnloadCell (key);
		}
	}

	public void EnsureAroundWorldPosition (Vector3 worldPosition)
	{
		if (!EnsureIndex ())
		{
			return;
		}

		AddCellsAround (worldPosition, null);
		DrainPending ();
	}

	/// <summary>
	/// The player's own cell loads past the budget, so a spawn facing a wall collides on tick 1
	/// </summary>
	public void EnsureAtWorldPosition (Vector3 worldPosition)
	{
		if (!EnsureIndex () || terrainGridMap is null)
		{
			return;
		}

		var center = terrainGridMap.LocalToMap (terrainGridMap.ToLocal (worldPosition));
		LoadCell ((center.X, center.Z));
		AddCellsAround (worldPosition, null);
		DrainPending ();
	}

	private void AddCellsAround (Vector3 worldPosition, HashSet<(int Gx, int Gz)>? keep)
	{
		if (terrainGridMap is null || index is null)
		{
			return;
		}

		var center = terrainGridMap.LocalToMap (terrainGridMap.ToLocal (worldPosition));
		var cellRadius = Mathf.Max (1, Mathf.CeilToInt (LoadRadiusMeters / Mathf.Max (1f, terrainGridMap.CellSize.X)));

		// The player's cell goes first, so the budget reaches it before the ring edge
		EnqueueCell (center.X, center.Z, keep);
		for (var gz = center.Z - cellRadius; gz <= center.Z + cellRadius; gz++)
		{
			for (var gx = center.X - cellRadius; gx <= center.X + cellRadius; gx++)
			{
				EnqueueCell (gx, gz, keep);
			}
		}
	}

	private void EnqueueCell (int gx, int gz, HashSet<(int Gx, int Gz)>? keep)
	{
		var cell = (Gx: gx, Gz: gz);
		keep?.Add (cell);
		if (!loaded.ContainsKey (cell) && queued.Add (cell))
		{
			pending.Enqueue (cell);
		}
	}

	/// <summary>
	/// Shape resources and node builds stay under PartsPerCall per call, so a teleport does not
	/// stall the tick
	/// </summary>
	private void DrainPending ()
	{
		if (index is null || collisionRoot is null || terrainGridMap is null)
		{
			return;
		}

		var parts = 0;
		while (pending.Count > 0)
		{
			var cell = pending.Dequeue ();
			queued.Remove (cell);
			if (loaded.ContainsKey (cell))
			{
				continue;
			}

			if (parts >= PartsPerCall)
			{
				pending.Enqueue (cell);
				queued.Add (cell);
				return;
			}

			parts += LoadCell (cell);
		}
	}

	/// <summary>
	/// Parts placed, 0 when the cell holds none
	/// </summary>
	private int LoadCell ((int Gx, int Gz) cell)
	{
		if (index is null || collisionRoot is null || terrainGridMap is null)
		{
			return 0;
		}

		if (loaded.ContainsKey (cell))
		{
			return 0;
		}

		if (!index.TryGetCell (cell.Gx, cell.Gz, out var list) || list.Count == 0)
		{
			loaded[cell] = null;
			return 0;
		}

		var cellNode = new Node3D
		{
			Name = $"ObjCol_{cell.Gx}_{cell.Gz}",
			Position = terrainGridMap.MapToLocal (new Vector3I (cell.Gx, 0, cell.Gz)),
		};
		var placed = 0;
		foreach (var part in list)
		{
			var shape = GetShape (part.ShapePath);
			if (shape is null)
			{
				continue;
			}

			var body = new StaticBody3D
			{
				CollisionLayer = 1,
				CollisionMask = 0,
				Transform = part.ToTransform (),
			};
			// Named by the baked stem, so a preview pick reports the model the part came from
			body.AddChild (new CollisionShape3D { Name = part.ShapeName, Shape = shape });
			cellNode.AddChild (body);
			placed++;
		}

		collisionRoot.AddChild (cellNode);
		loaded[cell] = cellNode;
		return placed;
	}

	private Shape3D? GetShape (string path)
	{
		if (shapes.TryGetValue (path, out var cached))
		{
			return cached;
		}

		var shape = ResourceLoader.Exists (path) ? ResourceLoader.Load<Shape3D> (path) : null;
		if (shape is null)
		{
			GD.PushWarning ($"WorldObjectCollisionStreamer: shape missing {path}");
		}

		shapes[path] = shape;
		return shape;
	}

	private bool EnsureIndex ()
	{
		if (index is not null)
		{
			return TryResolveRoots ();
		}

		if (!TryResolveRoots ())
		{
			return false;
		}

		index = ObjectCollisionChunkIndex.Load (TerrainBakePaths.ObjectChunkIndexJson);
		if (index.CellCount == 0)
		{
			GD.PushWarning (
				"WorldObjectCollisionStreamer: no baked object collision. Run Tools/pack_object_collision_chunks.ps1.");
			return false;
		}

		GD.Print ($"WorldObjectCollisionStreamer: index cells={index.CellCount} parts={index.PartCount}");
		return true;
	}

	private bool TryResolveRoots ()
	{
		if (GodotObject.IsInstanceValid (terrainGridMap) && GodotObject.IsInstanceValid (collisionRoot))
		{
			return true;
		}

		terrainGridMap = null;
		collisionRoot = null;
		var root = GetTree ()?.Root;
		if (root is null)
		{
			return false;
		}

		Node? walk = this;
		while (walk is not null)
		{
			if (walk.FindChild (TerrainGridFill.TerrainNodeName, recursive: true, owned: false) is GridMap grid)
			{
				terrainGridMap = grid;
				break;
			}

			walk = walk.GetParent ();
		}

		if (terrainGridMap is null)
		{
			foreach (var node in root.FindChildren ("*", nameof (GridMap), true, false))
			{
				if (node is GridMap grid && grid.Name == TerrainGridFill.TerrainNodeName)
				{
					terrainGridMap = grid;
					break;
				}
			}
		}

		if (terrainGridMap is null)
		{
			return false;
		}

		// Cell parts are in the GridMap local frame, so the root sits on the GridMap
		if (terrainGridMap.GetNodeOrNull (CollisionRootName) is Node3D existing)
		{
			collisionRoot = existing;
			return true;
		}

		collisionRoot = new Node3D { Name = CollisionRootName };
		terrainGridMap.AddChild (collisionRoot);
		DemoteSceneObjectBodies ();
		return true;
	}

	/// <summary>
	/// TerrainObjects subfolder instances (trees_ny, mazes, …) ship authored StaticBody3D on
	/// layer 1. ObjectChunks covers the same JSON, so those bodies double-block the player at
	/// invisible offsets. They move to the pick layer, where the GM view can still raycast them
	/// </summary>
	private void DemoteSceneObjectBodies ()
	{
		var root = GetTree ()?.Root;
		if (root is null
			|| root.FindChild (TerrainObjectsFill.NodeName, recursive: true, owned: false) is not Node3D objects
			|| objects.HasMeta (DemotedMetaKey))
		{
			return;
		}

		var moved = 0;
		foreach (var node in objects.FindChildren ("*", nameof (StaticBody3D), recursive: true, owned: false))
		{
			if (node is StaticBody3D body && body.CollisionLayer == 1u)
			{
				body.CollisionLayer = WorldObject.VisualPickCollisionLayer;
				body.CollisionMask = 0;
				moved++;
			}
		}

		objects.SetMeta (DemotedMetaKey, true);
		GD.Print ($"WorldObjectCollisionStreamer: scene bodies to pick layer: {moved}");
	}

	private void UnloadCell ((int Gx, int Gz) key)
	{
		if (!loaded.Remove (key, out var node))
		{
			return;
		}

		if (node is null || !GodotObject.IsInstanceValid (node))
		{
			return;
		}

		node.GetParent ()?.RemoveChild (node);
		node.Free ();
	}
}
