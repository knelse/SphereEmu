using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: reads tab/space-separated door rows:
/// ID, DoorType, (skip), X, Y, Z, Angle, DoorIdOr32767, target_x, target_y, target_z.
/// Only keeps DoorEntrance rows; if DoorIdOr32767 == 32767 then HasTarget=true and target coords are applied (unless weird).
/// Source space matches terrain: <c>(x,y,z)_src ↦ (x,-y,-z)</c>.
/// Rows with duplicate source coordinates (same X, Y, Z columns) are skipped after the first occurrence.
/// </summary>
[Tool]
public partial class DoorsFill : Node3D
{
	private const int DoorHasTargetSentinel = 32767;
	private const string DoorEntranceTypeValue = "DoorEntrance";

	[Export]
	public string DoorsDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\doors.txt";

	[Export]
	public string DoorScenePath { get; set; } = "res://Godot/Scenes/door.tscn";

	[Export]
	public string DoorModelName { get; set; } = "EDOOR";

	[ExportToolButton("Rebuild doors")]
	public Callable RebuildDoorsButton => Callable.From(RebuildDoors);

	public void RebuildDoors()
	{
		foreach (var child in GetChildren())
		{
			child.Free();
		}

		if (!ResourceLoader.Exists(DoorScenePath))
		{
			GD.PushError($"DoorsFill: scene not found: {DoorScenePath}");
			return;
		}

		var scene = ResourceLoader.Load<PackedScene>(DoorScenePath);
		if (scene is null)
		{
			GD.PushError($"DoorsFill: could not load: {DoorScenePath}");
			return;
		}

		if (!global::Godot.FileAccess.FileExists(DoorsDataFilePath))
		{
			GD.PushError($"DoorsFill: file not found: {DoorsDataFilePath}");
			return;
		}

		string text;
		try
		{
			text = File.ReadAllText(DoorsDataFilePath);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"DoorsFill: File.ReadAllText failed ({ex.Message}), falling back to Godot FileAccess");
			text = global::Godot.FileAccess.GetFileAsString(DoorsDataFilePath);
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			GD.PushWarning($"DoorsFill: empty file: {DoorsDataFilePath}");
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
		var duplicateRowsSkipped = 0;
		var rowsConsidered = 0;
		var rowsParsed = 0;
		var rowsSkippedNotEntrance = 0;
		var rowsSkippedWeirdTarget = 0;
		var spawned = 0;
		var parseErrors = 0;

		// Parse from the bottom so later file rows "win" when deduping by position.
		var lines = text.Split('\n');
		for (var i = lines.Length - 1; i >= 0; i--)
		{
			var lineNumber = i + 1;
			var line = lines[i].TrimEnd('\r');
			if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
			{
				continue;
			}

			rowsConsidered++;

			var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 11)
			{
				parseErrors++;
				GD.PushWarning($"DoorsFill: line {lineNumber}: expected ≥11 columns, skipping");
				continue;
			}

			if (!TryParseId(parts[0].Trim(), out var id) || id < 100)
			{
				parseErrors++;
				GD.PushWarning($"DoorsFill: line {lineNumber}: bad ID '{parts[0]}', skipping");
				continue;
			}

			var doorType = parts[1].Trim();
			if (!doorType.Equals(DoorEntranceTypeValue, StringComparison.OrdinalIgnoreCase))
			{
				rowsSkippedNotEntrance++;
				continue;
			}

			if (!TryParseDouble(parts[3], out var x)
				|| !TryParseDouble(parts[4], out var y)
				|| !TryParseDouble(parts[5], out var z))
			{
				parseErrors++;
				GD.PushWarning($"DoorsFill: line {lineNumber}: bad X/Y/Z, skipping");
				continue;
			}

			if (!TryParseAngle(parts[6], out var angleEncoded))
			{
				parseErrors++;
				GD.PushWarning($"DoorsFill: line {lineNumber}: bad Angle, skipping");
				continue;
			}

			if (!TryParseInt(parts[7], out var doorIdOrSentinel))
			{
				parseErrors++;
				GD.PushWarning($"DoorsFill: line {lineNumber}: bad DoorId '{parts[7]}', skipping");
				continue;
			}

			var hasTarget = doorIdOrSentinel == DoorHasTargetSentinel;
			if (!hasTarget && (doorIdOrSentinel < 5000 || doorIdOrSentinel > 5500))
			{
				parseErrors++;
				GD.PushWarning($"DoorsFill: line {lineNumber}: door id '{doorIdOrSentinel}' out of range (expected 5000..5500), skipping");
				continue;
			}

			double targetX = 0, targetY = 0, targetZ = 0;
			if (hasTarget)
			{
				if (LooksLikeWeirdTargetToken(parts[8]) || LooksLikeWeirdTargetToken(parts[9]) || LooksLikeWeirdTargetToken(parts[10]))
				{
					rowsSkippedWeirdTarget++;
					continue;
				}

				if (!TryParseDouble(parts[8], out targetX)
					|| !TryParseDouble(parts[9], out targetY)
					|| !TryParseDouble(parts[10], out targetZ))
				{
					parseErrors++;
					GD.PushWarning($"DoorsFill: line {lineNumber}: bad target X/Y/Z, skipping");
					continue;
				}

				if (IsWeirdTargetValue(targetX) || IsWeirdTargetValue(targetY) || IsWeirdTargetValue(targetZ) || ((int)targetX == 0 && (int)targetY == 0 && (int)targetZ == 0))
				{
					rowsSkippedWeirdTarget++;
					continue;
				}
			}

			rowsParsed++;

			var posKey = QuantizeSourcePosition(x, y, z);
			if (!seenSourcePositions.Add(posKey))
			{
				duplicateRowsSkipped++;
				continue;
			}

			var instance = scene.Instantiate<Door>();
			instance.Name = hasTarget ? $"Door_{id:X4}_Target" : $"Door_{id:X4}_{doorIdOrSentinel}";
			instance.Position = new Vector3((float)x, -(float)y, -(float)z);

			instance.ModelName = DoorModelName;
			instance.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				instance.ID = (ushort)id;
			}

			instance.DoorID = hasTarget ? DoorHasTargetSentinel : doorIdOrSentinel;
			instance.HasTarget = hasTarget;
			if (hasTarget)
			{
				instance.TargetX = targetX;
				instance.TargetY = targetY;
				instance.TargetZ = targetZ;
			}

			AddChild(instance);
			SetOwnerIfEditor(instance);
			spawned++;
		}

		GD.Print(
			$"DoorsFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, notEntranceSkipped={rowsSkippedNotEntrance}, weirdTargetSkipped={rowsSkippedWeirdTarget}, parseErrors={parseErrors}");
	}

	private static bool LooksLikeWeirdTargetToken(string s)
	{
		var t = s.Trim();
		return t.Contains("e-", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsWeirdTargetValue(double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v))
		{
			return true;
		}

		return Math.Abs(v) > 10000.0;
	}

	private static (long Qx, long Qy, long Qz) QuantizeSourcePosition(double x, double y, double z)
	{
		const double scale = 10000.0;
		return (
			(long)Math.Round(x * scale),
			(long)Math.Round(y * scale),
			(long)Math.Round(z * scale));
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

	private static bool TryParseInt(string s, out int v) =>
		int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

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
