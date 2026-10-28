using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Godot.Scripts.World;
using SphServer.Helpers;
using SphServer.Server.Simulation.Motion;
using SphServer.Shared.Networking;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Godot.Tools;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Server.Simulation;

/// <summary>
///     The server's player integrator: it drives MotionPlayer, the port of the reference client's
///     GameWorldMovement, over geometry streamed from raw .lnd/.mdl. The solver runs in the game
///     frame (Y down); the node, the store record and the wire are Godot Y-up, so the pose crosses
///     through (x, -y, -z) at the boundary
/// </summary>
public static class PlayerMotion
{
	// The wire's yaw is the client's own camera yaw (Godot space), so the solver's heading is its
	// negation, as the reference's CameraYaw = -SpawnAngle pair is
	public static float SolverAngle (float wireYaw) => -wireYaw;

	// The live client respawns a body past y 8000 in its fall(). The game frame is Y down, so the
	// void is a large positive Y, and the world's own floor sits near -2850
	private const float FallOutWorldMeters = 8000f;

	// Reference SetPlayerWorldPosition's range check, the bound on a pose worth keeping
	private const float SeedMaxWorldMeters = 20000f;

	// The seed descent budget: a store record sits above the raw .lnd tile it was recorded on, so the
	// support window has to be walked down to it. Measured 3.4 at the appsettings spawn, and the
	// old sweep's own seed lift was 4
	private const float SeedSettleMeters = 4f;

	// The seed pose is the store record, recorded under the baked-Godot rules, so the boundary is
	// the same (x,-y,-z) the sweep path used. Whether that record lands on a raw .lnd tile is not
	// derivable offline (the raw-to-Godot chain is a -90 rotation plus a ~7400 offset, measured by
	// MotionFrameProbe), so the seed logs the pose and the terrain support it finds. One run names
	// the frame; the numbers go in docs/motion-port-spec.md
	private static Vector3 ToRaw (Vector3 godot) => new (godot.X, -godot.Y, -godot.Z);

	private static Vector3 ToGodot (Vector3 raw) => new (raw.X, -raw.Y, -raw.Z);

	public static bool IsInGame (SphereClient client) =>
		GodotObject.IsInstanceValid (client)
		&& client.ClientStateManager.IsInGameState ()
		&& client.CurrentCharacter is not null;

	public static CharacterBody3D? FindModel (SphereClient client) =>
		NodeChildTools.FindFirstChildOfType<CharacterBody3D> (client, "ClientModel");

	private static readonly HashSet<Guid> seeded = [];

	// The jump mutators and the sphere effect this server sent the player, which the client's jump
	// reads. Kept apart from the integrator so a reseed keeps them
	private static readonly Dictionary<Guid, (bool X2, bool X4, bool IsJumpProhibited)> jumpEffects = [];

	/// <summary>
	///     Call when sending the client a jump mutator: x2 (1014, effect 5) or x4 (1015, effect 6)
	/// </summary>
	public static void AddJumpEffect (Guid entityId, bool x2)
	{
		var effects = jumpEffects.GetValueOrDefault (entityId);
		jumpEffects[entityId] = x2 ? effects with { X2 = true } : effects with { X4 = true };
		ApplyJumpEffects (entityId);
	}

	/// <summary>
	///     Call when sending the client's _player.mgrEffect: effect 600 (the sphere) sets it, the
	///     one-byte form clears it
	/// </summary>
	public static void SetJumpProhibited (Guid entityId, bool isJumpProhibited)
	{
		var effects = jumpEffects.GetValueOrDefault (entityId);
		jumpEffects[entityId] = effects with { IsJumpProhibited = isJumpProhibited };
		ApplyJumpEffects (entityId);
	}

	private static void ApplyJumpEffects (Guid entityId)
	{
		if (!players.TryGetValue (entityId, out var player))
		{
			return;
		}

		var effects = jumpEffects.GetValueOrDefault (entityId);
		player.JumpX2 = effects.X2;
		player.JumpX4 = effects.X4;
		player.IsJumpProhibited = effects.IsJumpProhibited;
	}

