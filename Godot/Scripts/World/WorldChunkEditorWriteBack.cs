#if TOOLS
using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Editor-only write-back of selected MainServer placements into world chunk scenes.
/// </summary>
public static class WorldChunkEditorWriteBack
{
	public static void WriteBackEditorSelection(string logPrefix)
	{
		if (!Engine.IsEditorHint())
		{
			GD.PushWarning($"{logPrefix}: write-back is editor-only.");
			return;
		}

		try
		{
			var editor = EditorInterface.Singleton;
			var selection = editor.GetSelection();
			var selected = selection.GetSelectedNodes();
			if (selected.Count == 0)
			{
				GD.PushWarning($"{logPrefix}: select one or more placements under MainServer first.");
				return;
			}

			var main = editor.GetEditedSceneRoot();
			if (main is null || main.Name != "MainServer")
			{
				GD.PushWarning($"{logPrefix}: open MainServer.tscn first.");
				return;
			}

			var placements = new List<Node3D>();
			var seen = new HashSet<ulong>();
			foreach (var node in selected)
			{
				if (!WorldChunkPacker.TryResolvePlacement(node, out var placement, out _, out var resolvedMain))
				{
					continue;
				}

				if (resolvedMain != main)
				{
					continue;
				}

				var id = placement.GetInstanceId();
				if (!seen.Add(id))
				{
					continue;
				}

				placements.Add(placement);
			}

			if (placements.Count == 0)
			{
				GD.PushWarning(
					$"{logPrefix}: selection has no world-chunk placements (Doors, MonsterSpawners, …).");
				return;
			}

			var result = WorldChunkPacker.WriteBackPlacements(main, placements);
			foreach (var placement in placements)
			{
				if (!WorldChunkPacker.TryResolveKind(placement, main, out var kind))
				{
					continue;
				}

				var (tileX, tileZ) = WorldTileKeys.FromWorld(placement.GlobalPosition);
				var path = WorldChunkCatalog.ChunkScenePath(kind, tileX, tileZ);
				editor.GetResourceFilesystem().UpdateFile(path);
			}

			editor.GetResourceFilesystem().UpdateFile(WorldChunkCatalog.IndexPath);
			GD.Print(
				$"{logPrefix}: write-back placements={result.PlacementsResolved}, "
				+ $"chunksWritten={result.ChunksWritten}, chunksDeleted={result.ChunksDeleted}, "
				+ $"nodesPacked={result.NodesPacked}");
		}
		catch (global::System.Exception ex)
		{
			GD.PushWarning($"{logPrefix}: write-back failed ({ex.Message}).");
		}
	}
}
#else
namespace SphServer.Godot.Scripts.World;

public static class WorldChunkEditorWriteBack
{
	public static void WriteBackEditorSelection(string logPrefix)
	{
	}
}
#endif
