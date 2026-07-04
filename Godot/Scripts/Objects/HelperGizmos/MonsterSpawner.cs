using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using SphServer.Godot.Scripts.Objects.Fill;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;
using SphServer.Godot.Scripts.Terrain.WalkSurface;
using SphServer.Shared.GameData.Enums;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

[Tool]
public partial class MonsterSpawner : Node3D
{
	private const int FirstMonsterId = 10000;

	private static readonly List<RespawnTimer> GlobalRespawnTimers = [];
	private static readonly object TimerLock = new();
	private static readonly object MonsterIdLock = new();
	private static ulong LastProcessedFrame;
	private static int _globalNextMonsterId = FirstMonsterId;

	private readonly List<int> _regularMonsterIds = [];
	private readonly List<int> _namedMonsterIds = [];
	private readonly MonsterSpawnPlacement _spawnPlacement = new();
	private readonly object _spawnPlacementLock = new();

	[Export]
	public int TargetNamedMonsterCount = 0;

	[Export]
	public int TargetRegularMonsterCount = 3;

	[Export]
	public float SpawnRadiusMeters = MonsterSpawnPlacement.DefaultSpawnRadiusMeters;

	[Export]
	public float LeashRadiusMeters = 50f;

	[Export]
	public float AggroRadiusMeters = 12f;

	[Export]
	public bool OutdoorChaseEnabled = true;

	[Export]
	public bool SpawnPlacementInvalid { get; private set; }

	[Export]
	public bool SpawnPlanInProgress { get; private set; }

	[Export]
	public int RegularMonsterMinLevel = 1;

	[Export]
	public int RegularMonsterMaxLevel = 3;

	[Export]
	public int NamedMonsterMinLevel = 1;

	[Export]
	public int NamedMonsterMaxLevel = 3;

	[Export]
	public int RegularMonsterRespawnDelaySeconds = 60;

	[Export]
	public int NamedMonsterRespawnDelaySeconds = 60;

	[Export]
	public string MonsterScenePath = "res://Godot/Scenes/Monster.tscn";

	[Export]
	public Array<Node3D> RegularMonsters { get; set; } = [];

	[Export]
	public Array<Node3D> NamedMonsters { get; set; } = [];

	[ExportToolButton("Delete and respawn all mobs")]
	public Callable DeleteAndRespawnAllMobsButton => Callable.From(DeleteAndRespawnAllMobs);

	[Export]
	public Array<Vector3> BakedSpawnSlots { get; private set; } = [];

	[ExportToolButton("Bake spawn slots")]
	public Callable BakeSpawnSlotsButton => Callable.From(BakeSpawnSlots);

	/// <summary>
	///     When set in the scene, this spawner activates and spawns on server load (debug / town presets).
	/// </summary>
	[Export]
	public bool SpawningEnabled { get; set; }

	private bool _spawningEnabled;
	private int _spawnGeneration;
	private MonsterSpawnPlan? _completedSpawnPlan;
	private int _planReadyGeneration;
	private bool _completedSpawnPlanFailed;
	private bool _enableSpawningOnNextApply;
	private SpawnPlanKind _pendingPlanKind;
	private int _deathRespawnMonsterId;
	private bool _deathRespawnIsNamed;
	private readonly Queue<PendingDeathRespawn> _pendingDeathRespawns = new();
	private int _nextBindSlotIndex;

	public Vector3 LeashCenterWorld => GlobalPosition;

	private enum SpawnPlanKind
	{
		Bulk,
		DeathRespawn,
	}

	private struct PendingDeathRespawn
	{
		public int MonsterId;
		public bool IsNamed;
	}

	private struct RespawnTimer
	{
		public double RemainingSeconds;
		public bool IsNamed;
		public int MonsterId;
		public MonsterSpawner Spawner;
	}

