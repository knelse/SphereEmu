using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using SphServer.Godot.Scripts.Objects.Fill;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;
using SphServer.Godot.Scripts.Terrain.WalkSurface;
using SphServer.Shared.GameData.Enums;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

[Tool]
public partial class MonsterSpawner : Node3D
{
	private const int FirstMonsterId = 10000;
	private const string ErrorNamePrefix = "ERROR - ";

	private static readonly List<RespawnTimer> GlobalRespawnTimers = [];
	private static readonly object TimerLock = new();
	private static readonly object MonsterIdLock = new();
	private static ulong LastProcessedFrame;
	private static int _globalNextMonsterId = FirstMonsterId;

	private readonly List<int> _regularMonsterIds = [];
	private readonly List<int> _namedMonsterIds = [];
	private readonly object _spawnPlacementLock = new();
	private int _cachedRegularMinLevel = int.MinValue;
	private int _cachedRegularMaxLevel = int.MinValue;
	private global::System.Collections.Generic.Dictionary<int, IReadOnlyList<MonsterType>>? _regularMonsterTypesByLevel;

	[Export]
	public int TargetNamedMonsterCount = 0;

	[Export]
	public int TargetRegularMonsterCount = 3;

	[Export]
	public float SpawnRadiusMeters = OutdoorFieldConfig.DefaultSpawnRadiusMeters;

	[Export]
	public float LeashRadiusMeters = OutdoorFieldConfig.DefaultLeashRadiusMeters;

	[Export]
	public float AggroRadiusMeters = 12f;

	[Export]
	public bool OutdoorChaseEnabled = true;

	[Export]
	public bool SpawnPlacementInvalid { get; private set; }

	[Export]
	public bool HasSpawnError { get; private set; }

	[Export]
	public string OriginalDisplayName { get; private set; } = string.Empty;

	[Export]
	public Array<Vector3> BakedSpawnSlots { get; private set; } = [];

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

	[ExportToolButton("Bake spawn slots")]
	public Callable BakeSpawnSlotsButton => Callable.From(BakeSpawnSlots);

	/// <summary>
	///     When set in the scene, this spawner activates and spawns on server load (debug / town presets).
	/// </summary>
	[Export]
	public bool SpawningEnabled { get; set; }

	private bool _spawningEnabled;
	private readonly Queue<PendingDeathRespawn> _pendingDeathRespawns = new();
	private int _nextBindSlotIndex;
	public Vector3 LeashCenterWorld => GlobalPosition;

	internal int DesiredBakedSlotPoolCount =>
		OutdoorFieldConfig.ComputeBakedSlotPoolCount(
			TargetRegularMonsterCount + TargetNamedMonsterCount,
			SpawnRadiusMeters);

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

	internal void RefreshEditorGizmo()
	{
		if (Engine.IsEditorHint())
		{
			UpdateGizmos();
		}
	}

	/// <summary>
	///     Used by the editor gizmo plugin; Godot Get() does not reliably return exported arrays on C# nodes.
	/// </summary>
	public Array<Vector3> GetEditorBakedSpawnSlots() => BakedSpawnSlots;

	public override void _Notification(int what)
	{
		if (Engine.IsEditorHint() && what == NotificationTransformChanged)
		{
			UpdateGizmos();
		}
	}

	public override bool _Set(StringName property, Variant value)
	{
		if (!base._Set(property, value))
		{
			return false;
		}

		RefreshEditorGizmo();
		return true;
	}

	public override void _Ready()
	{
		_spawningEnabled = false;

		if (Engine.IsEditorHint())
		{
			AdoptPersistedMonstersInEditor();
			RefreshEditorGizmo();
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
			if (_spawningEnabled || HasSpawnError)
			{
				return;
			}

			BeginBulkSpawnPlan(enableSpawningOnApply: true);
		}
	}

	public void BakeSpawnSlots()
	{
		MonsterSpawnSlotBaker.BakeForSpawner(this);
	}

	internal void MarkSpawnError()
	{
		if (string.IsNullOrEmpty(OriginalDisplayName))
		{
			OriginalDisplayName = Name;
		}

		if (!Name.ToString().StartsWith(ErrorNamePrefix, StringComparison.Ordinal))
		{
			Name = ErrorNamePrefix + OriginalDisplayName;
		}

		HasSpawnError = true;
		SpawnPlacementInvalid = true;
		NotifyPropertyListChanged();
		RefreshEditorGizmo();
	}

