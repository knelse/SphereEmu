using Godot;
using SphServer.Client;
using SphServer.Server.Config;
using SphServer.Server.GameplayLogic.Combat;
using SphServer.Sphere.Game.WorldObject;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Simulation;

public readonly record struct CombatResult (
	Guid AttackerId,
	Guid TargetId,
	int Applied,
	int RemainingHp,
	bool BecameDead,
	bool IsMiss);

public sealed class PlayerCombatState
{
	public int Cooldown;
	public bool Pending;
}

public static class PlayerCombat
{
	public const int CooldownTicks = 30;

	public const float RangeMeters = 3f;

	public static void Step (SphereClient client, CharacterBody3D body, PlayerInputSlot slot,
		PlayerCombatState state, Random rng, List<CombatResult> results)
	{
		if (state.Cooldown > 0)
		{
			state.Cooldown--;
		}

		var pressed = (slot.Buttons & PlayerCommand.Attack) != 0;
		if (state.Cooldown > 0)
		{
			if (pressed)
			{
				state.Pending = true;
			}

			return;
		}

		if (!pressed && !state.Pending)
		{
			return;
		}

		if (TrySwing (client, body, slot, rng, results))
		{
			state.Cooldown = CooldownTicks;
			state.Pending = false;
			return;
		}

		state.Pending = false;
	}

	private static bool TrySwing (SphereClient client, CharacterBody3D body, PlayerInputSlot slot, Random rng,
		List<CombatResult> results)
	{
		var character = client.CurrentCharacter;
		if (character is null || character.CurrentHP <= 0 || slot.TargetId == Guid.Empty)
		{
			return false;
		}

		if (ActiveWorldObjects.Get (slot.TargetId) is not Monster monster
			|| !GodotObject.IsInstanceValid (monster)
			|| monster.IsDead)
		{
			return false;
		}

		var radius = ServerConfig.AppConfig.ObjectVisibilityDistance;
		var toTarget = monster.GlobalPosition - body.GlobalPosition;
		var distanceSq = toTarget.LengthSquared ();
		if (distanceSq > radius * radius || distanceSq > RangeMeters * RangeMeters)
		{
			return false;
		}

		if (!RayReaches (body, monster))
		{
			return false;
		}

		var cfg = BalanceConfig.Get<CombatBalance> ("combat");
		if (cfg is null)
		{
			return false;
		}

		// swing uses the held item, already folded into MeleePAtk and MagicMAtk
		var weapon = character.GetHeldItem ();
		var bareHanded = !character.HoldsItemInHand || weapon.ObjectKind == GameObjectKind.Fists;
		var roll = DamageCalc.RollAttack (character.MeleePAtk, character.MagicMAtk, bareHanded,
			monster.CurrentPDef, monster.CurrentMDef, rng, cfg);
		var damageEvent = new DamageEvent (client.EntityID, client, roll.TotalDamage, roll.School, roll.IsCrit);
		var outcome = monster.TakeDamage (in damageEvent);
		results.Add (new CombatResult (client.EntityID, monster.EntityID, outcome.Applied, outcome.RemainingHp,
			outcome.BecameDead, roll.IsMiss));
		return true;
	}

	private static bool RayReaches (CharacterBody3D body, Node3D target)
	{
		var world = body.GetWorld3D ();
		if (world is null)
		{
			return false;
		}

		var query = PhysicsRayQueryParameters3D.Create (body.GlobalPosition, target.GlobalPosition);
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		query.HitFromInside = true;
		query.Exclude = new global::Godot.Collections.Array<Rid> { body.GetRid () };
		var hit = world.DirectSpaceState.IntersectRay (query);
		if (hit.Count == 0)
		{
			return true;
		}

		if (!hit.TryGetValue ("collider", out var collider))
		{
			return true;
		}

		var node = collider.Obj as Node;
		while (node is not null)
		{
			if (node == target)
			{
				return true;
			}

			node = node.GetParent ();
		}

		return false;
	}
}
