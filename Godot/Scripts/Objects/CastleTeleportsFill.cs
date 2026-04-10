using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: reads tab/space-separated castle teleport rows (ID, skip, skip, X, Y, Z, Angle),
/// clears existing children, and instances <see cref="CastleTeleportScenePath"/> per row.
/// </summary>
[Tool]
public partial class CastleTeleportsFill : Node3D
{
	private const string CastleTeleportTypeValue = "CastleTeleport";

	[Export]
	public string CastleTeleportsDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\castle_teleports.txt";

	[Export]
	public string CastleTeleportScenePath { get; set; } = "res://Godot/Scenes/castle_teleport.tscn";

	[ExportToolButton("Rebuild castle teleports")]
	public Callable RebuildCastleTeleportsButton => Callable.From(RebuildCastleTeleports);

	public void RebuildCastleTeleports()
	{
		foreach (var child in GetChildren())
		{
			child.Free();
		}

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(CastleTeleportScenePath, "CastleTeleportsFill", out var scene))
		{
			return;
		}

		if (!WorldObjectDumpFillCommon.TryReadTextFile(CastleTeleportsDataFilePath, "CastleTeleportsFill", out var text))
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

			if (!WorldObjectDumpFillCommon.MatchesTypeTokenIfPresent(parts, typeIndex: 1, expectedTypeValue: CastleTeleportTypeValue))
			{
				rowsSkippedNotMatchingType++;
				continue;
			}

			if (parts.Length < 7)
			{
				parseErrors++;
				GD.PushWarning($"CastleTeleportsFill: line {lineNumber}: expected ≥7 columns, skipping");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y, out var z, out var angleEncoded) || id < 100)
			{
				parseErrors++;
				GD.PushWarning($"CastleTeleportsFill: line {lineNumber}: parse failed, skipping");
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

			var instance = scene!.Instantiate<CastleTeleport>();
			instance.Name = WorldObjectDumpFillCommon.BuildPlacementName("CastleTeleport", id, x, y, z);
			instance.Position = new Vector3((float)x, -(float)y, -(float)z);
			instance.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				instance.ID = (ushort)id;
			}

			instance.ObjectType = ObjectType.CastleTeleport;

			AddChild(instance);
			WorldObjectDumpFillCommon.SetOwnerIfEditor(this, instance);
			spawned++;
		}

		GD.Print(
			$"CastleTeleportsFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, notTypeSkipped={rowsSkippedNotMatchingType}, weirdCoordSkipped={rowsSkippedWeirdCoords}, parseErrors={parseErrors}");
	}
}