	public override void _Ready()
	{
		_spawningEnabled = false;

		if (Engine.IsEditorHint())
		{
			AdoptPersistedMonstersInEditor();
			return;
		}

		StripPersistedMonsters();
		MonsterSpawnerActivationManager.Register(this);
		if (SpawningEnabled)
		{
			ActivateFromProximity();
		}
	}

	public override void _ExitTree()
	{
		if (!Engine.IsEditorHint())
		{
			MonsterSpawnerActivationManager.Unregister(this);
		}
	}

	internal bool IsActivated => _spawningEnabled;

	/// <summary>
	///     Enables spawning and fills missing mobs. Safe to call repeatedly; no-op once activated.
	/// </summary>
	internal void ActivateFromProximity()
	{
		if (Engine.IsEditorHint())
		{
			return;
		}

		lock (_spawnPlacementLock)
		{
			if (_spawningEnabled || SpawnPlanInProgress)
			{
				return;
			}

			_pendingPlanKind = SpawnPlanKind.Bulk;
			BeginBulkSpawnPlan(enableSpawningOnApply: true);
		}
	}

	public void BakeSpawnSlots()
	{
		MonsterSpawnSlotBaker.BakeForSpawner(this);
	}

	internal void SetBakedSpawnSlots(IReadOnlyList<Vector3> slots)
	{
		BakedSpawnSlots.Clear();
		foreach (var slot in slots)
		{
			BakedSpawnSlots.Add(slot);
		}

		NotifyPropertyListChanged();
	}

	/// <summary>
	///     Clears any monsters saved under this spawner. Used after Fill rebuild.
	/// </summary>
	public void EnsureEditorPreviewMonsters()
	{
		StripPersistedMonsters();
	}

	public override void _Process(double delta)
	{
		TryApplyCompletedSpawnPlan();

		if (Engine.IsEditorHint())
		{
			return;
		}

		if (!_spawningEnabled)
		{
			return;
		}

		TryStartNextDeathRespawnPlan();

		ScheduleRespawnsForDeadMonsters(RegularMonsters, _regularMonsterIds, RegularMonsterRespawnDelaySeconds, isNamed: false);
		ScheduleRespawnsForDeadMonsters(NamedMonsters, _namedMonsterIds, NamedMonsterRespawnDelaySeconds, isNamed: true);

		var frame = Engine.GetProcessFrames();
		if (LastProcessedFrame == frame)
		{
			return;
		}

		LastProcessedFrame = frame;
		ProcessGlobalRespawnTimers(delta);
	}

	public void DeleteAndRespawnAllMobs()
	{
		if (SpawnPlanInProgress)
		{
			GD.PushWarning($"MonsterSpawner '{Name}': spawn plan already in progress.");
			return;
		}

		Interlocked.Increment(ref _spawnGeneration);
		_planReadyGeneration = 0;
		_completedSpawnPlan = null;
		_completedSpawnPlanFailed = false;

		lock (_spawnPlacementLock)
		{
			ClearPendingRespawnTimersForThisSpawner();
			_pendingDeathRespawns.Clear();
			DeleteAllSpawnedMonsters();
			SpawnPlacementInvalid = false;
			_pendingPlanKind = SpawnPlanKind.Bulk;
			BeginBulkSpawnPlan(enableSpawningOnApply: !Engine.IsEditorHint());
		}
	}

	internal void DeleteAllSpawnedMonstersForBatch()
	{
		Interlocked.Increment(ref _spawnGeneration);
		_planReadyGeneration = 0;
		_completedSpawnPlan = null;
		_completedSpawnPlanFailed = false;

		lock (_spawnPlacementLock)
		{
			ClearPendingRespawnTimersForThisSpawner();
			_pendingDeathRespawns.Clear();
			DeleteAllSpawnedMonsters();
			SpawnPlacementInvalid = false;
		}
	}

