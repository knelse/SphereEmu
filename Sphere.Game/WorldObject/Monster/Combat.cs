using System;
using SphServer.Client;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>Melee v1 is physical-only; Magical is reserved (positive raw MAtk means heal, never melee damage).</summary>
public enum DamageSchool
{
	Physical = 0,
	Magical = 1
}

/// <summary>One damage application request; Amount is post-mitigation, &gt;= 0 (0 == miss, still an event).</summary>
public readonly record struct DamageEvent (
	ushort AttackerId,
	SphereClient? AttackerClient,
	int Amount,
	DamageSchool School,
	bool IsCrit);

/// <summary>Result of a damage application; BecameDead is true exactly once, on the transition to 0 HP.</summary>
public readonly record struct DamageOutcome (
	int Applied,
	int RemainingHp,
	bool BecameDead);

/// <summary>Damage math kept off the Godot node type so it is unit-testable without engine bootstrap.</summary>
public static class MonsterCombat
{
	/// <summary>HP clamps at 0; an already-dead monster (HP &lt;= 0) yields the no-op outcome.</summary>
	public static DamageOutcome ComputeOutcome (int currentHp, int amount)
	{
		if (amount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof (amount), amount,
				"Damage amount must be >= 0 (0 == miss; heals are not damage events).");
		}

		var hpBefore = Math.Max(currentHp, 0);
		var applied = Math.Min(amount, hpBefore);
		var remainingHp = hpBefore - applied;
		var becameDead = hpBefore > 0 && remainingHp == 0;

		return new DamageOutcome(applied, remainingHp, becameDead);
	}
}

public partial class Monster
{
	/// <summary>Dead == 0 HP; there is no separate death state.</summary>
	public bool IsDead => CurrentHp <= 0;

	/// <summary>Raised after every damage application on a live monster, including 0-damage misses.</summary>
	public event Action<Monster, DamageEvent, DamageOutcome>? Damaged;

	/// <summary>
	///     MAIN THREAD ONLY (packet handlers run inside the physics tick, so this seam is unlocked).
	///     Never sends packets — replies are the calling handler's job. No-op on an already-dead monster.
	/// </summary>
	public DamageOutcome TakeDamage (in DamageEvent hit)
	{
		var outcome = MonsterCombat.ComputeOutcome(CurrentHp, hit.Amount);
		if (IsDead)
		{
			return outcome;
		}

		CurrentHp = outcome.RemainingHp;
		OnDamaged(in hit, in outcome);

		if (outcome.BecameDead)
		{
			OnMonsterKilled(in hit, in outcome);
		}

		return outcome;
	}

	/// <summary>Overrides must call base to keep <see cref="Damaged" /> subscribers working.</summary>
	protected virtual void OnDamaged (in DamageEvent hit, in DamageOutcome outcome)
	{
		Damaged?.Invoke(this, hit, outcome);
	}
}
