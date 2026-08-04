using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Terrain.Fill;
using SphServer.Godot.Scripts.World;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Headless: build shared ground meshes/shapes + per-cell chunk scenes, and strip MeshLibrary
///     from terrain_scene so MainServer open no longer pulls ~288 MB.
///     Run: <c>.\Tools\pack_terrain_ground_chunks.ps1</c>
/// </summary>
public partial class TerrainGroundPackHeadless : Node
{
	public const string DefaultTerrainScenePath = "res://Godot/Scenes/terrain_scene.scn";
	public const string ChunksDirectory = TerrainGroundStreamer.ChunksDirectory;

	private const int ExitSuccess = 0;
	private const int ExitFailure = 1;

	public static bool IsActive { get; private set; }

	public override async void _Ready()
	{
		var options = ParseOptions();
		if (options.ShowHelp)
		{
			PrintHelp();
			Quit(ExitSuccess);
			return;
		}

		IsActive = true;
		try
		{
			Quit(await RunAsync(options));
		}
		catch (Exception ex)
		{
			GD.PushError($"TerrainGroundPackHeadless: failed: {ex}");
			Quit(ExitFailure);
		}
		finally
		{
			IsActive = false;
		}
	}

	private async Task<int> RunAsync(Options options)
	{
		StartupTiming.Mark("TerrainGroundPackHeadless: begin");
		Directory.CreateDirectory(ProjectSettings.GlobalizePath(TerrainTileMeshFactory.SharedMeshDirectory));
		Directory.CreateDirectory(ProjectSettings.GlobalizePath(TerrainTileMeshFactory.SharedShapeDirectory));
		Directory.CreateDirectory(ProjectSettings.GlobalizePath(ChunksDirectory));

		var index = new TerrainGroundIndex();
		if (!index.TryLoad(TerrainGroundIndex.DefaultMapPath))
		{
			return ExitFailure;
		}

		GD.Print($"TerrainGroundPackHeadless: {index.Count} occupied ground cells.");

		var uniqueMasters = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (_, _, master) in index.EnumerateCells())
		{
			uniqueMasters.Add(master);
		}

		var meshWatch = Stopwatch.StartNew();
		var meshesOk = 0;
		foreach (var master in uniqueMasters)
		{
			var mesh = TerrainTileMeshFactory.BuildTexturedMesh(master);
			if (mesh is null)
			{
				GD.PushWarning($"TerrainGroundPackHeadless: no mesh for '{master}'");
				continue;
			}

			var meshPath = $"{TerrainTileMeshFactory.SharedMeshDirectory}{master}.res";
			var meshErr = ResourceSaver.Save(mesh, meshPath);
			if (meshErr != Error.Ok)
			{
				GD.PushError($"TerrainGroundPackHeadless: save mesh failed {meshPath}: {meshErr}");
				continue;
			}

			var shape = mesh.CreateTrimeshShape();
			if (shape is not null)
			{
				var shapePath = $"{TerrainTileMeshFactory.SharedShapeDirectory}{master}.res";
				var shapeErr = ResourceSaver.Save(shape, shapePath);
				if (shapeErr != Error.Ok)
				{
					GD.PushWarning($"TerrainGroundPackHeadless: save shape failed {shapePath}: {shapeErr}");
				}
			}

			meshesOk++;
		}

		StartupTiming.MarkSpan(
			$"TerrainGroundPackHeadless: shared meshes ({meshesOk}/{uniqueMasters.Count})",
			meshWatch.ElapsedMilliseconds);

		// Cell size / origin for MapToLocal-equivalent placement (GridMap at identity local origin).
		const float tileSize = TerrainGroundIndex.DefaultTileSize;
		var chunkWatch = Stopwatch.StartNew();
		var chunksWritten = 0;
		foreach (var (gx, gz, master) in index.EnumerateCells())
		{
			var meshPath = $"{TerrainTileMeshFactory.SharedMeshDirectory}{master}.res";
			if (!ResourceLoader.Exists(meshPath))
			{
				continue;
			}

			var chunkRoot = new Node3D
			{
				Name = $"Ground_{gx}_{gz}",
				// Match GridMap.MapToLocal with CellCenter* off: origin at cell corner (gx*size),
				// not center — TerrainObjects / carved footprints were baked against that.
				Position = new Vector3(gx * tileSize, 0f, gz * tileSize),
			};

			// Must be in the SceneTree for Owner/Pack to accept ancestry checks.
			AddChild(chunkRoot);

			var meshInstance = new MeshInstance3D { Name = "Mesh", Mesh = ResourceLoader.Load<Mesh>(meshPath) };
			chunkRoot.AddChild(meshInstance);
			meshInstance.Owner = chunkRoot;

			var shapePath = $"{TerrainTileMeshFactory.SharedShapeDirectory}{master}.res";
			if (ResourceLoader.Exists(shapePath))
			{
				var body = new StaticBody3D { Name = "Body" };
				var collision = new CollisionShape3D
				{
					Name = "Collision",
					Shape = ResourceLoader.Load<Shape3D>(shapePath),
				};
				body.AddChild(collision);
				chunkRoot.AddChild(body);
				body.Owner = chunkRoot;
				collision.Owner = chunkRoot;
			}

			var packed = new PackedScene();
			var packErr = packed.Pack(chunkRoot);
			RemoveChild(chunkRoot);
			chunkRoot.QueueFree();
			if (packErr != Error.Ok)
			{
				GD.PushError($"TerrainGroundPackHeadless: Pack failed for {gx}_{gz}: {packErr}");
				continue;
			}

			var chunkPath = $"{ChunksDirectory}/{gx}_{gz}.tscn";
			var saveErr = ResourceSaver.Save(packed, chunkPath);
			if (saveErr != Error.Ok)
			{
				GD.PushError($"TerrainGroundPackHeadless: Save failed {chunkPath}: {saveErr}");
				continue;
			}

			chunksWritten++;
			if (chunksWritten % 500 == 0)
			{
				GD.Print($"TerrainGroundPackHeadless: wrote {chunksWritten} chunks…");
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
		}

		StartupTiming.MarkSpan(
			$"TerrainGroundPackHeadless: chunks ({chunksWritten})",
			chunkWatch.ElapsedMilliseconds);

		if (options.StripMeshLibrary)
		{
			if (!StripTerrainSceneMeshLibrary(options.TerrainScenePath))
			{
				return ExitFailure;
			}
		}

		GD.Print(
			$"TerrainGroundPackHeadless: done. meshes={meshesOk} chunks={chunksWritten} "
			+ $"strip={options.StripMeshLibrary}");
		return ExitSuccess;
	}

