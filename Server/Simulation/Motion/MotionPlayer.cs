using System;
using Godot;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     Reference FD3D9GameWorldScene::Impl player motion, the Y-down game frame. The fields are the
///     reference's names so a diff against GameWorldMovement.cpp reads straight across
/// </summary>
public sealed class MotionPlayer
{
	public float SpawnX;
	public float SpawnY;
	public float SpawnZ;
	public float SpawnAngle;
	public float CameraYaw;
	public float VelocityX;
	public float VelocityY;
	public float VelocityZ;
	public bool Grounded;
	public bool PlayerCollisionNeedsRecovery;

	// The old client's physical_velocity.x/z for the ground: the slide momentum that accumulates on
	// a slope and bleeds off through friction, added to the wish so a moving player keeps sliding
	public float SlideX;
	public float SlideZ;

	// The blocking contact of the last wish, so the caller's log can name what stopped the step
	public MotionCollisionHit LastHit;
	public bool LastHitValid;

	// Reference SnapToGround: the feet drop onto support under the spawn, if there is any
	public void SnapToGround (MotionTerrainField terrain, MotionStaticField statics, float spawnY)
	{
		var groundY = SpawnY;
		if (SupportHeightAt (terrain, statics, SpawnX, SpawnZ, spawnY, out var ground, out _))
		{
			SpawnY = ground;
			VelocityY = 0f;
			Grounded = true;
		}
	}

	/// <summary>
	///     Seed only: drop the support probe by MovementCollisionStep until the surface under the
	///     pose enters the support window, so a pose recorded above the tile starts on it. The
	///     window reaches 0.45 under the feet, so a seed 3.4 above the tile has no support and the
	///     step's gravity free-falls it, which the caller's pose gate then snaps back
	/// </summary>
	public bool SettleToSupportBelow (MotionTerrainField terrain, MotionStaticField statics, float maxDescentMeters)
	{
		for (var descent = 0f; descent <= maxDescentMeters; descent += MotionConfig.MovementCollisionStep)
		{
			if (!SupportHeightAt (terrain, statics, SpawnX, SpawnZ, SpawnY + descent, out var supportY, out _))
			{
				continue;
			}

			SpawnY = supportY;
			VelocityY = 0f;
			Grounded = true;
			return true;
		}

		return false;
	}

	/// <summary>
	///     Reference SetPlayerWorldPosition, which ends in SnapToGround. False when the pose is not
	///     finite or past 20000, the reference's reject of an off-map store record
	/// </summary>
	public bool SetPlayerWorldPosition (float x, float y, float z, float angle)
	{
		if (!float.IsFinite (x) || !float.IsFinite (y) || !float.IsFinite (z) || !float.IsFinite (angle))
		{
			return false;
		}

		if (Math.Abs (x) > 20000.0 || Math.Abs (y) > 20000.0 || Math.Abs (z) > 20000.0)
		{
			return false;
		}

		SpawnX = x;
		SpawnY = y;
		SpawnZ = z;
		SpawnAngle = angle;
		CameraYaw = -angle;
		VelocityX = 0f;
		VelocityY = 0f;
		VelocityZ = 0f;
		Grounded = false;
		PlayerCollisionNeedsRecovery = true;
		return true;
	}

	/// <summary>
	///     Reference SupportHeightAt: terrain and static floors in the window from MaxStepHeight
	///     below to GroundSnapHeight above the feet, the lowest Y kept, which is the highest surface
	/// </summary>
	public bool SupportHeightAt (MotionTerrainField terrain, MotionStaticField statics,
		float x, float z, float feetY, out float outY, out Vector3 outNormal)
	{
		var minY = feetY - MotionConfig.MaxStepHeight;
		var maxY = feetY + MotionConfig.GroundSnapHeight;
		var found = false;
		var bestY = maxY;
		var bestNormal = new Vector3 (0f, 1f, 0f);

		void ConsiderSupport (float height, Vector3 normal)
		{
			if (found && height >= bestY)
			{
				return;
			}

			found = true;
			bestY = height;
			bestNormal = normal;
		}

		if (terrain.SurfaceNearAt (x, z, feetY, out var terrainY, out var terrainNormal)
			&& terrainY >= minY && terrainY <= maxY)
		{
			ConsiderSupport (terrainY, terrainNormal);
		}

		if (statics.StaticFloorHeightAt (x, z, minY, maxY, out var staticY, out var staticNormal))
		{
			ConsiderSupport (staticY, staticNormal);
		}

		if (!found)
		{
			outY = maxY;
			outNormal = bestNormal;
			return false;
		}

		outY = bestY;
		outNormal = bestNormal;
		return true;
	}