	internal void ClearSpawnError()
	{
		HasSpawnError = false;
		SpawnPlacementInvalid = false;
		if (!string.IsNullOrEmpty(OriginalDisplayName)
			&& Name.ToString().StartsWith(ErrorNamePrefix, StringComparison.Ordinal))
		{
			Name = OriginalDisplayName;
		}

		NotifyPropertyListChanged();
		RefreshEditorGizmo();
	}

	internal void SetBakedSpawnSlots(IReadOnlyList<Vector3> slots)
	{
		BakedSpawnSlots.Clear();
		foreach (var slot in slots)
		{
			BakedSpawnSlots.Add(slot);
		}

		NotifyPropertyListChanged();
		RefreshEditorGizmo();
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
		lock (_spawnPlacementLock)
		{
			ClearPendingRespawnTimersForThisSpawner();
			_pendingDeathRespawns.Clear();
			DeleteAllSpawnedMonsters();
			SpawnPlacementInvalid = false;

			if (BakedSpawnSlots.Count < DesiredBakedSlotPoolCount)
			{
				MonsterSpawnSlotBaker.BakeForSpawner(this);
			}

			if (!TryApplyInstantBulkRespawn())
			{
				MarkSpawnPlacementInvalid();
				return;
			}

			_spawningEnabled = true;
			SpawnPlacementInvalid = false;
		}
	}

	internal void DeleteAllSpawnedMonstersForBatch()
	{
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
		var occupied = CaptureOccupiedWorldPositions();
		if (TryBuildPlanFromBakedSlots(TargetRegularMonsterCount, TargetNamedMonsterCount, occupied, out var plan))
		{
			if (enableSpawningOnApply)
			{
				_spawningEnabled = true;
			}

			SpawnPlacementInvalid = false;
			ApplySpawnPlan(plan);
			return;
		}

		MarkSpawnPlacementInvalid();
	}

