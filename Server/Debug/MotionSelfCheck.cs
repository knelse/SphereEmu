using System;
using Godot;
using SphServer.Server.Simulation.Motion;
using SphServer.Shared.Logger;

namespace SphServer.Server.Debug;

/// <summary>
///     Runs the ported solver on real .lnd/.mdl geometry and prints what it measures, so a
///     regression in support, step-up, slope slide, or recovery is a number and not a feel.
///     Headless: <c>dotnet run --project SphServer.csproj -- --motion-check</c> via Godot's script
///     mode, run from the editor output. It never runs in a normal server boot
/// </summary>
public static class MotionSelfCheck
{
	/// <summary>
	///     Empty terrain and statics, so only the integrator runs. Gravity from rest for one second
	///     must give y = g/2 = 4.90 and vy = g = 9.80, with +Y down. A sign slip shows as a negative
	///     y
	/// </summary>
	private static void GravityInFreeSpace ()
	{
		var terrain = new MotionTerrainField ();
		var statics = new MotionStaticField ();
		var player = new MotionPlayer ();
		player.SetPlayerWorldPosition (0f, 0f, 0f, 0f);
		for (var tick = 0; tick < 60; ++tick)
		{
			player.Update (terrain, statics, Vector2.Zero, false, 1f / 60f);
		}

		SphLogger.Info (
			$"MOTIONCHECK freefall: y={player.SpawnY:0.000} vy={player.VelocityY:0.000} "
			+ $"grounded={player.Grounded} (expect y=4.90 vy=9.80)");
	}

	/// <summary>
	///     A jump in empty space: impulse -5 against gravity 9.8 peaks 1.2755 above the start, the
	///     reference's own numbers. Apex is the lowest y, and +Y is down
	/// </summary>
	private static void JumpArcInFreeSpace ()
	{
		var terrain = new MotionTerrainField ();
		var statics = new MotionStaticField ();
		var player = new MotionPlayer ();
		player.SetPlayerWorldPosition (0f, 0f, 0f, 0f);
		player.Grounded = true;
		var apex = 0f;
		for (var tick = 0; tick < 120; ++tick)
		{
			player.Update (terrain, statics, Vector2.Zero, tick == 0, 1f / 60f);
			apex = Math.Min (apex, player.SpawnY);
		}

		SphLogger.Info (
			$"MOTIONCHECK freejump: apex={-apex:0.000} above start y={player.SpawnY:0.000} "
			+ $"vy={player.VelocityY:0.000} (expect apex 1.28)");
	}

	public static void Run (MotionWorldStream stream)
	{
		GravityInFreeSpace ();
		JumpArcInFreeSpace ();

		var terrain = stream.Terrain;
		var statics = stream.Statics;

		// A tile center in a dense region, and one over water-free highland
		// Y is a placeholder, the surface under XZ is measured below, since a resting body sits on
		// the surface line and the support window is only MaxStepHeight to GroundSnapHeight wide
		var spots = new (string Label, Vector3 Game)[]
		{
			("town", new Vector3 (450f, 0f, 1550f)),
			("highland", new Vector3 (-1250f, 0f, 1550f)),
			("edge", new Vector3 (3950f, 0f, 3950f)),
		};

		foreach (var (label, spot) in spots)
		{
			stream.EnsureAroundWorldPosition (spot);
			terrain.HeightAt (spot.X, spot.Z, 0f, out var surfaceY);
			// 0.1 above the surface, in this frame, keeps the surface inside the support window
			var game = new Vector3 (spot.X, surfaceY - 0.1f, spot.Z);
			var player = new MotionPlayer ();
			player.SetPlayerWorldPosition (game.X, game.Y, game.Z, 0f);
			var supported = player.SupportHeightAt (terrain, statics, game.X, game.Z, game.Y,
				out var supportY, out var supportNormal);
			SphLogger.Info (
				$"MOTIONCHECK {label}: at=({game.X:0},{game.Y:0},{game.Z:0}) "
				+ $"support={supported} supportY={supportY:0.000} normal=({supportNormal.X:0.00},"
				+ $"{supportNormal.Y:0.00},{supportNormal.Z:0.00}) "
				+ $"tiles={stream.LoadedTileCount} instances={stream.LoadedInstanceCount}");

			// Drop onto the surface, then walk forward one second and see where the body lands
			player.SnapToGround (terrain, statics, supportY);
			var startY = player.SpawnY;
			for (var tick = 0; tick < 60; ++tick)
			{
				player.Update (terrain, statics, new Vector2 (0f, -MotionConfig.WalkSpeed), false,
					1f / 60f);
			}

			SphLogger.Info (
				$"MOTIONCHECK {label}: snapY={startY:0.000} afterWalk=({player.SpawnX:0.00},"
				+ $"{player.SpawnY:0.00},{player.SpawnZ:0.00}) grounded={player.Grounded} "
				+ $"vy={player.VelocityY:0.00} travelled={(player.SpawnZ - game.Z):0.00}");

			// Free fall 20 above the surface: gravity is positive in this frame, so a fall grows Y
			var free = new MotionPlayer ();
			free.SetPlayerWorldPosition (game.X, supportY - 20f, game.Z, 0f);
			for (var tick = 0; tick < 60; ++tick)
			{
				free.Update (terrain, statics, Vector2.Zero, false, 1f / 60f);
			}

			SphLogger.Info (
				$"MOTIONCHECK {label}: fall20 y={free.SpawnY:0.00} vy={free.VelocityY:0.00} "
				+ $"grounded={free.Grounded} (vy>0 is downward in this frame)");

			// Penetration recovery: bury the body and confirm it leaves the surface
			var buried = new MotionPlayer ();
			buried.SetPlayerWorldPosition (game.X, supportY + 1.5f, game.Z, 0f);
			var recovered = buried.RecoverFromPenetration (terrain, statics);
			SphLogger.Info (
				$"MOTIONCHECK {label}: buriedY={supportY + 1.5f:0.00} recovered={recovered} "
				+ $"nowY={buried.SpawnY:0.00}");
		}
	}
}