	internal bool TryApplyInstantBulkRespawn()
	{
		lock (_spawnPlacementLock)
		{
			if (!TryBuildPlanFromBakedSlots(
					TargetRegularMonsterCount,
					TargetNamedMonsterCount,
					CaptureOccupiedWorldPositions(),
					out var plan))
			{
				return false;
			}

			SpawnPlacementInvalid = false;
			ApplySpawnPlan(plan);
			return true;
		}
	}

	internal void ApplyPreplannedSpawnPlan(MonsterSpawnPlan plan)
	{
		lock (_spawnPlacementLock)
		{
			SpawnPlacementInvalid = false;
			ApplySpawnPlan(plan);
		}
	}

	private void AdoptPersistedMonstersInEditor()
	{
		RegularMonsters.Clear();
		NamedMonsters.Clear();

		foreach (var child in GetChildren())
		{
			if (child is not Monster monster)
			{
				continue;
			}

			SetMonsterOwnerForPersistence(monster);

			if (monster.IsNamed)
			{
				NamedMonsters.Add(monster);
			}
			else
			{
				RegularMonsters.Add(monster);
			}
		}

		RebuildMonsterIdLists();
	}

	private void StripPersistedMonsters()
	{
		foreach (var child in GetChildren())
		{
			if (child is not Monster && !child.Name.ToString().StartsWith("Monster_", StringComparison.Ordinal))
			{
				continue;
			}

			if (Engine.IsEditorHint())
			{
				child.Free();
			}
			else
			{
				child.QueueFree();
			}
		}

		RegularMonsters.Clear();
		NamedMonsters.Clear();
		_regularMonsterIds.Clear();
		_namedMonsterIds.Clear();
	}

	private void BeginBulkSpawnPlan(bool enableSpawningOnApply)
	{
		_pendingPlanKind = SpawnPlanKind.Bulk;
		_enableSpawningOnNextApply = enableSpawningOnApply;

		var occupied = CaptureOccupiedWorldPositions();
		if (TryBuildPlanFromBakedSlots(TargetRegularMonsterCount, TargetNamedMonsterCount, occupied, out var instantPlan))
		{
			if (enableSpawningOnApply)
			{
				_spawningEnabled = true;
			}

			SpawnPlacementInvalid = false;
			ApplySpawnPlan(instantPlan);
			return;
		}

		SpawnPlanInProgress = true;
		NotifyPropertyListChanged();

		var generation = Volatile.Read(ref _spawnGeneration);
		var origin = GlobalPosition;
		var radius = SpawnRadiusMeters;
		var targetRegular = TargetRegularMonsterCount;
		var targetNamed = TargetNamedMonsterCount;

		_ = Task.Run(() => PlanSpawnAsync(this, generation, origin, radius, targetRegular, targetNamed, occupied));
	}

	private void BeginDeathRespawnPlan(int monsterId, bool isNamed)
	{
		if (TryApplyDeathRespawnFromBakedSlot(monsterId, isNamed))
		{
			return;
		}

		_pendingPlanKind = SpawnPlanKind.DeathRespawn;
		_deathRespawnMonsterId = monsterId;
		_deathRespawnIsNamed = isNamed;
		SpawnPlanInProgress = true;
		_enableSpawningOnNextApply = false;
		NotifyPropertyListChanged();

		var generation = Volatile.Read(ref _spawnGeneration);
		var origin = GlobalPosition;
		var radius = SpawnRadiusMeters;
		var targetRegular = isNamed ? 0 : 1;
		var targetNamed = isNamed ? 1 : 0;
		var occupied = CaptureOccupiedWorldPositions();

		_ = Task.Run(() => PlanSpawnAsync(this, generation, origin, radius, targetRegular, targetNamed, occupied));
	}