	/// <summary>
	///     Reference Jump
	/// </summary>
	public void Jump ()
	{
		if (Grounded)
		{
			VelocityY = MotionConfig.JumpImpulse;
			Grounded = false;
		}
	}

	/// <summary>
	///     The old client's dampMotion, the slide the live game has: a face steeper than 0.766 pushes
	///     downhill at sine * 4.0 per second, accumulating momentum, and friction bleeds it, so a
	///     steep slope settles at sine*4 / 0.2. A per-step push, as the reference slides, caps at 4.47
	///     and a running player never reaches the speed the live game does
	/// </summary>
	public void AccumulateSlopeSlide (MotionTerrainField terrain, float deltaSeconds)
	{
		if (!Grounded || deltaSeconds <= 0f)
		{
			return;
		}

		if (!terrain.SurfaceNearAt (SpawnX, SpawnZ, SpawnY, out _, out var normal))
		{
			return;
		}

		var normalY = MathF.Abs (normal.Y);
		if (normalY < MotionConfig.SlopeSteepNormalY)
		{
			var sine = MathF.Sqrt (Math.Max (0f, 1f - normalY * normalY));
			var downhill = new Vector2 (normal.X, normal.Z);
			var length = downhill.Length ();
			if (length > 0.0001f)
			{
				// The reference's downhill is -normal over normal.y, which points down the face
				SlideX += -downhill.X / length * (sine * MotionConfig.SlopeAcceleration) * deltaSeconds;
				SlideZ += -downhill.Y / length * (sine * MotionConfig.SlopeAcceleration) * deltaSeconds;
			}
		}

		// Friction, the old client's surface_friction weight, so the accumulation reaches its
		// equilibrium instead of growing without bound
		var speed = MathF.Sqrt (SlideX * SlideX + SlideZ * SlideZ);
		var reduction = deltaSeconds * MotionConfig.SlopeFrictionWeight;
		if (speed > reduction && speed > 0.0001f)
		{
			var remaining = speed - reduction;
			SlideX = SlideX / speed * remaining;
			SlideZ = SlideZ / speed * remaining;
		}
	}

	/// <summary>
	///     Reference ApplySlopeSlide, kept for the reference's own slide at the reference numbers
	/// </summary>
	public void ApplySlopeSlide (MotionTerrainField terrain, MotionStaticField statics, float deltaSeconds)
	{
		if (!Grounded || deltaSeconds <= 0f)
		{
			return;
		}

		if (!terrain.SurfaceNearAt (SpawnX, SpawnZ, SpawnY, out _, out var normal))
		{
			return;
		}

		var normalY = MathF.Abs (normal.Y);
		if (normalY >= MotionConfig.SlopeSlideNormalY || normalY <= 0.0001f)
		{
			return;
		}

		var downhillX = -normal.X / normalY;
		var downhillZ = -normal.Z / normalY;
		var horizontalLength = MathF.Sqrt (downhillX * downhillX + downhillZ * downhillZ);
		if (horizontalLength <= 0.0001f)
		{
			return;
		}

		var slopeStrength = MathF.Sqrt (Math.Max (0f, 1f - normalY * normalY));
		var distance = MotionConfig.JumpGravity * slopeStrength * MotionConfig.SlopeSlideFactor * deltaSeconds;
		TryMoveTo (terrain, statics, SpawnX + downhillX / horizontalLength * distance,
			SpawnZ + downhillZ / horizontalLength * distance);
	}

