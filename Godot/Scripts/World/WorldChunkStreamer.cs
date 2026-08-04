using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using SphServer.Client;
using SphServer.Server.Config;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Load-only streamer for world placement chunks (Phase 1). Chunks stay loaded once requested.
/// </summary>
[Tool]
public partial class WorldChunkStreamer : Node
{
	private readonly HashSet<(WorldContentKind Kind, int TileX, int TileZ)> loaded = [];
	private WorldContentIndex? index;
	private Node? mainServerRoot;
	private bool loadAllRequested;

	[Export]
	public float LoadRadiusMeters { get; set; }

	[Export]
	public bool AutoLoadAroundClients { get; set; } = true;

	public int LoadedChunkCount => loaded.Count;

	public override void _Ready()
	{
		if (LoadRadiusMeters <= 0f)
		{
			LoadRadiusMeters = 200f;
			try
			{
				if (!Engine.IsEditorHint())
				{
					LoadRadiusMeters = ServerConfig.AppConfig.ObjectVisibilityDistance * 2f;
				}
			}
			catch
			{
				// Config may be unavailable in some tool contexts.
			}
		}

		var watch = Stopwatch.StartNew();
		index = WorldContentIndex.GetOrLoad();
		StartupTiming.MarkSpan("WorldContentIndex.Load", watch.ElapsedMilliseconds);
		mainServerRoot = FindMainServerRoot();
		StartupTiming.Mark($"WorldChunkStreamer._Ready (index entries={index.Entries.Count}, tiles={CountTiles()})");
	}

	public void EnsureAroundWorldPosition(Vector3 worldPosition)
	{
		if (index is null || mainServerRoot is null)
		{
			return;
		}

		WorldChunkLoadOps.EnsureAround(
			mainServerRoot,
			index,
			loaded,
			worldPosition,
			LoadRadiusMeters,
			assignEditorOwner: false);
	}

	public void EnsureChunkLoaded(WorldContentKind kind, int tileX, int tileZ)
	{
		mainServerRoot ??= FindMainServerRoot();
		if (mainServerRoot is null)
		{
			return;
		}

		WorldChunkLoadOps.EnsureChunkLoaded(
			mainServerRoot,
			loaded,
			kind,
			tileX,
			tileZ,
			assignEditorOwner: false);
	}

	public int LoadAll()
	{
		loadAllRequested = true;
		index ??= WorldContentIndex.GetOrLoad();
		mainServerRoot ??= FindMainServerRoot();
		if (mainServerRoot is null)
		{
			return 0;
		}

		var count = WorldChunkLoadOps.LoadAll(mainServerRoot, index, loaded, assignEditorOwner: false);
		GD.Print($"WorldChunkStreamer: LoadAll loaded {count} chunk(s), total={loaded.Count}");
		return count;
	}

	public void NotifyClientPosition(SphereClient client)
	{
		if (!AutoLoadAroundClients || Engine.IsEditorHint() || loadAllRequested)
		{
			return;
		}

		if (client.CurrentCharacter is null)
		{
			return;
		}

		EnsureAroundWorldPosition(ClientWorldPosition.GetGodotWorldPosition(client));
	}

	public void CheckAllClients()
	{
		if (!AutoLoadAroundClients || Engine.IsEditorHint() || loadAllRequested)
		{
			return;
		}

		foreach (var client in ActiveClients.GetAll().Values)
		{
			NotifyClientPosition(client);
		}
	}

	private Node? FindMainServerRoot()
	{
		return GetParent() ?? GetTree()?.CurrentScene;
	}

	private int CountTiles()
	{
		var n = 0;
		if (index is null)
		{
			return 0;
		}

		foreach (var _ in index.EnumerateChunkTiles())
		{
			n++;
		}

		return n;
	}
}
