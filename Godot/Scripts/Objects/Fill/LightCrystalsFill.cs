using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.Fill;

/// <summary>
/// Editor tool: rebuilds light crystal objects from dump files:
/// - <see cref="LightCrystalsDataFilePath"/> (ObjectType.LightCrystal)
/// - <see cref="LightCrystalsYellowDataFilePath"/> (ObjectType.LightCrystalYellow)
/// </summary>
[Tool]
public partial class LightCrystalsFill : Node3D
{
	private const string LightCrystalTypeValue = "LightCrystal";
	private const string LightCrystalYellowTypeValue = "LightCrystalYellow";

	[Export]
	public string LightCrystalsDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\light_crystals.txt";

	[Export]
	public string LightCrystalsYellowDataFilePath { get; set; } = @"d:\SphereDev\_sphereDumps\light_crystals_yellow.txt";

	[Export]
	public string LightCrystalScenePath { get; set; } = "res://Godot/Scenes/item_light_crystal.tscn";

	[ExportToolButton("Rebuild light crystals")]
	public Callable RebuildLightCrystalsButton => Callable.From(RebuildLightCrystals);

	public void RebuildLightCrystals()
	{
		WorldObjectDumpFillCommon.ClearRebuildableChildren(this);

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(LightCrystalScenePath, "LightCrystalsFill", out var scene))
		{
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
		WorldObjectDumpFillCommon.SeedSeenSourcePositions(this, seenSourcePositions);
		var stats = new Stats();

		RebuildFromFile(
			LightCrystalsDataFilePath,
			scene!,
			ObjectType.LightCrystal,
			LightCrystalTypeValue,
			"LightCrystal",
			seenSourcePositions,
			ref stats);

		RebuildFromFile(
			LightCrystalsYellowDataFilePath,
			scene,
			ObjectType.LightCrystalYellow,
			LightCrystalYellowTypeValue,
			"LightCrystalYellow",
			seenSourcePositions,
			ref stats);

		GD.Print(
			$"LightCrystalsFill: considered={stats.RowsConsidered}, parsed={stats.RowsParsed}, spawned={stats.Spawned}, dupSkipped={stats.DuplicateRowsSkipped}, notTypeSkipped={stats.RowsSkippedNotMatchingType}, weirdCoordSkipped={stats.RowsSkippedWeirdCoords}, parseErrors={stats.ParseErrors}");
	}

	private void RebuildFromFile(
		string path,
		PackedScene scene,
		ObjectType objectType,
		string expectedTypeValue,
		string objectTypeNameForNaming,
		HashSet<(long Qx, long Qy, long Qz)> seenSourcePositions,
		ref Stats stats)
	{
		if (!WorldObjectDumpFillCommon.TryReadTextFile(path, "LightCrystalsFill", out var text))
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
				GD.PushWarning($"LightCrystalsFill: {expectedTypeValue} line {lineNumber}: expected ≥7 columns, skipping");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y, out var z, out var angleEncoded) || id < 100)
			{
				stats.ParseErrors++;
				GD.PushWarning($"LightCrystalsFill: {expectedTypeValue} line {lineNumber}: parse failed, skipping");
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
				GD.PushWarning($"LightCrystalsFill: scene root is not a WorldObject ({scene.ResourcePath}); skipping row {id:X4}");
				continue;
			}

			wo.Name = WorldObjectDumpFillCommon.BuildPlacementName(objectTypeNameForNaming, id, x, y, z);
			wo.Position = new Vector3((float)x, -(float)y, -(float)z);
			wo.Angle = angleEncoded;
			if (id is >= 0 and <= ushort.MaxValue)
			{
				wo.ID = (ushort)id;
			}

			wo.ObjectType = objectType;
			wo.ModelName = "mn_quartz";

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
