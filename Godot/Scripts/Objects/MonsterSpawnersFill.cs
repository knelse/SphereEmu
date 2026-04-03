using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: reads tab-separated spawner rows (ID, Type, Spawn type, X, Y, Z, Angle), clears existing children,
/// and instances the scene from <see cref="MonsterSpawnerScenePath"/> per row. Source space matches terrain: <c>(x,y,z)_src ↦ (x,-y,-z)</c>;
/// yaw matches <see cref="SphServer.Sphere.Game.WorldObject.WorldObject"/> (<c>t0 = Angle * π / 128</c> radians on Y).
/// Rows with duplicate source coordinates (same X, Y, Z columns) are skipped after the first occurrence.
/// </summary>
[Tool]
public partial class MonsterSpawnersFill : Node3D
{
	[Export]
	public string SpawnerDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\mob_spawner.txt";

	[Export]
	public string MonsterSpawnerScenePath { get; set; } = "res://Godot/Scenes/monster_spawner.tscn";

	[ExportToolButton ("Rebuild monster spawners")]
	public Callable RebuildMonsterSpawnersButton => Callable.From (RebuildMonsterSpawners);

	/// <summary>Clears child instances and repopulates from <see cref="SpawnerDataFilePath"/>.</summary>
	public void RebuildMonsterSpawners ()
	{
		foreach (var child in GetChildren ())
		{
			child.Free ();
		}

		if (!ResourceLoader.Exists (MonsterSpawnerScenePath))
		{
			GD.PushError ($"MonsterSpawnersFill: scene not found: {MonsterSpawnerScenePath}");
			return;
		}

		var scene = ResourceLoader.Load<PackedScene> (MonsterSpawnerScenePath);
		if (scene is null)
		{
			GD.PushError ($"MonsterSpawnersFill: could not load: {MonsterSpawnerScenePath}");
			return;
		}

		if (!global::Godot.FileAccess.FileExists (SpawnerDataFilePath))
		{
			GD.PushError ($"MonsterSpawnersFill: file not found: {SpawnerDataFilePath}");
			return;
		}

		var text = global::Godot.FileAccess.GetFileAsString (SpawnerDataFilePath);
		if (string.IsNullOrWhiteSpace (text))
		{
			GD.PushWarning ($"MonsterSpawnersFill: empty file: {SpawnerDataFilePath}");
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)> ();
		var duplicateRowsSkipped = 0;
		var lineNumber = 0;
		foreach (var rawLine in text.Split ('\n'))
		{
			lineNumber++;
			var line = rawLine.TrimEnd ('\r');
			if (string.IsNullOrWhiteSpace (line) || line.TrimStart ().StartsWith ('#'))
			{
				continue;
			}

			var parts = line.Split ('\t');
			if (parts.Length < 7)
			{
				GD.PushWarning ($"MonsterSpawnersFill: line {lineNumber}: expected ≥7 tab-separated columns, skipping");
				continue;
			}

			if (!TryParseId (parts[0].Trim (), out var id))
			{
				GD.PushWarning ($"MonsterSpawnersFill: line {lineNumber}: bad ID '{parts[0]}', skipping");
				continue;
			}

			if (!TryParseDouble (parts[3], out var x)
			    || !TryParseDouble (parts[4], out var y)
			    || !TryParseDouble (parts[5], out var z))
			{
				GD.PushWarning ($"MonsterSpawnersFill: line {lineNumber}: bad X/Y/Z, skipping");
				continue;
			}

			if (!TryParseAngle (parts[6], out var angleEncoded))
			{
				GD.PushWarning ($"MonsterSpawnersFill: line {lineNumber}: bad Angle, skipping");
				continue;
			}

			var posKey = QuantizeSourcePosition (x, y, z);
			if (!seenSourcePositions.Add (posKey))
			{
				duplicateRowsSkipped++;
				continue;
			}

			var instance = scene.Instantiate<Node3D> ();
			instance.Name = $"MonsterSpawner_{id:X}";
			instance.Transform = BuildPlacementTransform ((float) x, (float) y, (float) z, angleEncoded);
			AddChild (instance);
			SetOwnerIfEditor (instance);
		}

		if (duplicateRowsSkipped > 0)
		{
			GD.PushWarning (
				$"MonsterSpawnersFill: skipped {duplicateRowsSkipped} duplicate row(s) with the same source X, Y, Z");
		}
	}

	/// <summary>Quantizes source-space coordinates so near-identical file values dedupe as one position.</summary>
	private static (long Qx, long Qy, long Qz) QuantizeSourcePosition (double x, double y, double z)
	{
		const double scale = 10000.0;
		return (
			(long) Math.Round (x * scale),
			(long) Math.Round (y * scale),
			(long) Math.Round (z * scale));
	}

	private static Transform3D BuildPlacementTransform (float x, float y, float z, int angleEncoded)
	{
		var pos = new Vector3 (x, -y, -z);
		var t0 = (float) (angleEncoded * Math.PI / 128.0);
		var basis = Basis.FromEuler (new Vector3 (0f, t0, 0f), EulerOrder.Yxz);
		return new Transform3D (basis, pos);
	}

	/// <summary>ID column is hex (optional <c>0x</c>), matching <c>NpcSpawnTscnWriter</c> spawn TSVs.</summary>
	private static bool TryParseId (string s, out int id)
	{
		s = s.Trim ();
		if (s.StartsWith ("0x", StringComparison.OrdinalIgnoreCase))
		{
			s = s[2..];
		}

		return int.TryParse (s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
	}

	private static bool TryParseDouble (string s, out double v) =>
		double.TryParse (s.Trim (), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

	private static bool TryParseAngle (string s, out int angle)
	{
		if (int.TryParse (s.Trim (), NumberStyles.Integer, CultureInfo.InvariantCulture, out angle))
		{
			return true;
		}

		if (double.TryParse (s.Trim (), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
		{
			angle = (int) Math.Round (d);
			return true;
		}

		angle = 0;
		return false;
	}

	private void SetOwnerIfEditor (Node node)
	{
		if (!Engine.IsEditorHint ())
		{
			return;
		}

		var root = GetTree ()?.EditedSceneRoot;
		node.Owner = root ?? this;
	}
}