	// One integrator per player, the reference's per-scene Impl fields
	private static readonly Dictionary<Guid, MotionPlayer> players = [];

	// The last pose that sat on geometry, in the game frame. A fall that reaches the void returns
	// here, the live client's own respawn for a body past y 8000, which the reference has no answer
	// for and my port had none either, so a player through the map fell forever
	private static readonly Dictionary<Guid, Vector3> lastGood = [];

	public static void Apply (SphereClient client, PlayerInputSlot slot)
	{
		if (!slot.HasDatagram
			|| client.CurrentCharacter is not { } character
			|| SphServer.Server.SphereServer.ServerNode?.Motion is not { } stream)
		{
			return;
		}

		var body = FindModel (client);
		if (body is null || !body.IsInsideTree ())
		{
			return;
		}

		body.ProcessMode = Node.ProcessModeEnum.Inherit;

		// A node added this tick has no placement yet, and its global pose is (0,0,0). The store
		// record is the pose, so the node is set from it before seeding, as UpdateCoordinatesInWorld
		// does, otherwise the integrator seeds at the world origin and falls
		if (seeded.Add (client.EntityID) || !players.ContainsKey (client.EntityID))
		{
			client.GlobalPosition = character.Origin;
			body.Position = Vector3.Zero;
			var seededRotation = body.Rotation;
			seededRotation.Y = (float) character.Angle;
			body.Rotation = seededRotation;

			var game = ToRaw (character.Origin);
			var player = new MotionPlayer ();
			// A store record that is off-map is rejected by the reference, so the pose is kept as
			// the record has it, and the body is not teleported to a default
			if (!player.SetPlayerWorldPosition (game.X, game.Y, game.Z, SolverAngle ((float) character.Angle)))
			{
				player.SpawnX = game.X;
				player.SpawnY = game.Y;
				player.SpawnZ = game.Z;
				player.RotateView (SolverAngle ((float) character.Angle));
			}

			players[client.EntityID] = player;
			ApplyJumpEffects (client.EntityID);
			// A seed pose recorded under the old rules can be in the air, on a slope, or inside a
			// wall, so drop it, walk it out, and settle it, all with the geometry loaded for it
			stream.EnsureAroundWorldPosition (game);
			player.SnapToGround (stream.Terrain, stream.Statics, player.SpawnY);
			if (!player.Grounded)
			{
				// The record sits 3.4 m above the tile under it, so SnapToGround's 0.45 window finds
				// nothing and the first step free-falls the body: the fall through the floor
				player.SettleToSupportBelow (stream.Terrain, stream.Statics, SeedSettleMeters);
			}
			player.RecoverFromPenetration (stream.Terrain, stream.Statics);
			player.Grounded = true;
			// The seed is the last pose known to sit on geometry, so a later fall has somewhere to
			// return to
			lastGood[client.EntityID] = new Vector3 (player.SpawnX, player.SpawnY, player.SpawnZ);
			WritePose (client, character, body, player);
		}

		var motion = players[client.EntityID];

		// The wish is a game-space velocity: the client built it from its own camera basis in the
		// same axes, so XZ cross untouched
		var wishX = float.IsFinite (slot.WishX) ? slot.WishX : 0f;
		var wishZ = float.IsFinite (slot.WishZ) ? slot.WishZ : 0f;
		var jump = (slot.Buttons & PlayerCommand.Jump) != 0;

		var yaw = body.Rotation.Y;
		if (float.IsFinite (slot.Yaw))
		{
			yaw = ApproachYaw (yaw, slot.Yaw, 4f);
		}

		body.Rotation = new Vector3 (0f, yaw, 0f);
		body.UpDirection = Vector3.Up;
		client.ApplyVisualYaw (yaw);
		motion.RotateView (yaw);

		var step = ToRaw (client.GlobalPosition);
		PlaceDynamicObstacles (client, stream, step);
		motion.SpawnX = step.X;
		motion.SpawnZ = step.Z;
		// A pose the store recorded in the old frame can bury the body, so recovery runs before the
		// step, and only when a move left it embedded
		if (motion.PlayerCollisionNeedsRecovery)
		{
			stream.EnsureAroundWorldPosition (step);
			motion.RecoverFromPenetration (stream.Terrain, stream.Statics);
		}

		motion.SpawnY = step.Y;
		stream.EnsureAroundWorldPosition (step);
		if (character.CurrentHP > 0)
		{
			motion.Update (stream.Terrain, stream.Statics, new Vector2 (wishX, wishZ), jump,
				SimulationClock.SecondsPerTickF);
			var landed = new Vector3 (motion.SpawnX, motion.SpawnY, motion.SpawnZ);
			if (KillPlane.Crossed (step, landed))
			{
				motion.SpawnX = step.X;
				motion.SpawnY = step.Y;
				motion.SpawnZ = step.Z;
				motion.VelocityX = 0f;
				motion.VelocityY = 0f;
				motion.VelocityZ = 0f;
				motion.SlideX = 0f;
				motion.SlideZ = 0f;
				motion.Grounded = true;
				KillByPlane (client, character);
			}
		}

		stream.Statics.ClearDynamic ();

		// A body that falls past the world has nothing to land on and no way back, the live client's
		// own respawn arm at y 8000. It returns to the last pose that sat on geometry
		if (motion.SpawnY > FallOutWorldMeters)
		{
			if (lastGood.TryGetValue (client.EntityID, out var safe))
			{
				motion.SpawnX = safe.X;
				motion.SpawnY = safe.Y;
				motion.SpawnZ = safe.Z;
			}
			else
			{
				var origin = ToRaw (character.Origin);
				motion.SpawnX = origin.X;
				motion.SpawnY = origin.Y;
				motion.SpawnZ = origin.Z;
			}

			motion.VelocityX = 0f;
			motion.VelocityY = 0f;
			motion.VelocityZ = 0f;
			motion.SlideX = 0f;
			motion.SlideZ = 0f;
			motion.Grounded = false;
			motion.PlayerCollisionNeedsRecovery = true;
		}
		else if (MathF.Abs (motion.SpawnX) < SeedMaxWorldMeters
			&& MathF.Abs (motion.SpawnZ) < SeedMaxWorldMeters
			&& MathF.Abs (motion.SpawnY) < FallOutWorldMeters)
		{
			// A pose on geometry is the one worth returning to, so it is taken only above the void
			lastGood[client.EntityID] = new Vector3 (motion.SpawnX, motion.SpawnY, motion.SpawnZ);
		}

		WritePose (client, character, body, motion);
	}