	private bool TryBuildPlanFromBakedSlots(
		int targetRegular,
		int targetNamed,
		Vector3[] occupied,
		out MonsterSpawnPlan plan)
	{
		plan = null!;
		var totalNeeded = targetRegular + targetNamed;
		if (BakedSpawnSlots.Count < totalNeeded)
		{
			return false;
		}

		var candidates = new List<Vector3>(BakedSpawnSlots.Count);
		foreach (var slot in BakedSpawnSlots)
		{
			candidates.Add(slot);
		}

		ShuffleSlots(candidates);

		var regularPositions = new List<Vector3>(targetRegular);
		var namedPositions = new List<Vector3>(targetNamed);
		var picked = new List<Vector3>(occupied.Length + totalNeeded);
		foreach (var position in occupied)
		{
			picked.Add(position);
		}

		foreach (var candidate in candidates)
		{
			if (!IsBakedSlotStillValid(candidate, picked))
			{
				continue;
			}

			if (regularPositions.Count < targetRegular)
			{
				regularPositions.Add(candidate);
				picked.Add(candidate);
				continue;
			}

			if (namedPositions.Count < targetNamed)
			{
				namedPositions.Add(candidate);
				picked.Add(candidate);
			}

			if (regularPositions.Count >= targetRegular && namedPositions.Count >= targetNamed)
			{
				break;
			}
		}

		if (regularPositions.Count < targetRegular || namedPositions.Count < targetNamed)
		{
			return false;
		}

		plan = new MonsterSpawnPlan(regularPositions, namedPositions);
		return true;
	}

