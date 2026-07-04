using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.Fill;

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

	private readonly struct SpawnerBatchJob(
		MonsterSpawner spawner,
		Vector3 origin,
		float spawnRadiusMeters,
		int targetRegularCount,
		int targetNamedCount)
	{
		public MonsterSpawner Spawner { get; } = spawner;
		public Vector3 Origin { get; } = origin;
		public float SpawnRadiusMeters { get; } = spawnRadiusMeters;
		public int TargetRegularCount { get; } = targetRegularCount;
		public int TargetNamedCount { get; } = targetNamedCount;
	}

	private readonly struct SpawnerBatchPlan(MonsterSpawner spawner, MonsterSpawnPlan plan)
	{
		public MonsterSpawner Spawner { get; } = spawner;
		public MonsterSpawnPlan Plan { get; } = plan;
	}

	[Export]
	public string SpawnerDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\mob_spawner.txt";

	[Export]
	public string MonsterSpawnerScenePath { get; set; } = "res://Godot/Scenes/monster_spawner.tscn";

	[Export]
	public bool BatchRespawnInProgress { get; private set; }

	[ExportToolButton("Rebuild monster spawners")]
	public Callable RebuildMonsterSpawnersButton => Callable.From(RebuildMonsterSpawners);

	[ExportToolButton("Delete and respawn enabled spawners")]
	public Callable DeleteAndRespawnEnabledSpawnersButton => Callable.From(DeleteAndRespawnEnabledSpawners);

	[ExportToolButton("Bake spawn slots on all spawners")]
	public Callable BakeSpawnSlotsOnAllSpawnersButton => Callable.From(BakeSpawnSlotsOnAllSpawners);

	private volatile List<SpawnerBatchPlan>? _completedBatchPlans;
	private int _batchGeneration;

	public override void _Process(double delta)
	{
		TryApplyCompletedBatchRespawn();
	}

	public void BakeSpawnSlotsOnAllSpawners()
	{
		MonsterSpawnSlotBaker.BakeAllUnder(this);
	}

	public void DeleteAndRespawnEnabledSpawners()
	{
		if (BatchRespawnInProgress)
		{
			GD.PushWarning("MonsterSpawnersFill: batch respawn already in progress.");
			return;
		}

		var tree = GetTree();
		if (tree is not null)
		{
			MonsterMultiMeshVisuals.BeginBulkEditorUpdate(tree);
		}

		var instantApplied = 0;
		var jobs = new List<SpawnerBatchJob>();
		foreach (var child in GetChildren())
		{
			if (child is not MonsterSpawner spawner || !GodotObject.IsInstanceValid(spawner) || !spawner.SpawningEnabled)
			{
				continue;
			}

			if (spawner.SpawnPlanInProgress)
			{
				GD.PushWarning($"MonsterSpawnersFill: skipping '{spawner.Name}' — spawn plan already in progress.");
				continue;
			}

			spawner.DeleteAllSpawnedMonstersForBatch();
			if (spawner.TryApplyInstantBulkRespawn())
			{
				instantApplied++;
				continue;
			}

			jobs.Add(new SpawnerBatchJob(
				spawner,
				spawner.GlobalPosition,
				spawner.SpawnRadiusMeters,
				spawner.TargetRegularMonsterCount,
				spawner.TargetNamedMonsterCount));
		}

		if (jobs.Count == 0)
		{
			if (tree is not null)
			{
				MonsterMultiMeshVisuals.EndBulkEditorUpdate(tree);
			}

			GD.Print($"MonsterSpawnersFill: instant respawn on {instantApplied} enabled spawner(s).");
			return;
		}

		BatchRespawnInProgress = true;
		_completedBatchPlans = null;
		var generation = Interlocked.Increment(ref _batchGeneration);
		NotifyPropertyListChanged();

		GD.Print(
			$"MonsterSpawnersFill: instant={instantApplied}, planning respawn for {jobs.Count} enabled spawner(s) on a background thread...");

		_ = Task.Run(() => PlanBatchRespawnAsync(generation, jobs));
	}

	private void PlanBatchRespawnAsync(int generation, List<SpawnerBatchJob> jobs)
	{
		try
		{
			var plans = new SpawnerBatchPlan[jobs.Count];
			Parallel.For(0, jobs.Count, index =>
			{
				var job = jobs[index];
				WalkSurfaceCache.PreloadChunksForRadius(job.Origin.X, job.Origin.Z, job.SpawnRadiusMeters + 1f);
				var plan = MonsterSpawnPlanner.Plan(
					job.Origin,
					job.SpawnRadiusMeters,
					job.TargetRegularCount,
					job.TargetNamedCount,
					existingOccupied: null,
					new AtlasMonsterSpawnGroundQuery(),
					Random.Shared);
				plans[index] = new SpawnerBatchPlan(job.Spawner, plan);
			});

			if (generation != Volatile.Read(ref _batchGeneration))
			{
				return;
			}

			_completedBatchPlans = new List<SpawnerBatchPlan>(plans);
		}
		catch (Exception ex)
		{
			GD.PushError($"MonsterSpawnersFill: background batch planning failed: {ex.Message}");
			if (generation == Volatile.Read(ref _batchGeneration))
			{
				_completedBatchPlans = [];
			}
		}
	}

	private void TryApplyCompletedBatchRespawn()
	{
		var plans = _completedBatchPlans;
		if (plans is null)
		{
			return;
		}

		_completedBatchPlans = null;
		var tree = GetTree();

		try
		{
			var applied = 0;
			foreach (var entry in plans)
			{
				if (!GodotObject.IsInstanceValid(entry.Spawner))
				{
					continue;
				}

				entry.Spawner.ApplyPreplannedSpawnPlan(entry.Plan);
				applied++;
			}

			GD.Print($"MonsterSpawnersFill: applied respawn plans for {applied} enabled spawner(s).");
		}
		finally
		{
			BatchRespawnInProgress = false;
			NotifyPropertyListChanged();

			if (tree is not null)
			{
				MonsterMultiMeshVisuals.EndBulkEditorUpdate(tree);
			}
		}
	}

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
