using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
    private const float HomeArrivalDistanceMeters = 0.35f;
    private const float AttackStopDistanceMeters = 0.25f;
    private const float PathRepathIntervalSeconds = 0.45f;
    private const float ChaseGoalMoveThresholdMeters = 1.5f;

    private MonsterHomeBinding? _homeBinding;
    private MonsterLeashPhase _leashPhase;
    private MonsterNavMode _navMode = MonsterNavMode.Idle;
    private Vector3 _navGoalWorld;
    private float _navRepathCooldown;
    private readonly List<Vector3> _navWaypoints = [];
    private int _navWaypointIndex;

    public bool HasHomeBinding => _homeBinding.HasValue;

    public MonsterNavMode NavMode => _navMode;

    public bool HasActiveNavPath => _navWaypointIndex < _navWaypoints.Count;

    public IReadOnlyList<Vector3> NavWaypoints => _navWaypoints;

    public void BindHome(MonsterHomeBinding binding)
    {
        _homeBinding = binding;
        _leashPhase = MonsterLeashPhase.Inside;
        _navMode = MonsterNavMode.Idle;
        _navWaypoints.Clear();
        _navWaypointIndex = 0;
        _navRepathCooldown = 0f;
    }

    public void ClearHomeBinding()
    {
        _homeBinding = null;
        _navMode = MonsterNavMode.Idle;
        _navWaypoints.Clear();
        _navWaypointIndex = 0;
        _navRepathCooldown = 0f;
    }

    public bool TrySetNavPath(IReadOnlyList<Vector3> waypoints)
    {
        _navWaypoints.Clear();
        _navWaypointIndex = 0;
        if (waypoints.Count == 0)
        {
            return false;
        }

        foreach (var waypoint in waypoints)
        {
            _navWaypoints.Add(waypoint);
        }

        return true;
    }

    public void ClearNavPath()
    {
        _navWaypoints.Clear();
        _navWaypointIndex = 0;
    }

    public void StopNavigation()
    {
        _navMode = MonsterNavMode.Idle;
        ClearNavPath();
    }

    public bool TryNavigateTo(Vector3 goalWorld)
    {
        if (GetParent() is not MonsterSpawner spawner)
        {
            return false;
        }

        if (!spawner.TryNavigateMonster(this, goalWorld, out _))
        {
            return false;
        }

        _navGoalWorld = goalWorld;
        _navMode = MonsterNavMode.Chasing;
        return true;
    }

    public bool NavigateHome()
    {
        if (!_homeBinding.HasValue || GetParent() is not MonsterSpawner spawner)
        {
            StopNavigation();
            return false;
        }

        var home = _homeBinding.Value.HomeSlotWorld;
        if (!spawner.TryNavigateMonster(this, home, out _))
        {
            StopNavigation();
            return false;
        }

        _navGoalWorld = home;
        _navMode = MonsterNavMode.Returning;
        return true;
    }

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
        {
            var deltaSeconds = (float)delta;
            EnforceOutdoorLeash();
            UpdateOutdoorChaseAi(deltaSeconds);
            AdvanceNavPath(deltaSeconds);
        }

        base._Process(delta);
    }

    private void UpdateOutdoorChaseAi(float deltaSeconds)
    {
        if (_leashPhase == MonsterLeashPhase.AtBoundary || GetParent() is not MonsterSpawner spawner || !spawner.OutdoorChaseEnabled)
        {
            return;
        }

        _navRepathCooldown = Mathf.Max(0f, _navRepathCooldown - deltaSeconds);

        if (TryFindNearestChaseTarget(this, spawner, out var chaseTarget))
        {
            BeginOrRefreshChase(spawner, chaseTarget);
            return;
        }

        if (_navMode == MonsterNavMode.Chasing)
        {
            BeginReturnHome(spawner);
            return;
        }

        if (_navMode == MonsterNavMode.Returning && !HasActiveNavPath && IsNearHome())
        {
            _navMode = MonsterNavMode.Idle;
        }
        else if (_navMode == MonsterNavMode.Idle && ShouldReturnHome())
        {
            BeginReturnHome(spawner);
        }
    }

    private void BeginOrRefreshChase(MonsterSpawner spawner, Vector3 chaseTarget)
    {
        _navMode = MonsterNavMode.Chasing;
        if (_navRepathCooldown > 0f && HasActiveNavPath)
        {
            var goalDelta = _navGoalWorld - chaseTarget;
            goalDelta.Y = 0f;
            if (goalDelta.LengthSquared() < ChaseGoalMoveThresholdMeters * ChaseGoalMoveThresholdMeters)
            {
                return;
            }
        }

        if (IsWithinAttackRange(chaseTarget))
        {
            ClearNavPath();
            FaceToward(chaseTarget);
            return;
        }

        if (spawner.TryNavigateMonster(this, chaseTarget, out _))
        {
            _navGoalWorld = chaseTarget;
            _navRepathCooldown = PathRepathIntervalSeconds;
        }
    }

    private void BeginReturnHome(MonsterSpawner spawner)
    {
        if (!_homeBinding.HasValue || IsNearHome())
        {
            StopNavigation();
            return;
        }

        NavigateHome();
        _navRepathCooldown = PathRepathIntervalSeconds;
    }

    private bool ShouldReturnHome()
    {
        return _homeBinding.HasValue && !IsNearHome();
    }

    private bool IsNearHome()
    {
        if (!_homeBinding.HasValue)
        {
            return true;
        }

        var home = _homeBinding.Value.HomeSlotWorld;
        var delta = GlobalPosition - home;
        delta.Y = 0f;
        return delta.LengthSquared() <= HomeArrivalDistanceMeters * HomeArrivalDistanceMeters;
    }

    private bool IsWithinAttackRange(Vector3 targetWorld)
    {
        var attackRange = Mathf.Max(1f, DataRange);
        var delta = GlobalPosition - targetWorld;
        delta.Y = 0f;
        return delta.LengthSquared() <= attackRange * attackRange;
    }

    private static bool TryFindNearestChaseTarget(Monster monster, MonsterSpawner spawner, out Vector3 targetWorld)
    {
        targetWorld = default;
        var aggroRadius = Mathf.Max(1f, spawner.AggroRadiusMeters);
        var aggroRadiusSq = aggroRadius * aggroRadius;
        var monsterPosition = monster.GlobalPosition;
        var origin = spawner.GlobalPosition;
        var leashRadiusSq = spawner.LeashRadiusMeters * spawner.LeashRadiusMeters;
        var bestDistanceSq = float.MaxValue;
        var found = false;

        foreach (var client in ActiveClients.GetAll().Values)
        {
            if (!IsValidChaseClient(client, out var clientWorld))
            {
                continue;
            }

            var toClientFromLeash = clientWorld - origin;
            toClientFromLeash.Y = 0f;
            if (toClientFromLeash.LengthSquared() > leashRadiusSq)
            {
                continue;
            }

            var distanceSq = clientWorld.DistanceSquaredTo(monsterPosition);
            if (distanceSq > aggroRadiusSq || distanceSq >= bestDistanceSq)
            {
                continue;
            }

            bestDistanceSq = distanceSq;
            targetWorld = clientWorld;
            found = true;
        }

        return found;
    }

    private static bool IsValidChaseClient(SphereClient client, out Vector3 clientWorld)
    {
        clientWorld = default;
        if (!GodotObject.IsInstanceValid(client) || client.CurrentCharacter is null)
        {
            return false;
        }

        if (!client.ClientStateManager.IsInGameState())
        {
            return false;
        }

        var character = client.CurrentCharacter;
        clientWorld = new Vector3((float)character.X, (float)-character.Y, (float)-character.Z);
        return true;
    }

    private void EnforceOutdoorLeash()
    {
        if (!_homeBinding.HasValue || GetParent() is not MonsterSpawner spawner)
        {
            return;
        }

        var leashCenter = spawner.LeashCenterWorld;
        var leashRadius = spawner.LeashRadiusMeters;
        var position = GlobalPosition;
        if (OutdoorPathQuery.IsInsideLeash(position, leashCenter, leashRadius))
        {
            _leashPhase = MonsterLeashPhase.Inside;
            return;
        }

        _leashPhase = MonsterLeashPhase.AtBoundary;
        StopNavigation();
        TeleportToHomeSlot();
    }

    private void TeleportToHomeSlot()
    {
        var home = _homeBinding!.Value.HomeSlotWorld;
        var spawnPosition = home;
        spawnPosition.Y += GetSpawnOriginYOffset();
        GlobalPosition = spawnPosition;
        Angle = WorldObject.CreateRandomSpawnAngle();
        RegisterMultiMeshVisualDeferred();
        _leashPhase = MonsterLeashPhase.Inside;
        _navMode = MonsterNavMode.Idle;
    }

    private void AdvanceNavPath(float delta)
    {
        if (!HasActiveNavPath || GetParent() is not MonsterSpawner spawner)
        {
            return;
        }

        if (_navMode == MonsterNavMode.Chasing && IsWithinAttackRange(_navGoalWorld))
        {
            ClearNavPath();
            FaceToward(_navGoalWorld);
            return;
        }

        var target = _navWaypoints[_navWaypointIndex];
        target.Y += GetSpawnOriginYOffset();
        var current = GlobalPosition;
        var toTarget = target - current;
        toTarget.Y = 0f;
        var distance = toTarget.Length();
        var speed = Mathf.Max(0.5f, DataSpeed);
        if (distance <= AttackStopDistanceMeters)
        {
            _navWaypointIndex++;
            return;
        }

        var step = Mathf.Min(distance, speed * delta);
        var next = current + toTarget.Normalized() * step;
        if (!OutdoorPathQuery.IsInsideLeash(next, spawner.LeashCenterWorld, spawner.LeashRadiusMeters))
        {
            ClearNavPath();
            return;
        }

        next.Y = target.Y;
        GlobalPosition = next;
        FaceToward(next + toTarget);
        RegisterMultiMeshVisualDeferred();
    }

    private void FaceToward(Vector3 worldTarget)
    {
        var flatDelta = worldTarget - GlobalPosition;
        flatDelta.Y = 0f;
        if (flatDelta.LengthSquared() < 0.0001f)
        {
            return;
        }

        var yaw = Mathf.Atan2(-flatDelta.X, -flatDelta.Z);
        Angle = EncodeYawRadiansToAngle(yaw);
    }
}
