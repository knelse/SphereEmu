using Godot;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
///     Editor tool: bakes one <see cref="NavigationMesh" /> per terrain tile group directly from raw source
///     geometry (ground tile mesh + object collider triangles from <see cref="TerrainObjectsFill" />), without ever
///     creating <see cref="StaticBody3D" />/<see cref="CollisionShape3D" /> nodes or touching
///     <see cref="PhysicsServer3D" />. That is the whole point: this map's object colliders exist only to produce
///     navigation-mesh source geometry, and persisting tens of millions of collider triangles as live physics
///     shapes in the scene (an earlier approach) made <see cref="PhysicsServer3D" /> build BVH trees for all of them
///     on every scene load - hanging/crashing the editor. Baking once (headless) and keeping only the small baked
///     <see cref="NavigationMesh" /> resources avoids that cost entirely; <see cref="NavigationRegion3D" /> nodes
///     placed in the same navigation map auto-stitch at touching tile edges, so this naturally covers the whole map
///     without one giant bake operation.
/// </summary>
[Tool]
public partial class TerrainNavigationBaker : Node3D
{
    public const string NavigationRegionsRootName = "TerrainNavigation";

    [Export] public NodePath TerrainGridFillPath { get; set; } = "../TerrainGrid";

    [Export] public NodePath TerrainObjectsFillPath { get; set; } = "../TerrainObjects";

    /// <summary>Same map source as <see cref="TerrainGridFill.MapBinPath" />; used to enumerate tile groups.</summary>
    [Export]
    public string MapBinPath { get; set; } = "res://Godot/Terrain/map.txt";

    [Export] public float TileSizeWorld { get; set; } = 100f;

    [Export] public Vector3 TerrainWorldOrigin { get; set; } = new(-4000f, 0f, -4000f);

    /// <summary>Directory where baked per-tile NavigationMesh .res files are written.</summary>
    [Export]
    public string NavMeshResourcesDirectory { get; set; } = "res://Godot/Terrain/GeneratedNavMeshes/";

    // Bake parameters, all in world meters (this map is 80x80 tiles of 100 m each, see
    // TerrainGridFill.TerrainWorldOrigin doc). Tuned for a human-scale agent walking outdoors; adjust to taste in
    // the inspector without touching code - they're plain NavigationMesh properties.
    [Export] public float CellSize { get; set; } = 0.5f;
    [Export] public float CellHeight { get; set; } = 0.25f;
    [Export] public float AgentRadius { get; set; } = 0.4f;
    [Export] public float AgentHeight { get; set; } = 1.8f;
    [Export] public float AgentMaxClimb { get; set; } = 0.5f;
    [Export] public float AgentMaxSlope { get; set; } = 47f;
    [Export] public float RegionMinSize { get; set; } = 4f;
    [Export] public float EdgeMaxLength { get; set; } = 12f;
    [Export] public float EdgeMaxError { get; set; } = 1.3f;
    [Export] public float DetailSampleDistance { get; set; } = 6f;

    /// <summary>
    ///     When false (default), baked <see cref="NavigationMesh" /> resources are written under
    ///     <see cref="NavMeshResourcesDirectory" /> only - no <see cref="NavigationRegion3D" /> nodes are
    ///     created. Persisting thousands of regions in <c>terrain_scene.scn</c> makes the editor hang for
    ///     minutes syncing navigation while multimesh visuals never finish loading.
    /// </summary>
    [Export]
    public bool PersistRegionsInScene { get; set; }

    /// <summary>Master tile names excluded from navigation baking (unused placeholder tiles).</summary>
    private static readonly string[] SkippedMasterTileNames = ["fill_empt_00", "fill_00"];

    [ExportToolButton("Bake terrain navigation")]
    public Callable BakeTerrainNavigationButton => Callable.From(() => BakeTerrainNavigation());

