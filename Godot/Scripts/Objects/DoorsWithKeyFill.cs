using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: reads tab/space-separated door-with-key rows (ID, skip, skip, X, Y, Z, Angle, SubtypeID),
/// clears existing children, and instances <see cref="DoorWithKeyScenePath"/> per row.
/// </summary>
[Tool]
public partial class DoorsWithKeyFill : Node3D
{
	private const string DoorEntranceWithKeyTypeValue = "DoorEntranceWithKey";

	[Export]
	public string DoorsWithKeyDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\doors_with_key.txt";

	[Export]
	public string DoorWithKeyScenePath { get; set; } = "res://Godot/Scenes/door_with_key.tscn";

	[ExportToolButton("Rebuild doors with key")]
	public Callable RebuildDoorsWithKeyButton => Callable.From(RebuildDoorsWithKey);

	public void RebuildDoorsWithKey()
	{
		foreach (var child in GetChildren())
		{
			child.Free();
		}

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(DoorWithKeyScenePath, "DoorsWithKeyFill", out var scene))
		{
			return;
		}

		if (!WorldObjectDumpFillCommon.TryReadTextFile(DoorsWithKeyDataFilePath, "DoorsWithKeyFill", out var text))
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

			if (!WorldObjectDumpFillCommon.MatchesTypeTokenIfPresent(parts, typeIndex: 1, expectedTypeValue: DoorEntranceWithKeyTypeValue))
			{
				rowsSkippedNotMatchingType++;
				continue;
			}

			if (parts.Length < 8)
			{
				parseErrors++;
				GD.PushWarning($"DoorsWithKeyFill: line {lineNumber}: expected ≥8 columns, skipping");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y, out var z, out var angleEncoded) || id < 100)
			{
				parseErrors++;
				GD.PushWarning($"DoorsWithKeyFill: line {lineNumber}: parse failed, skipping");
				continue;
			}

			if (WorldObjectDumpFillCommon.ShouldSkipWeirdCoords(parts[3], parts[4], parts[5], x, y, z))
			{
				rowsSkippedWeirdCoords++;
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseInt(parts[7], out var subtypeId))
			{
				parseErrors++;
				GD.PushWarning($"DoorsWithKeyFill: line {lineNumber}: bad SubtypeID '{parts[7]}', skipping");
				continue;
			}

			rowsParsed++;

			var posKey = WorldObjectDumpFillCommon.QuantizeSourcePosition(x, y, z);
			if (!seenSourcePositions.Add(posKey))
			{
				duplicateRowsSkipped++;
				continue;
			}

			var instance = scene!.Instantiate<DoorWithKey>();
			instance.Name = WorldObjectDumpFillCommon.BuildPlacementName("DoorWithKey", id, x, y, z);
			instance.Position = new Vector3((float)x, -(float)y, -(float)z);
			instance.Angle = angleEncoded;
			instance.SubtypeID = subtypeId;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				instance.ID = (ushort)id;
			}

			instance.ObjectType = ObjectType.DoorEntranceWithKey;

			AddChild(instance);
			WorldObjectDumpFillCommon.SetOwnerIfEditor(this, instance);
			spawned++;
		}

		GD.Print(
			$"DoorsWithKeyFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, notTypeSkipped={rowsSkippedNotMatchingType}, weirdCoordSkipped={rowsSkippedWeirdCoords}, parseErrors={parseErrors}");
	}
}
