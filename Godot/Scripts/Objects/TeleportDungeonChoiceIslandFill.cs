using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
///     Editor tool: reads tab/space-separated dungeon fast-travel teleport rows (ID, skip, skip, X, Y, Z, Angle),
///     clears existing children, and instances <see cref="TeleportDungeonChoiceIslandScenePath" /> per row.
/// </summary>
[Tool]
public partial class TeleportDungeonChoiceIslandFill : Node3D
{
    private const string TeleportDungeonChoiceIslandTypeValue = "TeleportDungeonChoiceIsland";

    [Export]
    public string TeleportDungeonChoiceIslandDataFilePath { get; set; } =
        @"d:\SphereDev\_sphereDumps\teleport_dungeon_choice_island.txt";

    [Export]
    public string TeleportDungeonChoiceIslandScenePath { get; set; } =
        "res://Godot/Scenes/teleport_dungeon_choice_island.tscn";

    [ExportToolButton("Rebuild choice dungeon teleports")]
    public Callable RebuildTeleportDungeonChoiceIslandButton => Callable.From(RebuildTeleportDungeonChoiceIsland);

    public void RebuildTeleportDungeonChoiceIsland()
    {
        WorldObjectDumpFillCommon.ClearRebuildableChildren(this);

        if (!WorldObjectDumpFillCommon.TryLoadPackedScene(TeleportDungeonChoiceIslandScenePath,
                "TeleportDungeonChoiceIslandFill", out var scene))
        {
            return;
        }

        if (!WorldObjectDumpFillCommon.TryReadTextFile(TeleportDungeonChoiceIslandDataFilePath,
                "TeleportDungeonChoiceIslandFill", out var text))
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

            if (!WorldObjectDumpFillCommon.MatchesTypeTokenIfPresent(parts, 1, TeleportDungeonChoiceIslandTypeValue))
            {
                rowsSkippedNotMatchingType++;
                continue;
            }

            if (parts.Length < 7)
            {
                parseErrors++;
                GD.PushWarning($"TeleportDungeonChoiceIslandFill: line {lineNumber}: expected ≥7 columns, skipping");
                continue;
            }

            if (!WorldObjectDumpFillCommon.TryParseCommonPlacementColumns(parts, out var id, out var x, out var y,
                    out var z, out var angleEncoded) || id < 100)
            {
                parseErrors++;
                GD.PushWarning($"TeleportDungeonChoiceIslandFill: line {lineNumber}: parse failed, skipping");
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

            var instance = scene!.Instantiate<TeleportDungeonChoiceIsland>();
            instance.Name = WorldObjectDumpFillCommon.BuildPlacementName("TeleportDungeonChoiceIsland", id, x, y, z);
            instance.Position = new Vector3((float)x, -(float)y, -(float)z);
            instance.Angle = angleEncoded;
            if (id is >= 0 and <= ushort.MaxValue)
            {
                instance.ID = (ushort)id;
            }

            instance.ObjectType = ObjectType.TeleportDungeonChoiceIsland;

            AddChild(instance);
            WorldObjectDumpFillCommon.SetOwnerIfEditor(this, instance);
            spawned++;
        }

        GD.Print(
            $"TeleportDungeonChoiceIslandFill: considered={rowsConsidered}, parsed={rowsParsed}, spawned={spawned}, dupSkipped={duplicateRowsSkipped}, notTypeSkipped={rowsSkippedNotMatchingType}, weirdCoordSkipped={rowsSkippedWeirdCoords}, parseErrors={parseErrors}");
    }
}