    /// <summary>
    ///     Bakes one NavigationMesh per occupied terrain tile (ground mesh + any object colliders placed on it,
    ///     from <see cref="TerrainObjectsFill.LastBuiltObjectColliderFacesByTile" />, which must already be
    ///     populated by a preceding <see cref="TerrainObjectsFill.RebuildTerrainObjects" /> call), plus one extra
    ///     region for object colliders that fell outside the grid. Returns the number of regions baked.
    /// </summary>
    public int BakeTerrainNavigation()
    {
        var gridFill = GetNodeOrNull<TerrainGridFill>(TerrainGridFillPath);
        if (gridFill is null)
        {
            GD.PushError($"TerrainNavigationBaker: TerrainGridFill not found at {TerrainGridFillPath}");
            return 0;
        }

        var objectsFill = GetNodeOrNull<TerrainObjectsFill>(TerrainObjectsFillPath);
        if (objectsFill is null)
        {
            GD.PushError($"TerrainNavigationBaker: TerrainObjectsFill not found at {TerrainObjectsFillPath}");
            return 0;
        }

        var terrain = gridFill.GetNodeOrNull<GridMap>(TerrainGridFill.TerrainNodeName);
        if (terrain?.MeshLibrary is null)
        {
            GD.PushError("TerrainNavigationBaker: terrain GridMap or MeshLibrary is missing; run RebuildTerrainGrid first.");
            return 0;
        }

        var tileIndex = TerrainObjectsFill.TerrainTileGridIndex.TryBuild(MapBinPath, TileSizeWorld, TerrainWorldOrigin);
        if (tileIndex is null)
        {
            GD.PushError($"TerrainNavigationBaker: map not found: {MapBinPath}");
            return 0;
        }

        var meshLib = terrain.MeshLibrary;
        var itemIdByName = new Dictionary<string, int>();
        foreach (var id in meshLib.GetItemList())
        {
            itemIdByName[meshLib.GetItemName(id)] = id;
        }

        var objectFacesByTile = objectsFill.LastBuiltObjectColliderFacesByTile;

        Node3D? root = null;
        if (PersistRegionsInScene)
        {
            root = GetOrCreateCategory(NavigationRegionsRootName);
            ClearChildren(root);
        }

        var outDir = NavMeshResourcesDirectory.TrimEnd('/') + "/";
        var outDirAbs = ProjectSettings.GlobalizePath(outDir);
        DirAccess.MakeDirRecursiveAbsolute(outDirAbs);
        var dir = DirAccess.Open(outDirAbs);
        if (dir is not null)
        {
            dir.ListDirBegin();
            while (true)
            {
                var entry = dir.GetNext();
                if (entry == string.Empty)
                {
                    break;
                }

                if (entry.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
                {
                    dir.Remove(entry);
                }
            }

            dir.ListDirEnd();
        }

        var baked = 0;
        var considered = 0;
        var t0 = Time.GetTicksMsec();
        // Use the GridMap node's saved Position (same as TerrainGridFill.RebuildTerrainGrid sets) rather than
        // the exported TerrainWorldOrigin field, which can drift if someone moves the node in the editor.
        var gridWorldOrigin = terrain.Position;
        foreach (var (coord, masterName, occurrence) in tileIndex.EnumerateCells())
        {
            considered++;
            if (ShouldSkipMasterTile(masterName))
            {
                continue;
            }

            var tileGroupKey = TerrainObjectsFill.TerrainTileGridIndex.BuildTileGroupKey(masterName, occurrence);

            var sourceGeometry = new NavigationMeshSourceGeometryData3D();

            if (itemIdByName.TryGetValue(masterName, out var itemId))
            {
                var mesh = meshLib.GetItemMesh(itemId);
                if (mesh is not null)
                {
                    var cellPos = new Vector3I(coord.Gx, 0, coord.Gz);
                    var basis = terrain.GetCellItemBasis(cellPos);
                    // Not terrain.ToGlobal(): headless runs against an instantiated-but-not-added-to-tree copy,
                    // and ToGlobal() returns Identity outside a SceneTree. GridMap has identity rotation/scale
                    // and Position = terrain origin, so origin + MapToLocal matches the live scene.
                    var worldPos = gridWorldOrigin + terrain.MapToLocal(cellPos);
                    sourceGeometry.AddMesh(mesh, new Transform3D(basis, worldPos));
                }
            }

            if (objectFacesByTile.TryGetValue(tileGroupKey, out var faces) && faces.Count > 0)
            {
                sourceGeometry.AddFaces(faces.ToArray(), Transform3D.Identity);
            }

            if (!sourceGeometry.HasData())
            {
                continue;
            }

            BakeRegion(tileGroupKey, sourceGeometry, outDir, root);
            baked++;
            if (baked % 200 == 0)
            {
                var elapsedSec = (Time.GetTicksMsec() - t0) / 1000.0;
                GD.Print($"TerrainNavigationBaker: baked {baked}/{considered} tiles considered ({elapsedSec:F1}s elapsed)...");
            }
        }

        // Object colliders whose world position fell outside every grid cell (map gaps / edge placements) still
        // need a region so they're not silently dropped from navigation.
        if (objectFacesByTile.TryGetValue(TerrainObjectsFill.OutsideTerrainTileNodeName, out var outsideFaces)
            && outsideFaces.Count > 0)
        {
            var sourceGeometry = new NavigationMeshSourceGeometryData3D();
            sourceGeometry.AddFaces(outsideFaces.ToArray(), Transform3D.Identity);
            BakeRegion(TerrainObjectsFill.OutsideTerrainTileNodeName, sourceGeometry, outDir, root);
            baked++;
        }

        GD.Print($"TerrainNavigationBaker: baked {baked} navigation mesh file(s)"
            + (PersistRegionsInScene ? "." : " (files only, not attached to scene)."));
        return baked;
    }

    private void BakeRegion(
        string regionName,
        NavigationMeshSourceGeometryData3D sourceGeometry,
        string outDir,
        Node3D? regionsRoot)
    {
        var navMesh = new NavigationMesh
        {
            CellSize = CellSize,
            CellHeight = CellHeight,
            AgentRadius = AgentRadius,
            AgentHeight = AgentHeight,
            AgentMaxClimb = AgentMaxClimb,
            AgentMaxSlope = AgentMaxSlope,
            RegionMinSize = RegionMinSize,
            EdgeMaxLength = EdgeMaxLength,
            EdgeMaxError = EdgeMaxError,
            DetailSampleDistance = DetailSampleDistance,
            FilterLedgeSpans = true,
            FilterWalkableLowHeightSpans = true
        };

        NavigationServer3D.BakeFromSourceGeometryData(navMesh, sourceGeometry);

        var outPath = $"{outDir}{regionName}.res";
        var navMeshToAssign = navMesh;
        var err = ResourceSaver.Save(navMesh, outPath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"TerrainNavigationBaker: failed to save navmesh ({err}): {outPath}");
        }
        else
        {
            var loaded = ResourceLoader.Load<NavigationMesh>(outPath);
            if (loaded is not null)
            {
                navMeshToAssign = loaded;
            }
        }

        var region = new NavigationRegion3D { Name = regionName, NavigationMesh = navMeshToAssign };
        if (regionsRoot is not null)
        {
            regionsRoot.AddChild(region);
            SetOwnerIfEditor(region);
        }
    }

    private static bool ShouldSkipMasterTile(string masterName)
    {
        foreach (var skipped in SkippedMasterTileNames)
        {
            if (string.Equals(masterName, skipped, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private Node3D GetOrCreateCategory(string nodeName)
    {
        if (GetNodeOrNull(nodeName) is Node3D existing)
        {
            return existing;
        }

        var n = new Node3D { Name = nodeName };
        AddChild(n);
        SetOwnerIfEditor(n);
        return n;
    }

    private static void ClearChildren(Node3D node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void SetOwnerIfEditor(Node node)
    {
        if (!Engine.IsEditorHint())
        {
            return;
        }

        var root = GetTree()?.EditedSceneRoot ?? Owner ?? this;
        node.Owner = root;
    }
}
