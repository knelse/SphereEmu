using System;
using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     One-shot: register door chunk tiles / entries in world_content_index.bin.
///     Run: godot --headless --path &lt;repo&gt; res://Godot/Scenes/world_content_index_patch.tscn
/// </summary>
public partial class WorldContentIndexPatchHeadless : Node
{
	public override void _Ready()
	{
		try
		{
			WorldContentIndex.ClearLoaded();
			var index = WorldContentIndex.GetOrLoad();

			// 041A — existing tile -37_-12
			index.AddOrReplace(new WorldContentEntry(
				WorldContentKind.Door,
				-37,
				-12,
				new Vector3(-3683.29345703125f, -1092.7208251953125f, -1125.4884033203125f),
				"DoorEntrance_041A_0_0_0",
				[]));

			// 056A — new tile 1_14, Sunpool cemetery target (dump-space on the Door node; index stores Godot pos)
			index.AddOrReplace(new WorldContentEntry(
				WorldContentKind.Door,
				1,
				14,
				new Vector3(148.18035888671875f, -157.94940185546875f, 1440.2476806640625f),
				"DoorEntrance_056A_-2900_1499_106",
				[]));

			index.MarkChunkTile(WorldContentKind.Door, -37, -12);
			index.MarkChunkTile(WorldContentKind.Door, 1, 14);
			index.SaveTo(WorldChunkCatalog.IndexPath);
			WorldContentIndex.ReplaceLoaded(index);
			GD.Print("WorldContentIndexPatchHeadless: saved door entries for 041A (-37_-12) and 056A (1_14).");
			GetTree().Quit(0);
		}
		catch (Exception ex)
		{
			GD.PushError($"WorldContentIndexPatchHeadless: {ex}");
			GetTree().Quit(1);
		}
	}
}