	/// <summary>
	///     Reference TryMoveTo. A ground move lands on support inside the step/snap window, a rise
	///     needs a clear lift and a probe of the tread, an unsupported move walks off the edge, and
	///     every failure hands back the deepest blocking contact
	/// </summary>
	public bool TryMoveTo (MotionTerrainField terrain, MotionStaticField statics, float x, float z)
	{
		var startX = SpawnX;
		var startY = SpawnY;
		var startZ = SpawnZ;
		var deltaX = x - startX;
		var deltaZ = z - startZ;
		var distanceSquared = deltaX * deltaX + deltaZ * deltaZ;
		var blockingHit = default (MotionCollisionHit);
		var hasBlockingHit = false;

		void RememberHit (MotionCollisionHit hit)
		{
			if (!hasBlockingHit || hit.Penetration > blockingHit.Penetration)
			{
				blockingHit = hit;
				hasBlockingHit = true;
			}
		}

		bool TerrainPenetrates (float feetY, bool ignoreRisingWalkableSupport, out MotionCollisionHit hit)
		{
			hit = default;
			if (!terrain.SurfaceNearAt (x, z, feetY, out var terrainY, out var terrainNormal)
				|| feetY <= terrainY + MotionConfig.CollisionSkin)
			{
				return false;
			}

			var penetration = feetY - terrainY;
			var walkable = MathF.Abs (terrainNormal.Y) >= MotionConfig.CollisionFloorNormalThreshold;
			var risingAllowance = MotionConfig.CollisionSkin
				+ Math.Max (0f, -VelocityY) * MotionConfig.MaxSimulationStepSeconds;
			if (ignoreRisingWalkableSupport && VelocityY < 0f && walkable
				&& penetration <= risingAllowance)
			{
				return false;
			}

			hit.Normal = new Vector3 (terrainNormal.X, 0f, terrainNormal.Z);
			hit.Penetration = penetration;
			return true;
		}

		bool VerticalPathClear (float pathX, float pathZ, float fromY, float toY,
			out MotionCollisionHit hit)
		{
			var distance = MathF.Abs (toY - fromY);
			var stepSize = Math.Max (0.05f,
				Math.Min (MotionConfig.MovementCollisionStep, MotionConfig.PlayerCollisionRadius * 0.5f));
			var stepCount = Math.Max (1, (int) MathF.Ceiling (distance / stepSize));
			for (var step = 1; step <= stepCount; ++step)
			{
				var candidateY = fromY + (toY - fromY) * step / stepCount;
				if (statics.CapsuleOverlapsStatic (pathX, candidateY, pathZ, out hit))
				{
					return false;
				}
			}

			hit = default;
			return true;
		}

		bool CommitGrounded (float feetY)
		{
			SpawnX = x;
			SpawnY = feetY;
			SpawnZ = z;
			VelocityY = 0f;
			Grounded = true;
			return true;
		}

		if (!Grounded)
		{
			if (TerrainPenetrates (startY, true, out var airborneHit)
				|| statics.CapsuleOverlapsStatic (x, startY, z, out airborneHit))
			{
				LastHit = airborneHit;
				LastHitValid = true;
				return false;
			}

			SpawnX = x;
			SpawnZ = z;
			return true;
		}

		var supportY = startY;
		var hasSupport = SupportHeightAt (terrain, statics, x, z, startY, out supportY, out var supportNormal);
		var hasWalkableSupport = hasSupport
			&& MathF.Abs (supportNormal.Y) >= MotionConfig.CollisionFloorNormalThreshold;
		if (hasSupport)
		{
			var rise = startY - supportY;
			if (hasWalkableSupport
				&& rise <= MotionConfig.MaxStepHeight + MotionConfig.CollisionSkin
				&& supportY - startY <= MotionConfig.GroundSnapHeight + MotionConfig.CollisionSkin)
			{
				if (!TerrainPenetrates (supportY, false, out var landHit)
					&& !statics.CapsuleOverlapsStatic (x, supportY, z, out landHit))
				{
					return CommitGrounded (supportY);
				}

				RememberHit (landHit);
			}

			if (!hasWalkableSupport)
			{
				RememberHit (new MotionCollisionHit
				{
					Normal = new Vector3 (supportNormal.X, 0f, supportNormal.Z),
					Penetration = MotionConfig.CollisionSkin,
				});
			}

			if (supportY > startY + MotionConfig.CollisionSkin)
			{
				if (!TerrainPenetrates (startY, false, out var stepDownHit)
					&& !statics.CapsuleOverlapsStatic (x, startY, z, out stepDownHit))
				{
					SpawnX = x;
					SpawnZ = z;
					VelocityY = 0f;
					return true;
				}

				RememberHit (stepDownHit);
			}
		}

		if (distanceSquared > 0.0000001f)
		{
			var distance = MathF.Sqrt (distanceSquared);
			var directionX = deltaX / distance;
			var directionZ = deltaZ / distance;
			var liftY = startY - MotionConfig.MaxStepHeight - MotionConfig.CollisionSkin;
			if (VerticalPathClear (startX, startZ, startY, liftY, out var liftHit)
				&& !statics.CapsuleOverlapsStatic (x, liftY, z, out liftHit))
			{
				var foundStep = false;
				var stepY = startY;
				var bestRise = float.MaxValue;
				var probeRadius = Math.Max (MotionConfig.PlayerCollisionRadius - MotionConfig.CollisionSkin, 0.05f);
				for (var probeIndex = 1; probeIndex <= 4; ++probeIndex)
				{
					var probeDistance = probeRadius * (0.25f + probeIndex * 0.22f);
					var probeX = x + directionX * probeDistance;
					var probeZ = z + directionZ * probeDistance;
					if (!SupportHeightAt (terrain, statics, probeX, probeZ, startY,
							out var candidateY, out var candidateNormal)
						|| MathF.Abs (candidateNormal.Y) < MotionConfig.CollisionFloorNormalThreshold)
					{
						continue;
					}

					var probeRise = startY - candidateY;
					if (probeRise < -MotionConfig.CollisionSkin
						|| probeRise > MotionConfig.MaxStepHeight + MotionConfig.CollisionSkin
						|| probeRise >= bestRise)
					{
						continue;
					}

					if (TerrainPenetrates (candidateY, false, out var stepHit)
						|| statics.CapsuleOverlapsStatic (x, candidateY, z, out stepHit))
					{
						RememberHit (stepHit);
						continue;
					}

					foundStep = true;
					stepY = candidateY;
					bestRise = probeRise;
				}

				if (foundStep)
				{
					return CommitGrounded (stepY);
				}
			}
			else
			{
				RememberHit (liftHit);
			}
		}

		if (!hasSupport)
		{
			if (!TerrainPenetrates (startY, false, out var edgeHit)
				&& !statics.CapsuleOverlapsStatic (x, startY, z, out edgeHit))
			{
				SpawnX = x;
				SpawnZ = z;
				Grounded = false;
				VelocityY = Math.Max (VelocityY, 0f);
				return true;
			}

			RememberHit (edgeHit);
		}

		LastHit = blockingHit;
		LastHitValid = hasBlockingHit;
		return false;
	}

