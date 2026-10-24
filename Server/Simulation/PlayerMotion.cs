using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Shared.Godot.Tools;
using SphServer.Shared.Logger;

namespace SphServer.Server.Simulation;

public static class PlayerMotion
{
	// Client walk is member3546_ (2.1) units/sec. moveGround steps that by 1/192.
	// Run is * 2.2, the speed effect is * 1.5. Flat ground material scale is 1.
	public const float MaxSpeed = 2.1f * 1.5f * 2.2f;

	public const float JumpSpeed = 5f;

	public const float Gravity = 9.8f;

	// fall() caps at 30, and the drawn pose is the server pose, so an uncapped vy reads as a jump
	public const float TerminalFallSpeed = 30f;

	public const float YawStepRadians = 4f;

	// char*.mdl origin is the top, feet 1.8 below, and the game anchors at the model center, so
	// feet sit 0.9 under the pose: pose = ground + 0.9, the 1.8 box centered on the pose
	public const float OriginAboveGround = 0.9f;

	// PlaceShapeFeet drops the box bottom by this under the pose, so the box base is the floor line
	private const float FeetBelowOrigin = OriginAboveGround + SweepMargin;

	// Seed pose Y comes from the store, recorded under other rules, so the box can spawn buried. A
	// buried box gets SafeFraction 0, which zeroes the wish, so walking stops: lift it out
	private const float SeedLiftMeters = 4f;

	// BodyTestMotion shrinks the shape by Margin for the sweep, so a decimated wall (thin trimesh)
	// collapses and the sweep passes through it. 0.004 is Godot's own sweep default
	private const float SweepMargin = 0.004f;

	// probeGround: motionNearSupportOffset, motionFarSupportOffset, and the descent search that
	// starts at the box minimum_size and shrinks by 0.25 to 0.03
	private const float NearSupportMeters = 0.03f;

	private const float FarSupportMeters = 0.4f;

	private const float DescentStartMeters = 1.1f;

	private const float DescentRefineMeters = 0.03f;

	// A resting box sits at the sweep margin on the floor, so an embed must clear that plus this
	// skin, which is the reference client's CollisionSkin
	private const float UnstickSkinMeters = 0.02f;

	// GroundBox leaves the base at the last non-contact pose, so a resting box sits at or above the
	// floor line and only a hair below it registers, which keeps ray precision from lifting
	private const float MinSettleClimbMeters = 0.01f;

	// A wall wedged deeper than the push budget is walked out over these pushes in one tick
	private const int UnstickPushes = 4;

	// Fallback when the pushes leave the box wedged: a free pose on rings of this step
	private const float UnstickRingStepMeters = 0.16f;

	private const int UnstickRings = 5;

	private const int UnstickDirections = 12;

	// Feet raised by this clears a tread, so a low step walks over instead of blocking. A taller
	// wall still blocks, because the raised box sits inside it. motionFarSupportOffset is 0.4
	private const float StepUpMeters = 0.4f;

	// The lift ray starts above the box base by this, so the shape margin cannot miss the tread top
	private const float StepProbeAboveMeters = 0.05f;

	// A landing that gains less than this is the floor the box left, not a tread it climbed
	private const float MinStepGainMeters = 0.02f;

	// A decimated trimesh collapses between probes, so each probe covers less than the box width
	private const float SweepStepMeters = 0.1f;

	// SafeFraction is 0 while embedded, which zeroes the wish, so Unstick is the only escape. One
	// push is capped here, and UnstickPushes of them fit a 0.2 m wedge in a tick
	private const float MaxUnstickMeters = 0.05f;

	private const float FloorNormalY = 0.5f;

	// One wish is about 0.115 m at run speed, so two sub-steps, and each slide bounce costs one
	private const int MaxSweepSlides = 4;

	// dampMotion's slope_acceleration is sine * 4.0, so a slope is integrated here and a client
	// that refuses to slide is corrected
	private const float SlopeAcceleration = 4f;

	// dampMotion calls the slope steep below |normal.y| 0.766
	private const float SlopeNormalY = 0.766f;

	// A slope reaches this in under a second, and the wish already carries run speed
	private const float MaxSlopeSlideMetersPerSecond = 3f;

	// The reference client drops a pose past this, so an off-map store seed never seeds the box
	private const float SeedMaxWorldMeters = 20000f;

