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

    [Export]
    public int TargetNamedMonsterCount = 0;

    [Export]
    public int TargetRegularMonsterCount = 3;

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

    private struct RespawnTimer
    {
        public double RemainingSeconds;
        public bool IsNamed;
        public int MonsterId;
        public MonsterSpawner Spawner;
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            EnsureEditorPreviewMonsters();
            return;
        }

        RebuildMonsterIdLists();
        SpawnInitialMonsters();
    }

    /// <summary>
    ///     Ensures preview monsters exist under this spawner in the editor (used after Fill rebuild and on _Ready).
    /// </summary>
    public void EnsureEditorPreviewMonsters()
    {
        if (!Engine.IsEditorHint())
        {
            return;
        }

        RebuildMonsterIdLists();
        SpawnInitialMonsters();
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
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

    private void SpawnInitialMonsters()
    {
        var aliveRegular = CountAlive(RegularMonsters);
        for (var i = aliveRegular; i < TargetRegularMonsterCount; i++)
        {
            SpawnRegularMonster(TakeMonsterId());
        }

        var aliveNamed = CountAlive(NamedMonsters);
        for (var i = aliveNamed; i < TargetNamedMonsterCount; i++)
        {
            SpawnNamedMonster(TakeMonsterId());
        }
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
                timer.Spawner.CallDeferred(nameof(SpawnNamedMonster), timer.MonsterId);
            }
            else
            {
                timer.Spawner.CallDeferred(nameof(SpawnRegularMonster), timer.MonsterId);
            }
        });
    }

    private void SpawnRegularMonster(int monsterId)
    {
        var scene = GD.Load<PackedScene>(MonsterScenePath);
        if (scene is null)
        {
            GD.PushError($"MonsterSpawner: failed to load scene '{MonsterScenePath}'.");
            return;
        }

        if (scene.Instantiate() is not Monster monster)
        {
            GD.PushError($"MonsterSpawner: '{MonsterScenePath}' is not a Monster scene.");
            return;
        }

        const MonsterType monsterType = MonsterType.Палочник;
        const int level = 1;
        const bool isNamed = false;

        monster.MonsterType = monsterType;
        monster.Level = level;
        monster.IsNamed = isNamed;
        monster.Name = BuildMonsterNodeName(monsterType, level, isNamed, monsterId);

        AddChild(monster);
        monster.Transform = Transform3D.Identity;
        WorldObjectDumpFillCommon.SetOwnerIfEditor(this, monster);
        RegularMonsters.Add(monster);
        _regularMonsterIds.Add(monsterId);
    }

    private void SpawnNamedMonster(int monsterId)
    {
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
}