	/// <summary>
	///     Moves the body to a game-frame pose and settles it as the seed does, so the next step
	///     integrates from there. A body not seeded yet takes the pose through the store record
	/// </summary>
	public static bool Place (SphereClient client, Vector3 raw, float? yaw, out Vector3 placed)
	{
		placed = raw;
		if (client.CurrentCharacter is not { } character)
		{
			return false;
		}

		var body = FindModel (client);
		if (body is null || !players.TryGetValue (client.EntityID, out var motion)
			|| SphServer.Server.SphereServer.ServerNode?.Motion is not { } stream)
		{
			var origin = ToGodot (raw);
			character.X = origin.X;
			character.Y = origin.Y;
			character.Z = origin.Z;
			if (SavedCoords.TryContinent (raw.Y, raw.Z, out var continent)
				&& character.LastContinent != continent)
			{
				character.LastContinent = continent;
			}

			if (yaw is { } seedYaw)
			{
				character.Angle = seedYaw;
			}

			return true;
		}

		if (yaw is { } heading)
		{
			body.Rotation = new Vector3 (0f, heading, 0f);
			client.ApplyVisualYaw (heading);
			motion.RotateView (heading);
		}

		stream.EnsureAroundWorldPosition (raw);
		motion.SpawnX = raw.X;
		motion.SpawnY = raw.Y;
		motion.SpawnZ = raw.Z;
		motion.VelocityX = 0f;
		motion.VelocityY = 0f;
		motion.VelocityZ = 0f;
		motion.SlideX = 0f;
		motion.SlideZ = 0f;
		motion.SnapToGround (stream.Terrain, stream.Statics, motion.SpawnY);
		if (!motion.Grounded)
		{
			motion.SettleToSupportBelow (stream.Terrain, stream.Statics, SeedSettleMeters);
		}

		motion.RecoverFromPenetration (stream.Terrain, stream.Statics);
		motion.Grounded = true;
		placed = new Vector3 (motion.SpawnX, motion.SpawnY, motion.SpawnZ);
		lastGood[client.EntityID] = placed;
		WritePose (client, character, body, motion);
		return true;
	}

