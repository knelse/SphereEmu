using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: reads tab/space-separated spawner rows (ID, skip, skip, X, Y, Z, Angle),
/// clears existing children, and instances <see cref="MonsterSpawnerScenePath"/> per row.
/// Source space matches terrain: <c>(x,y,z)_src ↦ (x,-y,-z)</c>.
/// Rows with duplicate source coordinates (same X, Y, Z columns) are skipped after the first occurrence.
/// </summary>
[Tool]
public partial class MonsterSpawnersFill : Node3D
{
	private const string MobSpawnerTypeValue = "MobSpawner";

	[Export]
	public string SpawnerDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\mob_spawner.txt";

	[Export]
	public string MonsterSpawnerScenePath { get; set; } = "res://Godot/Scenes/monster_spawner.tscn";

	[ExportToolButton("Rebuild monster spawners")]
	public Callable RebuildMonsterSpawnersButton => Callable.From(RebuildMonsterSpawners);

	public void RebuildMonsterSpawners()
	{
		var tree = GetTree();
		if (tree is not null)
		{
			MonsterMultiMeshVisuals.BeginBulkEditorUpdate(tree);
		}

		try
		{
			WorldObjectDumpFillCommon.ClearRebuildableChildren(this);

			if (!WorldObjectDumpFillCommon.TryLoadPackedScene(MonsterSpawnerScenePath, "MonsterSpawnersFill", out var scene))
			{
				return;
			}

			if (!WorldObjectDumpFillCommon.TryReadTextFile(SpawnerDataFilePath, "MonsterSpawnersFill", out var text))
			{
				return;
			}

			var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
			WorldObjectDumpFillCommon.SeedSeenSourcePositions(this, seenSourcePositions);
			var duplicateRowsSkipped = 0;
			var rowsSkippedNotMatchingType = 0;
			var rowsSkippedWeirdCoords = 0;
			var rowsConsidered = 0;
			var rowsParsed = 0;
			var spawned = 0;
			var parseErrors = 0;

			foreach (var (lineNumber, parts) in WorldObjectDumpFillCommon.EnumerateDataLinesBottomUp(text))
			{
				rowsConsidered++;

				if (!WorldObjectDumpFillCommon.MatchesTypeTokenIfPresent(parts, typeIndex: 1, expectedTypeValue: MobSpawnerTypeValue))
				{
					rowsSkippedNotMatchingType++;
					continue;
				}

				if (parts.Length < 7)
				{
					parseErrors++;
					GD.PushWarning($"MonsterSpawnersFill: line {lineNumber}: expected ≥7 columns, skipping");
					continue;
				}

				if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y, out var z, out var angleEncoded) || id < 100)
				{
					parseErrors++;
					GD.PushWarning($"MonsterSpawnersFill: line {lineNumber}: parse failed, skipping");
					continue;
				}

				if (WorldObjectDumpFillCommon.ShouldSkipWeirdCoords(parts[3], parts[4], parts[5], x, y, z))
				{
					rowsSkippedWeirdCoords++;
					continue;
				}

				rowsParsed++;

				var posKey = WorldObjectDumpFillCommon.QuantizeSourcePosition(x, y, z);
				if (!seenSourcePositions.Add(posKey))
				{
					duplicateRowsSkipped++;
					continue;
				}

				var instance = scene!.Instantiate<Node3D>();
				instance.Name = WorldObjectDumpFillCommon.BuildPlacementName("MonsterSpawner", id, x, y, z);
				instance.Transform = BuildPlacementTransform((float)x, (float)y, (float)z, angleEncoded);
				AddChild(instance);
				WorldObjectDumpFillCommon.SetOwnerIfEditor(this, instance);
				spawned++;
			}

			EnsureEditorPreviewMonstersOnSpawners();

			GD.Print(
				$"MonsterSpawnersFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, notTypeSkipped={rowsSkippedNotMatchingType}, weirdCoordSkipped={rowsSkippedWeirdCoords}, parseErrors={parseErrors}");
		}
		finally
		{
			if (tree is not null)
			{
				MonsterMultiMeshVisuals.EndBulkEditorUpdate(tree);
			}
		}
	}

	private void EnsureEditorPreviewMonstersOnSpawners()
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		foreach (var child in GetChildren())
		{
			if (child is MonsterSpawner spawner && GodotObject.IsInstanceValid(spawner))
			{
				spawner.EnsureEditorPreviewMonsters();
			}
		}
	}

	private static Transform3D BuildPlacementTransform(float x, float y, float z, int angleEncoded)
	{
		var pos = new Vector3(x, -y, -z);
		var t0 = (float)(angleEncoded * Math.PI / 128.0);
		var basis = Basis.FromEuler(new Vector3(0f, t0, 0f), EulerOrder.Yxz);
		return new Transform3D(basis, pos);
	}
}
