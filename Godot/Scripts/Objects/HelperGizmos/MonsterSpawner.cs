using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using SphServer.Godot.Scripts.Objects.Fill;
using SphServer.Godot.Scripts.Navigation;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.World;
using SphServer.Shared.GameData.Enums;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

[Tool]
public partial class MonsterSpawner : Node3D
{
	private const int FirstMonsterId = 10000;
	private const string ErrorNamePrefix = "ERROR - ";
	private const int BonusNamedChancePercent = 3;

	private static readonly List<RespawnTimer> GlobalRespawnTimers = [];
	private static readonly object TimerLock = new ();
	private static readonly object MonsterIdLock = new ();
	private static ulong LastProcessedFrame;
	private static int _globalNextMonsterId = FirstMonsterId;

	private readonly List<int> _regularMonsterIds = [];
	private readonly List<int> _namedMonsterIds = [];
	private readonly List<NamedBossRank> _namedMonsterRanks = [];
	private readonly object _spawnPlacementLock = new ();
	private int _cachedRegularMinLevel = int.MinValue;
	private int _cachedRegularMaxLevel = int.MinValue;
	private global::System.Collections.Generic.Dictionary<int, IReadOnlyList<MonsterType>>? _regularMonsterTypesByLevel;

	[Export]
	public int TargetNamedMonsterCount = 0;

	[Export]
	public int TargetRegularMonsterCount = 3;

	/// <summary>
	/// Field default is outdoor. Indoor Y below -500 takes
	/// IndoorFieldConfig.DefaultSpawnRadiusMeters at placement
	/// </summary>
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

	/// <summary>
	/// Non-empty: types come from this list. Rolled level still sets stats and does not filter the
	/// list. Weighting matches the level pool (Курганник stays rare)
	/// </summary>
	[Export]
	public Array<MonsterType> MobTypeOverrides { get; set; } = [];

	[Export]
	public int RegularMonsterRespawnDelaySeconds = 60;

	[Export]
	public int NamedMonsterRespawnDelaySeconds = 60;

	[Export]
	public string MonsterScenePath = "res://Godot/Scenes/Monster.tscn";

	/// <summary>
	/// Not exported. Scene node_paths for Array of Node3D make Godot Get() fail ("Failed to get
	/// property 'RegularMonsters'")
	/// </summary>
	public Array<Node3D> RegularMonsters { get; set; } = [];

	/// <inheritdoc cref="RegularMonsters" />
	public Array<Node3D> NamedMonsters { get; set; } = [];

	[ExportToolButton ("Delete and respawn all mobs")]
	public Callable DeleteAndRespawnAllMobsButton => Callable.From (DeleteAndRespawnAllMobs);

	[ExportToolButton ("Bake spawn slots")]
	public Callable BakeSpawnSlotsButton => Callable.From (BakeSpawnSlots);

	/// <summary>
	/// Scene-set spawners activate on server load (debug / town presets)
	/// </summary>
	[Export]
	public bool SpawningEnabled { get; set; }

	private bool _spawningEnabled;
	private readonly Queue<PendingDeathRespawn> _pendingDeathRespawns = new ();
	private int _nextBindSlotIndex;
	private bool _bonusNamedResolved;
	private bool _bonusNamedSpawn;
	public Vector3 LeashCenterWorld => GlobalPosition;

	internal int DesiredBakedSlotPoolCount =>
		OutdoorFieldConfig.ComputeBakedSlotPoolCount (
			TargetRegularMonsterCount + TargetNamedMonsterCount,
			SpawnRadiusMeters);

	private struct PendingDeathRespawn
	{
		public int MonsterId;
		public NamedBossRank Rank;
	}

	private struct RespawnTimer
	{
		public double RemainingSeconds;
		public NamedBossRank Rank;
		public int MonsterId;
		public MonsterSpawner Spawner;
	}

	internal void RefreshEditorGizmo ()
	{
		if (Engine.IsEditorHint ())
		{
			UpdateGizmos ();
		}
	}

	/// <summary>
	/// Godot Get() does not reliably return exported arrays on C# nodes
	/// </summary>
	public Array<Vector3> GetEditorBakedSpawnSlots () => BakedSpawnSlots;

