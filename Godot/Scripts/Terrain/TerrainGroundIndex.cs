using System.Collections.Generic;
using System.IO;
using Godot;
using SphServer.Godot.Scripts.Util;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Occupied ground cells from <c>map.txt</c>, keyed by GridMap <c>(gx, gz)</c>
///     (same indexing as <see cref="TerrainGridFill"/>).
/// </summary>
public sealed class TerrainGroundIndex
{
	public const string DefaultMapPath = "res://Godot/Terrain/map.txt";
	public const float DefaultTileSize = 100f;

	private static TerrainGroundIndex? Loaded;
	private static readonly object LoadLock = new();

	private readonly Dictionary<(int Gx, int Gz), string> cells = new();

	public int Count => cells.Count;

	public static TerrainGroundIndex GetOrLoad(string mapPath = DefaultMapPath)
	{
		lock (LoadLock)
		{
			if (Loaded is not null)
			{
				return Loaded;
			}

			Loaded = new TerrainGroundIndex();
			Loaded.TryLoad(mapPath);
			return Loaded;
		}
	}

	public static void ClearLoaded()
	{
		lock (LoadLock)
		{
			Loaded = null;
		}
	}

	public bool TryGetMasterName(int gx, int gz, out string masterName) =>
		cells.TryGetValue((gx, gz), out masterName!);

	public bool HasCell(int gx, int gz) => cells.ContainsKey((gx, gz));

	public IEnumerable<(int Gx, int Gz, string MasterName)> EnumerateCells()
	{
		foreach (var (coord, master) in cells)
		{
			yield return (coord.Gx, coord.Gz, master);
		}
	}

	public bool TryLoad(string mapPath)
	{
		cells.Clear();
		if (!ResPathIO.TryReadAllBytes(mapPath, out var fileContents))
		{
			GD.PushError($"TerrainGroundIndex: map not found: {mapPath}");
			return false;
		}

		var list = MapFill.ReadFullGrid(fileContents);
		for (var i = 0; i < list.Count; i++)
		{
			var cell = list[i];
			if (cell.IsEmpty)
			{
				continue;
			}

			var gx = MapFill.GridWidth - (i % MapFill.GridWidth) - 1;
			var gz = i / MapFill.GridWidth;
			cells[(gx, gz)] = cell.MasterName;
		}

		return true;
	}
}