	private void BeginDeathRespawnPlan(int monsterId, bool isNamed)
	{
		if (TryApplyDeathRespawnFromBakedSlot(monsterId, isNamed))
		{
			return;
		}

		MarkSpawnPlacementInvalid();
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

	private bool IsBakedSlotStillValid(Vector3 candidate, IReadOnlyList<Vector3> occupied)
	{
		if (!OutdoorPathQuery.IsInsideLeash(candidate, GlobalPosition, SpawnRadiusMeters))
		{
			return false;
		}

		if (WalkSurfaceCache.HasWalkableField
			&& !WalkSurfaceCache.IsSpawnFootprintAcceptable(candidate.X, candidate.Z)
			&& !WalkSurfaceCache.IsLooseOutdoorWalkCandidate(candidate.X, candidate.Z))
		{
			return false;
		}

		var minSeparationSq = OutdoorFieldConfig.MinSlotSeparationMeters * OutdoorFieldConfig.MinSlotSeparationMeters;
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
		if (!_spawningEnabled || SpawnPlacementInvalid || HasSpawnError)
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

	private void ApplySpawnPlan(MonsterSpawnPlan plan)
	{
		SpawnPlacementInvalid = false;
		ResetBindSlotCounter();

		foreach (var position in plan.RegularPositions)
		{
			if (!SpawnRegularMonsterAt(TakeMonsterId(), position, ResolveSlotIndex(position)))
			{
				MarkSpawnPlacementInvalid();
				return;
			}
		}

		foreach (var position in plan.NamedPositions)
		{
			TrySpawnNamedMonsterAt(TakeMonsterId(), position);
		}
	}

	private void MarkSpawnPlacementInvalid()
	{
		SpawnPlacementInvalid = true;
		NotifyPropertyListChanged();
		GD.PushWarning(
			$"MonsterSpawner '{Name}': insufficient baked spawn slots (have {BakedSpawnSlots.Count}, "
			+ $"need {TargetRegularMonsterCount + TargetNamedMonsterCount}). Run Bake spawn slots.");
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

	public NavPathResult RequestOutdoorPath(Monster monster, Vector3 goalWorld)
	{
		var startWorld = monster.GlobalPosition;
		var resolvedGoal = OutdoorPathQuery.ClampGoalToLeash(goalWorld, LeashCenterWorld, LeashRadiusMeters);
		var atlasVerticalDelta = monster.GetAtlasVerticalDelta();
		if (Mathf.IsZeroApprox(atlasVerticalDelta))
		{
			atlasVerticalDelta = ResolveAtlasVerticalDelta(GlobalPosition);
		}

		if (OutdoorPathQuery.TrySampleOutdoorGroundY(
				startWorld.X, startWorld.Z, out var startGroundY, atlasVerticalDelta))
		{
			startWorld.Y = startGroundY;
		}

		if (OutdoorPathQuery.TrySampleOutdoorGroundY(
				resolvedGoal.X, resolvedGoal.Z, out var goalGroundY, atlasVerticalDelta))
		{
			resolvedGoal.Y = goalGroundY;
		}

		var request = new NavPathRequest(
			startWorld,
			resolvedGoal,
			LeashCenterWorld,
			LeashRadiusMeters);
		return OutdoorPathQuery.FindPath(request);
	}

	private float ResolveAtlasVerticalDelta(Vector3 referenceGodotGround)
	{
		var delta = MonsterSpawnGroundQuery.ComputeAtlasVerticalOffset(this);
		if (!Mathf.IsZeroApprox(delta))
		{
			return delta;
		}

		if (WalkSurfaceCache.TrySampleWalkableGround(referenceGodotGround.X, referenceGodotGround.Z, out var atlasY))
		{
			return atlasY - referenceGodotGround.Y;
		}

		return 0f;
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

	private static int RollInclusiveLevel(int minLevel, int maxLevel)
	{
		if (minLevel > maxLevel)
		{
			(minLevel, maxLevel) = (maxLevel, minLevel);
		}

		return Random.Shared.Next(minLevel, maxLevel + 1);
	}

	private void EnsureRegularMonsterTypeCache()
	{
		if (_regularMonsterTypesByLevel is not null
			&& _cachedRegularMinLevel == RegularMonsterMinLevel
			&& _cachedRegularMaxLevel == RegularMonsterMaxLevel)
		{
			return;
		}

		_cachedRegularMinLevel = RegularMonsterMinLevel;
		_cachedRegularMaxLevel = RegularMonsterMaxLevel;
		_regularMonsterTypesByLevel = MonsterSpawnerMonsterTypeLookup.BuildLevelSubset(
			RegularMonsterMinLevel,
			RegularMonsterMaxLevel);
	}

	private MonsterType PickRegularMonsterType(int level)
	{
		EnsureRegularMonsterTypeCache();
		if (_regularMonsterTypesByLevel is not null
			&& MonsterSpawnerMonsterTypeLookup.TryPickRandomMonsterType(_regularMonsterTypesByLevel, level, out var monsterType))
		{
			return monsterType;
		}

		GD.PushWarning(
			$"MonsterSpawner '{Name}': no monster type for regular level {level} (range {RegularMonsterMinLevel}-{RegularMonsterMaxLevel}); using {MonsterType.Палочник}.");
		return MonsterType.Палочник;
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

		var level = RollInclusiveLevel(RegularMonsterMinLevel, RegularMonsterMaxLevel);
		var monsterType = PickRegularMonsterType(level);
		const bool isNamed = false;

		monster.MonsterType = monsterType;
		monster.Level = level;
		monster.IsNamed = isNamed;
		monster.Name = BuildMonsterNodeName(monsterType, level, isNamed, monsterId);

		AddChild(monster);
		var resolvedPosition = ResolveBakedSlotPosition(spawnWorldPosition);
		var atlasVerticalDelta = ResolveAtlasVerticalDelta(resolvedPosition);
		monster.BindHome(
			new MonsterHomeBinding(
				slotIndex,
				resolvedPosition,
				GlobalPosition,
				LeashRadiusMeters,
				GetPath(),
				GetInstanceId(),
				atlasVerticalDelta),
			this);
		ApplySpawnTransform(monster, resolvedPosition);
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

	private Vector3 ResolveBakedSlotPosition(Vector3 bakedSlot)
	{
		if (MonsterSpawnGroundQuery.TryResolveSpawnGroundY(this, bakedSlot.X, bakedSlot.Z, out var groundY))
		{
			return new Vector3(bakedSlot.X, groundY, bakedSlot.Z);
		}

		return bakedSlot;
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
