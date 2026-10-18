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
    private const float DefaultAggroRadiusMeters = 12f;

    /// <summary>
    /// Well inside the 100m spawner leash, so a kited mob walks back instead of following to the
    /// leash teleport
    /// </summary>
    private const float ChaseGiveUpDistanceMeters = 30f;

    /// <summary>
    /// Origins differ by body height; a larger gap is another floor or a cliff
    /// </summary>
    private const float MaxTargetVerticalDeltaMeters = 4f;

    /// <summary>
    /// Idle drift past this sends the mob home; a nav path end can sit off the exact slot
    /// </summary>
    private const float HomeDriftMeters = 2f;

    /// <summary>
    /// Each sync repeats 4 packets per viewer, so a running mob is capped well below the tick rate
    /// </summary>
    private const float PositionBroadcastIntervalSeconds = 0.25f;

    /// <summary>
    /// Chase stops this far inside Range so a small player step does not break the swing
    /// </summary>
    private const float ChaseStopRangeFactor = 0.8f;

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
    private float positionBroadcastCooldown;

    /// <summary>
    /// /mob and other unbound monsters chase and return around where they first ticked
    /// </summary>
    private Vector3? unboundHomeWorld;

    public bool HasActiveNavPath => navWaypointIndex < navWaypoints.Count;

    internal float GetAtlasVerticalDelta () => homeBinding?.AtlasVerticalDelta ?? 0f;

    public void BindHome (MonsterHomeBinding binding, MonsterSpawner ownerSpawner)
    {
        homeBinding = binding;
        homeSpawner = ownerSpawner;
        unboundHomeWorld = null;
        navMode = MonsterNavMode.Idle;
        navWaypoints.Clear ();
        navWaypointIndex = 0;
        navRepathCooldown = 0f;
    }

    public void ClearHomeBinding ()
    {
        homeBinding = null;
        homeSpawner = null;
        unboundHomeWorld = null;
        navMode = MonsterNavMode.Idle;
        navWaypoints.Clear ();
        navWaypointIndex = 0;
        navRepathCooldown = 0f;
    }

    public bool TrySetNavPath (IReadOnlyList<Vector3> waypoints)
    {
        navWaypoints.Clear ();
        navWaypointIndex = 0;
        if (waypoints.Count == 0)
        {
            return false;
        }

        foreach (var waypoint in waypoints)
        {
            navWaypoints.Add (waypoint);
        }

        return true;
    }

    public void ClearNavPath ()
    {
        navWaypoints.Clear ();
        navWaypointIndex = 0;
    }

    public void StopNavigation ()
    {
        navMode = MonsterNavMode.Idle;
        ClearNavPath ();
    }

    public bool TryNavigateTo (Vector3 goalWorld)
    {
        if (!TryPathTo (goalWorld))
        {
            return false;
        }

        navGoalWorld = goalWorld;
        navMode = MonsterNavMode.Chasing;
        return true;
    }

    public bool NavigateHome ()
    {
        if (!TryGetHomeWorld (out var home))
        {
            StopNavigation ();
            return false;
        }

        if (!TryPathTo (home))
        {
            StopNavigation ();
            return false;
        }

        navGoalWorld = home;
        navMode = MonsterNavMode.Returning;
        return true;
    }

    public override void _PhysicsProcess (double delta)
    {
        if (!Engine.IsEditorHint ())
        {
            var deltaSeconds = (float) delta;
            if (!homeBinding.HasValue && unboundHomeWorld is null)
            {
                unboundHomeWorld = GlobalPosition;
            }

            EnforceOutdoorLeash ();
            if (OutdoorFieldConfig.NavigationMobMovementEnabled)
            {
                UpdateOutdoorChaseAi (deltaSeconds);
                AdvanceNavPath (deltaSeconds);
            }

            TryAttackNearbyPlayer (deltaSeconds);
            positionBroadcastCooldown = Mathf.Max (0f, positionBroadcastCooldown - deltaSeconds);
            if (positionBroadcastCooldown <= 0f)
            {
                SyncPositionToVisibleClients ();
            }
        }

        base._PhysicsProcess (delta);
    }

    private void SyncPositionToVisibleClients ()
    {
        var position = GlobalPosition;
        var angleRadians = DecodeAngleToYawRadians (Angle);
        var gameX = position.X;
        var gameY = -position.Y;
        var gameZ = -position.Z;

        if (hasBroadcastPosition
            && position.DistanceSquaredTo (lastBroadcastPosition) <= PositionBroadcastDelta * PositionBroadcastDelta
            && Math.Abs (angleRadians - lastBroadcastAngleRadians) <= PositionBroadcastDelta)
        {
            return;
        }

        hasBroadcastPosition = true;
        positionBroadcastCooldown = PositionBroadcastIntervalSeconds;
        lastBroadcastPosition = position;
        lastBroadcastAngleRadians = angleRadians;
        BroadcastEntityPositionToVisibleClients (gameX, gameY, gameZ, angleRadians);
    }

    private void ForcePositionSyncToVisibleClients ()
    {
        hasBroadcastPosition = false;
        SyncPositionToVisibleClients ();
    }

    private void UpdateOutdoorChaseAi (float deltaSeconds)
    {
        var hasSpawner = TryGetHomeSpawner (out var spawner);
        if (hasSpawner && !spawner.OutdoorChaseEnabled)
        {
            return;
        }

        navRepathCooldown = Mathf.Max (0f, navRepathCooldown - deltaSeconds);

        // Walking back ignores players, or a kited mob turns around at the give-up line forever
        if (navMode == MonsterNavMode.Returning)
        {
            if (!HasActiveNavPath)
            {
                navMode = MonsterNavMode.Idle;
            }

            return;
        }

        if (navMode == MonsterNavMode.Chasing && IsBeyondChaseGiveUp ())
        {
            BeginReturnHome ();
            return;
        }

        var aggroRadius = hasSpawner ? spawner.AggroRadiusMeters : DefaultAggroRadiusMeters;
        if (TryFindNearestChaseTarget (aggroRadius, out var chaseTarget))
        {
            BeginOrRefreshChase (chaseTarget);
            return;
        }

        if (navMode == MonsterNavMode.Chasing || ShouldReturnHome ())
        {
            BeginReturnHome ();
        }
    }

    private void BeginOrRefreshChase (Vector3 chaseTarget)
    {
        navMode = MonsterNavMode.Chasing;
        if (IsWithinChaseStopRange (chaseTarget))
        {
            ClearNavPath ();
            FaceToward (chaseTarget);
            return;
        }

        if (navRepathCooldown > 0f)
        {
            return;
        }

        if (HasActiveNavPath)
        {
            var goalDelta = navGoalWorld - chaseTarget;
            goalDelta.Y = 0f;
            if (goalDelta.LengthSquared () < ChaseGoalMoveThresholdMeters * ChaseGoalMoveThresholdMeters)
            {
                return;
            }
        }

        if (TryPathTo (chaseTarget))
        {
            navGoalWorld = chaseTarget;
        }

        navRepathCooldown = PathRepathIntervalSeconds;
    }

    private void BeginReturnHome ()
    {
        if (!TryGetHomeWorld (out _) || IsNearHome ())
        {
            StopNavigation ();
            return;
        }

        NavigateHome ();
        navRepathCooldown = PathRepathIntervalSeconds;
    }

    private bool ShouldReturnHome ()
    {
        return TryGetHomeWorld (out var home)
               && HorizontalDistanceSquared (GlobalPosition, home) > HomeDriftMeters * HomeDriftMeters;
    }

    private bool IsNearHome ()
    {
        if (!TryGetHomeWorld (out var home))
        {
            return true;
        }

        return HorizontalDistanceSquared (GlobalPosition, home)
               <= HomeArrivalDistanceMeters * HomeArrivalDistanceMeters;
    }

    private bool IsBeyondChaseGiveUp ()
    {
        return TryGetHomeWorld (out var home)
               && HorizontalDistanceSquared (GlobalPosition, home)
               > ChaseGiveUpDistanceMeters * ChaseGiveUpDistanceMeters;
    }

    private bool TryGetHomeWorld (out Vector3 homeWorld)
    {
        if (homeBinding.HasValue)
        {
            homeWorld = homeBinding.Value.HomeSlotWorld;
            return true;
        }

        homeWorld = unboundHomeWorld ?? default;
        return unboundHomeWorld.HasValue;
    }

    /// <summary>
    /// Navmesh only: with no path (indoor, unbaked tiles) the mob holds its ground rather than
    /// walking through walls
    /// </summary>
    private bool TryPathTo (Vector3 goalWorld)
    {
        if (TryGetHomeSpawner (out var spawner) && spawner.TryNavigateMonster (this, goalWorld, out _))
        {
            return true;
        }

        if (!TryGetHomeWorld (out var home))
        {
            return false;
        }

        var result = NavPathQuery.FindPath (this,
            new NavPathRequest (GlobalPosition, goalWorld, home, ChaseGiveUpDistanceMeters));
        return result.Success && TrySetNavPath (result.Waypoints);
    }

    private static float HorizontalDistanceSquared (Vector3 a, Vector3 b)
    {
        var delta = a - b;
        delta.Y = 0f;
        return delta.LengthSquared ();
    }

    private bool IsWithinChaseStopRange (Vector3 targetWorld)
    {
        if (DataRange <= 0)
        {
            return false;
        }

        var stopRange = DataRange * ChaseStopRangeFactor;
        return HorizontalDistanceSquared (GlobalPosition, targetWorld) <= stopRange * stopRange;
    }

    private void TryAttackNearbyPlayer (float deltaSeconds)
    {
        if (navMode == MonsterNavMode.Returning || !TryFindNearestAttackTarget (out var target, out var targetWorld))
        {
            return;
        }

        FaceToward (targetWorld);
        TryMeleeAttack (target, deltaSeconds);
    }

    private bool TryFindNearestAttackTarget (out SphereClient target, out Vector3 targetWorld)
    {
        target = null!;
        targetWorld = default;
        if (DataRange <= 0)
        {
            return false;
        }

        var rangeSq = (float) DataRange * DataRange;
        var origin = GlobalPosition;
        var bestDistanceSq = float.MaxValue;
        SphereClient? bestClient = null;
        Vector3 bestWorld = default;

        foreach (var client in ActiveClients.GetAll ().Values)
        {
            // A viewer that never got this mob's spawn would take hits from nothing
            if (!IsValidChaseClient (client, out var clientWorld)
                || client.CurrentCharacter is not { CurrentHP: > 0 }
                || !IsVisibleToClient (client)
                || Mathf.Abs (clientWorld.Y - origin.Y) > MaxTargetVerticalDeltaMeters)
            {
                continue;
            }

            var distanceSq = HorizontalDistanceSquared (clientWorld, origin);
            if (distanceSq > rangeSq || distanceSq >= bestDistanceSq)
            {
                continue;
            }

            bestDistanceSq = distanceSq;
            bestClient = client;
            bestWorld = clientWorld;
        }

        if (bestClient is null)
        {
            return false;
        }

        target = bestClient;
        targetWorld = bestWorld;
        return true;
    }

    private bool TryFindNearestChaseTarget (float aggroRadius, out Vector3 targetWorld)
    {
        targetWorld = default;
        if (!TryGetLeashDisk (out var leashCenter, out var leashRadius) || !TryGetHomeWorld (out var home))
        {
            return false;
        }

        aggroRadius = Mathf.Max (1f, aggroRadius);
        var aggroRadiusSq = aggroRadius * aggroRadius;
        var monsterPosition = GlobalPosition;
        var leashRadiusSq = leashRadius * leashRadius;
        var giveUpSq = ChaseGiveUpDistanceMeters * ChaseGiveUpDistanceMeters;
        var bestDistanceSq = float.MaxValue;
        Vector3? bestClientWorld = null;

        foreach (var client in ActiveClients.GetAll ().Values)
        {
            if (!IsValidChaseClient (client, out var clientWorld)
                || client.CurrentCharacter is not { CurrentHP: > 0 }
                || Mathf.Abs (clientWorld.Y - monsterPosition.Y) > MaxTargetVerticalDeltaMeters)
            {
                continue;
            }

            // Past the give-up line the chase would end as soon as it starts
            if (HorizontalDistanceSquared (clientWorld, leashCenter) > leashRadiusSq
                || HorizontalDistanceSquared (clientWorld, home) > giveUpSq)
            {
                continue;
            }

            var distanceSq = HorizontalDistanceSquared (clientWorld, monsterPosition);
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

        return TryResolveOutdoorChaseGoalWorld (bestClientWorld.Value, out targetWorld);
    }

    private static bool IsValidChaseClient (SphereClient client, out Vector3 clientWorld)
    {
        clientWorld = default;
        if (!GodotObject.IsInstanceValid (client) || !client.ClientStateManager.IsInGameState ())
        {
            return false;
        }

        return ClientWorldPosition.TryGetGodotWorldPosition (client, out clientWorld);
    }

    private bool TryResolveOutdoorChaseGoalWorld (Vector3 clientWorld, out Vector3 goalWorld)
    {
        goalWorld = new Vector3 (clientWorld.X, clientWorld.Y, clientWorld.Z);
        if (TrySampleGodotGroundY (clientWorld.X, clientWorld.Z, out var groundY))
        {
            goalWorld.Y = groundY;
        }

        return true;
    }

    private bool TrySampleGodotGroundY (float worldX, float worldZ, out float godotGroundY)
    {
        if (TryGetHomeSpawner (out var spawner) && TryGetLeashDisk (out var leashCenter, out var leashRadius))
        {
            TerrainNavMeshRuntime.EnsureTilesLoaded (spawner, leashCenter, leashRadius + 8f);
            TerrainNavMeshRuntime.TrySyncImmediate ();
        }

        var probeY = GlobalPosition.Y;
        return NavPathQuery.TrySampleGroundY (worldX, worldZ, probeY, out godotGroundY);
    }

    private void EnforceOutdoorLeash ()
    {
        if (!TryGetLeashDisk (out var leashCenter, out var leashRadius))
        {
            return;
        }

        if (IsInsideLeashDisk (GlobalTransform.Origin, leashCenter, leashRadius))
        {
            return;
        }

        StopNavigation ();
        TeleportToHomeSlot ();
    }

    private void TeleportToHomeSlot ()
    {
        if (!TryGetHomeWorld (out var spawnPosition))
        {
            return;
        }

        // The unbound home is a live origin that already carries the offset
        if (homeBinding.HasValue)
        {
            spawnPosition.Y += GetSpawnOriginYOffset (spawnPosition.Y);
        }

        GlobalPosition = spawnPosition;
        navMode = MonsterNavMode.Idle;
        Angle = WorldObject.CreateRandomSpawnAngle ();
        RegisterMultiMeshVisualDeferred ();
        ForcePositionSyncToVisibleClients ();
    }

    private void AdvanceNavPath (float delta)
    {
        if (!HasActiveNavPath || !TryGetLeashDisk (out _, out _))
        {
            return;
        }

        if (navMode == MonsterNavMode.Chasing && IsWithinChaseStopRange (navGoalWorld))
        {
            ClearNavPath ();
            FaceToward (navGoalWorld);
            return;
        }

        var target = navWaypoints[navWaypointIndex];
        var current = GlobalPosition;
        var toTarget = target - current;
        toTarget.Y = 0f;
        var distance = toTarget.Length ();
        var configured = navMode == MonsterNavMode.Chasing ? DataRunSpeed : DataWalkSpeed;
        var speed = Mathf.Max (0.5f, configured);
        if (distance <= AttackStopDistanceMeters)
        {
            navWaypointIndex++;
            return;
        }

        var step = Mathf.Min (distance, speed * delta);
        var next = current + toTarget.Normalized () * step;
        if (!TryGetLeashDisk (out var leashCenter, out var leashRadius)
            || !IsInsideLeashDisk (next, leashCenter, leashRadius))
        {
            ClearNavPath ();
            return;
        }

        next.Y = ResolveNavStandingY (next.X, next.Z);
        GlobalPosition = next;
        FaceToward (next + toTarget);
        RegisterMultiMeshVisualDeferred ();
    }

    private float ResolveNavStandingY (float worldX, float worldZ)
    {
        var currentY = GlobalPosition.Y;
        if (!TrySampleGodotGroundY (worldX, worldZ, out var groundY))
        {
            return currentY;
        }

        var targetY = groundY + GetSpawnOriginYOffset (groundY);
        return Mathf.Clamp (targetY, currentY - MaxNavVerticalStepMeters, currentY + MaxNavVerticalStepMeters);
    }

    private void FaceToward (Vector3 worldTarget)
    {
        var flatDelta = worldTarget - GlobalPosition;
        flatDelta.Y = 0f;
        if (flatDelta.LengthSquared () < 0.0001f)
        {
            return;
        }

        var yaw = Mathf.Atan2 (-flatDelta.X, -flatDelta.Z);
        Angle = EncodeYawRadiansToAngle (yaw);
    }

    private bool TryGetHomeSpawner (out MonsterSpawner spawner)
    {
        if (homeSpawner is not null && GodotObject.IsInstanceValid (homeSpawner))
        {
            spawner = homeSpawner;
            return true;
        }

        if (TryResolveSpawnerFromBinding (out spawner))
        {
            homeSpawner = spawner;
            return true;
        }

        if (TryResolveSpawnerFromAncestors (out spawner))
        {
            homeSpawner = spawner;
            return true;
        }

        spawner = null!;
        return false;
    }

    private bool TryResolveSpawnerFromBinding (out MonsterSpawner spawner)
    {
        spawner = null!;
        if (!homeBinding.HasValue)
        {
            return false;
        }

        var binding = homeBinding.Value;
        if (binding.OwnerSpawnerInstanceId != 0)
        {
            var instance = GodotObject.InstanceFromId (binding.OwnerSpawnerInstanceId);
            if (instance is MonsterSpawner instanceSpawner && GodotObject.IsInstanceValid (instanceSpawner))
            {
                spawner = instanceSpawner;
                return true;
            }
        }

        if (binding.OwnerSpawnerPath.IsEmpty || GetTree () is not SceneTree tree)
        {
            return false;
        }

        var pathSpawner = tree.Root.GetNodeOrNull (binding.OwnerSpawnerPath);
        if (pathSpawner is MonsterSpawner resolvedSpawner && GodotObject.IsInstanceValid (resolvedSpawner))
        {
            spawner = resolvedSpawner;
            return true;
        }

        return false;
    }

    private bool TryResolveSpawnerFromAncestors (out MonsterSpawner spawner)
    {
        for (var node = GetParent (); node is not null; node = node.GetParent ())
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

    private bool TryGetLeashDisk (out Vector3 leashCenter, out float leashRadius)
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

        if (TryGetHomeSpawner (out var spawner))
        {
            leashCenter = spawner.LeashCenterWorld;
            leashRadius = spawner.LeashRadiusMeters;
            return true;
        }

        if (unboundHomeWorld.HasValue)
        {
            leashCenter = unboundHomeWorld.Value;
            leashRadius = OutdoorFieldConfig.DefaultLeashRadiusMeters;
            return true;
        }

        leashCenter = default;
        leashRadius = 0f;
        return false;
    }

    private static bool IsInsideLeashDisk (Vector3 worldPosition, Vector3 leashCenterWorld, float leashRadiusMeters)
        => NavPathQuery.IsInsideLeash (worldPosition, leashCenterWorld, leashRadiusMeters);
}