	/// <summary>
	///     Pose out through the Y and Z negation. The node, the record and the wire all stay Godot
	///     space, which is what the client's receive path converts back
	/// </summary>
	private static void WritePose (SphereClient client, Shared.Db.DataModels.CharacterDbEntry character,
		CharacterBody3D body, MotionPlayer motion)
	{
		var pose = ToGodot (new Vector3 (motion.SpawnX, motion.SpawnY, motion.SpawnZ));
		var moved = Math.Abs ((float) character.X - pose.X) > 0.1f
			|| Math.Abs ((float) character.Y - pose.Y) > 0.1f
			|| Math.Abs ((float) character.Z - pose.Z) > 0.1f;
		character.X = pose.X;
		character.Y = pose.Y;
		character.Z = pose.Z;
		character.Angle = body.Rotation.Y;
		if (SavedCoords.TryContinent (-character.Y, -character.Z, out var continent)
			&& character.LastContinent != continent)
		{
			character.LastContinent = continent;
		}
		body.Position = Vector3.Zero;
		if (client.IsInsideTree ())
		{
			body.Position = Vector3.Zero;
			client.GlobalPosition = pose;
		}

		body.Velocity = new Vector3 (motion.VelocityX, -motion.VelocityY, -motion.VelocityZ);
		if (moved)
		{
			client.EnqueueClientEvent (new CurrentClientPositionChangedEvent ());
		}
	}

	// Client ReferenceField: the dynamic objects are gathered out to 64 m once a second or after a
	// 16 m move, and each step places them from their live poses
	private const int DynamicRegatherTicks = 60;
	private const float DynamicGatherMeters = 64f;
	private const float DynamicRegatherMeters = 16f;

	private sealed class DynamicGather
	{
		public int Age = DynamicRegatherTicks;
		public Vector3 Center;
		public readonly List<WorldObject> Objects = [];
	}

	private static readonly Dictionary<Guid, DynamicGather> dynamicGathers = [];

	// Every character model's header box, the kind-0 shape the client gives another player
	private static readonly MotionModelResource PlayerBox = MotionModelResource.AxisAlignedBox ("player",
		new Vector3 (-0.3f, -1.8f, -0.3f), new Vector3 (0.3f, 0f, 0.3f));