	private static bool StripTerrainSceneMeshLibrary(string scenePath)
	{
		if (!ResourceLoader.Exists(scenePath))
		{
			GD.PushError($"TerrainGroundPackHeadless: terrain scene missing: {scenePath}");
			return false;
		}

		// Preserve companion .uid so MainServer.tscn ext_resource UID keeps resolving after resave.
		var uidCompanionPath = ProjectSettings.GlobalizePath(scenePath) + ".uid";
		var uidText = File.Exists(uidCompanionPath)
			? File.ReadAllText(uidCompanionPath).Trim()
			: "uid://cd8g6idrm4k8v";

		var packed = ResourceLoader.Load<PackedScene>(scenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
		if (packed is null)
		{
			GD.PushError("TerrainGroundPackHeadless: failed to load terrain scene.");
			return false;
		}

		var root = packed.Instantiate<Node>();
		GridMap? terrain = null;
		foreach (var node in root.FindChildren("*", nameof(GridMap), recursive: true, owned: false))
		{
			if (node is GridMap grid && grid.Name == TerrainGridFill.TerrainNodeName)
			{
				terrain = grid;
				break;
			}
		}

		if (terrain is null)
		{
			root.QueueFree();
			GD.PushError("TerrainGroundPackHeadless: Terrain GridMap not found in terrain scene.");
			return false;
		}

		terrain.Clear();
		terrain.MeshLibrary = null;
		terrain.Set("bake_navigation", false);
		// Match historical Terrain GridMap: cell origin at corner (no half-cell centering).
		terrain.CellSize = new Vector3(
			TerrainGroundIndex.DefaultTileSize,
			1f,
			TerrainGroundIndex.DefaultTileSize);
		terrain.CellCenterX = false;
		terrain.CellCenterY = false;
		terrain.CellCenterZ = false;

		// Ensure streamer host exists for play/editor.
		if (root.GetNodeOrNull("TerrainGroundStreamer") is null)
		{
			var streamer = new TerrainGroundStreamer { Name = "TerrainGroundStreamer" };
			root.AddChild(streamer);
			streamer.Owner = root;
		}

		var outPacked = new PackedScene();
		var packErr = outPacked.Pack(root);
		root.QueueFree();
		if (packErr != Error.Ok)
		{
			GD.PushError($"TerrainGroundPackHeadless: pack stripped scene failed: {packErr}");
			return false;
		}

		var saveErr = ResourceSaver.Save(outPacked, scenePath);
		if (saveErr != Error.Ok)
		{
			GD.PushError($"TerrainGroundPackHeadless: save stripped scene failed: {saveErr}");
			return false;
		}

		if (!string.IsNullOrEmpty(uidText))
		{
			File.WriteAllText(uidCompanionPath, uidText + "\n");
		}

		GD.Print($"TerrainGroundPackHeadless: stripped MeshLibrary from {scenePath}");
		return true;
	}

	private static Options ParseOptions()
	{
		var options = new Options();
		var args = OS.GetCmdlineUserArgs();
		for (var i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--help":
				case "-h":
					options.ShowHelp = true;
					break;
				case "--terrain-scene":
					if (i + 1 < args.Length)
					{
						options.TerrainScenePath = args[++i];
					}

					break;
				case "--no-strip":
					options.StripMeshLibrary = false;
					break;
			}
		}

		return options;
	}

	private static void PrintHelp()
	{
		GD.Print(
			"""
			TerrainGroundPackHeadless options (after --):
			  --terrain-scene <path>   Default res://Godot/Scenes/terrain_scene.scn
			  --no-strip               Do not clear MeshLibrary on terrain_scene
			  --help
			""");
	}

	private void Quit(int code) => GetTree().Quit(code);

	private sealed class Options
	{
		public string TerrainScenePath { get; set; } = DefaultTerrainScenePath;
		public bool StripMeshLibrary { get; set; } = true;
		public bool ShowHelp { get; set; }
	}
}
