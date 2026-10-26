using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using SphServer.Shared.Logger;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     The raw vendored assets the solver streams: map.bin tile stems, Landscape lnd lookup, the
///     sst*.mbd placements the client instantiates, and the mdl a placement names. Paths are the
///     reference's, rooted in the repo instead of the client install
/// </summary>
public sealed class MotionAssetSource
{
	public const string LandscapeRoot = "res://Godot/Terrain/Landscape/original";
	public const string ModelRoot = "res://Godot/Models/original";

	public const int GridWidth = 80;
	public const int GridHeight = 80;
	public const int MapRecordBytes = 0x16;
	public const int PlacementRecordBytes = 44;

	// Client StaticPlacementLoader: a code at or past this is a special point, never created as a
	// static. Chests, tables, portals and NPC bodies carry such codes and do not block there
	private const int SpecialPlacementCode = 10000;

	// Client specialPoints_ is std::array<WorldSpecialPoint, 15000>, so a higher index never lands
	private const int SpecialPointCount = 15000;

	// Client loadStaticObjectGroups and instantiateStaticGroups: ids 0..499, group 99 never, and
	// each continent's hundred only when its landscape directory exists
	private const int StaticGroupCount = 500;
	private const int SkippedStaticGroup = 99;
	private static readonly (int First, string Landscape)[] ContinentStaticGroups =
		[(200, "Landscape_hr"), (300, "Landscape_ph"), (400, "Landscape_rd")];

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
	///     The static group files the client instantiates, in its id order: params\sst_NN.mbd below
	///     100, params\sstNNN.mbd from 100
	/// </summary>
	public static IEnumerable<string> StaticGroupFiles (string paramsDirectory)
	{
		var landscapeRoot = Absolute (LandscapeRoot);
		for (var id = 0; id < StaticGroupCount; ++id)
		{
			if (id == SkippedStaticGroup || !ContinentLoaded (landscapeRoot, id))
			{
				continue;
			}

			var file = Path.Combine (paramsDirectory, id < 100 ? $"sst_{id:00}.mbd" : $"sst{id:000}.mbd");
			if (File.Exists (file))
			{
				yield return file;
			}
		}
	}

	private static bool ContinentLoaded (string landscapeRoot, int id)
	{
		foreach (var (first, landscape) in ContinentStaticGroups)
		{
			if (id >= first && id < first + 100)
			{
				return Directory.Exists (Path.Combine (landscapeRoot, landscape));
			}
		}

		return true;
	}

	/// <summary>
	///     Client WorldObjectPlacement::decode: i32 count, 44-byte records, and a file whose body is
	///     not whole records is skipped. Rotation is read as X at 36, Y at 32, Z at 40, which
	///     PlacementMatrix consumes. Special-code records are skipped, as the client loader skips them
	/// </summary>
	public static List<MotionPlacement> ParsePlacements (string mbdPath)
	{
		var placements = new List<MotionPlacement> ();
		var bytes = File.ReadAllBytes (mbdPath);
		if (bytes.Length < 4 || (bytes.Length - 4) % PlacementRecordBytes != 0)
		{
			return placements;
		}

		var count = BinaryPrimitives.ReadInt32LittleEndian (bytes.AsSpan (0, 4));
		if (count < 0 || count > (bytes.Length - 4) / PlacementRecordBytes)
		{
			SphLogger.Info ($"MotionAssetSource: bad MBD header {Path.GetFileName (mbdPath)}");
			return placements;
		}

		for (var index = 0; index < count; ++index)
		{
			var off = 4 + index * PlacementRecordBytes;
			// 18-byte name, then the u16 code, the client WorldPlacement record
			var name = ReadAsciiName (bytes.AsSpan (off, 18));
			var code = BinaryPrimitives.ReadUInt16LittleEndian (bytes.AsSpan (off + 18, 2));
			if (name.Length == 0 || code >= SpecialPlacementCode)
			{
				continue;
			}

			var position = ReadVector3 (bytes, off + 20);
			var angleX = ReadSingle (bytes, off + 36);
			var angleY = ReadSingle (bytes, off + 32);
			var angleZ = ReadSingle (bytes, off + 40);
			placements.Add (new MotionPlacement (name.ToLowerInvariant (), position,
				new Vector3 (angleX, angleY, angleZ)));
		}

		return placements;
	}

	/// <summary>
	///     Client instantiateStaticGroups: a record at code 10000 + n is special point n, its rotation
	///     X (offset 32) the heading. A point at y 0 is unset, as GetSpecial rejects it
	/// </summary>
	public static List<MotionSpecialPoint> ParseSpecialPoints (string mbdPath)
	{
		var points = new List<MotionSpecialPoint> ();
		var bytes = File.ReadAllBytes (mbdPath);
		if (bytes.Length < 4 || (bytes.Length - 4) % PlacementRecordBytes != 0)
		{
			return points;
		}

		var count = BinaryPrimitives.ReadInt32LittleEndian (bytes.AsSpan (0, 4));
		if (count < 0 || count > (bytes.Length - 4) / PlacementRecordBytes)
		{
			return points;
		}

		for (var index = 0; index < count; ++index)
		{
			var off = 4 + index * PlacementRecordBytes;
			var code = BinaryPrimitives.ReadUInt16LittleEndian (bytes.AsSpan (off + 18, 2));
			if (code < SpecialPlacementCode || code - SpecialPlacementCode >= SpecialPointCount)
			{
				continue;
			}

			var position = ReadVector3 (bytes, off + 20);
			if (position.Y == 0f)
			{
				continue;
			}

			points.Add (new MotionSpecialPoint (code - SpecialPlacementCode, position, ReadSingle (bytes, off + 32)));
		}

		return points;
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

/// <summary>
///     A client special point: its index, game-space position and heading
/// </summary>
public readonly record struct MotionSpecialPoint (int Index, Vector3 Position, float Heading);
