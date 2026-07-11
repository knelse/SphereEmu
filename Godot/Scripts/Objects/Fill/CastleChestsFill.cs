using System;
using System.Collections.Generic;
using SphServer.Helpers;
using System.IO;
using Godot;
using SphServer.Helpers;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.Fill;

/// <summary>
/// Editor tool: reads tab/space-separated castle chest rows (ID, skip, skip, X, Y, Z, Angle),
/// clears existing children, and instances <see cref="WorldObjectScenePath"/> per row.
/// Source space matches terrain: <c>(x,y,z)_src ↦ (x,-y,-z)</c>.
/// Rows with duplicate source coordinates (same X, Y, Z columns) are skipped after the first occurrence.
/// </summary>
[Tool]
public partial class CastleChestsFill : Node3D
{
	private const int BlackTowerChestId = 0x0C1D;

	[Export]
	public string CastleChestsDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\castle_chests.txt";

	[Export]
	public string WorldObjectScenePath { get; set; } = "res://Godot/Scenes/castle_chest.tscn";

	[Export]
	public string ChestModelName { get; set; } = "cs_chest";

	[ExportToolButton("Rebuild castle chests")]
	public Callable RebuildCastleChestsButton => Callable.From(RebuildCastleChests);

	public void RebuildCastleChests()
	{
		WorldObjectDumpFillCommon.ClearRebuildableChildren(this);

		if (!ResourceLoader.Exists(WorldObjectScenePath))
		{
			GD.PushError($"CastleChestsFill: scene not found: {WorldObjectScenePath}");
			return;
		}

		var scene = ResourceLoader.Load<PackedScene>(WorldObjectScenePath);
		if (scene is null)
		{
			GD.PushError($"CastleChestsFill: could not load: {WorldObjectScenePath}");
			return;
		}

		if (!global::Godot.FileAccess.FileExists(CastleChestsDataFilePath))
		{
			GD.PushError($"CastleChestsFill: file not found: {CastleChestsDataFilePath}");
			return;
		}

		string text;
		try
		{
			text = File.ReadAllText(CastleChestsDataFilePath);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"CastleChestsFill: File.ReadAllText failed ({ex.Message}), falling back to Godot FileAccess");
			text = global::Godot.FileAccess.GetFileAsString(CastleChestsDataFilePath);
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			GD.PushWarning($"CastleChestsFill: empty file: {CastleChestsDataFilePath}");
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
		WorldObjectDumpFillCommon.SeedSeenSourcePositions(this, seenSourcePositions);
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
			if (parts.Length < 7)
			{
				parseErrors++;
				GD.PushWarning($"CastleChestsFill: line {lineNumber}: expected ≥7 columns, skipping");
				continue;
			}

			if (!TryParseId(parts[0].Trim(), out var id))
			{
				parseErrors++;
				GD.PushWarning($"CastleChestsFill: line {lineNumber}: bad ID '{parts[0]}', skipping");
				continue;
			}

			if (!TryParseDouble(parts[3], out var x)
				|| !TryParseDouble(parts[4], out var y)
				|| !TryParseDouble(parts[5], out var z))
			{
				parseErrors++;
				GD.PushWarning($"CastleChestsFill: line {lineNumber}: bad X/Y/Z, skipping");
				continue;
			}

			if (!TryParseAngle(parts[6], out var angleEncoded))
			{
				parseErrors++;
				GD.PushWarning($"CastleChestsFill: line {lineNumber}: bad Angle, skipping");
				continue;
			}

			rowsParsed++;

			var posKey = QuantizeSourcePosition(x, y, z);
			if (!seenSourcePositions.Add(posKey))
			{
				duplicateRowsSkipped++;
				continue;
			}

			var localPos = new Vector3((float)x, -(float)y, -(float)z);
			Castles castle;
			if (IsBlackTowerChest(id, localPos))
			{
				castle = Castles.Черная_Башня;
			}
			else
			{
				CastleTabletLookup.TryGetNearestCastle(this, ToGlobal(localPos), out castle);
			}

			var instance = scene.Instantiate<CastleChest>();
			instance.Name = $"CastleChest_{(int)castle:00}_{castle} _{id:X4}";
			instance.Position = localPos;
			instance.Castle = castle;

			instance.ModelName = ChestModelName;
			instance.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				instance.ID = (ushort)id;
			}

			AddChild(instance);
			SetOwnerIfEditor(instance);
			spawned++;
		}

		GD.Print(
			$"CastleChestsFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, parseErrors={parseErrors}");
	}

	/// <summary>
	/// Chest 0x0C1D at scene (~2100, -1700, 3112) belongs to Черная Башня, not the nearest Hyperion tablet.
	/// </summary>
	private static bool IsBlackTowerChest(int id, Vector3 localPos) =>
		id == BlackTowerChestId
		&& (int)localPos.X == 2100
		&& (int)localPos.Y == -1700
		&& (int)localPos.Z == 3112;

	private static (long Qx, long Qy, long Qz) QuantizeSourcePosition(double x, double y, double z)
	{
		const double scale = 10000.0;
		return (
			(long)Math.Round(x * scale),
			(long)Math.Round(y * scale),
			(long)Math.Round(z * scale));
	}

	private static bool TryParseId(string s, out int id) => FileFormatCulture.TryParseHexInt(s, out id);

	private static bool TryParseDouble(string s, out double v) => FileFormatCulture.TryParseDouble(s, out v);

	private static bool TryParseAngle(string s, out int angle) => FileFormatCulture.TryParseAngle(s, out angle);

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