	/// <summary>
	///     Reference UpdateVertical: a grounded body follows support while it is clear, else it
	///     integrates gravity, and a descent that meets a static face stops at the last clear pose
	///     through a six-step bisection
	/// </summary>
	public void UpdateVertical (MotionTerrainField terrain, MotionStaticField statics, float deltaSeconds)
	{
		if (deltaSeconds <= 0f)
		{
			return;
		}

		if (Grounded)
		{
			if (SupportHeightAt (terrain, statics, SpawnX, SpawnZ, SpawnY, out var groundY, out _))
			{
				if (!statics.CapsuleOverlapsStatic (SpawnX, groundY, SpawnZ, out _))
				{
					SpawnY = groundY;
					VelocityY = 0f;
					return;
				}

				if (!statics.CapsuleOverlapsStatic (SpawnX, SpawnY, SpawnZ, out _))
				{
					VelocityY = 0f;
					return;
				}
			}

			Grounded = false;
		}

		var previousY = SpawnY;
		var newVelocityY = VelocityY + MotionConfig.JumpGravity * deltaSeconds;
		// The old client's fall() caps vy at 30 downward, so a long drop stays at the live game's
		// speed. Y is down here, so a fall is positive
		if (newVelocityY > MotionConfig.TerminalFallSpeed)
		{
			newVelocityY = MotionConfig.TerminalFallSpeed;
		}

		var targetY = SpawnY + (VelocityY + newVelocityY) * 0.5f * deltaSeconds;
		var stepSize = Math.Max (0.05f,
			Math.Min (MotionConfig.MovementCollisionStep, MotionConfig.PlayerCollisionRadius * 0.5f));

		if (newVelocityY < 0f)
		{
			var distance = MathF.Abs (targetY - previousY);
			var stepCount = Math.Max (1, (int) MathF.Ceiling (distance / stepSize));
			var safeY = previousY;
			for (var step = 1; step <= stepCount; ++step)
			{
				var candidateY = previousY + (targetY - previousY) * step / stepCount;
				if (!statics.CapsuleOverlapsStatic (SpawnX, candidateY, SpawnZ, out _))
				{
					safeY = candidateY;
					continue;
				}

				var low = safeY;
				var high = candidateY;
				for (var iteration = 0; iteration < 6; ++iteration)
				{
					var middle = (low + high) * 0.5f;
					if (statics.CapsuleOverlapsStatic (SpawnX, middle, SpawnZ, out _))
					{
						high = middle;
					}
					else
					{
						low = middle;
					}
				}

				SpawnY = low;
				VelocityY = 0f;
				return;
			}

			SpawnY = targetY;
			VelocityY = newVelocityY;
			return;
		}

		var foundFloor = false;
		var floorY = targetY;
		if (terrain.SurfaceNearAt (SpawnX, SpawnZ, previousY, out var terrainY, out _)
			&& previousY > terrainY + MotionConfig.CollisionSkin)
		{
			SpawnY = terrainY;
			VelocityY = 0f;
			Grounded = true;
			return;
		}

		var minFloorY = previousY - MotionConfig.CollisionSkin;
		var maxFloorY = targetY + MotionConfig.CollisionSkin;
		if (terrain.SurfaceNearAt (SpawnX, SpawnZ, previousY, out terrainY, out _)
			&& terrainY >= minFloorY && terrainY <= maxFloorY)
		{
			foundFloor = true;
			floorY = terrainY;
		}

		if (statics.StaticFloorHeightAt (SpawnX, SpawnZ, minFloorY, maxFloorY, out var staticY, out _)
			&& (!foundFloor || staticY < floorY))
		{
			foundFloor = true;
			floorY = staticY;
		}

		if (foundFloor && !statics.CapsuleOverlapsStatic (SpawnX, floorY, SpawnZ, out _))
		{
			SpawnY = floorY;
			VelocityY = 0f;
			Grounded = true;
			return;
		}

		var riseDistance = targetY - previousY;
		var riseStepCount = Math.Max (1, (int) MathF.Ceiling (MathF.Abs (riseDistance) / stepSize));
		var riseSafeY = previousY;
		for (var step = 1; step <= riseStepCount; ++step)
		{
			var candidateY = previousY + riseDistance * step / riseStepCount;
			if (!statics.CapsuleOverlapsStatic (SpawnX, candidateY, SpawnZ, out var hit))
			{
				riseSafeY = candidateY;
				continue;
			}

			var low = riseSafeY;
			var high = candidateY;
			for (var iteration = 0; iteration < 6; ++iteration)
			{
				var middle = (low + high) * 0.5f;
				if (statics.CapsuleOverlapsStatic (SpawnX, middle, SpawnZ, out _))
				{
					high = middle;
				}
				else
				{
					low = middle;
				}
			}

			SpawnY = low;
			VelocityY = 0f;
			Grounded = hit.Normal.Y <= -MotionConfig.CollisionFloorNormalThreshold;
			if (Grounded
				&& SupportHeightAt (terrain, statics, SpawnX, SpawnZ, SpawnY, out var contactY, out _)
				&& !statics.CapsuleOverlapsStatic (SpawnX, contactY, SpawnZ, out _))
			{
				SpawnY = contactY;
			}

			return;
		}

		SpawnY = targetY;
		VelocityY = newVelocityY;
	}

