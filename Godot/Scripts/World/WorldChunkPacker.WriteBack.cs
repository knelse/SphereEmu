using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Incremental write-back: persist selected MainServer placements into their tile chunk scenes
///     without a full LoadAll / Repack. Merges live edits over on-disk siblings for the same tile.
/// </summary>
public static partial class WorldChunkPacker
{
	public static WorldChunkWriteBackResult WriteBackPlacements(
		Node mainServer,
		IReadOnlyList<Node3D> placements)
	{
		var index = WorldContentIndex.GetOrLoad();
		var tilesByKind = new Dictionary<WorldContentKind, HashSet<(int TileX, int TileZ)>>();
		var excludeFromTile = new Dictionary<(WorldContentKind Kind, int TileX, int TileZ), HashSet<string>>();
		var resolved = 0;

		foreach (var placement in placements)
		{
			if (placement is null || !GodotObject.IsInstanceValid(placement))
			{
				continue;
			}

			if (!TryResolveKind(placement, mainServer, out var kind))
			{
				GD.PushWarning($"WorldChunkPacker: could not resolve kind for '{placement.Name}'.");
				continue;
			}

			resolved++;
			var newTile = WorldTileKeys.FromWorld(placement.GlobalPosition);
			AddTile(tilesByKind, kind, newTile);

			if (index.TryGetEntry(kind, placement.Name, out var previous)
				&& (previous.TileX != newTile.TileX || previous.TileZ != newTile.TileZ))
			{
				var oldTile = (previous.TileX, previous.TileZ);
				AddTile(tilesByKind, kind, oldTile);
				ExcludeName(excludeFromTile, kind, oldTile, placement.Name);
			}
		}

		var chunksWritten = 0;
		var chunksDeleted = 0;
		var nodesPacked = 0;

		foreach (var (kind, tiles) in tilesByKind)
		{
			foreach (var (tileX, tileZ) in tiles)
			{
				excludeFromTile.TryGetValue((kind, tileX, tileZ), out var excluded);
				var result = RewriteTile(mainServer, index, kind, tileX, tileZ, excluded);
				chunksWritten += result.Written ? 1 : 0;
				chunksDeleted += result.Deleted ? 1 : 0;
				nodesPacked += result.NodesPacked;
			}
		}

		index.SaveTo(WorldChunkCatalog.IndexPath);

		return new WorldChunkWriteBackResult(resolved, chunksWritten, chunksDeleted, nodesPacked);
	}

	public static bool TryResolvePlacement(
		Node selected,
		out Node3D placement,
		out WorldContentKind kind,
		out Node mainServer)
	{
		placement = null!;
		kind = default;
		mainServer = null!;
		if (selected is null || !GodotObject.IsInstanceValid(selected))
		{
			return false;
		}

		var foundMain = FindMainServer(selected);
		if (foundMain is null)
		{
			return false;
		}

		mainServer = foundMain;

		for (var node = selected; node is not null && node != mainServer; node = node.GetParent())
		{
			var parent = node.GetParent();
			if (parent is null)
			{
				break;
			}

			if (TryMatchKindParent(mainServer, parent, out kind) && node is Node3D node3D)
			{
				placement = node3D;
				return true;
			}
		}

		return false;
	}

	public static bool TryResolveKind(Node placement, Node mainServer, out WorldContentKind kind)
	{
		kind = default;
		var parent = placement.GetParent();
		return parent is not null && TryMatchKindParent(mainServer, parent, out kind);
	}

