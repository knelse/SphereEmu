using System;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     The reference FGameWorldConfig numbers that drive the solver, one set for server and client.
///     They are the reference's, not ours, so both sides integrate the same world
/// </summary>
public static class MotionConfig
{
	public const float TileSize = 100f;

	// Client roomIndex and advanceTeleport: past game y 1000 are the dungeon rooms, under the
	// FILL_EMPT filler tiles, so terrain there is a surface thousands of metres above the room
	public const float DungeonDepth = 1000f;
	public const int OriginRow = 40;
	public const int OriginColumn = 39;
	public const int VisibleRadius = 3;
	public const float StaticObjectRadius = 250f;

	// 2.1 is the client's own walkingSpeed, and 6.93 is that with the speed buff (1.5) and run (2.2)
	// stacked, so a wish at full run under a buff passes the clamp whole
	public const float WalkSpeed = 2.1f;
	public const float RunMultiplier = 2.2f;
	public const float WishMaxSpeed = 2.1f * 1.5f * 2.2f;

	// The client's own fall() caps vy at 30
	public const float TerminalFallSpeed = 30f;

	public const float PlayerCollisionRadius = 0.32f;
	public const float PlayerCollisionHeight = 1.8f;

	public const float MaxStepHeight = 0.6f;
	public const float GroundSnapHeight = 0.45f;
	public const float MovementCollisionStep = 0.12f;
	public const float MaxMovementDeltaSeconds = 0.10f;
	public const float MaxSimulationStepSeconds = 0.0333333f;
	public const float CollisionSkin = 0.025f;
	public const float CollisionFloorNormalThreshold = 0.65f;

	public const float SlopeSlideNormalY = 0.65f;
	public const float SlopeSlideFactor = 0.6f;

	// The old client's dampMotion: a face steeper than 0.766 pushes downhill at sine * 4.0 per
	// second, and friction bleeds the momentum, so the slide settles at sine*4 / friction
	public const float SlopeSteepNormalY = 0.766f;
	public const float SlopeAcceleration = 4f;

	// The old client's surface_friction, indexed by the supporting surface's direction: terrain
	// and faces are 0, a kind-2 triangle names its own (1 is the slick one)
	public static readonly float[] SurfaceFriction = [2.0f, 0.2f, 0.75f, 1.2f, 2.5f, 3.0f, 4.0f];

	// The old client's surface material when the body touches no material triangle. Movement scales
	// by material / 6, so a canopy or bush (3) halves it
	public const int NoSurfaceMaterial = 6;

	// The old client's fall() landing: the slide keeps motionDamping of itself and gains the
	// impact speed along the face, |n.y| * speed * downhill * 0.6
	public const float SlopeDamping = 0.4f;
	public const float LandingSlideFactor = 0.6f;

	// Y is down, so the jump impulse is negative and gravity positive, as the reference has them.
	// The client's MBC jump takes -10 under jump x2 (effect 5) and -20 under jump x4 (effect 6)
	public const float JumpImpulse = -5.0f;
	public const float JumpImpulseX2 = -10.0f;
	public const float JumpImpulseX4 = -20.0f;
	public const float JumpGravity = 9.8f;

	// kStaticCollisionCellSize, the instance grid the static queries walk
	public const float StaticCollisionCellSize = 8f;

	// A .mdl triangle is collision only when this flag is clear
	public const ushort NonCollisionTriangleFlag = 0x100;

	// A skinned model has no triangle collision, so it blocks on a cylinder of this radius
	public static float SkinnedProxyRadius (float width, float depth) =>
		Math.Clamp (
			Math.Max (width, depth) * 0.18f,
			0.24f,
			PlayerCollisionRadius * 1.15f);
}
