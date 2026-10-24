using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using SphServer.Shared.Logger;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     The raw vendored assets the solver streams: map.bin tile stems, Landscape lnd lookup,
///     sst*.mbd placements, and the mdl a placement names. Paths are the reference's, rooted in the
///     repo instead of the client install
/// </summary>
public sealed class MotionAssetSource
{
	public const string LandscapeRoot = "res://Godot/Terrain/Landscape/original";
	public const string ModelRoot = "res://Godot/Models/original";

	public const int GridWidth = 80;
	public const int GridHeight = 80;
	public const int MapRecordBytes = 0x16;
	public const int PlacementRecordBytes = 44;

	// The reference's ClassifyStaticPlacement: char19 and the edoor markers are actor placeholders,
	// and these are technical markers, so none of them blocks anything
	private static readonly string[] TechnicalMarkerPrefixes =
		["m_way", "pyram", "tn3_light", "start"];

	private static readonly string[] DoorMarkerNames = ["edoor", "edoor2", "edoor_i", "edoor2_i"];

	private static readonly string[] LandscapeDirs = ["Landscape", "Landscape_ph", "Landscape_hr", "Landscape_rd"];
	private static readonly string[] ModelDirs = ["models", "Models_hr", "Models_ph", "Models_rd"];

	private readonly Dictionary<(int Gx, int Gz), string> mapCells = [];
	private readonly Dictionary<string, string> lndByStem = new (StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> mdlByStem = new (StringComparer.OrdinalIgnoreCase);
	private bool indexed;

	public int MapCellCount => mapCells.Count;

	/// <summary>
	///     Reference LoadMapGrid: 80x80 records of stride 0x16, a cell is present when its name is
	///     non-empty, and TerrainStem appends the two variant digits
	/// </summary>
	public bool LoadMap ()
	{
		var path = Absolute (Path.Combine (LandscapeRoot, "map.bin"));
		if (!File.Exists (path))
		{
			SphLogger.Info ($"MotionAssetSource: map missing {path}");
			return false;
		}

		var bytes = File.ReadAllBytes (path);
		if (bytes.Length < GridWidth * GridHeight * MapRecordBytes)
		{
			SphLogger.Info ($"MotionAssetSource: map too short {bytes.Length}");
			return false;
		}

		mapCells.Clear ();
		for (var z = 0; z < GridHeight; ++z)
		{
			for (var x = 0; x < GridWidth; ++x)
			{
				var off = (z * GridWidth + x) * MapRecordBytes;
				var name = ReadAsciiName (bytes.AsSpan (off, 20));
				if (name.Length == 0)
				{
					continue;
				}

				var first = bytes[off + 20];
				var second = bytes[off + 21];
				mapCells[(x, z)] = $"{name}_{first}{second}";
			}
		}

		return true;
	}

	/// <summary>
	///     One scan of the vendored trees, keyed by stem, so a tile/model resolves without probing
	///     each of the four directories per lookup
	/// </summary>
	private void EnsureIndex ()
	{
		if (indexed)
		{
			return;
		}

		indexed = true;
		foreach (var dir in LandscapeDirs)
		{
			IndexFiles (Path.Combine (LandscapeRoot, dir), ".lnd", lndByStem);
		}

		foreach (var dir in ModelDirs)
		{
			IndexFiles (Path.Combine (ModelRoot, dir), ".mdl", mdlByStem);
		}

		SphLogger.Info ($"MotionAssetSource: lnd={lndByStem.Count} mdl={mdlByStem.Count} map={mapCells.Count}");
	}

	private static void IndexFiles (string directory, string extension, Dictionary<string, string> into)
	{
		var absolute = Absolute (directory);
		if (!Directory.Exists (absolute))
		{
			return;
		}

		foreach (var file in Directory.EnumerateFiles (absolute, $"*{extension}", SearchOption.TopDirectoryOnly))
		{
			var stem = Path.GetFileNameWithoutExtension (file);
			if (!into.ContainsKey (stem))
			{
				into[stem] = file;
			}
		}
	}

	public string? FindLnd (string stem)
	{
		EnsureIndex ();
		return lndByStem.TryGetValue (stem, out var path) ? path : null;
	}

	public string? FindMdl (string stem)
	{
		EnsureIndex ();
		return mdlByStem.TryGetValue (stem, out var path) ? path : null;
	}

	public bool TryGetStem (int gx, int gz, out string stem)
	{
		EnsureIndex ();
		return mapCells.TryGetValue ((gx, gz), out stem!);
	}

	public IEnumerable<(int Gx, int Gz, string Stem)> MapCells ()
	{
		EnsureIndex ();
		foreach (var (coord, stem) in mapCells)
		{
			yield return (coord.Gx, coord.Gz, stem);
		}
	}

	/// <summary>
	///     Reference ParseStaticPlacementFile: u16 count, u16 zero, 44-byte records. The reference
	///     reorders the rotation floats to X at 36, Y at 32; the client's own placement decoder
	///     (WorldPlacement.cpp) reads them in file order, 32/36/40, and that is the matrix its
	///     renderer draws with, so they are kept in file order here
	/// </summary>
	public static List<MotionPlacement> ParsePlacements (string mbdPath)
	{
		var placements = new List<MotionPlacement> ();
		var bytes = File.ReadAllBytes (mbdPath);
		if (bytes.Length < 4)
		{
			return placements;
		}

		var count = BinaryPrimitives.ReadUInt16LittleEndian (bytes.AsSpan (0, 2));
		if (BinaryPrimitives.ReadUInt16LittleEndian (bytes.AsSpan (2, 2)) != 0
			|| bytes.Length < 4 + count * PlacementRecordBytes)
		{
			SphLogger.Info ($"MotionAssetSource: bad MBD header {Path.GetFileName (mbdPath)}");
			return placements;
		}

		for (var index = 0; index < count; ++index)
		{
			var off = 4 + index * PlacementRecordBytes;
			var name = ReadAsciiName (bytes.AsSpan (off, 20));
			if (name.Length == 0)
			{
				continue;
			}

			var position = ReadVector3 (bytes, off + 20);
			var angleX = ReadSingle (bytes, off + 32);
			var angleY = ReadSingle (bytes, off + 36);
			var angleZ = ReadSingle (bytes, off + 40);
			placements.Add (new MotionPlacement (name.ToLowerInvariant (), position,
				new Vector3 (angleX, angleY, angleZ)));
		}

		return placements;
	}

	/// <summary>
	///     A placement that blocks: char19, the edoor markers, the numbered technical markers, and
	///     the empty placeholder all spawn nothing to collide with
	/// </summary>
	public static bool IsBlockingPlacement (string key)
	{
		if (key == "char19" || key == "empty")
		{
			return false;
		}

		foreach (var marker in DoorMarkerNames)
		{
			if (key == marker)
			{
				return false;
			}
		}

		if (key == "m_enter" || key == "finish")
		{
			return false;
		}

		foreach (var prefix in TechnicalMarkerPrefixes)
		{
			// Reference StartsWithNumberedPlacementName: the prefix, then a digit
			if (key.Length > prefix.Length && key.StartsWith (prefix, StringComparison.Ordinal)
				&& char.IsAsciiDigit (key[prefix.Length]))
			{
				return false;
			}
		}

		return true;
	}

	public static string Absolute (string resPath) =>
		resPath.Contains ("://", StringComparison.Ordinal)
			? ProjectSettings.GlobalizePath (resPath)
			: resPath;

	private static string ReadAsciiName (ReadOnlySpan<byte> bytes)
	{
		var end = 0;
		while (end < bytes.Length && bytes[end] != 0)
		{
			++end;
		}

		return end == 0 ? string.Empty : Encoding.ASCII.GetString (bytes[..end]);
	}

	private static Vector3 ReadVector3 (byte[] bytes, int offset) =>
		new (
			ReadSingle (bytes, offset),
			ReadSingle (bytes, offset + 4),
			ReadSingle (bytes, offset + 8));

	private static float ReadSingle (byte[] bytes, int offset) =>
		BinaryPrimitives.ReadSingleLittleEndian (bytes.AsSpan (offset, 4));
}

/// <summary>
///     One placement: the model stem, its game-space position, and its rotation as (pitch, yaw,
///     roll), which is the order MotionMatrix.Placement expects
/// </summary>
public readonly record struct MotionPlacement (string ModelName, Vector3 Position, Vector3 Rotation);