	public static bool IsInGame (SphereClient client) =>
		GodotObject.IsInstanceValid (client)
		&& client.ClientStateManager.IsInGameState ()
		&& client.CurrentCharacter is not null;

	public static CharacterBody3D? FindModel (SphereClient client) =>
		NodeChildTools.FindFirstChildOfType<CharacterBody3D> (client, "ClientModel");

	// TEMP DIAGNOSTIC - remove after use. One MOTION line per player per second
	public static bool TraceMotion = true;
	private static int MotionTraceTicks;
	private static int YTraceTicks;
	public static Vector3 LastSweepTravel;
	public static Vector3 LastSweepNormal;
	public static float LastSweepDepth;
	public static float LastSweepSafe;
	public static int LastSweepCount;
	public static Vector3 LastSnapTravel;
	public static Vector3 LastUnstick;
	public static string LastBlockers = "";

	private static readonly HashSet<Guid> seeded = [];

	private static readonly Dictionary<Guid, MotionState> motion = new ();

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
		body.CollisionMask = 1;
		PlaceShapeFeet (body);

		if (seeded.Add (client.EntityID))
		{
			// Same vector UpdateCoordinatesInWorld uses. A second negation flips the client
			client.GlobalPosition = character.Origin;
			body.Position = Vector3.Zero;
			var seededRotation = body.Rotation;
			seededRotation.Y = (float) character.Angle;
			body.Rotation = seededRotation;
			client.GlobalPosition = SeedPose (body, client.GlobalPosition);
			motion[client.EntityID] = new MotionState ();
		}