	/// <summary>
	///     The mobs, NPCs, chests, doors, portals and other players around this player, as the
	///     client's contact query blocks on them. The client draws a spawned object at the spawn
	///     packet's (x, -y, -z) and yaw Angle * pi / 128, so the same pose places it here
	/// </summary>
	private static void PlaceDynamicObstacles (SphereClient client, MotionWorldStream stream, Vector3 raw)
	{
		var statics = stream.Statics;
		statics.ClearDynamic ();
		if (!dynamicGathers.TryGetValue (client.EntityID, out var gather))
		{
			gather = new DynamicGather ();
			dynamicGathers[client.EntityID] = gather;
		}

		var center = ToGodot (raw);
		if (++gather.Age >= DynamicRegatherTicks
			|| gather.Center.DistanceSquaredTo (center) > DynamicRegatherMeters * DynamicRegatherMeters)
		{
			gather.Age = 0;
			gather.Center = center;
			gather.Objects.Clear ();
			foreach (var obj in ActiveWorldObjects.GetAll ().Values)
			{
				// Players come from ActiveClients; a yellow crystal is never shown to the client
				if (obj is SphereClient || !GodotObject.IsInstanceValid (obj) || !obj.IsInsideTree ()
					|| obj.ObjectType == ObjectType.Light_Crystal_Yellow
					|| obj.GlobalPosition.DistanceSquaredTo (center) > DynamicGatherMeters * DynamicGatherMeters)
				{
					continue;
				}

				gather.Objects.Add (obj);
			}
		}

		foreach (var obj in gather.Objects)
		{
			if (!GodotObject.IsInstanceValid (obj) || !obj.IsInsideTree () || obj is Monster { IsDead: true })
			{
				continue;
			}

			var modelName = obj.ClientModelName;
			if (modelName.Length == 0 || stream.Model (modelName) is not { } resource)
			{
				continue;
			}

			statics.AddDynamic (resource, DynamicWorld (resource, ToRaw (obj.GlobalPosition),
				WorldObject.DecodeAngleToYawRadians (obj.Angle)));
		}

		foreach (var other in ActiveClients.GetAll ().Values)
		{
			if (other.EntityID == client.EntityID || !IsInGame (other))
			{
				continue;
			}

			var otherBody = FindModel (other);
			var position = ToRaw (otherBody?.GlobalPosition ?? other.GlobalPosition);
			if (position.DistanceSquaredTo (raw) > DynamicGatherMeters * DynamicGatherMeters)
			{
				continue;
			}

			statics.AddDynamic (PlayerBox, MotionMatrix.Placement (position, Vector3.Zero));
		}
	}

	// Client updateBounds: a kind-0 box sits at the position with no rotation; the other kinds take
	// fromEuler of the object's yaw, which for a yaw-only pose is the placement matrix
	private static MotionMatrix DynamicWorld (MotionModelResource resource, Vector3 position, float yaw) =>
		resource.DynamicAxisAligned
			? MotionMatrix.Placement (position, Vector3.Zero)
			: MotionMatrix.Placement (position, new Vector3 (0f, yaw, 0f));

	/// <summary>
	///     Holds the body on the near side of the plane and writes HP 0 before the pose is saved,
	///     so a drop in the same moment cannot reload them alive past it
	/// </summary>
	private static void KillByPlane (SphereClient client, Shared.Db.DataModels.CharacterDbEntry character)
	{
		if (character.CurrentHP <= 0)
		{
			return;
		}

		var hp = character.CurrentHP;
		character.CurrentHP = 0;
		client.DisarmUnderwaterBreath ();
		// hit_type 1, second 0: the client reads that as DeathOriginSpecial.Падение (gMsg 751)
		client.MaybeQueueNetworkPacketSend (
			CommonPackets.BuildPlayerReceiveHit (client.EntityID, hitType: 1, hpDelta: -hp, secondDelta: 0,
				flags: 7));
		NetworkedStatsUpdater.Update (character, refreshPeers: false);
		client.BroadcastHpToVisibleClients (-hp, 0);
		client.SchedulePlayerRespawn ();
		client.SaveCharacter ();
	}

	public static float ApproachYaw (float current, float target, float maxStep)
	{
		var delta = Mathf.Wrap (target - current, -Mathf.Pi, Mathf.Pi);
		if (Math.Abs (delta) <= maxStep)
		{
			return current + delta;
		}

		return current + Math.Sign (delta) * maxStep;
	}

	public static void Forget (Guid entityId)
	{
		seeded.Remove (entityId);
		players.Remove (entityId);
		dynamicGathers.Remove (entityId);
		jumpEffects.Remove (entityId);
	}
}
