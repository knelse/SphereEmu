using System.Text;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
/// Reads a binary map index (22 bytes per tile: 20-byte null-padded ASCII name, two variant bytes)
/// and builds placeholder entries for known master tiles. Ported from the Python map fill script.
/// </summary>
public static class MapFill
{
	public const int GridWidth = 80;
	public const int RecordSizeBytes = 22;

	public static readonly Vector3 DefaultRotation = new(0, Mathf.DegToRad(90), 0);

	public sealed class TilePlaceholder
	{
		public required string MasterName { get; init; }
		public required Vector3 Location { get; init; }
		public required Vector3 Rotation { get; init; }
		public string? Continent { get; init; }
	}

	public sealed class Result
	{
		public required Dictionary<string, List<TilePlaceholder>> ContinentPlaceholders { get; init; }
		public required Dictionary<(float X, float Y), TilePlaceholder> PlacedTilesPlaceholders { get; init; }
		public required List<TilePlaceholder> AllPlaceholders { get; init; }
	}

	/// <summary>
	/// One cell from <c>map.bin</c> for full-grid consumers (e.g. Godot GridMap).
	/// <see cref="MasterName"/> is empty when the cell is intentionally empty (e.g. <c>fill_empt</c>).
	/// </summary>
	public readonly struct MapBinCell
	{
		public MapBinCell(string masterName)
		{
			MasterName = masterName;
		}

		public string MasterName { get; }
		public bool IsEmpty => MasterName.Length == 0;
	}

	/// <summary>
	/// Reads every 22-byte record in <paramref name="mapBinPath"/> without bbox filtering.
	/// </summary>
	public static List<MapBinCell> ReadFullGrid(string mapBinPath)
	{
		var fileContents = File.ReadAllBytes(mapBinPath);
		return ReadFullGrid(fileContents);
	}

	/// <summary>
	/// Reads every full record from raw <paramref name="fileContents"/> (same layout as <see cref="Parse"/>).
	/// </summary>
	public static List<MapBinCell> ReadFullGrid(ReadOnlyMemory<byte> fileContents)
	{
		var bytes = fileContents.Span;
		var result = new List<MapBinCell>();
		var offset = 0;
		while (offset + RecordSizeBytes <= bytes.Length)
		{
			var nameFromMap = ReadAsciiName(bytes.Slice(offset, 20)).ToLowerInvariant();
			var variant1 = bytes[offset + 20];
			var variant2 = bytes[offset + 21];
			offset += RecordSizeBytes;

			// In map.bin, "fill_empt" is a concrete terrain tile (we have assets for fill_empt_00),
			// and it should be rendered like any other tile in the full grid.
			if (nameFromMap.Contains("fill_empt", StringComparison.Ordinal))
			{
				result.Add(new MapBinCell("fill_empt_00"));
				continue;
			}

			var masterName = $"{nameFromMap}_{variant1}{variant2}";
			result.Add(new MapBinCell(PatchCasingFromMap(masterName)));
		}

		return result;
	}

	/// <summary>
	/// Parses <paramref name="mapBinPath"/> and fills placeholder collections.
	/// </summary>
	/// <param name="mapBinPath">Path to the map binary file.</param>
	/// <param name="tileSize">World units per grid cell (matches Python <c>TILE_SIZE</c>).</param>
	/// <param name="masterTileBboxes">Set of valid master names (e.g. keys of <c>master_tile_bboxes</c>).</param>
	/// <param name="masterWaterHeights">Set of valid master names for water (e.g. keys of <c>master_water_heights</c>).</param>
	/// <param name="lndToContinentMap">Maps <c>actual_master_name.ToLowerInvariant()</c> to continent prefix.</param>
	/// <param name="continentPrefixes">Prefixes used only to initialize empty lists in <see cref="Result.ContinentPlaceholders"/>.</param>
	public static Result Parse(
		string mapBinPath,
		float tileSize,
		IReadOnlySet<string> masterTileBboxes,
		IReadOnlySet<string> masterWaterHeights,
		IReadOnlyDictionary<string, string> lndToContinentMap,
		IEnumerable<string> continentPrefixes)
	{
		var continentPlaceholders = new Dictionary<string, List<TilePlaceholder>>();
		foreach (var prefix in continentPrefixes)
		{
			continentPlaceholders[prefix] = new List<TilePlaceholder>();
		}

		var placedTilesPlaceholders = new Dictionary<(float X, float Y), TilePlaceholder>();
		var allPlaceholders = new List<TilePlaceholder>();

		var fileContents = File.ReadAllBytes(mapBinPath);
		var offset = 0;
		var index = -1;

		while (offset + RecordSizeBytes <= fileContents.Length)
		{
			var nameFromMap = ReadAsciiName(fileContents.AsSpan(offset, 20)).ToLowerInvariant();
			var variant1 = fileContents[offset + 20];
			var variant2 = fileContents[offset + 21];
			offset += RecordSizeBytes;
			index++;

			// "fill_empt" has concrete assets (fill_empt_00) but isn't part of bbox/water sets,
			// so we skip it in placeholder parsing (this pass is only for known master tiles).
			if (nameFromMap.Contains("fill_empt", StringComparison.Ordinal))
			{
				continue;
			}

			var masterName = $"{nameFromMap}_{variant1}{variant2}";
			var masterNameAlt = PatchCasingFromMap(masterName);

			string? actualMasterName = null;
			if (masterTileBboxes.Contains(masterName) || masterWaterHeights.Contains(masterName))
			{
				actualMasterName = masterName;
			}
			else if (masterTileBboxes.Contains(masterNameAlt) || masterWaterHeights.Contains(masterNameAlt))
			{
				actualMasterName = masterNameAlt;
			}
			else
			{
				continue;
			}

			lndToContinentMap.TryGetValue(actualMasterName.ToLowerInvariant(), out var continentPrefix);

			var xCoord = (index % GridWidth) * tileSize;
			var yCoord = (index / GridWidth) * tileSize;
			var tileCoordsKey = (xCoord, yCoord);

			var placeholder = new TilePlaceholder
			{
				MasterName = actualMasterName,
				Location = new Vector3(xCoord, yCoord, 0f),
				Rotation = DefaultRotation,
				Continent = continentPrefix
			};

			if (masterName.Contains("patch", StringComparison.OrdinalIgnoreCase)
				&& placedTilesPlaceholders.TryGetValue(tileCoordsKey, out var oldPlaceholder))
			{
				placedTilesPlaceholders.Remove(tileCoordsKey);
				allPlaceholders.Remove(oldPlaceholder);
			}

			placedTilesPlaceholders[tileCoordsKey] = placeholder;
			allPlaceholders.Add(placeholder);
		}

		return new Result
		{
			ContinentPlaceholders = continentPlaceholders,
			PlacedTilesPlaceholders = placedTilesPlaceholders,
			AllPlaceholders = allPlaceholders
		};
	}

	/// <summary>
	/// Map binary stores lowercase names; master assets use <c>Patch</c> (capital P). Same as the Python map fill script.
	/// </summary>
	private static string PatchCasingFromMap(string masterNameLower) =>
		masterNameLower.Replace("patch", "Patch", StringComparison.Ordinal);

	private static string ReadAsciiName(ReadOnlySpan<byte> nameBytes)
	{
		var len = nameBytes.Length;
		for (var i = 0; i < nameBytes.Length; i++)
		{
			if (nameBytes[i] == 0)
			{
				len = i;
				break;
			}
		}

		return len == 0 ? string.Empty : Encoding.ASCII.GetString(nameBytes.Slice(0, len));
	}
}
