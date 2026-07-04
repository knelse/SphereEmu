using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.Fill;

/// <summary>
/// Editor tool: reads tab/space-separated teleport in-dungeon rows (ID, skip, skip, X, Y, Z, Angle),
/// clears existing children, and instances <see cref="TeleportInDungeonScenePath"/> per row.
/// </summary>
[Tool]
public partial class TeleportInDungeonFill : Node3D
{
	private const string TeleportInDungeonTypeValue = "TeleportInDungeon";

	[Export]
	public string TeleportInDungeonDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\teleport_in_dungeon.txt";

	[Export]
	public string TeleportInDungeonScenePath { get; set; } = "res://Godot/Scenes/teleport_in_dungeon.tscn";

	[ExportToolButton("Rebuild in-dungeon teleports")]
	public Callable RebuildTeleportInDungeonButton => Callable.From(RebuildTeleportInDungeon);

	public void RebuildTeleportInDungeon()
	{
		WorldObjectDumpFillCommon.ClearRebuildableChildren(this);

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(TeleportInDungeonScenePath, "TeleportInDungeonFill", out var scene))
		{
			return;
		}

		if (!WorldObjectDumpFillCommon.TryReadTextFile(TeleportInDungeonDataFilePath, "TeleportInDungeonFill", out var text))
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

			if (!WorldObjectDumpFillCommon.MatchesTypeTokenIfPresent(parts, typeIndex: 1, expectedTypeValue: TeleportInDungeonTypeValue))
			{
				rowsSkippedNotMatchingType++;
				continue;
			}

			if (parts.Length < 7)
			{
				parseErrors++;
				GD.PushWarning($"TeleportInDungeonFill: line {lineNumber}: expected ≥7 columns, skipping");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y, out var z, out var angleEncoded) || id < 100)
			{
				parseErrors++;
				GD.PushWarning($"TeleportInDungeonFill: line {lineNumber}: parse failed, skipping");
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

			var instance = scene!.Instantiate<TeleportInDungeon>();
			instance.Name = WorldObjectDumpFillCommon.BuildPlacementName("TeleportInDungeon", id, x, y, z);
			instance.Position = new Vector3((float)x, -(float)y, -(float)z);
			instance.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				instance.ID = (ushort)id;
			}

			instance.ObjectType = ObjectType.TeleportInDungeon;

			AddChild(instance);
			WorldObjectDumpFillCommon.SetOwnerIfEditor(this, instance);
			spawned++;
		}

		GD.Print(
			$"TeleportInDungeonFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, notTypeSkipped={rowsSkippedNotMatchingType}, weirdCoordSkipped={rowsSkippedWeirdCoords}, parseErrors={parseErrors}");
	}
}
