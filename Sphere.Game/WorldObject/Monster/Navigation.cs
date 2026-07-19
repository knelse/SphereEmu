using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Godot.Scripts.Navigation;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
    private const float HomeArrivalDistanceMeters = 0.35f;
    private const float AttackStopDistanceMeters = 0.25f;
    private const float PathRepathIntervalSeconds = 0.45f;
    private const float ChaseGoalMoveThresholdMeters = 1.5f;
    private const float MaxNavVerticalStepMeters = 2f;
    private const double PositionBroadcastDelta = 0.1;

    private MonsterHomeBinding? homeBinding;
    private MonsterSpawner? homeSpawner;
    private MonsterNavMode navMode = MonsterNavMode.Idle;
    private Vector3 navGoalWorld;
    private float navRepathCooldown;
    private readonly List<Vector3> navWaypoints = [];
    private int navWaypointIndex;
    private Vector3 lastBroadcastPosition;
    private double lastBroadcastAngleRadians;
    private bool hasBroadcastPosition;

    public bool HasActiveNavPath => navWaypointIndex < navWaypoints.Count;

    internal float GetAtlasVerticalDelta() => homeBinding?.AtlasVerticalDelta ?? 0f;

    public void BindHome(MonsterHomeBinding binding, MonsterSpawner ownerSpawner)
    {
        homeBinding = binding;
        homeSpawner = ownerSpawner;
        navMode = MonsterNavMode.Idle;
        navWaypoints.Clear();
        navWaypointIndex = 0;
        navRepathCooldown = 0f;
    }

    public void ClearHomeBinding()
    {
        homeBinding = null;
        homeSpawner = null;
        navMode = MonsterNavMode.Idle;
        navWaypoints.Clear();
        navWaypointIndex = 0;
        navRepathCooldown = 0f;
    }

    public bool TrySetNavPath(IReadOnlyList<Vector3> waypoints)
    {
        navWaypoints.Clear();
        navWaypointIndex = 0;
        if (waypoints.Count == 0)
        {
            return false;
        }

        foreach (var waypoint in waypoints)
        {
            navWaypoints.Add(waypoint);
        }

        return true;
    }

    public void ClearNavPath()
    {
        navWaypoints.Clear();
        navWaypointIndex = 0;
    }

    public void StopNavigation()
    {
        navMode = MonsterNavMode.Idle;
        ClearNavPath();
    }

    public bool TryNavigateTo(Vector3 goalWorld)
    {
        if (!TryGetHomeSpawner(out var spawner))
        {
            return false;
        }

        if (!spawner.TryNavigateMonster(this, goalWorld, out _))
        {
            return false;
        }

        navGoalWorld = goalWorld;
        navMode = MonsterNavMode.Chasing;
        return true;
    }

    public bool NavigateHome()
    {
        if (!homeBinding.HasValue || !TryGetHomeSpawner(out var spawner))
        {
            StopNavigation();
            return false;
        }

        var home = homeBinding.Value.HomeSlotWorld;
        if (!spawner.TryNavigateMonster(this, home, out _))
        {
            StopNavigation();
            return false;
        }

        navGoalWorld = home;
        navMode = MonsterNavMode.Returning;
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Engine.IsEditorHint())
        {
            var deltaSeconds = (float)delta;
            EnforceOutdoorLeash();
            UpdateOutdoorChaseAi(deltaSeconds);
            AdvanceNavPath(deltaSeconds);
            SyncPositionToVisibleClients();
        }

        base._PhysicsProcess(delta);
    }

    private void SyncPositionToVisibleClients()
    {
        var position = GlobalPosition;
        var angleRadians = DecodeAngleToYawRadians(Angle);
        var gameX = position.X;
        var gameY = -position.Y;
        var gameZ = -position.Z;

        if (hasBroadcastPosition
            && position.DistanceSquaredTo(lastBroadcastPosition) <= PositionBroadcastDelta * PositionBroadcastDelta
            && Math.Abs(angleRadians - lastBroadcastAngleRadians) <= PositionBroadcastDelta)
        {
            return;
        }

        hasBroadcastPosition = true;
        lastBroadcastPosition = position;
        lastBroadcastAngleRadians = angleRadians;
        BroadcastEntityPositionToVisibleClients(gameX, gameY, gameZ, angleRadians);
    }

    private void ForcePositionSyncToVisibleClients()
    {
        hasBroadcastPosition = false;
        SyncPositionToVisibleClients();
    }

    private void UpdateOutdoorChaseAi(float deltaSeconds)
    {
        if (!TryGetHomeSpawner(out var spawner) || !spawner.OutdoorChaseEnabled)
        {
            return;
        }

        navRepathCooldown = Mathf.Max(0f, navRepathCooldown - deltaSeconds);

        if (TryFindNearestChaseTarget(this, spawner, out var chaseTarget))
        {
            BeginOrRefreshChase(spawner, chaseTarget);
            return;
        }

        if (navMode == MonsterNavMode.Chasing)
        {
            BeginReturnHome();
            return;
        }

        if (navMode == MonsterNavMode.Returning && !HasActiveNavPath && IsNearHome())
        {
            navMode = MonsterNavMode.Idle;
        }
        else if (navMode == MonsterNavMode.Idle && ShouldReturnHome())
        {
            BeginReturnHome();
        }
    }

    private void BeginOrRefreshChase(MonsterSpawner spawner, Vector3 chaseTarget)
    {
        navMode = MonsterNavMode.Chasing;
        if (navRepathCooldown > 0f && HasActiveNavPath)
        {
            var goalDelta = navGoalWorld - chaseTarget;
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
            navGoalWorld = chaseTarget;
            navRepathCooldown = PathRepathIntervalSeconds;
        }
    }

    private void BeginReturnHome()
    {
        if (!homeBinding.HasValue || IsNearHome())
        {
            StopNavigation();
            return;
        }

        NavigateHome();
        navRepathCooldown = PathRepathIntervalSeconds;
    }

    private bool ShouldReturnHome()
    {
        return homeBinding.HasValue && !IsNearHome();
    }

    private bool IsNearHome()
    {
        if (!homeBinding.HasValue)
        {
            return true;
        }

        var home = homeBinding.Value.HomeSlotWorld;
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
        if (!monster.TryGetLeashDisk(out var leashCenter, out var leashRadius))
        {
            return false;
        }

        var aggroRadius = Mathf.Max(1f, spawner.AggroRadiusMeters);
        var aggroRadiusSq = aggroRadius * aggroRadius;
        var monsterPosition = monster.GlobalTransform.Origin;
        var leashRadiusSq = leashRadius * leashRadius;
        var bestDistanceSq = float.MaxValue;
        Vector3? bestClientWorld = null;

        foreach (var client in ActiveClients.GetAll().Values)
        {
            if (!IsValidChaseClient(client, out var clientWorld))
            {
                continue;
            }

            var toClientFromLeash = clientWorld - leashCenter;
            toClientFromLeash.Y = 0f;
            if (toClientFromLeash.LengthSquared() > leashRadiusSq)
            {
                continue;
            }

            var toMonster = clientWorld - monsterPosition;
            toMonster.Y = 0f;
            var distanceSq = toMonster.LengthSquared();
            if (distanceSq > aggroRadiusSq || distanceSq >= bestDistanceSq)
            {
                continue;
            }

            bestDistanceSq = distanceSq;
            bestClientWorld = clientWorld;
        }

        if (bestClientWorld is null)
        {
            return false;
        }

        return monster.TryResolveOutdoorChaseGoalWorld(bestClientWorld.Value, out targetWorld);
    }

    private static bool IsValidChaseClient(SphereClient client, out Vector3 clientWorld)
    {
        clientWorld = default;
        if (!GodotObject.IsInstanceValid(client) || !client.ClientStateManager.IsInGameState())
        {
            return false;
        }

        return ClientWorldPosition.TryGetGodotWorldPosition(client, out clientWorld);
    }

    private bool TryResolveOutdoorChaseGoalWorld(Vector3 clientWorld, out Vector3 goalWorld)
    {
        goalWorld = new Vector3(clientWorld.X, clientWorld.Y, clientWorld.Z);
        if (TrySampleGodotGroundY(clientWorld.X, clientWorld.Z, out var groundY))
        {
            goalWorld.Y = groundY;
        }

        return true;
    }

    private bool TrySampleGodotGroundY(float worldX, float worldZ, out float godotGroundY)
    {
        if (TryGetHomeSpawner(out var spawner) && TryGetLeashDisk(out var leashCenter, out var leashRadius))
        {
            TerrainNavMeshRuntime.EnsureTilesLoaded(spawner, leashCenter, leashRadius + 8f);
            TerrainNavMeshRuntime.TrySyncImmediate();
        }

        var probeY = GlobalPosition.Y;
        return NavPathQuery.TrySampleGroundY(worldX, worldZ, probeY, out godotGroundY);
    }

    private void EnforceOutdoorLeash()
    {
        if (!TryGetLeashDisk(out var leashCenter, out var leashRadius))
        {
            return;
        }

        if (IsInsideLeashDisk(GlobalTransform.Origin, leashCenter, leashRadius))
        {
            return;
        }

        StopNavigation();
        TeleportToHomeSlot();
    }

    private void TeleportToHomeSlot()
    {
        var home = homeBinding!.Value.HomeSlotWorld;
        var spawnPosition = home;
        spawnPosition.Y += GetSpawnOriginYOffset(spawnPosition.Y);
        GlobalPosition = spawnPosition;
        navMode = MonsterNavMode.Idle;
        Angle = WorldObject.CreateRandomSpawnAngle();
        RegisterMultiMeshVisualDeferred();
        ForcePositionSyncToVisibleClients();
    }

    private void AdvanceNavPath(float delta)
    {
        if (!HasActiveNavPath || !TryGetLeashDisk(out _, out _))
        {
            return;
        }

        if (navMode == MonsterNavMode.Chasing && IsWithinAttackRange(navGoalWorld))
        {
            ClearNavPath();
            FaceToward(navGoalWorld);
            return;
        }

        var target = navWaypoints[navWaypointIndex];
        var current = GlobalPosition;
        var toTarget = target - current;
        toTarget.Y = 0f;
        var distance = toTarget.Length();
        var speed = Mathf.Max(0.5f, DataSpeed);
        if (distance <= AttackStopDistanceMeters)
        {
            navWaypointIndex++;
            return;
        }

        var step = Mathf.Min(distance, speed * delta);
        var next = current + toTarget.Normalized() * step;
        if (!TryGetLeashDisk(out var leashCenter, out var leashRadius)
            || !IsInsideLeashDisk(next, leashCenter, leashRadius))
        {
            ClearNavPath();
            return;
        }

        next.Y = ResolveNavStandingY(next.X, next.Z);
        GlobalPosition = next;
        FaceToward(next + toTarget);
        RegisterMultiMeshVisualDeferred();
    }

    private float ResolveNavStandingY(float worldX, float worldZ)
    {
        var currentY = GlobalPosition.Y;
        if (!TrySampleGodotGroundY(worldX, worldZ, out var groundY))
        {
            return currentY;
        }

        var targetY = groundY + GetSpawnOriginYOffset(groundY);
        return Mathf.Clamp(targetY, currentY - MaxNavVerticalStepMeters, currentY + MaxNavVerticalStepMeters);
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

    private bool TryGetHomeSpawner(out MonsterSpawner spawner)
    {
        if (homeSpawner is not null && GodotObject.IsInstanceValid(homeSpawner))
        {
            spawner = homeSpawner;
            return true;
        }

        if (TryResolveSpawnerFromBinding(out spawner))
        {
            homeSpawner = spawner;
            return true;
        }

        if (TryResolveSpawnerFromAncestors(out spawner))
        {
            homeSpawner = spawner;
            return true;
        }

        spawner = null!;
        return false;
    }

    private bool TryResolveSpawnerFromBinding(out MonsterSpawner spawner)
    {
        spawner = null!;
        if (!homeBinding.HasValue)
        {
            return false;
        }

        var binding = homeBinding.Value;
        if (binding.OwnerSpawnerInstanceId != 0)
        {
            var instance = GodotObject.InstanceFromId(binding.OwnerSpawnerInstanceId);
            if (instance is MonsterSpawner instanceSpawner && GodotObject.IsInstanceValid(instanceSpawner))
            {
                spawner = instanceSpawner;
                return true;
            }
        }

        if (binding.OwnerSpawnerPath.IsEmpty || GetTree() is not SceneTree tree)
        {
            return false;
        }

        var pathSpawner = tree.Root.GetNodeOrNull(binding.OwnerSpawnerPath);
        if (pathSpawner is MonsterSpawner resolvedSpawner && GodotObject.IsInstanceValid(resolvedSpawner))
        {
            spawner = resolvedSpawner;
            return true;
        }

        return false;
    }

    private bool TryResolveSpawnerFromAncestors(out MonsterSpawner spawner)
    {
        for (var node = GetParent(); node is not null; node = node.GetParent())
        {
            if (node is MonsterSpawner ancestorSpawner)
            {
                spawner = ancestorSpawner;
                return true;
            }
        }

        spawner = null!;
        return false;
    }

    private bool TryGetLeashDisk(out Vector3 leashCenter, out float leashRadius)
    {
        if (homeBinding.HasValue)
        {
            var binding = homeBinding.Value;
            leashCenter = binding.LeashCenterWorld;
            leashRadius = binding.LeashRadiusMeters > 0f
                ? binding.LeashRadiusMeters
                : OutdoorFieldConfig.DefaultLeashRadiusMeters;
            return true;
        }

        if (TryGetHomeSpawner(out var spawner))
        {
            leashCenter = spawner.LeashCenterWorld;
            leashRadius = spawner.LeashRadiusMeters;
            return true;
        }

        leashCenter = default;
        leashRadius = 0f;
        return false;
    }

    private static bool IsInsideLeashDisk(Vector3 worldPosition, Vector3 leashCenterWorld, float leashRadiusMeters)
        => NavPathQuery.IsInsideLeash(worldPosition, leashCenterWorld, leashRadiusMeters);
}
