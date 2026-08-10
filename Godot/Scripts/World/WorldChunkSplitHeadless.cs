using System;
using System.Threading.Tasks;
using Godot;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Headless: split MainServer placement children into tile chunk scenes + world_content_index.bin.
///     Run: <c>.\Tools\split_world_chunks.ps1</c>
/// </summary>
public partial class WorldChunkSplitHeadless : Node
{
	public const string DefaultScenePath = "res://Godot/Scenes/MainServer.tscn";

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
			var exit = await RunAsync(options);
			Quit(exit);
		}
		catch (Exception ex)
		{
			GD.PushError($"WorldChunkSplitHeadless: failed: {ex}");
			Quit(ExitFailure);
		}
		finally
		{
			IsActive = false;
		}
	}

	private async Task<int> RunAsync(Options options)
	{
		if (!ResourceLoader.Exists(options.ScenePath))
		{
			GD.PushError($"WorldChunkSplitHeadless: scene not found: {options.ScenePath}");
			return ExitFailure;
		}

		var packed = ResourceLoader.Load<PackedScene>(options.ScenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
		if (packed is null)
		{
			GD.PushError("WorldChunkSplitHeadless: failed to load PackedScene.");
			return ExitFailure;
		}

		var root = packed.Instantiate<Node>();
		AddChild(root);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Re-pack path: hydrate existing chunks into thin MainServer, then rewrite.
		if (ResourceLoader.Exists(WorldChunkCatalog.IndexPath)
			|| DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(WorldChunkCatalog.ChunksRoot)))
		{
			var streamer = new WorldChunkStreamer { Name = "WorldChunkStreamerTemp" };
			root.AddChild(streamer);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			var loaded = streamer.LoadAll();
			GD.Print($"WorldChunkSplitHeadless: re-pack LoadAll={loaded}");
		}

		var result = WorldChunkPacker.PackFromMainServer(root, clearParents: true, extractSlots: !options.KeepSlotsInChunks);
		GD.Print(
			$"WorldChunkSplitHeadless: wrote {result.ChunksWritten} chunks, packed {result.NodesPacked} nodes, "
			+ $"extracted {result.SlotArraysExtracted} slot arrays, index entries={result.IndexEntries}");

		if (options.SaveMainServer)
		{
			var thin = new PackedScene();
			var packError = thin.Pack(root);
			if (packError != Error.Ok)
			{
				GD.PushError($"WorldChunkSplitHeadless: packing MainServer failed: {packError}");
				return ExitFailure;
			}

			var saveError = ResourceSaver.Save(thin, options.ScenePath);
			if (saveError != Error.Ok)
			{
				GD.PushError($"WorldChunkSplitHeadless: saving MainServer failed: {saveError}");
				return ExitFailure;
			}

			GD.Print($"WorldChunkSplitHeadless: saved thin MainServer to {options.ScenePath}");
		}

		return ExitSuccess;
	}

	private static Options ParseOptions()
	{
		var options = new Options();
		var args = OS.GetCmdlineUserArgs();
		for (var i = 0; i < args.Length; i++)
		{
			var arg = args[i];
			switch (arg)
			{
				case "--help":
				case "-h":
					options.ShowHelp = true;
					break;
				case "--scene":
					if (i + 1 < args.Length)
					{
						options.ScenePath = args[++i];
					}

					break;
				case "--keep-slots-in-chunks":
					options.KeepSlotsInChunks = true;
					break;
				case "--no-save-main":
					options.SaveMainServer = false;
					break;
			}
		}

		return options;
	}

	private static void PrintHelp()
	{
		GD.Print(
			"""
			WorldChunkSplitHeadless options (after --):
			  --scene <path>           MainServer scene (default res://Godot/Scenes/MainServer.tscn)
			  --keep-slots-in-chunks   Do not extract BakedSpawnSlots into the index sidecar
			  --no-save-main           Write chunks/index only; leave MainServer.tscn unchanged
			  --help
			""");
	}

	private void Quit(int code)
	{
		GetTree().Quit(code);
	}

	private sealed class Options
	{
		public string ScenePath { get; set; } = DefaultScenePath;
		public bool KeepSlotsInChunks { get; set; }
		public bool SaveMainServer { get; set; } = true;
		public bool ShowHelp { get; set; }
	}
}
