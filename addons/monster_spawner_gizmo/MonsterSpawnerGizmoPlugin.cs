using System;
using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;

namespace SphServer.Addons.MonsterSpawnerGizmo;

public partial class MonsterSpawnerGizmoPlugin : EditorNode3DGizmoPlugin
{
	private const int CircleSegments = 64;
	private const float SlotCrossSize = 1.2f;
	private const float SlotVerticalHeight = 2f;
	private const string SpawnerScriptPath = "res://Godot/Scripts/Objects/HelperGizmos/MonsterSpawner.cs";

	private static GodotObject? _spawnerScript;

	private static GodotObject SpawnerScript =>
		_spawnerScript ??= GD.Load<CSharpScript>(SpawnerScriptPath);

	public MonsterSpawnerGizmoPlugin()
	{
		CreateMaterial("spawn_ok", new Color(0.25f, 0.95f, 0.35f, 0.9f), billboard: false, onTop: true);
		CreateMaterial("spawn_error", new Color(0.95f, 0.28f, 0.22f, 0.95f), billboard: false, onTop: true);
		CreateMaterial("slot", new Color(0.95f, 0.15f, 0.12f, 0.98f), billboard: false, onTop: true);
		CreateMaterial("leash", new Color(0.45f, 0.6f, 1f, 0.35f), billboard: false, onTop: true);
	}

	// Godot C# passes Node3D here even for custom script types (see godot#82869).
	public override bool _HasGizmo(Node3D node) => IsMonsterSpawner(node);

	public override void _Redraw(EditorNode3DGizmo gizmo)
	{
		var node = gizmo.GetNode3D();
		if (!IsMonsterSpawner(node))
		{
			return;
		}

		gizmo.Clear();

		var hasError = node.Get("HasSpawnError").AsBool();
		var spawnRadius = node.Get("SpawnRadiusMeters").AsSingle();
		var leashRadius = node.Get("LeashRadiusMeters").AsSingle();
		var bakedSlots = ReadBakedSpawnSlots(node);

		var spawnMaterial = GetMaterial(hasError ? "spawn_error" : "spawn_ok", gizmo);
		gizmo.AddLines(BuildHorizontalCircle(spawnRadius), spawnMaterial, billboard: false);

		if (leashRadius > spawnRadius + 0.05f)
		{
			gizmo.AddLines(
				BuildHorizontalCircle(leashRadius),
				GetMaterial("leash", gizmo),
				billboard: false);
		}

		var slotMaterial = GetMaterial("slot", gizmo);
		foreach (var worldSlot in bakedSlots)
		{
			gizmo.AddLines(BuildSlotCross(node, worldSlot), slotMaterial, billboard: false);
		}
	}

	private static bool IsMonsterSpawner(Node3D node)
	{
		var script = node.GetScript().AsGodotObject();
		return script is not null && SpawnerScript is not null && script == SpawnerScript;
	}

	private static IReadOnlyList<Vector3> ReadBakedSpawnSlots(Node3D node)
	{
		if (node.HasMethod("GetEditorBakedSpawnSlots"))
		{
			var fromScript = node.Call("GetEditorBakedSpawnSlots");
			if (fromScript.VariantType == Variant.Type.Array)
			{
				var slots = new List<Vector3>();
				foreach (var item in fromScript.AsGodotArray())
				{
					slots.Add(item.AsVector3());
				}

				if (slots.Count > 0)
				{
					return slots;
				}
			}
		}

		var variant = node.Get("BakedSpawnSlots");
		if (variant.VariantType == Variant.Type.Nil)
		{
			return [];
		}

		if (variant.VariantType == Variant.Type.PackedVector3Array)
		{
			return variant.AsVector3Array();
		}

		if (variant.VariantType != Variant.Type.Array)
		{
			return [];
		}

		var fromProperty = new List<Vector3>();
		foreach (var item in variant.AsGodotArray())
		{
			fromProperty.Add(item.AsVector3());
		}

		return fromProperty;
	}

	private static Vector3[] BuildHorizontalCircle(float radius)
	{
		var lines = new Vector3[CircleSegments * 2];
		for (var i = 0; i < CircleSegments; i++)
		{
			var angle0 = (float)(i * Math.Tau / CircleSegments);
			var angle1 = (float)((i + 1) * Math.Tau / CircleSegments);
			lines[i * 2] = new Vector3(Mathf.Cos(angle0) * radius, 0f, Mathf.Sin(angle0) * radius);
			lines[i * 2 + 1] = new Vector3(Mathf.Cos(angle1) * radius, 0f, Mathf.Sin(angle1) * radius);
		}

		return lines;
	}

	private static Vector3[] BuildSlotCross(Node3D spawner, Vector3 bakedSlot)
	{
		// Baked slots already carry an accurate Godot-world Y (snapped to the baked navmesh surface during
		// validation - see MonsterSpawnSlotBaker/OutdoorSpawnSlotValidator), so no re-resolution needed here.
		var center = spawner.ToLocal(bakedSlot);
		var half = SlotCrossSize * 0.5f;
		var top = center + new Vector3(0f, SlotVerticalHeight, 0f);
		return
		[
			center + new Vector3(-half, 0f, 0f), center + new Vector3(half, 0f, 0f),
			center + new Vector3(0f, 0f, -half), center + new Vector3(0f, 0f, half),
			center, top,
		];
	}
}