	public override void _Notification (int what)
	{
		if (Engine.IsEditorHint () && what == NotificationTransformChanged)
		{
			UpdateGizmos ();
		}
	}

	public override bool _Set (StringName property, Variant value)
	{
		if (!base._Set (property, value))
		{
			return false;
		}

		RefreshEditorGizmo ();
		return true;
	}

	public override void _Ready ()
	{
		_spawningEnabled = false;
		WorldChunkSlotHydration.HydrateIfNeeded (WorldContentKind.Monster, this);

		if (Engine.IsEditorHint ())
		{
			AdoptPersistedMonstersInEditor ();
			RefreshEditorGizmo ();
			return;
		}

		// Headless spawn-slot bake loads MainServer without runtime spawn
		if (MonsterSpawnSlotHeadlessBake.IsActive || AlchemyMaterialSpawnSlotHeadlessBake.IsActive)
		{
			return;
		}

		MonsterSpawnerActivationManager.Register (this);

		// Town/debug presets keep hierarchy mobs (cats). Spawn Y offset still applies to those
		// already-placed instances
		if (SpawningEnabled && TryAdoptPersistedMonstersAtRuntime ())
		{
			_spawningEnabled = true;
			FillMissingMonstersAfterAdopt ();
			return;
		}

		StripPersistedMonsters ();
		if (SpawningEnabled)
		{
			ActivateFromProximity ();
		}
	}

	public override void _ExitTree ()
	{
		if (!Engine.IsEditorHint ())
		{
			MonsterSpawnerActivationManager.Unregister (this);
		}
	}

	internal bool IsActivated => _spawningEnabled;

	internal void ActivateFromProximity ()
	{
		if (Engine.IsEditorHint ())
		{
			return;
		}

		lock (_spawnPlacementLock)
		{
			// SpawnPlacementInvalid means a prior activate already proved the slots unusable.
			// Proximity retries only spam warnings until a rebake
			if (_spawningEnabled || HasSpawnError || SpawnPlacementInvalid)
			{
				return;
			}

			BeginBulkSpawnPlan (enableSpawningOnApply: true);
		}
	}

	public void BakeSpawnSlots ()
	{
		_ = MonsterSpawnSlotBaker.BakeForSpawnerAsync (this);
	}

	internal void MarkSpawnError ()
	{
		if (string.IsNullOrEmpty (OriginalDisplayName))
		{
			OriginalDisplayName = Name;
		}

		if (!Name.ToString ().StartsWith (ErrorNamePrefix, StringComparison.Ordinal))
		{
			Name = ErrorNamePrefix + OriginalDisplayName;
		}

		HasSpawnError = true;
		SpawnPlacementInvalid = true;
		NotifyPropertyListChanged ();
		RefreshEditorGizmo ();
	}

	internal void ClearSpawnError ()
	{
		HasSpawnError = false;
		SpawnPlacementInvalid = false;
		if (!string.IsNullOrEmpty (OriginalDisplayName)
			&& Name.ToString ().StartsWith (ErrorNamePrefix, StringComparison.Ordinal))
		{
			Name = OriginalDisplayName;
		}

		NotifyPropertyListChanged ();
		RefreshEditorGizmo ();
	}

	internal void SetBakedSpawnSlots (IReadOnlyList<Vector3> slots, bool syncIndex = true)
	{
		BakedSpawnSlots.Clear ();
		foreach (var slot in slots)
		{
			BakedSpawnSlots.Add (slot);
		}

		if (syncIndex)
		{
			WorldChunkSlotHydration.UpdateIndexSlots (WorldContentKind.Monster, this, slots);
		}

		NotifyPropertyListChanged ();
		RefreshEditorGizmo ();
	}

	/// <summary>
	/// Fill rebuild drops monsters saved under this spawner
	/// </summary>
	public void EnsureEditorPreviewMonsters ()
	{
		StripPersistedMonsters ();
	}

	public override void _Process (double delta)
	{
		if (Engine.IsEditorHint ())
		{
			return;
		}

		if (!_spawningEnabled)
		{
			return;
		}

		TryStartNextDeathRespawnPlan ();

		ScheduleRespawnsForDeadMonsters (RegularMonsters, _regularMonsterIds, null, RegularMonsterRespawnDelaySeconds);
		ScheduleRespawnsForDeadMonsters (NamedMonsters, _namedMonsterIds, _namedMonsterRanks, NamedMonsterRespawnDelaySeconds);

		var frame = Engine.GetProcessFrames ();
		if (LastProcessedFrame == frame)
		{
			return;
		}

		LastProcessedFrame = frame;
		ProcessGlobalRespawnTimers (delta);
	}

