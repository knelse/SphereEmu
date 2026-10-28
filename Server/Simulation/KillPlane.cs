using Godot;
using SphServer.Server.Simulation.Motion;

namespace SphServer.Server.Simulation;

/// <summary>
///     Client motionHazardBand: a 5 unit slab every 250 units of game Y, from -745 through 505.
///     The slabs span the 80 by 80 map and another 300 units past each edge. A step kills when
///     either end is past that edge or the Y segment meets a slab, so a long step cannot skip it
/// </summary>
public static class KillPlane
{
	public const float MarginMeters = 300f;

	public const float BandThickness = 5f;

	// Map cell (0, 0) is the corner at -4000, -4000. 80 cells of 100 m end at 4000
	private const float WorldOrigin = -4000f;

	private const int FirstBand = -745;

	private const int LastBand = 505;

	private const int BandStride = 250;

	private static readonly float EdgeMin = WorldOrigin - MarginMeters;

	private static readonly float EdgeMax =
		WorldOrigin + MotionAssetSource.GridWidth * MotionConfig.TileSize + MarginMeters;

	public static bool Crossed (Vector3 from, Vector3 to)
	{
		if (PastEdge (from.X, from.Z) || PastEdge (to.X, to.Z))
		{
			return true;
		}

		var minY = MathF.Min (from.Y, to.Y);
		var maxY = MathF.Max (from.Y, to.Y);
		for (var lower = FirstBand; lower <= LastBand; lower += BandStride)
		{
			if (maxY > lower && minY < lower + BandThickness)
			{
				return true;
			}
		}

		return false;
	}

	private static bool PastEdge (float x, float z) =>
		x < EdgeMin || x >= EdgeMax || z < EdgeMin || z >= EdgeMax;
}
