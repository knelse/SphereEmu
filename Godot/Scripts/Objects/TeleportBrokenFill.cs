using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: reads tab/space-separated teleport broken rows (ID, skip, skip, X, Y, Z, Angle),
/// clears existing children, and instances <see cref="TeleportBrokenScenePath"/> per row.
/// </summary>
[Tool]
public partial class TeleportBrokenFill : Node3D
{
	private const string TeleportBrokenTypeValue = "TeleportBroken";

	[Export]
	public string TeleportBrokenDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\teleport_broken.txt";

	[Export]
	public string TeleportBrokenScenePath { get; set; } = "res://Godot/Scenes/teleport_broken.tscn";

	[ExportToolButton("Rebuild broken teleports")]
	public Callable RebuildTeleportBrokenButton => Callable.From(RebuildTeleportBroken);

	public void RebuildTeleportBroken()
	{
		WorldObjectDumpFillCommon.ClearRebuildableChildren(this);

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(TeleportBrokenScenePath, "TeleportBrokenFill", out var scene))
		{
			return;
		}

		if (!WorldObjectDumpFillCommon.TryReadTextFile(TeleportBrokenDataFilePath, "TeleportBrokenFill", out var text))
		{
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
		WorldObjectDumpFillCommon.SeedSeenSourcePositions(this, seenSourcePositions);
		var duplicateRowsSkipped = 0;
		var rowsSkippedNotMatchingType = 0;
		var rowsSkippedWeirdCoords = 0;
		var rowsConsidered = 0;
		var rowsParsed = 0;
		var spawned = 0;
		var parseErrors = 0;

		foreach (var (lineNumber, parts) in WorldObjectDumpFillCommon.EnumerateDataLinesBottomUp(text))
		{
			rowsConsidered++;

			if (!WorldObjectDumpFillCommon.MatchesTypeTokenIfPresent(parts, typeIndex: 1, expectedTypeValue: TeleportBrokenTypeValue))
			{
				rowsSkippedNotMatchingType++;
				continue;
			}

			if (parts.Length < 7)
			{
				parseErrors++;
				GD.PushWarning($"TeleportBrokenFill: line {lineNumber}: expected ≥7 columns, skipping");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y, out var z, out var angleEncoded) || id < 100)
			{
				parseErrors++;
				GD.PushWarning($"TeleportBrokenFill: line {lineNumber}: parse failed, skipping");
				continue;
			}

			if (WorldObjectDumpFillCommon.ShouldSkipWeirdCoords(parts[3], parts[4], parts[5], x, y, z))
			{
				rowsSkippedWeirdCoords++;
				continue;
			}

			rowsParsed++;

			var posKey = WorldObjectDumpFillCommon.QuantizeSourcePosition(x, y, z);
			if (!seenSourcePositions.Add(posKey))
			{
				duplicateRowsSkipped++;
				continue;
			}

			var instance = scene!.Instantiate<TeleportBroken>();
			instance.Name = WorldObjectDumpFillCommon.BuildPlacementName("TeleportBroken", id, x, y, z);
			instance.Position = new Vector3((float)x, -(float)y, -(float)z);
			instance.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				instance.ID = (ushort)id;
			}

			instance.ObjectType = ObjectType.TeleportBroken;

			AddChild(instance);
			WorldObjectDumpFillCommon.SetOwnerIfEditor(this, instance);
			spawned++;
		}

		GD.Print(
			$"TeleportBrokenFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, notTypeSkipped={rowsSkippedNotMatchingType}, weirdCoordSkipped={rowsSkippedWeirdCoords}, parseErrors={parseErrors}");
	}
}

