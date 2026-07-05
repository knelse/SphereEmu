using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
///     Editor / headless node that builds bake-only physics colliders for terrain GridMap tiles and
///     all terrain object placements (plants, rocks, buildings, props).
/// </summary>
[Tool]
public partial class TerrainPhysicsColliderFill : Node3D
{
    [Export]
    public NodePath TerrainGridFillPath { get; set; } = new("../TerrainGridFill");

    [Export]
    public string ObjectDataDirectory { get; set; } = "res://Godot/Terrain/ObjectDataJson/";

    [Export]
    public string ModelsDirectory { get; set; } = "res://Godot/Models/";

    [ExportToolButton("Rebuild physics colliders")]
    public Callable RebuildPhysicsCollidersButton => Callable.From(RebuildPhysicsColliders);

    public TerrainPhysicsColliderBuilder.BuildStats? LastBuildStats { get; private set; }

    public bool RebuildPhysicsColliders()
    {
        var gridFill = ResolveTerrainGridFill();
        if (gridFill is null)
        {
            GD.PushError("TerrainPhysicsColliderFill: TerrainGridFill not found.");
            return false;
        }

        if (!gridFill.RebuildTerrainGrid())
        {
            GD.PushError("TerrainPhysicsColliderFill: terrain GridMap rebuild failed.");
            return false;
        }

        var terrain = gridFill.GetNodeOrNull<GridMap>(TerrainGridFill.TerrainNodeName);
        if (terrain is null)
        {
            GD.PushError("TerrainPhysicsColliderFill: Terrain GridMap missing.");
            return false;
        }

        LastBuildStats = TerrainPhysicsColliderBuilder.Build(
            this,
            terrain,
            ObjectDataDirectory,
            ModelsDirectory);

        GD.Print(
            $"TerrainPhysicsColliderFill: terrain mesh items={LastBuildStats.TerrainMeshItems}, "
            + $"cells={LastBuildStats.TerrainCells}, object bodies={LastBuildStats.ObjectBodies} "
            + $"(plants={LastBuildStats.Plants}, rocks={LastBuildStats.Rocks}, "
            + $"buildings/props={LastBuildStats.BuildingsAndProps}, skipped={LastBuildStats.SkippedPlacements}).");
        return true;
    }

    public GridMap? ResolveTerrainGridMap()
    {
        var gridFill = ResolveTerrainGridFill();
        return gridFill?.GetNodeOrNull<GridMap>(TerrainGridFill.TerrainNodeName);
    }

    private TerrainGridFill? ResolveTerrainGridFill()
    {
        if (TerrainGridFillPath.IsEmpty)
        {
            return GetParent()?.GetNodeOrNull<TerrainGridFill>(TerrainGridFill.TerrainNodeName)
                ?? GetParent() as TerrainGridFill;
        }

        return GetNodeOrNull<TerrainGridFill>(TerrainGridFillPath);
    }
}
