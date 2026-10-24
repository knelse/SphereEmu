using System;
using Godot;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Terrain GridMap plus the TerrainObjects placement frame, as the runtime resolves them from the
/// live node tree. A headless bake has no tree, so this instantiates terrain_scene under the tree
/// root and reads the same pair. MainServer's Ry(-90) on TerrainScene is a shared parent of the
/// GridMap and TerrainObjects, and gridGlobalInv * objectsGlobal cancels it, so the frame matches
/// the live game. Tools/check_object_collision_frame.ps1 asserts that
/// </summary>
public sealed class TerrainCollisionFrame : IDisposable
{
    /// <summary>
    /// The instantiated terrain scene root. The GridMap lives under it, so its LocalToMap and
    /// MapToLocal only work while this stays in the tree
    /// </summary>
    private readonly Node3D sceneRoot;

    public GridMap GridMap { get; }

    public Transform3D GridGlobal { get; }

    /// <summary>
    /// TerrainObjects local into GridMap-local, as GridMap.CellSize units
    /// </summary>
    public Transform3D ToGridMapLocal { get; }

    /// <summary>
    /// Placement transform into world, where the multimesh draws the object
    /// </summary>
    public Transform3D ToWorld => GridGlobal * ToGridMapLocal;

    public float TileSize => GridMap.CellSize.X > 0f ? GridMap.CellSize.X : TerrainGroundIndex.DefaultTileSize;

    private TerrainCollisionFrame (Node3D sceneRoot, GridMap gridMap, Transform3D gridGlobal, Transform3D toGridMapLocal)
    {
        this.sceneRoot = sceneRoot;
        GridMap = gridMap;
        GridGlobal = gridGlobal;
        ToGridMapLocal = toGridMapLocal;
    }

    /// <summary>
    /// null when the terrain scene has no Terrain GridMap
    /// </summary>
    public static TerrainCollisionFrame? TryResolve (string terrainScenePath)
    {
        if (Engine.GetMainLoop () is not SceneTree tree || !ResourceLoader.Exists (terrainScenePath))
        {
            GD.PushError ($"TerrainCollisionFrame: tree or scene missing: {terrainScenePath}");
            return null;
        }

        var packed = ResourceLoader.Load<PackedScene> (terrainScenePath, cacheMode: ResourceLoader.CacheMode.Ignore);
        if (packed?.Instantiate<Node3D> () is not { } root)
        {
            GD.PushError ($"TerrainCollisionFrame: instantiate failed: {terrainScenePath}");
            return null;
        }

        tree.Root.AddChild (root);
        try
        {
            if (root.FindChild (TerrainGridFill.TerrainNodeName, recursive: true, owned: false) is not GridMap grid)
            {
                GD.PushError ($"TerrainCollisionFrame: '{TerrainGridFill.TerrainNodeName}' GridMap not in {terrainScenePath}");
                return null;
            }

            var objects = root.FindChild (TerrainObjectsFill.NodeName, recursive: true, owned: false) as Node3D;
            if (objects is null)
            {
                GD.PushWarning (
                    $"TerrainCollisionFrame: '{TerrainObjectsFill.NodeName}' not in {terrainScenePath}; "
                    + "placements use the GridMap frame directly.");
            }

            var gridGlobal = grid.GlobalTransform;
            var toGridMapLocal = gridGlobal.AffineInverse () * (objects?.GlobalTransform ?? Transform3D.Identity);
            return new TerrainCollisionFrame (root, grid, gridGlobal, toGridMapLocal);
        }
        catch
        {
            root.Free ();
            throw;
        }
    }

    public void Dispose ()
    {
        if (GodotObject.IsInstanceValid (sceneRoot))
        {
            sceneRoot.GetParent ()?.RemoveChild (sceneRoot);
            sceneRoot.Free ();
        }
    }
}
