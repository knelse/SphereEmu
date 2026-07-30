using System;
using Godot;
using SphServer.Client;
using SphServer.Shared.Logger;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
	/// <summary>Guards the kill pipeline against a second killing blow re-running it.</summary>
	private bool _deathStarted;

	/// <summary>Raised exactly once, on the 0-HP crossing, after <see cref="OnDamaged" />.</summary>
	public event Action<Monster, DamageEvent, DamageOutcome>? MonsterKilled;

	/// <summary>Overrides must call base to keep subscribers and the despawn working.</summary>
	protected virtual void OnMonsterKilled (in DamageEvent hit, in DamageOutcome outcome)
	{
		MonsterKilled?.Invoke(this, hit, outcome);
		PerformDeath(hit.AttackerId);
	}

	/// <summary>
	///     Death pipeline: stop physics, death signal, then (one frame later) despawn + registry
	///     unwind + free. The client plays its full death animation from the signal.
	/// </summary>
	private void PerformDeath (ushort killerGlobalId)
	{
		if (_deathStarted)
		{
			return;
		}

		_deathStarted = true;

		// Halt nav/AI/position broadcast so the corpse does not keep chasing while the client plays the death.
		SetPhysicsProcess(false);

		BroadcastDeathSignalToVisibleClients(killerGlobalId);

		SphLogger.Info($"Monster {Name} [{ID:X4}] killed by {killerGlobalId:X4}.");

		// Deferred: the killing-blow damage echo is enqueued by the handler after TakeDamage returns,
		// and the despawn must reach the client after that echo — a damage delta for an already
		// removed entity crashes the client's script VM (BoundCheckArray in _player, observed live).
		Callable.From(FinishDespawn).CallDeferred();
	}

	private void FinishDespawn ()
	{
		if (!GodotObject.IsInstanceValid(this))
		{
			return;
		}

		BroadcastDespawnToVisibleClients();
		RemoveFromWorldRegistry();
		QueueFree();
	}

	/// <summary>
	///     Don't spawn a dying mob to a client entering range after the killing blow — it would get
	///     a live spawn and never see the death. The pending despawn broadcast is harmless for it.
	/// </summary>
	protected override void ShowForClient (SphereClient client)
	{
		if (_deathStarted)
		{
			return;
		}

		base.ShowForClient(client);
	}
}
