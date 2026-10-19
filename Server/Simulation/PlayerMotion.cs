using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Godot.Tools;

namespace SphServer.Server.Simulation;

public static class PlayerMotion
{
	// Client walk is member3546_ (2.1) units/sec. moveGround steps that by 1/192.
	// Run is * 2.2, the speed effect is * 1.5. Flat ground material scale is 1.
	public const float MaxSpeed = 2.1f * 1.5f * 2.2f;

	public const float JumpSpeed = 5f;

	public const float Gravity = 9.8f;

	public const float YawStepRadians = 4f;

	// char*.mdl origin is the top, feet 1.8 below. Game anchors at center instead of bottom.
	public const float OriginAboveGround = 0.9f;

	public static bool IsInGame (SphereClient client) =>
		GodotObject.IsInstanceValid (client)
		&& client.ClientStateManager.IsInGameState ()
		&& client.CurrentCharacter is not null;

	public static CharacterBody3D? FindModel (SphereClient client) =>
		NodeChildTools.FindFirstChildOfType<CharacterBody3D> (client, "ClientModel");

	private static readonly HashSet<Guid> seeded = [];

	public static void Apply (SphereClient client, PlayerInputSlot slot)
	{
		if (!slot.HasDatagram || client.CurrentCharacter is not { } character)
		{
			return;
		}

		var body = FindModel (client);
		if (body is null || !body.IsInsideTree ())
		{
			return;
		}

		body.ProcessMode = Node.ProcessModeEnum.Inherit;
		body.CollisionLayer = 2;
		body.CollisionMask = 0b11;

		if (seeded.Add (client.EntityID))
		{
			// Same vector UpdateCoordinatesInWorld uses. A second negation flips the client
			client.GlobalPosition = character.Origin;
			body.Position = Vector3.Zero;
			var seededRotation = body.Rotation;
			seededRotation.Y = (float) character.Angle;
			body.Rotation = seededRotation;
		}

		// Client XZ wish. Source basis is (Right, Down, Forward): Godot (x, -y, -z).
		var wishX = float.IsFinite (slot.WishX) ? slot.WishX : 0f;
		var wishZ = float.IsFinite (slot.WishZ) ? slot.WishZ : 0f;
		var wish = new Vector2 (wishX, wishZ);
		var wishSpeed = wish.Length ();
		if (wishSpeed > MaxSpeed)
		{
			wish *= MaxSpeed / wishSpeed;
		}

		var yaw = body.Rotation.Y;
		if (float.IsFinite (slot.Yaw))
		{
			yaw = ApproachYaw (yaw, slot.Yaw, YawStepRadians);
		}

		// Euler X/Z would turn strafe into the up axis and the floor would drop it
		body.Rotation = new Vector3 (0f, yaw, 0f);
		body.UpDirection = Vector3.Up;

		var idle = wish.LengthSquared () < 1e-6f && (slot.Buttons & PlayerCommand.Jump) == 0;
		var jump = !idle && body.IsOnFloor () && (slot.Buttons & PlayerCommand.Jump) != 0;
		var velocity = idle
			? Vector3.Zero
			: new Vector3 (wish.X, jump ? JumpSpeed : 0f, -wish.Y);
		var step = velocity * (float) SimulationClock.SecondsPerTick;
		var placed = body.GlobalPosition + new Vector3 (step.X, jump ? step.Y : 0f, step.Z);
		if (!jump && TryGroundY (body, placed.X, placed.Z, out var groundY))
		{
			placed.Y = groundY;
		}

		body.Velocity = velocity;
		if (client.IsInsideTree ())
		{
			body.Position = Vector3.Zero;
			client.GlobalPosition = placed;
		}

		var moved = Mathf.Abs ((float) character.X - placed.X) > 0.1f
					|| Mathf.Abs ((float) character.Y - placed.Y) > 0.1f
					|| Mathf.Abs ((float) character.Z - placed.Z) > 0.1f;
		character.X = placed.X;
		character.Y = placed.Y;
		character.Z = placed.Z;
		character.Angle = yaw;
		if (moved)
		{
			client.EnqueueClientEvent (new CurrentClientPositionChangedEvent ());
		}
	}

	private static bool TryGroundY (Node3D from, float worldX, float worldZ, out float groundY)
	{
		groundY = 0f;
		var feetY = from.GlobalPosition.Y;
		SphServer.Server.SphereServer.ServerNode?.TerrainGround?.EnsureAroundWorldPosition (
			new Vector3 (worldX, feetY, worldZ));

		var world = from.GetWorld3D ();
		if (world is null)
		{
			return false;
		}

		// Layer 1 is the streamed tile. The player body is layer 2, so this ray
		// cannot stand on the capsule and climb.
		var query = PhysicsRayQueryParameters3D.Create (
			new Vector3 (worldX, feetY + OriginAboveGround + 2f, worldZ),
			new Vector3 (worldX, -1024f, worldZ));
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		query.HitFromInside = false;
		query.CollisionMask = 1;
		var hit = world.DirectSpaceState.IntersectRay (query);
		if (!hit.TryGetValue ("position", out var position))
		{
			return false;
		}

		// Game Y is flipped, so the body lift is +Godot Y. Re-applying it
		// whenever the hit is merely below the feet climbs into the sky.
		var surface = position.AsVector3 ().Y;
		var stand = surface + OriginAboveGround;
		if (feetY > stand + 0.05f)
		{
			groundY = stand;
			return true;
		}

		if (Mathf.Abs (surface - feetY) <= 0.05f)
		{
			groundY = stand;
			return true;
		}

		return false;
	}

	public static float ApproachYaw (float current, float target, float maxStep)
	{
		var delta = Mathf.Wrap (target - current, -Mathf.Pi, Mathf.Pi);
		if (Mathf.Abs (delta) <= maxStep)
		{
			return current + delta;
		}

		return current + Mathf.Sign (delta) * maxStep;
	}
}