	private static WorldChunkTileRewriteResult RewriteTile(
		Node mainServer,
		WorldContentIndex index,
		WorldContentKind kind,
		int tileX,
		int tileZ,
		HashSet<string>? excludedNames)
	{
		var parentPath = WorldContentKindPaths.ParentNodePath(kind);
		var parent = mainServer.GetNodeOrNull(parentPath);
		if (parent is null)
		{
			GD.PushWarning($"WorldChunkPacker: parent missing '{parentPath}'.");
			return default;
		}

		WorldChunkCatalog.EnsureChunkDirectory(kind);
		var path = WorldChunkCatalog.ChunkScenePath(kind, tileX, tileZ);
		var packByName = new Dictionary<string, Node>(StringComparer.Ordinal);
		var indexPositionByName = new Dictionary<string, Vector3>(StringComparer.Ordinal);
		excludedNames ??= [];

		if (ResourceLoader.Exists(path))
		{
			var packedExisting = ResourceLoader.Load<PackedScene>(path, cacheMode: ResourceLoader.CacheMode.Ignore);
			if (packedExisting is not null)
			{
				var diskRoot = packedExisting.Instantiate<Node>();
				foreach (var child in diskRoot.GetChildren())
				{
					var name = child.Name.ToString();
					if (excludedNames.Contains(name) || ParentHasChildNamed(parent, name))
					{
						continue;
					}

					diskRoot.RemoveChild(child);
					WorldChunkLoadOps.ClearOwnerRecursive(child);
					SanitizePlacementForChunkPack(child);
					packByName[name] = child;
					if (child is Node3D diskNode3D)
					{
						indexPositionByName[name] = diskNode3D.Position;
					}
				}

				diskRoot.QueueFree();
			}
		}

		// Temp pack root must be in the SceneTree before we add duplicates (avoids GlobalTransform errors).
		var chunkRoot = new Node3D
		{
			Name = $"__WriteBack_{WorldContentKindPaths.FolderName(kind)}_{WorldTileKeys.FormatKey(tileX, tileZ)}",
		};
		mainServer.AddChild(chunkRoot);

		try
		{
			foreach (var child in parent.GetChildren())
			{
				if (child is not Node3D live || excludedNames.Contains(live.Name))
				{
					continue;
				}

				var liveTile = WorldTileKeys.FromWorld(live.GlobalPosition);
				if (liveTile.TileX != tileX || liveTile.TileZ != tileZ)
				{
					continue;
				}

				if (packByName.Remove(live.Name, out var stale))
				{
					stale.QueueFree();
				}

				// Keep the editor node in place — pack a duplicate under chunkRoot.
				indexPositionByName[live.Name] = live.GlobalPosition;
				var duplicate = live.Duplicate();
				if (duplicate is null)
				{
					GD.PushWarning($"WorldChunkPacker: Duplicate failed for '{live.Name}'.");
					continue;
				}

				chunkRoot.AddChild(duplicate);
				if (duplicate is Node3D dup3D)
				{
					dup3D.GlobalTransform = live.GlobalTransform;
				}

				WorldChunkLoadOps.ClearOwnerRecursive(duplicate);
				SanitizePlacementForChunkPack(duplicate);
				duplicate.Owner = chunkRoot;
				packByName[live.Name] = duplicate;
			}

			if (packByName.Count == 0)
			{
				DeleteChunkFileIfExists(path);
				index.UnmarkChunkTile(kind, tileX, tileZ);
				return new WorldChunkTileRewriteResult(Written: false, Deleted: true, NodesPacked: 0);
			}

			// Attach any disk-only nodes under chunkRoot for Pack.
			foreach (var (name, node) in packByName)
			{
				if (node.GetParent() == chunkRoot)
				{
					continue;
				}

				if (node.GetParent() is { } currentParent)
				{
					currentParent.RemoveChild(node);
				}

				chunkRoot.AddChild(node);
				node.Owner = chunkRoot;
			}

			var pendingEntries = new List<WorldContentEntry>();
			foreach (var (name, node) in packByName)
			{
				var position = indexPositionByName.TryGetValue(name, out var cached)
					? cached
					: node is Node3D n3 ? n3.Position : Vector3.Zero;
				var slots = ReadSlotsWithoutClearing(node);
				if (slots.Length == 0 && index.TryGetSlots(kind, name, out var previousSlots))
				{
					slots = previousSlots;
				}

				pendingEntries.Add(new WorldContentEntry(kind, tileX, tileZ, position, name, slots));
			}

			var packed = new PackedScene();
			var packError = packed.Pack(chunkRoot);
			if (packError != Error.Ok)
			{
				GD.PushError($"WorldChunkPacker: WriteBack Pack failed for {kind} {tileX}_{tileZ}: {packError}");
				return default;
			}

			var saveError = ResourceSaver.Save(packed, path);
			if (saveError != Error.Ok)
			{
				GD.PushError($"WorldChunkPacker: WriteBack Save failed for {path}: {saveError}");
				return default;
			}

			foreach (var entry in pendingEntries)
			{
				index.AddOrReplace(entry);
			}

			index.MarkChunkTile(kind, tileX, tileZ);
			return new WorldChunkTileRewriteResult(
				Written: true,
				Deleted: false,
				NodesPacked: packByName.Count);
		}
		finally
		{
			// Live editor placements were never moved. Only free the temp pack subtree.
			if (GodotObject.IsInstanceValid(chunkRoot))
			{
				foreach (var child in chunkRoot.GetChildren())
				{
					chunkRoot.RemoveChild(child);
					child.Free();
				}

				if (chunkRoot.GetParent() is { } chunkParent)
				{
					chunkParent.RemoveChild(chunkRoot);
				}

				chunkRoot.Free();
			}
		}
	}