	public void DeleteAndRespawnAllMobs ()
	{
		lock (_spawnPlacementLock)
		{
			ClearPendingRespawnTimersForThisSpawner ();
			_pendingDeathRespawns.Clear ();
			DeleteAllSpawnedMonsters ();
			SpawnPlacementInvalid = false;

			if (BakedSpawnSlots.Count < DesiredBakedSlotPoolCount)
			{
				MonsterSpawnSlotBaker.BakeForSpawner (this);
			}

			if (!TryApplyInstantBulkRespawn ())
			{
				MarkSpawnPlacementInvalid ();
				return;
			}

			_spawningEnabled = true;
			SpawnPlacementInvalid = false;
		}
	}

	internal void DeleteAllSpawnedMonstersForBatch ()
	{
		lock (_spawnPlacementLock)
		{
			ClearPendingRespawnTimersForThisSpawner ();
			_pendingDeathRespawns.Clear ();
			DeleteAllSpawnedMonsters ();
			SpawnPlacementInvalid = false;
		}
	}

	internal bool TryApplyInstantBulkRespawn ()
	{
		lock (_spawnPlacementLock)
		{
			if (!TryPlanPopulation (CaptureOccupiedWorldPositions (), out var plan))
			{
				return false;
			}

			SpawnPlacementInvalid = false;
			ApplySpawnPlan (plan);
			return true;
		}
	}

	internal void ApplyPreplannedSpawnPlan (MonsterSpawnPlan plan)
	{
		lock (_spawnPlacementLock)
		{
			SpawnPlacementInvalid = false;
			ApplySpawnPlan (plan);
		}
	}

	private void AdoptPersistedMonstersInEditor ()
	{
		RegularMonsters.Clear ();
		NamedMonsters.Clear ();

		foreach (var child in GetChildren ())
		{
			if (child is not Monster monster)
			{
				continue;
			}

			SetMonsterOwnerForPersistence (monster);

			if (monster.NamedBossRank != NamedBossRank.None)
			{
				NamedMonsters.Add (monster);
			}
			else
			{
				RegularMonsters.Add (monster);
			}
		}

		RebuildMonsterIdLists ();
	}

	/// <summary>
	/// Scene slots are navmesh height, so signed spawn Y still applies to hierarchy mobs
	/// </summary>
	private bool TryAdoptPersistedMonstersAtRuntime ()
	{
		RegularMonsters.Clear ();
		NamedMonsters.Clear ();
		_regularMonsterIds.Clear ();
		_namedMonsterIds.Clear ();
		_namedMonsterRanks.Clear ();
		ResetBindSlotCounter ();

		var adopted = 0;
		foreach (var child in GetChildren ())
		{
			if (child is not Monster monster || !IsInstanceValid (monster))
			{
				continue;
			}

			var slotWorld = monster.GlobalPosition;
			monster.BindHome (
				new MonsterHomeBinding (
					ResolveSlotIndex (slotWorld),
					slotWorld,
					GlobalPosition,
					LeashRadiusMeters,
					GetPath (),
					GetInstanceId (),
					atlasVerticalDelta: 0f),
				this);
			ApplySpawnTransform (monster, slotWorld, randomizeAngle: false);
			SetMonsterOwnerForPersistence (monster);

			if (monster.NamedBossRank != NamedBossRank.None)
			{
				NamedMonsters.Add (monster);
			}
			else
			{
				RegularMonsters.Add (monster);
			}

			adopted++;
		}

		if (adopted == 0)
		{
			return false;
		}

		RebuildMonsterIdLists ();
		return true;
	}