		if (!motion.TryGetValue (client.EntityID, out var state))
		{
			state = new MotionState ();
			motion[client.EntityID] = state;
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
		client.ApplyVisualYaw (yaw);

		var dt = (float) SimulationClock.SecondsPerTick;
		var vy = state.VerticalSpeed;
		if (state.OnFloor)
		{
			vy = (slot.Buttons & PlayerCommand.Jump) != 0 ? JumpSpeed : 0f;
		}
		else
		{
			vy = Mathf.Max (vy - Gravity * dt, -TerminalFallSpeed);
		}

		var step = new Vector3 (wish.X, vy, -wish.Y) * dt;
		var probe = client.GlobalPosition;
		SphServer.Server.SphereServer.ServerNode?.TerrainGround?.EnsureAroundWorldPosition (probe);
		// The player's own cell loads past the budget, so a spawn inside a wall is caught on tick 1
		SphServer.Server.SphereServer.ServerNode?.ObjectCollision?.EnsureAtWorldPosition (probe);

		body.Position = Vector3.Zero;
		var start = client.GlobalPosition;
		var free = Unstick (body, start, state.OnFloor, vy);
		LastUnstick = free - start;
		var placed = Sweep (body, free, step, out _, out var hitCeiling);
		// StepUp sweeps again and overwrites these, so the blocking contact of this tick's wish is
		// kept here, where the log can name it
		var traceTravel = LastSweepTravel;
		var traceNormal = LastSweepNormal;
		var traceDepth = LastSweepDepth;
		var traceSafe = LastSweepSafe;
		var traceCount = LastSweepCount;

		// A tread higher than the sweep's reach blocks, so a lift over it is tried before the tick
		// commits. A real wall blocks the lifted sweep too, so it gains nothing
		if (state.OnFloor)
		{
			placed = StepUp (body, free, placed, step);
		}

		// probeGround owns the floor: near support 0.03, far support 0.4, then a descent search.
		// A settle probe under its own margin has no resolution, which pumped, sank and buried
		var onFloor = vy <= 0f && GroundBox (body, placed, out placed);

		if (onFloor)
		{
			// A base under the floor line is a measured burial, so the pose climbs onto the surface
			placed = SettleToFloor (body, placed);

			// A slope is integrated here, so a client that refuses to slide is corrected to the same
			// downhill motion every client reads
			placed = SlopeSlide (body, placed, dt);
		}

		if (hitCeiling && vy > 0f)
		{
			vy = 0f;
		}

		if (onFloor && vy <= 0f)
		{
			vy = 0f;
		}

		state.VerticalSpeed = vy;
		state.OnFloor = onFloor;
		body.Velocity = new Vector3 (wish.X, vy, -wish.Y);
		if (client.IsInsideTree ())
		{
			body.Position = Vector3.Zero;
			client.GlobalPosition = placed;
		}

		// TEMP DIAGNOSTIC - remove after use. Every tick Y moves, so the bob is measured, not
		// guessed. dy above a millimetre, at most 30 lines/sec. bodyY is the physics body's own
		// pose, which gravity integrates between our writes
		if (TraceMotion && Mathf.Abs (placed.Y - probe.Y) > 0.001f && YTraceTicks++ % 2 == 0)
		{
			SphLogger.Info (
				$"BOB {client.EntityID:N}: dy={(placed.Y - probe.Y):0.0000} vy={vy:0.00} "
				+ $"floor={onFloor} onFloor={state.OnFloor} hits={traceCount} depth={traceDepth:0.000} "
				+ $"unstickY={LastUnstick.Y:0.0000} wish=({wish.X:0.0},{wish.Y:0.0}) "
				+ $"bodyPos={body.Position.Y:0.0000} bodyY={body.GlobalPosition.Y:0.0000} "
				+ $"velY={body.Velocity.Y:0.00} "
				+ $"freeY={free.Y:0.0000} stepY={step.Y:0.0000}");
		}

		// TEMP DIAGNOSTIC - remove after use. probe/placed/Origin all printed so a second writer
		// moving the node shows up. d is the tick delta
		if (TraceMotion && MotionTraceTicks++ % SimulationClock.TickHz == 0)
		{
			LastBlockers = DescribeBlockers (body);
			// The floor line under the box, measured, so the box base and the visual low point can
			// be read against it: gap is how far the box base sits under the surface
			var traceGroundY = SampleGroundY (body, placed);
			SphLogger.Info (
				$"MOTION {client.EntityID:N}: wish=({wish.X:0.00},{wish.Y:0.00}) vy={vy:0.00} "
				+ $"seq={slot.LastApplied} d=({placed.X - probe.X:0.000},{placed.Y - probe.Y:0.000},"
				+ $"{placed.Z - probe.Z:0.000}) floor={onFloor} ceil={hitCeiling} onFloor={state.OnFloor} "
				+ $"cells={SphServer.Server.SphereServer.ServerNode?.ObjectCollision?.LoadedCellCount ?? -1} "
				+ $"trav=({traceTravel.X:0.000},{traceTravel.Y:0.000},{traceTravel.Z:0.000}) "
				+ $"snap=({LastSnapTravel.X:0.000},{LastSnapTravel.Y:0.000},{LastSnapTravel.Z:0.000}) "
				+ $"hits={traceCount} depth={traceDepth:0.000} safe={traceSafe:0.000} "
				+ $"normal=({traceNormal.X:0.00},{traceNormal.Y:0.00},{traceNormal.Z:0.00}) "
				+ $"feetY={placed.Y - FeetBelowOrigin:0.000} ground={traceGroundY:0.000}"
				+ $" gap={(float.IsFinite (traceGroundY) ? placed.Y - FeetBelowOrigin - traceGroundY : float.NaN):0.000} "
				+ $"unstick=({LastUnstick.X:0.000},{LastUnstick.Y:0.000},{LastUnstick.Z:0.000}) "
				+ $"blockers={LastBlockers} "
				+ $"probe=({probe.X:0.00},{probe.Y:0.00},{probe.Z:0.00}) "
				+ $"placed=({placed.X:0.00},{placed.Y:0.00},{placed.Z:0.00}) "
				+ $"node=({client.GlobalPosition.X:0.00},{client.GlobalPosition.Y:0.00},{client.GlobalPosition.Z:0.00}) "
				+ $"origin=({character.Origin.X:0.00},{character.Origin.Y:0.00},{character.Origin.Z:0.00}) "
				+ $"xyz=({character.X:0.00},{character.Y:0.00},{character.Z:0.00})");
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

	// Bottom of the box sits OriginAboveGround under the body, plus the sweep margin
	private static void PlaceShapeFeet (CharacterBody3D body)
	{
		if (body.GetNodeOrNull<CollisionShape3D> ("ClientShape") is not { Shape: BoxShape3D box } shape)
		{
			return;
		}

		var lift = box.Size.Y * 0.5f - OriginAboveGround - SweepMargin;
		shape.Position = new Vector3 (0f, lift, 0f);
	}

	// BodyTestMotion returns the hit normal for the capsule sweep. A contact spends the wish along
	// its tangent, so a wall stops the box and the rest of the wish walks it along the face
	private static Vector3 Sweep (CharacterBody3D body, Vector3 position, Vector3 motion, out bool hitFloor,
		out bool hitCeiling)
	{
		hitFloor = false;
		hitCeiling = false;
		LastSweepTravel = Vector3.Zero;
		LastSweepNormal = Vector3.Zero;
		LastSweepDepth = 0f;
		LastSweepSafe = 1f;
		LastSweepCount = 0;
		if (body.GetWorld3D () is null)
		{
			return position + motion;
		}

		// GetTravel includes the de-penetration push (up to max_recovery_speed), so an overlapped
		// capsule lands 0.7 m away with wish 0. SafeFraction times the wish is motion only
		var parameters = new PhysicsTestMotionParameters3D
		{
			Margin = SweepMargin,
			RecoveryAsCollision = false,
		};
		var result = new PhysicsTestMotionResult3D ();
		var slides = 0;
		var remaining = motion;
		while (remaining.LengthSquared () >= 1e-10f)
		{
			parameters.From = new Transform3D (body.GlobalBasis, position);
			parameters.Motion = remaining.LimitLength (SweepStepMeters);
			if (!PhysicsServer3D.BodyTestMotion (body.GetRid (), parameters, result)
				|| result.GetCollisionCount () == 0)
			{
				position += parameters.Motion;
				remaining -= parameters.Motion;
				continue;
			}

			LastSweepTravel = result.GetTravel ();
			LastSweepNormal = result.GetCollisionNormal ();
			LastSweepDepth = result.GetCollisionDepth (0);
			LastSweepCount = result.GetCollisionCount ();
			var safeFraction = result.GetCollisionSafeFraction ();
			LastSweepSafe = safeFraction;
			position += parameters.Motion * safeFraction;
			var normal = result.GetCollisionNormal ();
			if (normal.Y > FloorNormalY)
			{
				hitFloor = true;
			}
			else if (normal.Y < -FloorNormalY)
			{
				hitCeiling = true;
			}

			var slid = result.GetRemainder ().Slide (normal);
			// A contact gets SafeFraction 0, and sliding from the same pose re-probes the same
			// contact, so the wish is spent on it and the box never moves. The slide is tangent to
			// the contact, so a sub-step along it cannot deepen it, and the next probe reads the
			// corner there. Both floor and wall advance this way, which is how a wish along a wall
			// keeps run speed instead of freezing at the corner
			if (safeFraction <= 0f)
			{
				var slidStep = slid.LimitLength (SweepStepMeters);
				if (slidStep.LengthSquared () <= 1e-10f)
				{
					return position;
				}

				position += slidStep;
				remaining = slid - slidStep;
				continue;
			}

			// A partial hit spends part of the wish, so the slide restarts the probe at the new pose
			if (slides >= MaxSweepSlides)
			{
				return position;
			}

			remaining = slid;
			slides++;
		}

		return position;
	}

	/// <summary>
	/// A lift is only tried when the plain sweep ran short of the wish. The lifted pose floats the
	/// box over the tread, and the landing comes from a ray under the feet: the box volume spans the
	/// whole 1.8 height, so it reads the floor a tread stands on and buries the box in it
	/// </summary>
	private static Vector3 StepUp (CharacterBody3D body, Vector3 floor, Vector3 placed, Vector3 step)
	{
		var wanted = new Vector2 (step.X, step.Z).Length ();
		var ran = new Vector2 (placed.X - floor.X, placed.Z - floor.Z).Length ();
		if (wanted - ran <= SweepMargin)
		{
			return placed;
		}

		var lifted = floor + Vector3.Up * (StepUpMeters + SweepMargin);
		var over = Sweep (body, lifted, step, out _, out _);
		var climbed = new Vector2 (over.X - lifted.X, over.Z - lifted.Z).Length ();
		// A tread taller than the lift, a real wall, stops the lifted sweep too, so nothing is won
		if (wanted - climbed > SweepMargin)
		{
			return placed;
		}

		// The box base rests on the surface under it, so a lift over a wall lands back on the floor
		// it left and the gain test below rejects it
		var surface = SampleSurfaceUnderFeet (body, over);
		if (!float.IsFinite (surface))
		{
			return placed;
		}

		var landed = over;
		landed.Y = surface + FeetBelowOrigin;
		if (landed.Y - floor.Y < MinStepGainMeters)
		{
			return placed;
		}

		return landed;
	}

	/// <summary>
	/// probeGround's floor rule: near support 0.03 is grounded with no move, no support within 0.4
	/// is airborne, and a gap runs a descent search (0.25 steps to 0.03) that leaves the box just
	/// above the surface. A settle probe under the sweep margin has no resolution: it pumped, sank
	/// and buried
	/// </summary>
	private static bool GroundBox (CharacterBody3D body, Vector3 position, out Vector3 landed)
	{
		landed = position;
		if (HasContact (body, position + Vector3.Up * NearSupportMeters))
		{
			return true;
		}

		if (!HasContact (body, position + Vector3.Up * FarSupportMeters))
		{
			return false;
		}

		var descent = DescentStartMeters;
		var pos = position;
		var floorY = position.Y - DescentStartMeters;
		while (descent >= DescentRefineMeters && pos.Y > floorY)
		{
			descent *= 0.25f;
			float previousY;
			do
			{
				previousY = pos.Y;
				pos.Y -= descent;
			}
			while (!HasContact (body, pos) && pos.Y > floorY);

			pos.Y = previousY;
		}

		landed = pos;
		return true;
	}

	/// <summary>
	/// The layer-1 surface under a lifted box base, by ray from just above the base down to the base
	/// it left. A ray, not the box volume: the volume spans the 1.8 height and reads the floor a
	/// tread stands on
	/// </summary>
	private static float SampleSurfaceUnderFeet (CharacterBody3D body, Vector3 lifted)
	{
		if (body.GetWorld3D ()?.DirectSpaceState is not { } space)
		{
			return float.PositiveInfinity;
		}

		var baseY = lifted.Y - FeetBelowOrigin;
		var from = new Vector3 (lifted.X, baseY + StepProbeAboveMeters, lifted.Z);
		var to = from - Vector3.Up * (StepUpMeters + 2 * SweepMargin + StepProbeAboveMeters);
		var query = PhysicsRayQueryParameters3D.Create (from, to);
		query.CollisionMask = 1;
		var hit = space.IntersectRay (query);
		return hit.Count > 0 ? hit["position"].AsVector3 ().Y : float.PositiveInfinity;
	}

	/// <summary>
	/// The layer-1 surface above a box base, by ray up from just under the base. The ray reports the
	/// closest hit, so the floor is the line, and a tabletop over a standing box is not
	/// </summary>
	private static float SampleFloorLineY (CharacterBody3D body, Vector3 position, float baseY)
	{
		if (body.GetWorld3D ()?.DirectSpaceState is not { } space)
		{
			return float.PositiveInfinity;
		}

		var from = new Vector3 (position.X, baseY - DescentRefineMeters, position.Z);
		var to = from + Vector3.Up * (DescentRefineMeters + DescentStartMeters);
		var query = PhysicsRayQueryParameters3D.Create (from, to);
		query.CollisionMask = 1;
		var hit = space.IntersectRay (query);
		return hit.Count > 0 ? hit["position"].AsVector3 ().Y : float.PositiveInfinity;
	}

	/// <summary>
	/// Lands a box whose base sits under the floor line. GroundBox reads it as grounded, and the
	/// sweep runs RecoveryAsCollision off, so nothing lifts a seed sunk into the floor
	/// </summary>
	private static Vector3 SettleToFloor (CharacterBody3D body, Vector3 position)
	{
		var baseY = position.Y - FeetBelowOrigin;
		var floorY = SampleFloorLineY (body, position, baseY);
		// The target base is the floor line plus the sweep margin, where GroundBox's descent leaves a
		// box that has just entered contact, so a resting box is never lifted and the bob stays gone
		var climb = floorY + SweepMargin - baseY;
		if (!float.IsFinite (climb) || climb < MinSettleClimbMeters || climb > DescentStartMeters)
		{
			return position;
		}

		return position + Vector3.Up * climb;
	}

	/// <summary>
	/// The layer-1 surface under a pose, by ray. The log compares this to the box base, so a foot
	/// clip is a measured number and not a guess about the tile
	/// </summary>
	private static float SampleGroundY (CharacterBody3D body, Vector3 position)
	{
		if (body.GetWorld3D ()?.DirectSpaceState is not { } space)
		{
			return float.PositiveInfinity;
		}

		var from = position + Vector3.Up * FarSupportMeters;
		var query = PhysicsRayQueryParameters3D.Create (from, from - Vector3.Up * (FarSupportMeters + DescentStartMeters));
		query.CollisionMask = 1;
		var hit = space.IntersectRay (query);
		return hit.Count > 0 ? hit["position"].AsVector3 ().Y : float.PositiveInfinity;
	}

	// The box shape at a candidate pose overlaps any layer-1 shape
	private static bool HasContact (CharacterBody3D body, Vector3 position)
	{
		if (body.GetWorld3D ()?.DirectSpaceState is not { } space
			|| body.GetNodeOrNull<CollisionShape3D> ("ClientShape") is not { Shape: { } shape } collision)
		{
			return false;
		}

		var placed = new Transform3D (body.GlobalBasis, position) * collision.Transform;
		return space.IntersectShape (
			new PhysicsShapeQueryParameters3D
			{
				Shape = shape,
				Transform = placed,
				Margin = SweepMargin,
				CollisionMask = 1,
				CollideWithBodies = true,
				Exclude = [body.GetRid ()],
			},
			1).Count > 0;
	}

	/// <summary>
	/// The zero-motion recovery probe: GetTravel is the de-penetration vector Godot wants, the one
	/// that flew the player before, and GetCollisionNormal is the floor normal under a resting box
	/// </summary>
	private static bool RecoveryContact (CharacterBody3D body, Vector3 position, out Vector3 travel,
		out Vector3 normal, out float depth)
	{
		travel = normal = Vector3.Zero;
		depth = 0f;
		if (body.GetWorld3D () is null)
		{
			return false;
		}

		var parameters = new PhysicsTestMotionParameters3D
		{
			From = new Transform3D (body.GlobalBasis, position),
			Motion = Vector3.Zero,
			Margin = SweepMargin,
			RecoveryAsCollision = true,
		};
		var result = new PhysicsTestMotionResult3D ();
		if (!PhysicsServer3D.BodyTestMotion (body.GetRid (), parameters, result)
			|| result.GetCollisionCount () == 0)
		{
			return false;
		}

		travel = result.GetTravel ();
		normal = result.GetCollisionNormal ();
		depth = result.GetCollisionDepth (0);
		return true;
	}

	/// <summary>
	/// Pushes the box out of overlapping shapes over at most UnstickPushes probes, each capped at
	/// MaxUnstickMeters. An embedded body gets SafeFraction 0, which zeroes the wish, so this runs
	/// before the sweep
	/// </summary>
	private static Vector3 Unstick (CharacterBody3D body, Vector3 position, bool onFloor, float vy)
	{
		// A resting box sits at margin depth on the ground, and pushing out of that every tick is
		// the bob. A landing drives the box in by -vy * dt, which is not a wedge to escape either
		var allowance = SweepMargin + UnstickSkinMeters + Mathf.Max (0f, -vy) * SimulationClock.SecondsPerTickF;
		for (var push = 0; push < UnstickPushes; push++)
		{
			if (!RecoveryContact (body, position, out var travel, out var normal, out var depth)
				|| depth < allowance)
			{
				return position;
			}

			// A sloped floor pushes out along its normal, so the horizontal part walks the player
			// downhill. Floor contacts push up only; walls keep the full push
			travel = travel.LimitLength (MaxUnstickMeters);
			if (normal.Y > FloorNormalY)
			{
				if (onFloor)
				{
					return position;
				}

				travel = new Vector3 (0f, travel.Y, 0f);
			}

			position += travel;
		}

		// Wedged deeper than the push budget, the pushes walk the box along the wall face, so a ring
		// search is the way out
		return FindFreePose (body, position, out var free) ? free : position;
	}

	/// <summary>
	/// The first free pose on widening rings around a wedged pose, its Y from GroundBox, so a
	/// candidate rests on the surface under it
	/// </summary>
	private static bool FindFreePose (CharacterBody3D body, Vector3 position, out Vector3 free)
	{
		for (var ring = 1; ring <= UnstickRings; ring++)
		{
			var distance = UnstickRingStepMeters * ring;
			for (var direction = 0; direction < UnstickDirections; direction++)
			{
				var angle = direction * Mathf.Tau / UnstickDirections;
				var candidate = position + new Vector3 (Mathf.Cos (angle) * distance, 0f, Mathf.Sin (angle) * distance);
				// GroundBox leaves landed at the candidate when it has no support, so one value covers
				// a resting and an airborne free pose
				GroundBox (body, candidate, out var landed);
				if (!HasContact (body, landed))
				{
					free = landed;
					return true;
				}
			}
		}

		free = position;
		return false;
	}

	/// <summary>
	/// dampMotion's downhill term, sine * 4.0 along the support normal's XZ, integrated for the tick.
	/// The server owns movement, so a client that refuses to slide is corrected, and every client
	/// reads the same slope motion
	/// </summary>
	private static Vector3 SlopeSlide (CharacterBody3D body, Vector3 position, float dt)
	{
		if (!RecoveryContact (body, position, out _, out var normal, out _)
			|| Mathf.Abs (normal.Y) >= SlopeNormalY)
		{
			return position;
		}

		var downhill = new Vector3 (normal.X, 0f, normal.Z);
		if (downhill.LengthSquared () <= 1e-6f)
		{
			return position;
		}

		var sine = Mathf.Sqrt (Mathf.Max (0f, 1f - normal.Y * normal.Y));
		var speed = Mathf.Min (sine * SlopeAcceleration, MaxSlopeSlideMetersPerSecond);
		// The slide goes through the sweep, so a wall at the bottom of the slope stops it
		return Sweep (body, position, downhill.Normalized () * (speed * dt), out _, out _);
	}

	/// <summary>
	/// A seed pose is the stored Origin, recorded under other rules and possibly off-map, so it is
	/// range-checked, then lifted, dropped, and walked out of a wedge in one step, which the
	/// per-tick push cannot do at MaxUnstickMeters
	/// </summary>
	private static Vector3 SeedPose (CharacterBody3D body, Vector3 position)
	{
		// A NaN, infinite, or off-map store record seeds the box in the void, where nothing recovers
		if (!position.IsFinite ()
			|| Mathf.Abs (position.X) > SeedMaxWorldMeters
			|| Mathf.Abs (position.Y) > SeedMaxWorldMeters
			|| Mathf.Abs (position.Z) > SeedMaxWorldMeters)
		{
			SphLogger.Info ($"SEED {body.GetPath ()}: off-map pose {position}, kept");
			return position;
		}

		if (body.GetWorld3D () is null)
		{
			return position;
		}

		if (!HasContact (body, position))
		{
			// A free seed falls to the surface under it, so a record stored in the air lands
			GroundBox (body, position, out var landed);
			return landed;
		}

		// One recovery probe returns the whole penetration, so a buried box is lifted in a step
		var lifted = position;
		if (RecoveryContact (body, position, out var travel, out _, out _) && travel.Y > 0f)
		{
			lifted = position + new Vector3 (0f, Mathf.Min (travel.Y, SeedLiftMeters), 0f);
		}

		GroundBox (body, lifted, out var settled);
		if (!HasContact (body, settled))
		{
			return settled;
		}

		// Y only lifts a box buried in the ground. A box inside a wall has no upward way out, so the
		// ring gives the spawn a standing pose beside it
		return FindFreePose (body, settled, out var free) ? free : settled;
	}

	/// <summary>
	/// Layer-1 shapes overlapping the capsule now, named by node path and cell, so an embedded
	/// player shows which baked part holds them. IntersectShape is the only query with collider
	/// identity; depth and normal come from BodyTestMotion
	/// </summary>
	private static string DescribeBlockers (CharacterBody3D body)
	{
		if (body.GetWorld3D ()?.DirectSpaceState is not { } space
			|| body.GetNodeOrNull<CollisionShape3D> ("ClientShape") is not { Shape: { } shape } collision)
		{
			return "none";
		}

		var placed = new Transform3D (body.GlobalBasis, body.GlobalPosition) * collision.Transform;
		var hits = space.IntersectShape (
			new PhysicsShapeQueryParameters3D
			{
				Shape = shape,
				Transform = placed,
				CollisionMask = 1,
				CollideWithBodies = true,
				Exclude = [body.GetRid ()],
			},
			8);
		if (hits.Count == 0)
		{
			return "0";
		}

		var names = new List<string> (hits.Count);
		foreach (var hit in hits)
		{
			var name = "shape";
			if (hit["collider"].AsGodotObject () is Node node)
			{
				name = node.GetPath ().ToString ();
			}

			names.Add ($"{name}#{hit["shape"].AsInt32 ()}");
		}

		return $"{hits.Count}:{string.Join ("|", names)}";
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

	private sealed class MotionState
	{
		public float VerticalSpeed;
		public bool OnFloor;
	}
}
