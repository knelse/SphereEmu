using System.Globalization;
using Godot;
using SphServer.Helpers;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: reads tab/space-separated castle tablet rows (ID, skip, skip, X, Y, Z, Angle, CastleID),
/// clears existing children, and instances <see cref="WorldObjectScenePath"/> per row.
/// Source space matches terrain: <c>(x,y,z)_src ↦ (x,-y,-z)</c>.
/// Rows with duplicate source coordinates (same X, Y, Z columns) are skipped after the first occurrence.
/// </summary>
[Tool]
public partial class CastleTabletsFill : Node3D
{
	[Export]
	public string CastleTabletsDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\castle_tablets.txt";

	[Export]
	public string WorldObjectScenePath { get; set; } = "res://Godot/Scenes/castle_tablet.tscn";

	[Export]
	public string TabletModelName { get; set; } = "cs_table";

	[ExportToolButton("Rebuild castle tablets")]
	public Callable RebuildCastleTabletsButton => Callable.From(RebuildCastleTablets);

	public void RebuildCastleTablets()
	{
		foreach (var child in GetChildren())
		{
			child.Free();
		}

		if (!ResourceLoader.Exists(WorldObjectScenePath))
		{
			GD.PushError($"CastleTabletsFill: scene not found: {WorldObjectScenePath}");
			return;
		}

		var scene = ResourceLoader.Load<PackedScene>(WorldObjectScenePath);
		if (scene is null)
		{
			GD.PushError($"CastleTabletsFill: could not load: {WorldObjectScenePath}");
			return;
		}

		if (!global::Godot.FileAccess.FileExists(CastleTabletsDataFilePath))
		{
			GD.PushError($"CastleTabletsFill: file not found: {CastleTabletsDataFilePath}");
			return;
		}

		string text;
		try
		{
			text = File.ReadAllText(CastleTabletsDataFilePath);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"CastleTabletsFill: File.ReadAllText failed ({ex.Message}), falling back to Godot FileAccess");
			text = global::Godot.FileAccess.GetFileAsString(CastleTabletsDataFilePath);
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			GD.PushWarning($"CastleTabletsFill: empty file: {CastleTabletsDataFilePath}");
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

			var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 8)
			{
				parseErrors++;
				GD.PushWarning($"CastleTabletsFill: line {lineNumber}: expected ≥8 columns, skipping");
				continue;
			}

			if (!TryParseId(parts[0].Trim(), out var id))
			{
				parseErrors++;
				GD.PushWarning($"CastleTabletsFill: line {lineNumber}: bad ID '{parts[0]}', skipping");
				continue;
			}

			if (!TryParseDouble(parts[3], out var x)
				|| !TryParseDouble(parts[4], out var y)
				|| !TryParseDouble(parts[5], out var z))
			{
				parseErrors++;
				GD.PushWarning($"CastleTabletsFill: line {lineNumber}: bad X/Y/Z, skipping");
				continue;
			}

			if (!TryParseAngle(parts[6], out var angleEncoded))
			{
				parseErrors++;
				GD.PushWarning($"CastleTabletsFill: line {lineNumber}: bad Angle, skipping");
				continue;
			}

			if (!TryParseCastleId(parts[7], out var castle))
			{
				parseErrors++;
				GD.PushWarning($"CastleTabletsFill: line {lineNumber}: bad CastleID '{parts[7]}', skipping");
				continue;
			}

			rowsParsed++;

			var posKey = QuantizeSourcePosition(x, y, z);
			if (!seenSourcePositions.Add(posKey))
			{
				duplicateRowsSkipped++;
				continue;
			}

			var instance = scene.Instantiate<CastleTablet>();
			instance.Name = $"CastleTablet_{id:X4}_{castle}";
			instance.Position = new Vector3((float)x, -(float)y, -(float)z);

			instance.ModelName = TabletModelName;
			instance.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				instance.ID = (ushort)id;
			}
			instance.Castle = castle;

			AddChild(instance);
			SetOwnerIfEditor(instance);
			spawned++;
		}

		GD.Print(
			$"CastleTabletsFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, parseErrors={parseErrors}");
	}

	private static (long Qx, long Qy, long Qz) QuantizeSourcePosition(double x, double y, double z)
	{
		const double scale = 10000.0;
		return (
			(long)Math.Round(x * scale),
			(long)Math.Round(y * scale),
			(long)Math.Round(z * scale));
	}

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
