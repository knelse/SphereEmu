using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using SphServer.Helpers;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: reads tab-separated castle entrances rows (ID, skip, skip, X, Y, Z, Angle, CastleID),
/// clears existing children, and instances the scene from <see cref="CastleEntranceScenePath"/> per row.
/// Source space matches terrain: <c>(x,y,z)_src ↦ (x,-y,-z)</c>; yaw matches WorldObject (<c>t0 = Angle * π / 128</c> radians on Y).
/// Rows with duplicate source coordinates (same X, Y, Z columns) are skipped after the first occurrence.
/// </summary>
[Tool]
public partial class CastleEntrancesFill : Node3D
{
	[Export]
	public string CastleEntrancesDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\castle_entrances.txt";

	[Export]
	public string CastleEntranceScenePath { get; set; } = "res://Godot/Scenes/castle_entrance.tscn";

	[ExportToolButton("Rebuild castle entrances")]
	public Callable RebuildCastleEntrancesButton => Callable.From(RebuildCastleEntrances);

	/// <summary>Clears child instances and repopulates from <see cref="CastleEntrancesDataFilePath"/>.</summary>
	public void RebuildCastleEntrances()
	{
		foreach (var child in GetChildren())
		{
			child.Free();
		}

		if (!ResourceLoader.Exists(CastleEntranceScenePath))
		{
			GD.PushError($"CastleEntranceFill: scene not found: {CastleEntranceScenePath}");
			return;
		}

		var scene = ResourceLoader.Load<PackedScene>(CastleEntranceScenePath);
		if (scene is null)
		{
			GD.PushError($"CastleEntranceFill: could not load: {CastleEntranceScenePath}");
			return;
		}

		if (!global::Godot.FileAccess.FileExists(CastleEntrancesDataFilePath))
		{
			GD.PushError($"CastleEntrancesFill: file not found: {CastleEntrancesDataFilePath}");
			return;
		}

		string text;
		try
		{
			// Godot's FileAccess.GetFileAsString() can behave unexpectedly with some encodings/dumps.
			// Use .NET's reader first (works for absolute Windows paths), then fall back to Godot.
			text = File.ReadAllText(CastleEntrancesDataFilePath);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"CastleEntrancesFill: File.ReadAllText failed ({ex.Message}), falling back to Godot FileAccess");
			text = global::Godot.FileAccess.GetFileAsString(CastleEntrancesDataFilePath);
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			GD.PushWarning($"CastleEntrancesFill: empty file: {CastleEntrancesDataFilePath}");
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
		var duplicateRowsSkipped = 0;
		var rowsConsidered = 0;
		var rowsParsed = 0;
		var spawned = 0;
		var parseErrors = 0;
		var lineNumber = 0;
		using var sr = new StringReader(text);
		while (true)
		{
			var rawLine = sr.ReadLine();
			if (rawLine is null)
			{
				break;
			}

			lineNumber++;
			var line = rawLine;
			if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
			{
				continue;
			}

			rowsConsidered++;

			// Dumps are typically TSV, but in practice they may be copied/normalized into space-separated text.
			// Split on any whitespace and only consume the first columns we need.
			var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 8)
			{
				parseErrors++;
				GD.PushWarning($"CastleEntrancesFill: line {lineNumber}: expected ≥8 columns, skipping");
				continue;
			}

			if (!TryParseId(parts[0].Trim(), out var id))
			{
				parseErrors++;
				GD.PushWarning($"CastleEntrancesFill: line {lineNumber}: bad ID '{parts[0]}', skipping");
				continue;
			}

			if (!TryParseDouble(parts[3], out var x)
				|| !TryParseDouble(parts[4], out var y)
				|| !TryParseDouble(parts[5], out var z))
			{
				parseErrors++;
				GD.PushWarning($"CastleEntrancesFill: line {lineNumber}: bad X/Y/Z, skipping");
				continue;
			}

			if (!TryParseAngle(parts[6], out var angleEncoded))
			{
				parseErrors++;
				GD.PushWarning($"CastleEntrancesFill: line {lineNumber}: bad Angle, skipping");
				continue;
			}

			if (!TryParseCastleId(parts[7], out var castle))
			{
				parseErrors++;
				GD.PushWarning($"CastleEntrancesFill: line {lineNumber}: bad CastleID '{parts[7]}', skipping");
				continue;
			}

			rowsParsed++;

			var posKey = QuantizeSourcePosition(x, y, z);
			if (!seenSourcePositions.Add(posKey))
			{
				duplicateRowsSkipped++;
				continue;
			}

			var instance = scene.Instantiate<CastleEntrance>();
			instance.Name = $"CastleEntrance_{(int)castle:00}_{castle} _{id:X4}";
			var pos = BuildPlacementPosition((float)x, (float)y, (float)z);

			instance.Position = pos;
			instance.Castle = castle;
			instance.Angle = angleEncoded;

			AddChild(instance);
			SetOwnerIfEditor(instance);
			spawned++;
		}

		GD.Print(
			$"CastleEntrancesFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, parseErrors={parseErrors}");

		if (duplicateRowsSkipped > 0)
		{
			GD.PushWarning(
				$"CastleEntrancesFill: skipped {duplicateRowsSkipped} duplicate row(s) with the same source X, Y, Z");
		}
	}

	/// <summary>Quantizes source-space coordinates so near-identical file values dedupe as one position.</summary>
	private static (long Qx, long Qy, long Qz) QuantizeSourcePosition(double x, double y, double z)
	{
		const double scale = 10000.0;
		return (
			(long)Math.Round(x * scale),
			(long)Math.Round(y * scale),
			(long)Math.Round(z * scale));
	}

	private static Vector3 BuildPlacementPosition(float x, float y, float z)
	{
		return new Vector3(x, -y, -z);
	}

	/// <summary>ID column is hex (optional <c>0x</c>).</summary>
	private static bool TryParseId(string s, out int id)
	{
		s = s.Trim();
		if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			s = s[2..];
		}

		return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
	}

	private static bool TryParseDouble(string s, out double v) =>
		double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

	private static bool TryParseAngle(string s, out int angle)
	{
		if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out angle))
		{
			return true;
		}

		if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
		{
			angle = (int)Math.Round(d);
			return true;
		}

		angle = 0;
		return false;
	}

	private static bool TryParseCastleId(string s, out Castles castle)
	{
		if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
			&& Enum.IsDefined(typeof(Castles), i))
		{
			castle = (Castles)i;
			return true;
		}

		castle = default;
		return false;
	}

	private void SetOwnerIfEditor(Node node)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		var root = GetTree()?.EditedSceneRoot;
		node.Owner = root ?? this;
	}
}
