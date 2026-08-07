using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Shared chunk instantiate / reparent logic for play streamer and editor plugin.
/// </summary>
public static class WorldChunkLoadOps
{
	public static void EnsureAround(
		Node mainServerRoot,
		WorldContentIndex index,
		HashSet<(WorldContentKind Kind, int TileX, int TileZ)> loaded,
		Vector3 worldPosition,
		float radiusMeters,
		bool assignEditorOwner)
	{
		foreach (var (tileX, tileZ) in index.TilesNear(worldPosition, radiusMeters))
		{
			foreach (var kind in WorldContentKindPaths.All)
			{
				if (index.HasChunk(kind, tileX, tileZ))
				{
					EnsureChunkLoaded(mainServerRoot, loaded, kind, tileX, tileZ, assignEditorOwner);
				}
			}
		}
	}

	public static int LoadAll(
		Node mainServerRoot,
		WorldContentIndex index,
		HashSet<(WorldContentKind Kind, int TileX, int TileZ)> loaded,
		bool assignEditorOwner)
	{
		var count = 0;
		foreach (var (kind, tileX, tileZ) in index.EnumerateChunkTiles())
		{
			var before = loaded.Count;
			EnsureChunkLoaded(mainServerRoot, loaded, kind, tileX, tileZ, assignEditorOwner);
			if (loaded.Count > before)
			{
				count++;
			}
		}

		return count;
	}

	public static void EnsureChunkLoaded(
		Node mainServerRoot,
		HashSet<(WorldContentKind Kind, int TileX, int TileZ)> loaded,
		WorldContentKind kind,
		int tileX,
		int tileZ,
		bool assignEditorOwner)
	{
		var key = (kind, tileX, tileZ);
		if (loaded.Contains(key))
		{
			return;
		}

		var path = WorldChunkCatalog.ChunkScenePath(kind, tileX, tileZ);
		if (!ResourceLoader.Exists(path))
		{
			loaded.Add(key);
			return;
		}

		var watch = Stopwatch.StartNew();
		var packed = ResourceLoader.Load<PackedScene>(path);
		if (packed is null)
		{
			GD.PushWarning($"WorldChunkLoadOps: failed to load {path}");
			return;
		}

		var parentPath = WorldContentKindPaths.ParentNodePath(kind);
		var parent = mainServerRoot.GetNodeOrNull(parentPath);
		if (parent is null)
		{
			GD.PushWarning($"WorldChunkLoadOps: parent missing '{parentPath}' for {path}");
			return;
		}

		var chunkRoot = packed.Instantiate<Node>();
		foreach (var child in chunkRoot.GetChildren())
		{
			ClearOwnerRecursive(child);
			if (child is WorldObject worldObject)
			{
				worldObject.CompactDuplicatedIdNameSuffix();
			}

			chunkRoot.RemoveChild(child);
			parent.AddChild(child);
			// Keep Owner null in editor so Save Scene does not bake streamed nodes into MainServer.
			if (assignEditorOwner && Engine.IsEditorHint())
			{
				child.Owner = mainServerRoot;
			}

			WorldChunkSlotHydration.HydrateIfNeeded(kind, child);
		}

		chunkRoot.QueueFree();
		loaded.Add(key);
		StartupTiming.MarkSpan(
			$"LoadChunk {WorldContentKindPaths.FolderName(kind)}/{WorldTileKeys.FormatKey(tileX, tileZ)}",
			watch.ElapsedMilliseconds);
	}

	public static void ClearOwnerRecursive(Node node)
	{
		node.Owner = null;
		foreach (var child in node.GetChildren())
		{
			ClearOwnerRecursive(child);
		}
	}
}
