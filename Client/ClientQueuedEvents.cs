using SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;

namespace SphServer.Client;

public abstract record ClientQueuedEvent;

public sealed record CurrentClientPositionChangedEvent : ClientQueuedEvent;

public sealed record EntityPositionUpdateEvent(ushort EntityId, double X, double Y, double Z, double Angle)
    : ClientQueuedEvent;

/// <summary>One damage application against a single resolved target (main hit or AoE splash).</summary>
public sealed record CombatHitEvent(
    ushort AttackerGlobalId,
    ushort TargetGlobalId,
    ushort TargetLocalId,
    AttackFrameKind FrameKind) : ClientQueuedEvent;

/// <summary>
///     Signed self HP delta. Negative = damage, positive = heal.
///     Only <see cref="ChangeCharacterHealthHandler"/> applies HP and sends hit/death wire.
/// </summary>
public sealed record CharacterHealthChangeEvent(ushort EntityId, int HealthDiff) : ClientQueuedEvent;
