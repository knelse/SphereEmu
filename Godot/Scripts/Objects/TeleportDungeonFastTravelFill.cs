using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: reads tab/space-separated dungeon fast-travel teleport rows (ID, skip, skip, X, Y, Z, Angle),
/// clears existing children, and instances <see cref="TeleportDungeonFastTravelScenePath"/> per row.
/// </summary>
[Tool]
public partial class TeleportDungeonFastTravelFill : Node3D
{
	private const string TeleportDungeonFastTravelTypeValue = "TeleportDungeonFastTravel";

	[Export]
	public string TeleportDungeonFastTravelDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\teleport_dungeon_fast_travel.txt";

	[Export]
	public string TeleportDungeonFastTravelScenePath { get; set; } = "res://Godot/Scenes/teleport_dungeon_fast_travel.tscn";

	[ExportToolButton("Rebuild dungeon fast-travel teleports")]
	public Callable RebuildTeleportDungeonFastTravelButton => Callable.From(RebuildTeleportDungeonFastTravel);

	public void RebuildTeleportDungeonFastTravel()
	{
		foreach (var child in GetChildren())
		{
			child.Free();
		}

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(TeleportDungeonFastTravelScenePath, "TeleportDungeonFastTravelFill", out var scene))
		{
			return;
		}

		if (!WorldObjectDumpFillCommon.TryReadTextFile(TeleportDungeonFastTravelDataFilePath, "TeleportDungeonFastTravelFill", out var text))
		{
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
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

			if (!WorldObjectDumpFillCommon.MatchesTypeTokenIfPresent(parts, typeIndex: 1, expectedTypeValue: TeleportDungeonFastTravelTypeValue))
			{
				rowsSkippedNotMatchingType++;
				continue;
			}

			if (parts.Length < 7)
			{
				parseErrors++;
				GD.PushWarning($"TeleportDungeonFastTravelFill: line {lineNumber}: expected ≥7 columns, skipping");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y, out var z, out var angleEncoded) || id < 100)
			{
				parseErrors++;
				GD.PushWarning($"TeleportDungeonFastTravelFill: line {lineNumber}: parse failed, skipping");
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

			var instance = scene!.Instantiate<TeleportDungeonFastTravel>();
			instance.Name = WorldObjectDumpFillCommon.BuildPlacementName("TeleportDungeonFastTravel", id, x, y, z);
			instance.Position = new Vector3((float)x, -(float)y, -(float)z);
			instance.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				instance.ID = (ushort)id;
			}

			instance.ObjectType = ObjectType.TeleportDungeonFastTravel;

			AddChild(instance);
			WorldObjectDumpFillCommon.SetOwnerIfEditor(this, instance);
			spawned++;
		}

		GD.Print(
			$"TeleportDungeonFastTravelFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, notTypeSkipped={rowsSkippedNotMatchingType}, weirdCoordSkipped={rowsSkippedWeirdCoords}, parseErrors={parseErrors}");
	}
}