	/// <summary>
	///     Reference RecoverFromPenetration: terrain support first, then four pushes along the
	///     deepest contact, then a ring search of five rings by twelve directions
	/// </summary>
	public bool RecoverFromPenetration (MotionTerrainField terrain, MotionStaticField statics)
	{
		if (terrain.SurfaceNearAt (SpawnX, SpawnZ, SpawnY, out var terrainY, out _)
			&& SpawnY > terrainY + MotionConfig.CollisionSkin)
		{
			SpawnY = terrainY;
			VelocityY = 0f;
			Grounded = true;
		}

		for (var iteration = 0; iteration < 4; ++iteration)
		{
			if (!statics.CapsuleOverlapsStatic (SpawnX, SpawnY, SpawnZ, out var hit))
			{
				PlayerCollisionNeedsRecovery = false;
				return true;
			}

			var normalLength = MathF.Sqrt (MotionMath.Dot (hit.Normal, hit.Normal));
			if (normalLength <= 0.0001f)
			{
				break;
			}

			var push = hit.Penetration + MotionConfig.CollisionSkin + 0.001f;
			SpawnX += hit.Normal.X / normalLength * push;
			SpawnY += hit.Normal.Y / normalLength * push;
			SpawnZ += hit.Normal.Z / normalLength * push;
		}

		const int directionCount = 12;
		for (var ring = 1; ring <= 5; ++ring)
		{
			var distance = MotionConfig.PlayerCollisionRadius * 0.5f * ring;
			for (var direction = 0; direction < directionCount; ++direction)
			{
				var angle = direction * (2f * MathF.PI / directionCount);
				var candidateX = SpawnX + MathF.Cos (angle) * distance;
				var candidateZ = SpawnZ + MathF.Sin (angle) * distance;
				var hasSupport = SupportHeightAt (terrain, statics, candidateX, candidateZ, SpawnY,
					out var candidateY, out _);
				if (statics.CapsuleOverlapsStatic (candidateX, candidateY, candidateZ, out _))
				{
					continue;
				}

				SpawnX = candidateX;
				SpawnY = candidateY;
				SpawnZ = candidateZ;
				Grounded = hasSupport;
				PlayerCollisionNeedsRecovery = false;
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     Reference Update, the movement half. The wish is a game-space velocity the client built
	///     from its own camera basis, so it needs no yaw here, and the sub-step count is the larger
	///     of the distance and the time split, so a fast tick cannot step through a wall
	/// </summary>
	public void Update (MotionTerrainField terrain, MotionStaticField statics, Vector2 wish,
		bool jump, float deltaSeconds)
	{
		deltaSeconds = Math.Clamp (deltaSeconds, 0f, Math.Max (0.01f, MotionConfig.MaxMovementDeltaSeconds));
		if (jump)
		{
			Jump ();
		}

		var wishLength = wish.Length ();
		if (wishLength > MotionConfig.WishMaxSpeed)
		{
			wish *= MotionConfig.WishMaxSpeed / wishLength;
			wishLength = wish.Length ();
		}

		var moving = wishLength > 0.0001f && deltaSeconds > 0f;
		var simulationStepSeconds = Math.Max (0.005f, MotionConfig.MaxSimulationStepSeconds);
		if (moving)
		{
			// The wish plus the slide momentum, the old client's commanded + physical
			VelocityX = wish.X + SlideX;
			VelocityZ = wish.Y + SlideZ;
			var displacementX = VelocityX * deltaSeconds;
			var displacementZ = VelocityZ * deltaSeconds;
			var distance = MathF.Sqrt (displacementX * displacementX + displacementZ * displacementZ);
			var collisionStep = Math.Max (0.05f,
				Math.Min (MotionConfig.MovementCollisionStep, MotionConfig.PlayerCollisionRadius * 0.5f));
			var distanceStepCount = Math.Max (1, (int) MathF.Ceiling (distance / collisionStep));
			var timeStepCount = Math.Max (1, (int) MathF.Ceiling (deltaSeconds / simulationStepSeconds));
			var stepCount = Math.Max (distanceStepCount, timeStepCount);
			var stepX = displacementX / stepCount;
			var stepZ = displacementZ / stepCount;
			var stepSeconds = deltaSeconds / stepCount;
			for (var step = 0; step < stepCount; ++step)
			{
				var startX = SpawnX;
				var startZ = SpawnZ;
				var moved = TryMoveTo (terrain, statics, startX + stepX, startZ + stepZ);
				if (!moved)
				{
					var normalX = LastHit.Normal.X;
					var normalZ = LastHit.Normal.Z;
					var normalLength = MathF.Sqrt (normalX * normalX + normalZ * normalZ);
					if (normalLength > 0.0001f)
					{
						normalX /= normalLength;
						normalZ /= normalLength;
						if (normalX * stepX + normalZ * stepZ > 0f)
						{
							normalX = -normalX;
							normalZ = -normalZ;
						}

						var intoSurface = stepX * normalX + stepZ * normalZ;
						var slideX = stepX - normalX * Math.Min (intoSurface, 0f);
						var slideZ = stepZ - normalZ * Math.Min (intoSurface, 0f);
						moved = slideX * slideX + slideZ * slideZ > 0.0000001f
							&& TryMoveTo (terrain, statics, startX + slideX, startZ + slideZ);
					}
				}

				if (!moved)
				{
					var moveXFirst = MathF.Abs (stepX) >= MathF.Abs (stepZ);
					if (moveXFirst)
					{
						moved = TryMoveTo (terrain, statics, startX + stepX, startZ);
						if (moved)
						{
							TryMoveTo (terrain, statics, SpawnX, SpawnZ + stepZ);
						}
						else
						{
							moved = TryMoveTo (terrain, statics, startX, startZ + stepZ);
						}
					}
					else
					{
						moved = TryMoveTo (terrain, statics, startX, startZ + stepZ);
						if (moved)
						{
							TryMoveTo (terrain, statics, SpawnX + stepX, SpawnZ);
						}
						else
						{
							moved = TryMoveTo (terrain, statics, startX + stepX, startZ);
						}
					}
				}

				UpdateVertical (terrain, statics, stepSeconds);
			}

			// The slide grows after the move, the old client's dampMotion at the end of moveGround,
			// so a slope run carries momentum into the next tick
			AccumulateSlopeSlide (terrain, deltaSeconds);
		}
		else
		{
			// Standing on a slope, the old client's moveGround with a zero wish: the displacement is
			// the accumulated slide, and friction bleeds it the same way
			VelocityX = SlideX;
			VelocityZ = SlideZ;
			var slideX = SlideX * deltaSeconds;
			var slideZ = SlideZ * deltaSeconds;
			var slideDistance = MathF.Sqrt (slideX * slideX + slideZ * slideZ);
			var idleCollisionStep = Math.Max (0.05f,
				Math.Min (MotionConfig.MovementCollisionStep, MotionConfig.PlayerCollisionRadius * 0.5f));
			var idleStepCount = Math.Max (1, (int) MathF.Ceiling (slideDistance / idleCollisionStep));
			var idleStepSeconds = deltaSeconds / idleStepCount;
			for (var step = 0; step < idleStepCount; ++step)
			{
				TryMoveTo (terrain, statics, SpawnX + slideX / idleStepCount, SpawnZ + slideZ / idleStepCount);
				UpdateVertical (terrain, statics, idleStepSeconds);
			}

			AccumulateSlopeSlide (terrain, deltaSeconds);
		}
	}

	/// <summary>
	///     Reference RotateView, the yaw the pose reports back. Angle is the game-space heading, the
	///     negation of the camera yaw, as SetPlayerWorldPosition's CameraYaw = -SpawnAngle is
	/// </summary>
	public void RotateView (float yawRadians)
	{
		CameraYaw = yawRadians;
		while (CameraYaw > MathF.PI)
		{
			CameraYaw -= 2f * MathF.PI;
		}

		while (CameraYaw < -MathF.PI)
		{
			CameraYaw += 2f * MathF.PI;
		}

		SpawnAngle = -CameraYaw;
	}
}
