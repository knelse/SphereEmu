using System;
using SphServer.Client;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;
using SphServer.Server.Config;
using SphServer.Server.GameplayLogic.Combat;
using SphServer.Shared.BitStream;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Networking;
using static Stat;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
/// Melee v1 is physical-only; Magical is reserved (positive raw MAtk means heal, never melee
/// damage).
/// </summary>
public enum DamageSchool
{
    Physical = 0,
    Magical = 1
}

/// <summary>
/// One damage application request; Amount is post-mitigation, >= 0 (0 == miss, still an event).
/// </summary>
public readonly record struct DamageEvent (
    ushort AttackerId,
    SphereClient? AttackerClient,
    int Amount,
    DamageSchool School,
    bool IsCrit);

/// <summary>
/// Result of a damage application; BecameDead is true exactly once, on the transition to 0 HP.
/// </summary>
public readonly record struct DamageOutcome (
    int Applied,
    int RemainingHp,
    bool BecameDead);

/// <summary>
/// Damage math kept off the Godot node type so it is unit-testable without engine bootstrap.
/// </summary>
public static class MonsterCombat
{
    /// <summary>
    /// HP clamps at 0; an already-dead monster (HP &lt;= 0) yields the no-op outcome.
    /// </summary>
    public static DamageOutcome ComputeOutcome (int currentHp, int amount)
    {
        // A negative amount would raise HP, and a throw here reaches the packet path with nothing
        // to catch it
        var damage = Math.Max (amount, 0);
        var hpBefore = Math.Max (currentHp, 0);
        var applied = Math.Min (damage, hpBefore);
        var remainingHp = hpBefore - applied;
        var becameDead = hpBefore > 0 && remainingHp == 0;

        return new DamageOutcome (applied, remainingHp, becameDead);
    }
}

public partial class Monster
{
    private readonly Random attackRng = new ();
    private float attackCooldownSeconds;

    /// <summary>
    /// Dead == 0 HP; there is no separate death state.
    /// </summary>
    public bool IsDead => CurrentHp <= 0;

    /// <summary>
    /// One swing inside DataRange; cats (object row 0/0) always deal 0
    /// </summary>
    private void TryMeleeAttack (SphereClient target, float deltaSeconds)
    {
        if (IsDead || DataAttackDelay <= 0f)
        {
            return;
        }

        attackCooldownSeconds -= deltaSeconds;
        if (attackCooldownSeconds > 0f)
        {
            return;
        }

        attackCooldownSeconds = DataAttackDelay;

        var character = target.CurrentCharacter;
        if (character is null || character.CurrentHP <= 0)
        {
            return;
        }

        if (MonsterType == MonsterType.Кошка)
        {
            return;
        }

        var cfg = BalanceConfig.Get<CombatBalance> ("combat");
        if (cfg is null)
        {
            return;
        }

        // Not bare-handed: fistStatSheetDamage must not turn a 0-attack row into a hit.
        var roll = DamageCalc.RollAttack (CurrentPAtk, CurrentMAtk, false, character.PDef, character.MDef, attackRng,
            cfg);
        if (roll.TotalDamage <= 0)
        {
            return;
        }

        var applied = Math.Min (roll.TotalDamage, character.CurrentHP);
        applied = Math.Clamp (applied, 0, 30000);
        if (applied <= 0)
        {
            return;
        }

        var hpAfter = character.CurrentHP - applied;
        character.CurrentHP = (ushort) hpAfter;
        NetworkedStatsUpdater.MarkSent (character, HpCurrent);
        // Position stream process is the swapped client index
        var playerProcess = SphBitStream.ByteSwap (character.ClientIndex);
        if (hpAfter <= 0)
        {
            var killer = target.GetLocalObjectId (ID);
            // hit_type above 3 is this monster's process on that client
            var hitType = killer > 3 ? killer : (ushort) 0;
            target.MaybeQueueNetworkPacketSend (
                CommonPackets.BuildPlayerReceiveHit (playerProcess, hitType, hpDelta: -100000,
                    secondDelta: 0, flags: 7));
            target.SchedulePlayerRespawn ();
        }
        else
        {
            target.MaybeQueueNetworkPacketSend (
                ChangeCharacterHealthHandler.BuildHealthChangePacket (
                    character.ClientIndex, character.ClientIndex, -applied, ID));
            target.MaybeQueueNetworkPacketSend (
                CommonPackets.BuildPlayerApplyHpDelta (playerProcess, ID, -applied));
        }

        target.SaveCharacter ();
    }

    /// <summary>
    /// Raised after every damage application on a live monster, including 0-damage misses.
    /// </summary>
    public event Action<Monster, DamageEvent, DamageOutcome>? Damaged;

    /// <summary>
    /// Runs on the physics tick; already-dead is a no-op and this sends no packets
    /// </summary>
    public DamageOutcome TakeDamage (in DamageEvent hit)
    {
        var outcome = MonsterCombat.ComputeOutcome (CurrentHp, hit.Amount);
        if (IsDead)
        {
            return outcome;
        }

        CurrentHp = outcome.RemainingHp;
        OnDamaged (in hit, in outcome);

        if (outcome.BecameDead)
        {
            OnMonsterKilled (in hit, in outcome);
        }

        return outcome;
    }

    /// <summary>
    /// Overrides must call base to keep hit history and <see cref="Damaged" /> subscribers working.
    /// </summary>
    protected virtual void OnDamaged (in DamageEvent hit, in DamageOutcome outcome)
    {
        var clientId = hit.AttackerClient?.localId ?? hit.AttackerId;
        if (clientId != 0)
        {
            hitHistory.Record (clientId, hit.School, DateTime.UtcNow);
        }

        Damaged?.Invoke (this, hit, outcome);
    }
}