	private void FillMissingMonstersAfterAdopt ()
	{
		var needRegular = Math.Max (0, TargetRegularMonsterCount - CountAlive (RegularMonsters));
		var needNamed = Math.Max (0, EffectiveNamedTarget () - CountAlive (NamedMonsters));
		if (needRegular == 0 && needNamed == 0)
		{
			return;
		}

		lock (_spawnPlacementLock)
		{
			var occupied = CaptureOccupiedWorldPositions ();
			if (!TryBuildPlanFromBakedSlots (needRegular, needNamed, occupied, out var plan))
			{
				// The extra named roll must not fail a camp that already fits its regulars
				if (needRegular == 0 && _bonusNamedSpawn)
				{
					return;
				}

				if (_bonusNamedSpawn && needNamed > 0
					&& TryBuildPlanFromBakedSlots (needRegular, 0, occupied, out plan))
				{
					SpawnPlacementInvalid = false;
					ApplySpawnPlan (plan);
					return;
				}

				MarkSpawnPlacementInvalid ();
				return;
			}

			SpawnPlacementInvalid = false;
			ApplySpawnPlan (plan);
		}
	}

	private void StripPersistedMonsters ()
	{
		foreach (var child in GetChildren ())
		{
			if (child is not Monster && !child.Name.ToString ().StartsWith ("Monster_", StringComparison.Ordinal))
			{
				continue;
			}

			if (Engine.IsEditorHint ())
			{
				child.Free ();
			}
			else
			{
				child.QueueFree ();
			}
		}

		RegularMonsters.Clear ();
		NamedMonsters.Clear ();
		_regularMonsterIds.Clear ();
		_namedMonsterIds.Clear ();
		_namedMonsterRanks.Clear ();
	}

	private void BeginBulkSpawnPlan (bool enableSpawningOnApply)
	{
		var occupied = CaptureOccupiedWorldPositions ();
		if (TryPlanPopulation (occupied, out var plan))
		{
			SpawnPlacementInvalid = false;
			ApplySpawnPlan (plan);
			// Proximity treats the spawner as done once activated, so a failed first spawn stays
			// inactive
			if (enableSpawningOnApply && !SpawnPlacementInvalid)
			{
				_spawningEnabled = true;
			}

			return;
		}

		MarkSpawnPlacementInvalid ();
	}

	private void BeginDeathRespawnPlan (int monsterId, NamedBossRank rank)
	{
		if (TryApplyDeathRespawnFromBakedSlot (monsterId, rank))
		{
			return;
		}

		MarkSpawnPlacementInvalid ();
	}

