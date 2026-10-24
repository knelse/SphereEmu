using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Godot.Scripts.World;
using SphServer.Server.Simulation.Motion;
using SphServer.Shared.Godot.Tools;

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
			// A seed pose recorded under the old rules can be in the air, on a slope, or inside a
			// wall, so drop it, walk it out, and settle it, all with the geometry loaded for it
			stream.EnsureAroundWorldPosition (game);
			var supportFound = player.SupportHeightAt (stream.Terrain, stream.Statics, player.SpawnX,
				player.SpawnZ, player.SpawnY, out var seedSupportY, out _);
			// One line per spawn, and it names the frame the solver streams in against the store
			// record: support True and supportY near spawnY means the record lands on a raw .lnd
			// tile; support False, or a Y hundreds of metres off, means the boundary is wrong
			SphServer.Shared.Logger.SphLogger.Info (
				$"SEED {client.EntityID:N}: origin=({character.X:0.0},{character.Y:0.0},{character.Z:0.0}) "
				+ $"raw=({game.X:0.0},{game.Y:0.0},{game.Z:0.0}) "
				+ $"node=({client.GlobalPosition.X:0.0},{client.GlobalPosition.Y:0.0},{client.GlobalPosition.Z:0.0}) "
				+ $"support={supportFound} supportY={seedSupportY:0.0} spawnY={player.SpawnY:0.0} "
				+ $"tiles={stream.LoadedTileCount} instances={stream.LoadedInstanceCount}");
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
		motion.Update (stream.Terrain, stream.Statics, new Vector2 (wishX, wishZ), jump,
			SimulationClock.SecondsPerTickF);

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
			SphServer.Shared.Logger.SphLogger.Info (
				$"FALLRESCUE {client.EntityID:N}: past {FallOutWorldMeters} to {step.Y:0.0}, "
				+ $"restored ({motion.SpawnX:0.0},{motion.SpawnY:0.0},{motion.SpawnZ:0.0})");
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
	}
}
