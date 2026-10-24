using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.Fill;
using SphServer.Shared.Logger;

namespace SphServer.Server.Debug;

/// <summary>
///     Headless: measure where the visual ground for a raw .lnd tile lands in Godot world space.
///     The solver streams raw .lnd at origin (tileX*100, tileZ*100); the renderer draws the baked
///     chunks under terrain_scene, which MainServer rotates and offsets. Print the chain so the
///     pose conversion is measured, not derived. Run: <c>.\Tools\motion_frame_probe.ps1</c>
/// </summary>
public partial class MotionFrameProbeHeadless : Node
{
	// MainServer.tscn Transform3D on the TerrainScene instance
	private const float SceneCos = -4.371139e-08f;
	private const float SceneOriginX = 1708.8f;
	private const float SceneOriginZ = 1709f;

	private const int ExitSuccess = 0;

	public override void _Ready ()
	{
		var packed = ResourceLoader.Load<PackedScene> ("res://Godot/Scenes/terrain_scene.scn");
		if (packed?.Instantiate<Node3D> () is not { } scene)
		{
			SphLogger.Info ("FRAMEPROBE: terrain_scene missing");
			GetTree ().Quit (ExitSuccess);
			return;
		}

		// MainServer.tscn stores the TerrainScene transform as basis rows, so the rows go in as
		// written: row0 = (cos, 0, 1), row2 = (-1, 0, -cos)
		scene.Name = "TerrainScene";
		scene.Transform = new Transform3D (
			new Basis (new Vector3 (SceneCos, 0f, 1f), new Vector3 (0f, 1f, 0f), new Vector3 (-1f, 0f, -SceneCos)),
			new Vector3 (SceneOriginX, 0f, SceneOriginZ));
		AddChild (scene);

		if (scene.FindChild (TerrainGridFill.TerrainNodeName, recursive: true, owned: false) is not GridMap grid)
		{
			SphLogger.Info ("FRAMEPROBE: Terrain GridMap not in terrain_scene");
			GetTree ().Quit (ExitSuccess);
			return;
		}

		SphLogger.Info (
			$"FRAMEPROBE grid.GlobalTransform origin=({grid.GlobalTransform.Origin.X:0.0},"
			+ $"{grid.GlobalTransform.Origin.Y:0.0},{grid.GlobalTransform.Origin.Z:0.0}) "
			+ $"x=({grid.GlobalTransform.Basis.X.X:0.000},{grid.GlobalTransform.Basis.X.Y:0.000},"
			+ $"{grid.GlobalTransform.Basis.X.Z:0.000}) "
			+ $"z=({grid.GlobalTransform.Basis.Z.X:0.000},{grid.GlobalTransform.Basis.Z.Y:0.000},"
			+ $"{grid.GlobalTransform.Basis.Z.Z:0.000})");

		// Three raw tiles, the self-check spots, and what the visual ground says about each
		var probes = new (int RawX, int RawZ)[] { (4, 15), (-13, 15), (39, 39) };
		foreach (var (rawX, rawZ) in probes)
		{
			var gx = MapFill.GridWidth - rawX - 1;
			var gz = rawZ;
			var local = grid.MapToLocal (new Vector3I (gx, 0, gz));
			var global = grid.GlobalTransform * local;
			SphLogger.Info (
				$"FRAMEPROBE rawTile=({rawX},{rawZ}) rawOrigin=({rawX * 100f:0},{rawZ * 100f:0}) "
				+ $"gridCell=({gx},{gz}) local=({local.X:0},{local.Y:0},{local.Z:0}) "
				+ $"godot=({global.X:0.0},{global.Y:0.0},{global.Z:0.0})");
		}

		GetTree ().Quit (ExitSuccess);
	}
}