	private bool TryApplyDeathRespawnFromBakedSlot(int monsterId, bool isNamed)
	{
		if (BakedSpawnSlots.Count == 0)
		{
			return false;
		}

		lock (_spawnPlacementLock)
		{
			var occupied = CaptureOccupiedWorldPositions();
			var candidates = new List<Vector3>(BakedSpawnSlots.Count);
			foreach (var slot in BakedSpawnSlots)
			{
				candidates.Add(slot);
			}

			ShuffleSlots(candidates);
			foreach (var candidate in candidates)
			{
				if (!IsBakedSlotStillValid(candidate, occupied))
				{
					continue;
				}

				if (isNamed)
				{
					if (TrySpawnNamedMonsterAt(monsterId, candidate))
					{
						return true;
					}
				}
				else if (SpawnRegularMonsterAt(monsterId, candidate, ResolveSlotIndex(candidate)))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static bool IsBakedSlotStillValid(Vector3 candidate, IReadOnlyList<Vector3> occupied)
	{
		if (WalkSurfaceCache.HasOutdoorSpawnChannel
			&& !WalkSurfaceCache.IsOutdoorSpawnFootprintAcceptable(candidate.X, candidate.Z))
		{
			return false;
		}

		var minSeparationSq = MonsterSpawnPlacement.MinMobSeparationMeters * MonsterSpawnPlacement.MinMobSeparationMeters;
		foreach (var position in occupied)
		{
			var dx = candidate.X - position.X;
			var dz = candidate.Z - position.Z;
			if (dx * dx + dz * dz < minSeparationSq)
			{
				return false;
			}
		}

		return true;
	}

	private static void ShuffleSlots(List<Vector3> candidates)
	{
		for (var i = candidates.Count - 1; i > 0; i--)
		{
			var j = Random.Shared.Next(i + 1);
			(candidates[i], candidates[j]) = (candidates[j], candidates[i]);
		}
	}

	private static void PlanSpawnAsync(
		MonsterSpawner spawner,
		int generation,
		Vector3 origin,
		float radius,
		int targetRegular,
		int targetNamed,
		Vector3[] occupied)
	{
		try
		{
			WalkSurfaceCache.PreloadChunksForRadius(origin.X, origin.Z, radius + 1f);
			var plan = MonsterSpawnPlanner.Plan(
				origin,
				radius,
				targetRegular,
				targetNamed,
				occupied,
				new AtlasMonsterSpawnGroundQuery(),
				Random.Shared);

			spawner._completedSpawnPlan = plan;
			spawner._completedSpawnPlanFailed = false;
			Volatile.Write(ref spawner._planReadyGeneration, generation);
		}
		catch (Exception ex)
		{
			GD.PushError($"MonsterSpawner: background spawn planning failed: {ex.Message}");
			spawner._completedSpawnPlan = null;
			spawner._completedSpawnPlanFailed = true;
			Volatile.Write(ref spawner._planReadyGeneration, generation);
		}
	}

	private void TryApplyCompletedSpawnPlan()
	{
		lock (_spawnPlacementLock)
		{
			TryApplyCompletedSpawnPlanCore();
		}
	}

	private bool TryApplyCompletedSpawnPlanCore()
	{
		var readyGeneration = Volatile.Read(ref _planReadyGeneration);
		if (readyGeneration == 0 || readyGeneration != Volatile.Read(ref _spawnGeneration))
		{
			return false;
		}

		Volatile.Write(ref _planReadyGeneration, 0);

		if (_completedSpawnPlanFailed)
		{
			_completedSpawnPlanFailed = false;
			MarkSpawnPlacementInvalid();
			FinishSpawnPlanInProgress();
			return true;
		}

		var plan = _completedSpawnPlan;
		_completedSpawnPlan = null;
		if (plan is null)
		{
			FinishSpawnPlanInProgress();
			return false;
		}

		if (_enableSpawningOnNextApply)
		{
			_spawningEnabled = true;
		}

		if (_pendingPlanKind == SpawnPlanKind.DeathRespawn)
		{
			ApplyDeathRespawnPlan(plan, _deathRespawnMonsterId, _deathRespawnIsNamed);
		}
		else
		{
			ApplySpawnPlan(plan);
		}

		FinishSpawnPlanInProgress();
		return true;
	}

	private void FinishSpawnPlanInProgress()
	{
		SpawnPlanInProgress = false;
		_enableSpawningOnNextApply = false;
		NotifyPropertyListChanged();
		TryStartNextDeathRespawnPlanCore();
	}

	private void EnqueueDeathRespawn(int monsterId, bool isNamed)
	{
		lock (_spawnPlacementLock)
		{
			if (!_spawningEnabled || SpawnPlacementInvalid)
			{
				return;
			}

			_pendingDeathRespawns.Enqueue(new PendingDeathRespawn
			{
				MonsterId = monsterId,
				IsNamed = isNamed,
			});
		}

		TryStartNextDeathRespawnPlan();
	}

	private void TryStartNextDeathRespawnPlan()
	{
		lock (_spawnPlacementLock)
		{
			TryStartNextDeathRespawnPlanCore();
		}
	}

	private void TryStartNextDeathRespawnPlanCore()
	{
		if (SpawnPlanInProgress || !_spawningEnabled || SpawnPlacementInvalid)
		{
			return;
		}

		if (Volatile.Read(ref _planReadyGeneration) != 0)
		{
			return;
		}

		if (_pendingDeathRespawns.Count == 0)
		{
			return;
		}

		var next = _pendingDeathRespawns.Dequeue();
		BeginDeathRespawnPlan(next.MonsterId, next.IsNamed);
	}

	private void ApplyDeathRespawnPlan(MonsterSpawnPlan plan, int monsterId, bool isNamed)
	{
		SpawnPlacementInvalid = false;
		_spawnPlacement.ResetOccupied(CollectOccupiedWorldPositions());

		if (isNamed)
		{
			if (plan.NamedPositions.Count > 0 && TrySpawnNamedMonsterAt(monsterId, plan.NamedPositions[0]))
			{
				return;
			}

			if (!TrySpawnNamedMonster(monsterId))
			{
				MarkSpawnPlacementInvalid();
			}

			return;
		}

		if (plan.RegularPositions.Count > 0 && SpawnRegularMonsterAt(monsterId, plan.RegularPositions[0], ResolveSlotIndex(plan.RegularPositions[0])))
		{
			return;
		}

		if (WalkSurfaceCache.HasOutdoorSpawnChannel && WalkSurfaceCache.HasChunkCoverageAt(GlobalPosition.X, GlobalPosition.Z))
		{
			MarkSpawnPlacementInvalid();
			return;
		}

		if (!TrySpawnRegularMonster(monsterId))
		{
			MarkSpawnPlacementInvalid();
		}
	}

	private void ApplySpawnPlan(MonsterSpawnPlan plan)
	{
		SpawnPlacementInvalid = false;
		ResetBindSlotCounter();
		_spawnPlacement.Reset(CollectOccupiedWorldPositions());

		foreach (var position in plan.RegularPositions)
		{
			if (!SpawnRegularMonsterAt(TakeMonsterId(), position, ResolveSlotIndex(position)))
			{
				MarkSpawnPlacementInvalid();
				return;
			}
		}

		for (var i = CountAlive(RegularMonsters); i < TargetRegularMonsterCount; i++)
		{
			if (WalkSurfaceCache.HasOutdoorSpawnChannel && WalkSurfaceCache.HasChunkCoverageAt(GlobalPosition.X, GlobalPosition.Z))
			{
				MarkSpawnPlacementInvalid();
				return;
			}

			if (!TrySpawnRegularMonster(TakeMonsterId()))
			{
				MarkSpawnPlacementInvalid();
				return;
			}
		}

		foreach (var position in plan.NamedPositions)
		{
			TrySpawnNamedMonsterAt(TakeMonsterId(), position);
		}

		for (var i = CountAlive(NamedMonsters); i < TargetNamedMonsterCount; i++)
		{
			TrySpawnNamedMonster(TakeMonsterId());
		}
	}

	private void MarkSpawnPlacementInvalid()
	{
		SpawnPlacementInvalid = true;
		NotifyPropertyListChanged();
		GD.PushWarning($"MonsterSpawner '{Name}': no valid spawn positions remain within {SpawnRadiusMeters:0.##}m.");
	}

	private Vector3[] CaptureOccupiedWorldPositions()
	{
		var occupied = new List<Vector3>();
		foreach (var position in CollectOccupiedWorldPositions())
		{
			occupied.Add(position);
		}

		return occupied.ToArray();
	}

	private IEnumerable<Vector3> CollectOccupiedWorldPositions()
	{
		foreach (var node in RegularMonsters)
		{
			if (IsInstanceValid(node))
			{
				yield return node.GlobalPosition;
			}
		}

		foreach (var node in NamedMonsters)
		{
			if (IsInstanceValid(node))
			{
				yield return node.GlobalPosition;
			}
		}
	}

	private bool TryFindSpawnWorldPosition(out Vector3 spawnWorldPosition)
	{
		return _spawnPlacement.TryFindSpawnPosition(this, SpawnRadiusMeters, out spawnWorldPosition);
	}

	private void ClearPendingRespawnTimersForThisSpawner()
	{
		lock (TimerLock)
		{
			GlobalRespawnTimers.RemoveAll(timer => timer.Spawner == this);
		}

		_pendingDeathRespawns.Clear();
	}

	private void DeleteAllSpawnedMonsters()
	{
		foreach (var node in RegularMonsters)
		{
			if (IsInstanceValid(node))
			{
				if (Engine.IsEditorHint())
				{
					node.Free();
				}
				else
				{
					node.QueueFree();
				}
			}
		}

		foreach (var node in NamedMonsters)
		{
			if (IsInstanceValid(node))
			{
				if (Engine.IsEditorHint())
				{
					node.Free();
				}
				else
				{
					node.QueueFree();
				}
			}
		}

		RegularMonsters.Clear();
		NamedMonsters.Clear();
		_regularMonsterIds.Clear();
		_namedMonsterIds.Clear();
	}

	private static int CountAlive(Array<Node3D> spawnedMonsters)
	{
		var alive = 0;
		foreach (var node in spawnedMonsters)
		{
			if (IsInstanceValid(node))
			{
				alive++;
			}
		}

		return alive;
	}

	private void ScheduleRespawnsForDeadMonsters(
		Array<Node3D> spawnedMonsters,
		List<int> monsterIds,
		int respawnDelaySeconds,
		bool isNamed)
	{
		for (var i = spawnedMonsters.Count - 1; i >= 0; i--)
		{
			if (IsInstanceValid(spawnedMonsters[i]))
			{
				continue;
			}

			var monsterId = i < monsterIds.Count ? monsterIds[i] : TakeMonsterId();
			if (i < monsterIds.Count)
			{
				monsterIds.RemoveAt(i);
			}

			spawnedMonsters.RemoveAt(i);
			lock (TimerLock)
			{
				GlobalRespawnTimers.Add(new RespawnTimer
				{
					RemainingSeconds = respawnDelaySeconds,
					IsNamed = isNamed,
					MonsterId = monsterId,
					Spawner = this,
				});
			}
		}
	}

	private static void ProcessGlobalRespawnTimers(double delta)
	{
		List<RespawnTimer> expired;
		lock (TimerLock)
		{
			if (GlobalRespawnTimers.Count == 0)
			{
				return;
			}

			expired = [];
			for (var i = GlobalRespawnTimers.Count - 1; i >= 0; i--)
			{
				var timer = GlobalRespawnTimers[i];
				timer.RemainingSeconds -= delta;
				if (timer.RemainingSeconds <= 0)
				{
					expired.Add(timer);
					GlobalRespawnTimers.RemoveAt(i);
				}
				else
				{
					GlobalRespawnTimers[i] = timer;
				}
			}
		}

		foreach (var timer in expired)
		{
			if (!IsInstanceValid(timer.Spawner))
			{
				continue;
			}

			timer.Spawner.EnqueueDeathRespawn(timer.MonsterId, timer.IsNamed);
		}
	}

	private bool TrySpawnRegularMonster(int monsterId)
	{
		if (!TryFindSpawnWorldPosition(out var spawnWorldPosition))
		{
			return false;
		}

		return SpawnRegularMonsterAt(monsterId, spawnWorldPosition, ResolveSlotIndex(spawnWorldPosition));
	}

	public NavPathResult RequestOutdoorPath(Monster monster, Vector3 goalWorld)
	{
		var request = new NavPathRequest(
			monster.GlobalPosition,
			OutdoorPathQuery.ClampGoalToLeash(goalWorld, LeashCenterWorld, LeashRadiusMeters),
			LeashCenterWorld,
			LeashRadiusMeters);
		return OutdoorPathQuery.FindPath(request);
	}

	public bool TryNavigateMonster(Monster monster, Vector3 goalWorld, out NavPathFailReason reason)
	{
		var result = RequestOutdoorPath(monster, goalWorld);
		reason = result.Reason;
		if (!result.Success)
		{
			return false;
		}

		return monster.TrySetNavPath(result.Waypoints);
	}

	private int ResolveSlotIndex(Vector3 spawnWorldPosition)
	{
		for (var i = 0; i < BakedSpawnSlots.Count; i++)
		{
			var slot = BakedSpawnSlots[i];
			var dx = slot.X - spawnWorldPosition.X;
			var dz = slot.Z - spawnWorldPosition.Z;
			if (dx * dx + dz * dz < 0.05f * 0.05f)
			{
				return i;
			}
		}

		var index = _nextBindSlotIndex;
		_nextBindSlotIndex++;
		return index;
	}

	private void ResetBindSlotCounter()
	{
		_nextBindSlotIndex = 0;
	}

	private bool SpawnRegularMonsterAt(int monsterId, Vector3 spawnWorldPosition, int slotIndex)
	{
		var scene = GD.Load<PackedScene>(MonsterScenePath);
		if (scene is null)
		{
			GD.PushError($"MonsterSpawner: failed to load scene '{MonsterScenePath}'.");
			return false;
		}

		if (scene.Instantiate() is not Monster monster)
		{
			GD.PushError($"MonsterSpawner: '{MonsterScenePath}' is not a Monster scene.");
			return false;
		}

		const MonsterType monsterType = MonsterType.Палочник;
		const int level = 1;
		const bool isNamed = false;

		monster.MonsterType = monsterType;
		monster.Level = level;
		monster.IsNamed = isNamed;
		monster.Name = BuildMonsterNodeName(monsterType, level, isNamed, monsterId);

		AddChild(monster);
		ApplySpawnTransform(monster, spawnWorldPosition);
		monster.BindHome(new MonsterHomeBinding(slotIndex, spawnWorldPosition));
		SetMonsterOwnerForPersistence(monster);
		RegularMonsters.Add(monster);
		_regularMonsterIds.Add(monsterId);
		return true;
	}

	private static void ApplySpawnTransform(Monster monster, Vector3 spawnWorldPosition)
	{
		spawnWorldPosition.Y += monster.GetSpawnOriginYOffset();
		monster.GlobalPosition = spawnWorldPosition;
		monster.Angle = WorldObject.CreateRandomSpawnAngle();
		monster.RegisterMultiMeshVisualDeferred();
	}

	private bool TrySpawnNamedMonster(int monsterId)
	{
		return false;
	}

	private bool TrySpawnNamedMonsterAt(int monsterId, Vector3 spawnWorldPosition)
	{
		return false;
	}

	private static int TakeMonsterId()
	{
		lock (MonsterIdLock)
		{
			return _globalNextMonsterId++;
		}
	}

	private void RebuildMonsterIdLists()
	{
		_regularMonsterIds.Clear();
		foreach (var node in RegularMonsters)
		{
			if (!IsInstanceValid(node))
			{
				continue;
			}

			_regularMonsterIds.Add(TryParseMonsterIdFromName(node.Name, out var id) ? id : TakeMonsterId());
		}

		_namedMonsterIds.Clear();
		foreach (var node in NamedMonsters)
		{
			if (!IsInstanceValid(node))
			{
				continue;
			}

			_namedMonsterIds.Add(TryParseMonsterIdFromName(node.Name, out var id) ? id : TakeMonsterId());
		}

		EnsureMonsterIdCounterAboveTakenIds();
	}

	private void EnsureMonsterIdCounterAboveTakenIds()
	{
		var minNextId = FirstMonsterId;
		foreach (var id in _regularMonsterIds)
		{
			minNextId = Mathf.Max(minNextId, id + 1);
		}

		foreach (var id in _namedMonsterIds)
		{
			minNextId = Mathf.Max(minNextId, id + 1);
		}

		lock (MonsterIdLock)
		{
			_globalNextMonsterId = Mathf.Max(_globalNextMonsterId, minNextId);
		}
	}

	private static bool TryParseMonsterIdFromName(StringName name, out int id)
	{
		id = 0;
		var parts = name.ToString().Split('_');
		if (parts.Length < 4)
		{
			return false;
		}

		return int.TryParse(parts[^1], out id);
	}

	private static string BuildMonsterNodeName(MonsterType type, int level, bool isNamed, int id)
	{
		var prefix = isNamed ? "MonsterNamed" : "Monster";
		return $"{prefix}_{type}_{level}_{id}";
	}

	private void SetMonsterOwnerForPersistence(Monster monster)
	{
		WorldObjectDumpFillCommon.SetOwnerIfEditor(this, monster);

		if (!Engine.IsEditorHint() || monster.Owner != this)
		{
			return;
		}

		var sceneRoot = GetTree()?.EditedSceneRoot;
		if (sceneRoot is not null && sceneRoot != this)
		{
			monster.Owner = sceneRoot;
		}
	}
}
