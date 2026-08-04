using System.Collections.Generic;
using Godot;
using Godot.Collections;
using SphServer.Godot.Scripts.Objects.HelperGizmos;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Hydrates <c>BakedSpawnSlots</c> from <see cref="WorldContentIndex" /> when chunk scenes store empty arrays.
/// </summary>
public static class WorldChunkSlotHydration
{
	public static void HydrateIfNeeded(WorldContentKind kind, Node node)
	{
		if (kind is not (WorldContentKind.Monster or WorldContentKind.Alchemy))
		{
			return;
		}

		var index = WorldContentIndex.GetOrLoad();
		if (!index.TryGetSlots(kind, node.Name, out var slots) || slots.Length == 0)
		{
			return;
		}

		switch (node)
		{
			case MonsterSpawner monster when monster.BakedSpawnSlots.Count == 0:
				monster.SetBakedSpawnSlots(slots);
				break;
			case AlchemyMaterialSpawner alchemy when alchemy.BakedSpawnSlots.Count == 0:
				alchemy.SetBakedSpawnSlots(slots);
				break;
		}
	}

	public static Vector3[] CopySlots(Array<Vector3> slots)
	{
		var result = new Vector3[slots.Count];
		for (var i = 0; i < slots.Count; i++)
		{
			result[i] = slots[i];
		}

		return result;
	}

	public static void UpdateIndexSlots(WorldContentKind kind, Node3D node, IReadOnlyList<Vector3> slots)
	{
		var (tileX, tileZ) = WorldTileKeys.FromWorld(node.GlobalPosition);
		var index = WorldContentIndex.GetOrLoad();
		index.AddOrReplace(new WorldContentEntry(
			kind,
			tileX,
			tileZ,
			node.GlobalPosition,
			node.Name,
			slots is Vector3[] arr ? arr : [.. slots]));
	}
}
