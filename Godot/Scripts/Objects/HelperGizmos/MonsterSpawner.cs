using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using SphServer.Godot.Scripts.Objects;
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
	public bool SpawnPlacementInvalid { get; private set; }

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

	/// <summary>
	///     When set in the scene, this spawner activates and spawns on server load (debug / town presets).
	/// </summary>
	[Export]
	public bool SpawningEnabled { get; set; }

	private bool _spawningEnabled;

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
			if (_spawningEnabled)
			{
				return;
			}

			_spawningEnabled = true;
			SpawnInitialMonstersCore();
		}
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
		if (Engine.IsEditorHint() || !_spawningEnabled)
		{
			return;
		}

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
			if (!Engine.IsEditorHint())
			{
				_spawningEnabled = true;
			}

			ClearPendingRespawnTimersForThisSpawner();
			DeleteAllSpawnedMonsters();
			SpawnPlacementInvalid = false;
			SpawnInitialMonstersCore();
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

	private void SpawnInitialMonstersCore()
	{
		SpawnPlacementInvalid = false;
		_spawnPlacement.Reset(CollectOccupiedWorldPositions());

		var aliveRegular = CountAlive(RegularMonsters);
		for (var i = aliveRegular; i < TargetRegularMonsterCount; i++)
		{
			if (!TrySpawnRegularMonster(TakeMonsterId()))
			{
				MarkSpawnPlacementInvalid();
				return;
			}
		}

		var aliveNamed = CountAlive(NamedMonsters);
		for (var i = aliveNamed; i < TargetNamedMonsterCount; i++)
		{
			if (!TrySpawnNamedMonster(TakeMonsterId()))
			{
				MarkSpawnPlacementInvalid();
				return;
			}
		}
	}

	private void MarkSpawnPlacementInvalid()
	{
		SpawnPlacementInvalid = true;
		NotifyPropertyListChanged();
		GD.PushWarning($"MonsterSpawner '{Name}': no valid spawn positions remain within {SpawnRadiusMeters:0.##}m.");
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
		RespawnTimer[] snapshot;
		lock (TimerLock)
		{
			if (GlobalRespawnTimers.Count == 0)
			{
				return;
			}

			snapshot = GlobalRespawnTimers.ToArray();
			GlobalRespawnTimers.Clear();
		}

		Parallel.For(0, snapshot.Length, i => snapshot[i].RemainingSeconds -= delta);

		var expired = new List<RespawnTimer>();
		lock (TimerLock)
		{
			foreach (var timer in snapshot)
			{
				if (timer.RemainingSeconds <= 0)
				{
					expired.Add(timer);
				}
				else
				{
					GlobalRespawnTimers.Add(timer);
				}
			}
		}

		Parallel.ForEach(expired, timer =>
		{
			if (!IsInstanceValid(timer.Spawner))
			{
				return;
			}

			if (timer.IsNamed)
			{
				timer.Spawner.CallDeferred(nameof(TrySpawnNamedMonsterDeferred), timer.MonsterId);
			}
			else
			{
				timer.Spawner.CallDeferred(nameof(TrySpawnRegularMonsterDeferred), timer.MonsterId);
			}
		});
	}

	private void TrySpawnRegularMonsterDeferred(int monsterId)
	{
		lock (_spawnPlacementLock)
		{
			if (!_spawningEnabled || SpawnPlacementInvalid)
			{
				return;
			}

			_spawnPlacement.ResetOccupied(CollectOccupiedWorldPositions());
			if (!TrySpawnRegularMonster(monsterId))
			{
				MarkSpawnPlacementInvalid();
			}
		}
	}

	private void TrySpawnNamedMonsterDeferred(int monsterId)
	{
		lock (_spawnPlacementLock)
		{
			if (!_spawningEnabled || SpawnPlacementInvalid)
			{
				return;
			}

			_spawnPlacement.ResetOccupied(CollectOccupiedWorldPositions());
			if (!TrySpawnNamedMonster(monsterId))
			{
				MarkSpawnPlacementInvalid();
			}
		}
	}

	private bool TrySpawnRegularMonster(int monsterId)
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

		if (!TryFindSpawnWorldPosition(out var spawnWorldPosition))
		{
			monster.Free();
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