	private static Vector3[] ReadSlotsWithoutClearing(Node node) =>
		node switch
		{
			MonsterSpawner monster when monster.BakedSpawnSlots.Count > 0 =>
				WorldChunkSlotHydration.CopySlots(monster.BakedSpawnSlots),
			AlchemyMaterialSpawner alchemy when alchemy.BakedSpawnSlots.Count > 0 =>
				WorldChunkSlotHydration.CopySlots(alchemy.BakedSpawnSlots),
			_ => [],
		};

	private static bool ParentHasChildNamed(Node parent, string name)
	{
		foreach (var child in parent.GetChildren())
		{
			if (child.Name == name)
			{
				return true;
			}
		}

		return false;
	}

	private static void DeleteChunkFileIfExists(string resPath)
	{
		if (!ResourceLoader.Exists(resPath))
		{
			return;
		}

		var absolute = ProjectSettings.GlobalizePath(resPath);
		if (File.Exists(absolute))
		{
			File.Delete(absolute);
		}
	}

	private static Node? FindMainServer(Node start)
	{
		for (var node = start; node is not null; node = node.GetParent())
		{
			if (node.Name == "MainServer")
			{
				return node;
			}
		}

		return null;
	}

	private static bool TryMatchKindParent(Node mainServer, Node parent, out WorldContentKind kind)
	{
		kind = default;
		var relative = mainServer.GetPathTo(parent).ToString();
		foreach (var candidate in WorldContentKindPaths.All)
		{
			if (WorldContentKindPaths.ParentNodePath(candidate) != relative)
			{
				continue;
			}

			kind = candidate;
			return true;
		}

		return false;
	}

	private static void AddTile(
		Dictionary<WorldContentKind, HashSet<(int TileX, int TileZ)>> tilesByKind,
		WorldContentKind kind,
		(int TileX, int TileZ) tile)
	{
		if (!tilesByKind.TryGetValue(kind, out var set))
		{
			set = [];
			tilesByKind[kind] = set;
		}

		set.Add(tile);
	}

	private static void ExcludeName(
		Dictionary<(WorldContentKind Kind, int TileX, int TileZ), HashSet<string>> excludeFromTile,
		WorldContentKind kind,
		(int TileX, int TileZ) tile,
		string nodeName)
	{
		var key = (kind, tile.TileX, tile.TileZ);
		if (!excludeFromTile.TryGetValue(key, out var set))
		{
			set = new HashSet<string>(StringComparer.Ordinal);
			excludeFromTile[key] = set;
		}

		set.Add(nodeName);
	}

	private readonly record struct WorldChunkTileRewriteResult(bool Written, bool Deleted, int NodesPacked);
}

public readonly record struct WorldChunkWriteBackResult(
	int PlacementsResolved,
	int ChunksWritten,
	int ChunksDeleted,
	int NodesPacked);
