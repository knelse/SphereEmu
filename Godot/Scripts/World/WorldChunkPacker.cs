using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Packs placement children under MainServer parents into per-tile chunk scenes + content index.
///     Used by headless split and editor "Repack chunks" tools.
/// </summary>
public static class WorldChunkPacker
{
	public static WorldChunkPackResult PackFromMainServer(Node mainServer, bool clearParents = true, bool extractSlots = true)
	{
		var watch = Stopwatch.StartNew();
		var previous = WorldContentIndex.GetOrLoad();
		var index = new WorldContentIndex();
		var chunksWritten = 0;
		var nodesPacked = 0;
		var slotsExtracted = 0;

		foreach (var kind in WorldContentKindPaths.All)
		{
			var parentPath = WorldContentKindPaths.ParentNodePath(kind);
			var parent = mainServer.GetNodeOrNull(parentPath);
			if (parent is null)
			{
				continue;
			}

			var byTile = new Dictionary<(int TileX, int TileZ), List<Node>>();
			foreach (var child in parent.GetChildren())
			{
				if (child is not Node3D node3D)
				{
					continue;
				}

				var (tileX, tileZ) = WorldTileKeys.FromWorld(node3D.GlobalPosition);
				var tileKey = (tileX, tileZ);
				if (!byTile.TryGetValue(tileKey, out var list))
				{
					list = [];
					byTile[tileKey] = list;
				}

				list.Add(child);
			}

			WorldChunkCatalog.EnsureChunkDirectory(kind);

			foreach (var ((tileX, tileZ), nodes) in byTile)
			{
				var chunkRoot = new Node3D
				{
					Name = $"Chunk_{WorldContentKindPaths.FolderName(kind)}_{WorldTileKeys.FormatKey(tileX, tileZ)}",
				};

				foreach (var node in nodes)
				{
					var slots = Array.Empty<Vector3>();
					if (extractSlots)
					{
						slots = ExtractAndClearSlots(kind, node, ref slotsExtracted);
						if (slots.Length == 0
							&& previous.TryGetSlots(kind, node.Name, out var previousSlots))
						{
							slots = previousSlots;
						}
					}

					var position = node is Node3D n3 ? n3.GlobalPosition : Vector3.Zero;
					index.AddOrReplace(new WorldContentEntry(
						kind,
						tileX,
						tileZ,
						position,
						node.Name,
						slots));

					parent.RemoveChild(node);
					chunkRoot.AddChild(node);
					// Only the placement root needs an owner. Recursing into PackedScene
					// instance internals causes unique-name clashes and embeds GLB meshes.
					node.Owner = chunkRoot;
					nodesPacked++;
				}

				index.MarkChunkTile(kind, tileX, tileZ);
				var packed = new PackedScene();
				var packError = packed.Pack(chunkRoot);
				if (packError != Error.Ok)
				{
					GD.PushError($"WorldChunkPacker: Pack failed for {kind} {tileX}_{tileZ}: {packError}");
					chunkRoot.QueueFree();
					continue;
				}

				var path = WorldChunkCatalog.ChunkScenePath(kind, tileX, tileZ);
				var saveError = ResourceSaver.Save(packed, path);
				if (saveError != Error.Ok)
				{
					GD.PushError($"WorldChunkPacker: Save failed for {path}: {saveError}");
				}
				else
				{
					chunksWritten++;
				}

				chunkRoot.QueueFree();
			}
		}

		index.SaveTo(WorldChunkCatalog.IndexPath);
		WorldContentIndex.ReplaceLoaded(index);
		StartupTiming.MarkSpan(
			$"WorldChunkPacker ({chunksWritten} chunks, {nodesPacked} nodes, {slotsExtracted} slot arrays)",
			watch.ElapsedMilliseconds);

		return new WorldChunkPackResult(chunksWritten, nodesPacked, slotsExtracted, index.Entries.Count);
	}

	private static Vector3[] ExtractAndClearSlots(WorldContentKind kind, Node node, ref int slotsExtracted)
	{
		switch (node)
		{
			case MonsterSpawner monster when monster.BakedSpawnSlots.Count > 0:
				{
					var slots = WorldChunkSlotHydration.CopySlots(monster.BakedSpawnSlots);
					monster.SetBakedSpawnSlots([], syncIndex: false);
					slotsExtracted++;
					return slots;
				}
			case AlchemyMaterialSpawner alchemy when alchemy.BakedSpawnSlots.Count > 0:
				{
					var slots = WorldChunkSlotHydration.CopySlots(alchemy.BakedSpawnSlots);
					alchemy.SetBakedSpawnSlots([], syncIndex: false);
					slotsExtracted++;
					return slots;
				}
			default:
				return [];
		}
	}
}

public readonly record struct WorldChunkPackResult(
	int ChunksWritten,
	int NodesPacked,
	int SlotArraysExtracted,
	int IndexEntries);
