using SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Client;

public abstract record ClientQueuedEvent;

public sealed record CurrentClientPositionChangedEvent : ClientQueuedEvent;

public sealed record EntityPositionUpdateEvent (
    Guid EntityId, ushort ModuleTag, double X, double Y, double Z, double Angle)
    : ClientQueuedEvent;

/// <summary>
/// One damage application against a single resolved target (main hit or AoE splash).
/// </summary>
public sealed record CombatHitEvent (
    Guid AttackerEntityId,
    Guid TargetEntityId,
    AttackFrameKind FrameKind) : ClientQueuedEvent;

/// <summary>
/// Signed self HP delta, negative for damage and positive for heal. KillerProcessId
/// above 3 is the attacker's process; 0 keeps the nameless death line
/// </summary>
public sealed record CharacterHealthChangeEvent (
    Guid EntityId,
    int HealthDiff,
    Guid KillerProcessId = default,
    DamageOriginSpecial? Origin = null)
    : ClientQueuedEvent;
