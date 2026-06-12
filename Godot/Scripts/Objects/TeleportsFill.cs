using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: rebuilds teleport-related objects from dump files:
/// - <see cref="TeleportsDataFilePath"/> (ObjectType.Teleport)
/// - <see cref="TargetTeleportsDataFilePath"/> (ObjectType.TeleportWithTarget; expects optional SubtypeID as column 8)
/// - <see cref="TournamentTeleportsDataFilePath"/> (ObjectType.TournamentTeleport)
/// </summary>
[Tool]
public partial class TeleportsFill : Node3D
{
	private const string TeleportTypeValue = "Teleport";
	private const string TeleportWithTargetTypeValue = "TeleportWithTarget";
	private const string TournamentTeleportTypeValue = "TournamentTeleport";

	[Export]
	public string TeleportsDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\teleports.txt";

	[Export]
	public string TargetTeleportsDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\target_tps.txt";

	[Export]
	public string TournamentTeleportsDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\teleport_tournament.txt";

	[Export]
	public string TeleportScenePath { get; set; } = "res://Godot/Scenes/teleport.tscn";

	[Export]
	public string TargetTeleportScenePath { get; set; } = "res://Godot/Scenes/teleport_with_target.tscn";

	[Export]
	public string TournamentTeleportScenePath { get; set; } = "res://Godot/Scenes/teleport_tournament.tscn";

	[ExportToolButton("Rebuild teleports")]
	public Callable RebuildTeleportsButton => Callable.From(RebuildTeleports);

	public void RebuildTeleports()
	{
		WorldObjectDumpFillCommon.ClearRebuildableChildren(this);

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(TeleportScenePath, "TeleportsFill", out var teleportScene)
			|| !WorldObjectDumpFillCommon.TryLoadPackedScene(TargetTeleportScenePath, "TeleportsFill", out var targetTeleportScene)
			|| !WorldObjectDumpFillCommon.TryLoadPackedScene(TournamentTeleportScenePath, "TeleportsFill", out var tournamentTeleportScene))
		{
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
		WorldObjectDumpFillCommon.SeedSeenSourcePositions(this, seenSourcePositions);
		var stats = new Stats();

		RebuildSimple(TeleportsDataFilePath, teleportScene!, ObjectType.Teleport, expectedTypeValue: TeleportTypeValue, objectTypeNameForNaming: "Teleport", seenSourcePositions, ref stats);
		RebuildSimple(TournamentTeleportsDataFilePath, tournamentTeleportScene!, ObjectType.TournamentTeleport, expectedTypeValue: TournamentTeleportTypeValue, objectTypeNameForNaming: "TournamentTeleport", seenSourcePositions, ref stats);
		RebuildTargetTeleports(TargetTeleportsDataFilePath, targetTeleportScene!, seenSourcePositions, ref stats);

		GD.Print(
			$"TeleportsFill: considered={stats.RowsConsidered}, parsed={stats.RowsParsed}, spawned={stats.Spawned}, dupSkipped={stats.DuplicateRowsSkipped}, notTypeSkipped={stats.RowsSkippedNotMatchingType}, weirdCoordSkipped={stats.RowsSkippedWeirdCoords}, parseErrors={stats.ParseErrors}");
	}

	private void RebuildSimple(
		string path,
		PackedScene scene,
		ObjectType objectType,
		string expectedTypeValue,
		string objectTypeNameForNaming,
		HashSet<(long Qx, long Qy, long Qz)> seenSourcePositions,
		ref Stats stats)
	{
		if (!WorldObjectDumpFillCommon.TryReadTextFile(path, "TeleportsFill", out var text))
		{
			return;
		}

		foreach (var (lineNumber, parts) in WorldObjectDumpFillCommon.EnumerateDataLinesBottomUp(text))
		{
			stats.RowsConsidered++;

			if (!WorldObjectDumpFillCommon.MatchesTypeTokenIfPresent(parts, typeIndex: 1, expectedTypeValue: expectedTypeValue))
			{
				stats.RowsSkippedNotMatchingType++;
				continue;
			}

			if (parts.Length < 7)
			{
				stats.ParseErrors++;
				GD.PushWarning($"TeleportsFill: {expectedTypeValue} line {lineNumber}: expected ≥7 columns, skipping");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y, out var z, out var angleEncoded) || id < 100)
			{
				stats.ParseErrors++;
				GD.PushWarning($"TeleportsFill: {expectedTypeValue} line {lineNumber}: parse failed, skipping");
				continue;
			}

			if (WorldObjectDumpFillCommon.ShouldSkipWeirdCoords(parts[3], parts[4], parts[5], x, y, z))
			{
				stats.RowsSkippedWeirdCoords++;
				continue;
			}

			stats.RowsParsed++;

			var posKey = WorldObjectDumpFillCommon.QuantizeSourcePosition(x, y, z);
			if (!seenSourcePositions.Add(posKey))
			{
				stats.DuplicateRowsSkipped++;
				continue;
			}

			var instance = scene.Instantiate<Node3D>();
			if (instance is not WorldObject wo)
			{
				GD.PushWarning($"TeleportsFill: scene root is not a WorldObject ({scene.ResourcePath}); skipping row {id:X4}");
				continue;
			}

			wo.Name = WorldObjectDumpFillCommon.BuildPlacementName(objectTypeNameForNaming, id, x, y, z);
			wo.Position = new Vector3((float)x, -(float)y, -(float)z);
			wo.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				wo.ID = (ushort)id;
			}