	private bool TryBuildPlanFromBakedSlots (
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

		var candidates = new List<Vector3> (BakedSpawnSlots.Count);
		foreach (var slot in BakedSpawnSlots)
		{
			candidates.Add (slot);
		}

		ShuffleSlots (candidates);

		var regularPositions = new List<Vector3> (targetRegular);
		var namedPositions = new List<Vector3> (targetNamed);
		var picked = new List<Vector3> (occupied.Length + totalNeeded);
		foreach (var position in occupied)
		{
			picked.Add (position);
		}

		foreach (var candidate in candidates)
		{
			if (!IsBakedSlotStillValid (candidate, picked))
			{
				continue;
			}

			if (regularPositions.Count < targetRegular)
			{
				regularPositions.Add (candidate);
				picked.Add (candidate);
				continue;
			}

			if (namedPositions.Count < targetNamed)
			{
				namedPositions.Add (candidate);
				picked.Add (candidate);
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

		plan = new MonsterSpawnPlan (regularPositions, namedPositions);
		return true;
	}

	private bool TryApplyDeathRespawnFromBakedSlot (int monsterId, NamedBossRank rank)
	{
		if (BakedSpawnSlots.Count == 0)
		{
			return false;
		}

		lock (_spawnPlacementLock)
		{
			var occupied = CaptureOccupiedWorldPositions ();
			var candidates = new List<Vector3> (BakedSpawnSlots.Count);
			foreach (var slot in BakedSpawnSlots)
			{
				candidates.Add (slot);
			}

			ShuffleSlots (candidates);
			foreach (var candidate in candidates)
			{
				if (!IsBakedSlotStillValid (candidate, occupied))
				{
					continue;
				}

				if (rank != NamedBossRank.None)
				{
					if (TrySpawnNamedMonsterAt (monsterId, candidate, ResolveSlotIndex (candidate), rank))
					{
						return true;
					}
				}
				else if (SpawnRegularMonsterAt (monsterId, candidate, ResolveSlotIndex (candidate)))
				{
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Walkability is not re-checked (slots were validated on the navmesh). A moved spawner or an
	/// occupied nearby slot still rejects the slot
	/// </summary>
	private bool IsBakedSlotStillValid (Vector3 candidate, IReadOnlyList<Vector3> occupied)
	{
		if (!NavPathQuery.IsInsideLeash (candidate, GlobalPosition, SpawnRadiusMeters))
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

	private static void ShuffleSlots (List<Vector3> candidates)
	{
		for (var i = candidates.Count - 1; i > 0; i--)
		{
			var j = Random.Shared.Next (i + 1);
			(candidates[i], candidates[j]) = (candidates[j], candidates[i]);
		}
	}

	private void EnqueueDeathRespawn (int monsterId, NamedBossRank rank)
	{
		lock (_spawnPlacementLock)
		{
			if (!_spawningEnabled || SpawnPlacementInvalid)
			{
				return;
			}

			_pendingDeathRespawns.Enqueue (new PendingDeathRespawn
			{
				MonsterId = monsterId,
				Rank = rank,
			});
		}

		TryStartNextDeathRespawnPlan ();
	}

	private void TryStartNextDeathRespawnPlan ()
	{
		lock (_spawnPlacementLock)
		{
			TryStartNextDeathRespawnPlanCore ();
		}
	}

	private void TryStartNextDeathRespawnPlanCore ()
	{
		if (!_spawningEnabled || SpawnPlacementInvalid || HasSpawnError)
		{
			return;
		}

		if (_pendingDeathRespawns.Count == 0)
		{
			return;
		}

		var next = _pendingDeathRespawns.Dequeue ();
		BeginDeathRespawnPlan (next.MonsterId, next.Rank);
	}

	private void ApplySpawnPlan (MonsterSpawnPlan plan)
	{
		SpawnPlacementInvalid = false;
		ResetBindSlotCounter ();

		foreach (var position in plan.RegularPositions)
		{
			if (!SpawnRegularMonsterAt (TakeMonsterId (), position, ResolveSlotIndex (position)))
			{
				MarkSpawnPlacementInvalid ();
				return;
			}
		}

		foreach (var position in plan.NamedPositions)
		{
			if (!TrySpawnNamedMonsterAt (TakeMonsterId (), position, ResolveSlotIndex (position), NamedBossRank.RandomSpawn))
			{
				MarkSpawnPlacementInvalid ();
				return;
			}
		}
	}

	/// <summary>
	/// Camps with no named target and no cat override roll one extra named mob. If slots cannot fit
	/// it, regulars still spawn
	/// </summary>
	private bool TryPlanPopulation (Vector3[] occupied, out MonsterSpawnPlan plan)
	{
		var named = EffectiveNamedTarget ();
		if (TryBuildPlanFromBakedSlots (TargetRegularMonsterCount, named, occupied, out plan))
		{
			return true;
		}

		if (_bonusNamedSpawn && named > TargetNamedMonsterCount)
		{
			return TryBuildPlanFromBakedSlots (TargetRegularMonsterCount, TargetNamedMonsterCount, occupied, out plan);
		}

		return false;
	}

	private int EffectiveNamedTarget ()
	{
		if (TargetNamedMonsterCount > 0 || IsFixedToCats ())
		{
			return TargetNamedMonsterCount;
		}

		if (!_bonusNamedResolved)
		{
			_bonusNamedResolved = true;
			_bonusNamedSpawn = Random.Shared.Next (100) < BonusNamedChancePercent;
		}

		return _bonusNamedSpawn ? 1 : TargetNamedMonsterCount;
	}

	private bool IsFixedToCats ()
	{
		if (MobTypeOverrides.Count == 0)
		{
			return false;
		}

		foreach (var type in MobTypeOverrides)
		{
			if (type != MonsterType.Кошка)
			{
				return false;
			}
		}

		return true;
	}

	private void MarkSpawnPlacementInvalid ()
	{
		SpawnPlacementInvalid = true;
		NotifyPropertyListChanged ();
		GD.PushWarning (
			$"MonsterSpawner '{Name}': insufficient baked spawn slots (have {BakedSpawnSlots.Count}, "
			+ $"need {TargetRegularMonsterCount + EffectiveNamedTarget ()}). Run Bake spawn slots.");
	}

	private Vector3[] CaptureOccupiedWorldPositions ()
	{
		var occupied = new List<Vector3> ();
		foreach (var position in CollectOccupiedWorldPositions ())
		{
			occupied.Add (position);
		}

		return occupied.ToArray ();
	}

	private IEnumerable<Vector3> CollectOccupiedWorldPositions ()
	{
		foreach (var node in RegularMonsters)
		{
			if (IsInstanceValid (node))
			{
				yield return node.GlobalPosition;
			}
		}

		foreach (var node in NamedMonsters)
		{
			if (IsInstanceValid (node))
			{
				yield return node.GlobalPosition;
			}
		}
	}

	private void ClearPendingRespawnTimersForThisSpawner ()
	{
		lock (TimerLock)
		{
			GlobalRespawnTimers.RemoveAll (timer => timer.Spawner == this);
		}

		_pendingDeathRespawns.Clear ();
	}

	private void DeleteAllSpawnedMonsters ()
	{
		foreach (var node in RegularMonsters)
		{
			if (IsInstanceValid (node))
			{
				if (Engine.IsEditorHint ())
				{
					node.Free ();
				}
				else
				{
					node.QueueFree ();
				}
			}
		}

		foreach (var node in NamedMonsters)
		{
			if (IsInstanceValid (node))
			{
				if (Engine.IsEditorHint ())
				{
					node.Free ();
				}
				else
				{
					node.QueueFree ();
				}
			}
		}

		RegularMonsters.Clear ();
		NamedMonsters.Clear ();
		_regularMonsterIds.Clear ();
		_namedMonsterIds.Clear ();
		_namedMonsterRanks.Clear ();
	}

	private static int CountAlive (Array<Node3D> spawnedMonsters)
	{
		var alive = 0;
		foreach (var node in spawnedMonsters)
		{
			if (IsInstanceValid (node))
			{
				alive++;
			}
		}

		return alive;
	}

	private void ScheduleRespawnsForDeadMonsters (
		Array<Node3D> spawnedMonsters,
		List<int> monsterIds,
		List<NamedBossRank>? ranks,
		int respawnDelaySeconds)
	{
		for (var i = spawnedMonsters.Count - 1; i >= 0; i--)
		{
			if (IsInstanceValid (spawnedMonsters[i]))
			{
				continue;
			}

			var monsterId = i < monsterIds.Count ? monsterIds[i] : TakeMonsterId ();
			var rank = NamedBossRank.None;
			if (ranks is not null)
			{
				rank = i < ranks.Count ? ranks[i] : NamedBossRank.RandomSpawn;
				if (i < ranks.Count)
				{
					ranks.RemoveAt (i);
				}
			}

			if (i < monsterIds.Count)
			{
				monsterIds.RemoveAt (i);
			}

			spawnedMonsters.RemoveAt (i);
			lock (TimerLock)
			{
				GlobalRespawnTimers.Add (new RespawnTimer
				{
					RemainingSeconds = respawnDelaySeconds,
					Rank = rank,
					MonsterId = monsterId,
					Spawner = this,
				});
			}
		}
	}

	private static void ProcessGlobalRespawnTimers (double delta)
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
					expired.Add (timer);
					GlobalRespawnTimers.RemoveAt (i);
				}
				else
				{
					GlobalRespawnTimers[i] = timer;
				}
			}
		}

		foreach (var timer in expired)
		{
			if (!IsInstanceValid (timer.Spawner))
			{
				continue;
			}

			timer.Spawner.EnqueueDeathRespawn (timer.MonsterId, timer.Rank);
		}
	}

	public NavPathResult RequestOutdoorPath (Monster monster, Vector3 goalWorld)
	{
		var resolvedGoal = NavPathQuery.ClampGoalToLeash (goalWorld, LeashCenterWorld, LeashRadiusMeters);
		return NavPathQuery.FindPath (
			this,
			new NavPathRequest (monster.GlobalPosition, resolvedGoal, LeashCenterWorld, LeashRadiusMeters));
	}

	public bool TryNavigateMonster (Monster monster, Vector3 goalWorld, out NavPathFailReason reason)
	{
		var result = RequestOutdoorPath (monster, goalWorld);
		reason = result.Reason;
		if (!result.Success)
		{
			return false;
		}

		return monster.TrySetNavPath (result.Waypoints);
	}

	private int ResolveSlotIndex (Vector3 spawnWorldPosition)
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

	private void ResetBindSlotCounter ()
	{
		_nextBindSlotIndex = 0;
	}

	private static int RollInclusiveLevel (int minLevel, int maxLevel)
	{
		if (minLevel > maxLevel)
		{
			(minLevel, maxLevel) = (maxLevel, minLevel);
		}

		return Random.Shared.Next (minLevel, maxLevel + 1);
	}

	private void EnsureRegularMonsterTypeCache ()
	{
		if (_regularMonsterTypesByLevel is not null
			&& _cachedRegularMinLevel == RegularMonsterMinLevel
			&& _cachedRegularMaxLevel == RegularMonsterMaxLevel)
		{
			return;
		}

		_cachedRegularMinLevel = RegularMonsterMinLevel;
		_cachedRegularMaxLevel = RegularMonsterMaxLevel;
		_regularMonsterTypesByLevel = MonsterSpawnerMonsterTypeLookup.BuildLevelSubset (
			RegularMonsterMinLevel,
			RegularMonsterMaxLevel);
	}

	private MonsterType PickRegularMonsterType (int level)
	{
		if (MobTypeOverrides.Count > 0
			&& MonsterSpawnerMonsterTypeLookup.TryPickWeightedRandomMonsterType (
				[.. MobTypeOverrides], out var overrideType))
		{
			return overrideType;
		}

		EnsureRegularMonsterTypeCache ();
		if (_regularMonsterTypesByLevel is not null
			&& MonsterSpawnerMonsterTypeLookup.TryPickRandomMonsterType (_regularMonsterTypesByLevel, level, out var monsterType))
		{
			return monsterType;
		}

		GD.PushWarning (
			$"MonsterSpawner '{Name}': no monster type for regular level {level} (range {RegularMonsterMinLevel}-{RegularMonsterMaxLevel}); using {MonsterType.Палочник}.");
		return MonsterType.Палочник;
	}

	private bool SpawnRegularMonsterAt (int monsterId, Vector3 spawnWorldPosition, int slotIndex)
	{
		var scene = GD.Load<PackedScene> (MonsterScenePath);
		if (scene is null)
		{
			GD.PushError ($"MonsterSpawner: failed to load scene '{MonsterScenePath}'.");
			return false;
		}

		if (scene.Instantiate () is not Monster monster)
		{
			GD.PushError ($"MonsterSpawner: '{MonsterScenePath}' is not a Monster scene.");
			return false;
		}

		var level = RollInclusiveLevel (RegularMonsterMinLevel, RegularMonsterMaxLevel);
		var monsterType = PickRegularMonsterType (level);

		monster.MonsterType = monsterType;
		monster.Level = level;
		monster.NamedBossRank = NamedBossRank.None;
		monster.Name = BuildMonsterNodeName (monsterType, level, false, monsterId);

		AddChild (monster);
		var resolvedPosition = ResolveBakedSlotPosition (spawnWorldPosition);
		monster.BindHome (
			new MonsterHomeBinding (
				slotIndex,
				resolvedPosition,
				GlobalPosition,
				LeashRadiusMeters,
				GetPath (),
				GetInstanceId (),
				atlasVerticalDelta: 0f),
			this);
		ApplySpawnTransform (monster, resolvedPosition);
		SetMonsterOwnerForPersistence (monster);
		RegularMonsters.Add (monster);
		_regularMonsterIds.Add (monsterId);
		return true;
	}

	private static void ApplySpawnTransform (Monster monster, Vector3 spawnWorldPosition, bool randomizeAngle = true)
	{
		spawnWorldPosition.Y += monster.GetSpawnOriginYOffset (spawnWorldPosition.Y);
		monster.GlobalPosition = spawnWorldPosition;
		if (randomizeAngle)
		{
			monster.Angle = WorldObject.CreateRandomSpawnAngle ();
		}

		// _Ready registered visibility at the pre-placement pose; refresh after the final slot Y
		WorldObjectVisibilityManager.Unregister (monster);
		WorldObjectVisibilityManager.Register (monster);
		monster.RegisterMultiMeshVisualDeferred ();
	}

	private Vector3 ResolveBakedSlotPosition (Vector3 bakedSlot)
	{
		// BakedSpawnSlots already store navmesh-snapped Y. Roof buildings often have no roof
		// collider, so a physics ray hits bare ground and pulls the slot down
		return bakedSlot;
	}

	private bool TrySpawnNamedMonsterAt (int monsterId, Vector3 spawnWorldPosition, int slotIndex, NamedBossRank rank)
	{
		var scene = GD.Load<PackedScene> (MonsterScenePath);
		if (scene is null)
		{
			GD.PushError ($"MonsterSpawner: failed to load scene '{MonsterScenePath}'.");
			return false;
		}

		if (scene.Instantiate () is not Monster monster)
		{
			GD.PushError ($"MonsterSpawner: '{MonsterScenePath}' is not a Monster scene.");
			return false;
		}

		var level = RollInclusiveLevel (NamedMonsterMinLevel, NamedMonsterMaxLevel);
		var monsterType = PickRegularMonsterType (level);

		monster.MonsterType = monsterType;
		monster.Level = level;
		monster.NamedBossRank = rank == NamedBossRank.None ? NamedBossRank.RandomSpawn : rank;
		monster.Name = BuildMonsterNodeName (monsterType, level, true, monsterId);

		AddChild (monster);
		var resolvedPosition = ResolveBakedSlotPosition (spawnWorldPosition);
		monster.BindHome (
			new MonsterHomeBinding (
				slotIndex,
				resolvedPosition,
				GlobalPosition,
				LeashRadiusMeters,
				GetPath (),
				GetInstanceId (),
				atlasVerticalDelta: 0f),
			this);
		ApplySpawnTransform (monster, resolvedPosition);
		SetMonsterOwnerForPersistence (monster);
		NamedMonsters.Add (monster);
		_namedMonsterIds.Add (monsterId);
		_namedMonsterRanks.Add (monster.NamedBossRank);
		return true;
	}

	private static int TakeMonsterId ()
	{
		lock (MonsterIdLock)
		{
			return _globalNextMonsterId++;
		}
	}

	private void RebuildMonsterIdLists ()
	{
		_regularMonsterIds.Clear ();
		foreach (var node in RegularMonsters)
		{
			if (!IsInstanceValid (node))
			{
				continue;
			}

			_regularMonsterIds.Add (TryParseMonsterIdFromName (node.Name, out var id) ? id : TakeMonsterId ());
		}

		_namedMonsterIds.Clear ();
		_namedMonsterRanks.Clear ();
		foreach (var node in NamedMonsters)
		{
			if (!IsInstanceValid (node))
			{
				continue;
			}

			_namedMonsterIds.Add (TryParseMonsterIdFromName (node.Name, out var id) ? id : TakeMonsterId ());
			_namedMonsterRanks.Add (node is Monster named ? named.NamedBossRank : NamedBossRank.RandomSpawn);
		}

		EnsureMonsterIdCounterAboveTakenIds ();
	}

	private void EnsureMonsterIdCounterAboveTakenIds ()
	{
		var minNextId = FirstMonsterId;
		foreach (var id in _regularMonsterIds)
		{
			minNextId = Mathf.Max (minNextId, id + 1);
		}

		foreach (var id in _namedMonsterIds)
		{
			minNextId = Mathf.Max (minNextId, id + 1);
		}

		lock (MonsterIdLock)
		{
			_globalNextMonsterId = Mathf.Max (_globalNextMonsterId, minNextId);
		}
	}

	private static bool TryParseMonsterIdFromName (StringName name, out int id)
	{
		id = 0;
		var parts = name.ToString ().Split ('_');
		if (parts.Length < 4)
		{
			return false;
		}

		return int.TryParse (parts[^1], out id);
	}

	private static string BuildMonsterNodeName (MonsterType type, int level, bool named, int id)
	{
		var prefix = named ? "MonsterNamed" : "Monster";
		return $"{prefix}_{type}_{level}_{id}";
	}

	private void SetMonsterOwnerForPersistence (Monster monster)
	{
		WorldObjectDumpFillCommon.SetOwnerIfEditor (this, monster);

		if (!Engine.IsEditorHint () || monster.Owner != this)
		{
			return;
		}

		var sceneRoot = GetTree ()?.EditedSceneRoot;
		if (sceneRoot is not null && sceneRoot != this)
		{
			monster.Owner = sceneRoot;
		}
	}
}