			// Ensure the correct type even if scene/script defaults differ.
			wo.ObjectType = objectType;

			AddChild(wo);
			WorldObjectDumpFillCommon.SetOwnerIfEditor(this, wo);
			stats.Spawned++;
		}
	}

	private void RebuildTargetTeleports(
		string path,
		PackedScene scene,
		HashSet<(long Qx, long Qy, long Qz)> seenSourcePositions,
		ref Stats stats)
	{
		if (!WorldObjectDumpFillCommon.TryReadTextFile(path, "TeleportsFill", out var text))
		{
			return;
		}

		foreach (var (lineNumber, parts) in WorldObjectDumpFillCommon.EnumerateDataLinesBottomUp(text))
		{
			stats.RowsConsidered++;

			if (!WorldObjectDumpFillCommon.MatchesTypeTokenIfPresent(parts, typeIndex: 1, expectedTypeValue: TeleportWithTargetTypeValue))
			{
				stats.RowsSkippedNotMatchingType++;
				continue;
			}

			if (parts.Length < 7)
			{
				stats.ParseErrors++;
				GD.PushWarning($"TeleportsFill: {TeleportWithTargetTypeValue} line {lineNumber}: expected ≥7 columns, skipping");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y, out var z, out var angleEncoded) || id < 100)
			{
				stats.ParseErrors++;
				GD.PushWarning($"TeleportsFill: {TeleportWithTargetTypeValue} line {lineNumber}: parse failed, skipping");
				continue;
			}

			if (WorldObjectDumpFillCommon.ShouldSkipWeirdCoords(parts[3], parts[4], parts[5], x, y, z))
			{
				stats.RowsSkippedWeirdCoords++;
				continue;
			}

			var subtypeId = 0;
			if (parts.Length >= 8 && WorldObjectDumpFillCommon.TryParseInt(parts[7], out var st))
			{
				subtypeId = st;
			}

			stats.RowsParsed++;

			var posKey = WorldObjectDumpFillCommon.QuantizeSourcePosition(x, y, z);
			if (!seenSourcePositions.Add(posKey))
			{
				stats.DuplicateRowsSkipped++;
				continue;
			}

			var instance = scene.Instantiate<Node3D>();
			if (instance is not WorldObject wo)
			{
				GD.PushWarning($"TeleportsFill: target scene root is not a WorldObject ({scene.ResourcePath}); skipping row {id:X4}");
				continue;
			}

			wo.Name = WorldObjectDumpFillCommon.BuildPlacementName("TeleportWithTarget", id, x, y, z);
			wo.Position = new Vector3((float)x, -(float)y, -(float)z);
			wo.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				wo.ID = (ushort)id;
			}

			wo.ObjectType = ObjectType.TeleportWithTarget;
			if (wo is TeleportWithTarget tpt)
			{
				tpt.SubtypeID = subtypeId;
			}

			AddChild(wo);
			WorldObjectDumpFillCommon.SetOwnerIfEditor(this, wo);
			stats.Spawned++;
		}
	}

	private struct Stats
	{
		public int RowsConsidered;
		public int RowsParsed;
		public int Spawned;
		public int DuplicateRowsSkipped;
		public int RowsSkippedNotMatchingType;
		public int RowsSkippedWeirdCoords;
		public int ParseErrors;
	}
}

