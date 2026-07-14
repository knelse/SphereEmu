using System.Threading;
using System.Threading.Tasks;
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
    [Export] public float CellSize { get; set; } = 0.1f;
    [Export] public float CellHeight { get; set; } = 0.1f;
    [Export] public float AgentRadius { get; set; } = 0.1f;
    [Export] public float AgentHeight { get; set; } = 1.8f;
    [Export] public float AgentMaxClimb { get; set; } = 1.0f;
    [Export] public float AgentMaxSlope { get; set; } = 70f;
    [Export] public float RegionMinSize { get; set; } = 4f;
    [Export] public float EdgeMaxLength { get; set; } = 12f;
    [Export] public float EdgeMaxError { get; set; } = 1.3f;
    [Export] public float DetailSampleDistance { get; set; } = 6f;

    /// <summary>XZ grid size (meters) when clustering object collider triangles into projected obstructions.</summary>
    [Export] public float ObstructionGridCellSize { get; set; } = 0.25f;

    /// <summary>
    ///     Concurrent <see cref="NavigationServer3D.BakeFromSourceGeometryData" /> calls during a full-map bake.
    ///     0 (default) uses <see cref="System.Environment.ProcessorCount" />. The actual bake call is the only
    ///     part run off the main thread; every earlier version of this baker ran it once per tile in a plain
    ///     sequential loop (~0.5s/tile), taking ~45 minutes for the whole map - Recast baking has always been
    ///     documented as safe to call from background threads (it's exactly what the "_async" NavigationServer3D
    ///     variant / <c>Tools/bake_and_export_single_nav.gd</c> already relies on), it just was never wired up
    ///     for a full-map C# bake before.
    /// </summary>
    [Export] public int MaxConcurrentBakeJobs { get; set; }

    /// <summary>
    ///     When false (default), baked <see cref="NavigationMesh" /> resources are written under
    ///     <see cref="NavMeshResourcesDirectory" /> only - no <see cref="NavigationRegion3D" /> nodes are
    ///     created. Persisting thousands of regions in <c>terrain_scene.scn</c> makes the editor hang for
    ///     minutes syncing navigation while multimesh visuals never finish loading.
    /// </summary>
    [Export]
    public bool PersistRegionsInScene { get; set; }

    /// <summary>Master tile names excluded from navigation baking (unused placeholder tiles).</summary>
    private static readonly string[] SkippedMasterTileNames = ["fill_empt_00"];

    /// <summary>
    ///     When non-empty, <see cref="BakeTerrainNavigation" /> bakes only this tile group key
    ///     (e.g. <c>Cliffn_rd05_00_04</c>) and does not delete other nav .res files.
    /// </summary>
    [Export]
    public string BakeOnlyTileGroupKey { get; set; } = "";

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

        var tileIndex = TerrainObjectsFill.TerrainTileGridIndex.TryBuild(MapBinPath, TileSizeWorld, terrain.Position);
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
        var bakeOnly = !string.IsNullOrWhiteSpace(BakeOnlyTileGroupKey);
        if (!bakeOnly)
        {
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
        }

        var t0 = Time.GetTicksMsec();
        var considered = 0;
        // Ground tiles are positioned directly from the live GridMap's own local Position - currently
        // (0,0,0), which is real (verified against the committed baked nav meshes), not a "never
        // rebuilt" placeholder. Do NOT substitute TerrainWorldOrigin (-4000 default) here: that used to
        // silently shift every newly-baked tile 4000m away from the ~99.9% of already-baked tiles (and
        // from TerrainObjectsFill's obstruction geometry, which is now expressed in this same frame).
        var gridWorldOrigin = terrain.Position;

        // Pass 1 (cheap, single-threaded): gather source geometry per tile. Nothing here touches
        // NavigationServer3D, so it stays sequential.
        var jobs = new List<(string RegionName, NavigationMeshSourceGeometryData3D Source)>();
        foreach (var (coord, masterName, occurrence) in tileIndex.EnumerateCells())
        {
            considered++;
            if (ShouldSkipMasterTile(masterName))
            {
                continue;
            }

            var tileGroupKey = TerrainObjectsFill.TerrainTileGridIndex.BuildTileGroupKey(masterName, occurrence);
            if (bakeOnly && !string.Equals(tileGroupKey, BakeOnlyTileGroupKey, StringComparison.Ordinal))
            {
                continue;
            }

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
                    var groundFaces = new List<Vector3>();
                    AppendMeshFacesWorldSpace(mesh, new Transform3D(basis, worldPos), groundFaces);
                    if (groundFaces.Count > 0)
                    {
                        // AddFaces with world-space triangles is more reliable than AddMesh, which reads
                        // visual mesh data back from the GPU and often fails headless or on ArrayMesh tiles.
                        sourceGeometry.AddFaces(groundFaces.ToArray(), Transform3D.Identity);
                    }
                }
            }

            if (objectFacesByTile.TryGetValue(tileGroupKey, out var faces) && faces.Count > 0)
            {
                if (bakeOnly)
                {
                    GD.Print($"TerrainNavigationBaker: {tileGroupKey} obstruction tris={faces.Count / 3}");
                }

                // Raw collider triangles must not be fed through AddFaces: Godot treats AddFaces geometry as
                // walkable surface input, so dense vertical collider soup zeroes out the baked walk mesh.
                // Project coarse XZ obstructions carved from collider bounds instead.
                AppendObjectObstructionsFromFaces(sourceGeometry, faces, ObstructionGridCellSize);
            }
            else if (bakeOnly)
            {
                GD.PushWarning($"TerrainNavigationBaker: {tileGroupKey} has no object obstruction faces");
            }

            if (!sourceGeometry.HasData())
            {
                continue;
            }

            jobs.Add((tileGroupKey, sourceGeometry));

            if (bakeOnly)
            {
                break;
            }
        }

        // Object colliders whose world position fell outside every grid cell (map gaps / edge placements) still
        // need a region so they're not silently dropped from navigation.
        if (!bakeOnly
            && objectFacesByTile.TryGetValue(TerrainObjectsFill.OutsideTerrainTileNodeName, out var outsideFaces)
            && outsideFaces.Count > 0)
        {
            var sourceGeometry = new NavigationMeshSourceGeometryData3D();
            AppendObjectObstructionsFromFaces(sourceGeometry, outsideFaces, ObstructionGridCellSize);
            if (sourceGeometry.HasData())
            {
                jobs.Add((TerrainObjectsFill.OutsideTerrainTileNodeName, sourceGeometry));
            }
        }

        // Pass 2: the actual Recast bake is the only expensive part (~0.5s/tile) and is documented as
        // safe to call off the main thread - run it concurrently across jobs. File I/O (ResourceSaver /
        // ResourceLoader) happens per-job on distinct paths, so it rides along on the same worker task;
        // only NavigationRegion3D scene-node creation is deferred back to this (main) thread afterwards.
        var results = new (bool Success, string RegionName, NavigationMesh? NavMesh)[jobs.Count];
        var maxParallel = MaxConcurrentBakeJobs > 0 ? MaxConcurrentBakeJobs : global::System.Environment.ProcessorCount;
        if (jobs.Count > 1 && maxParallel > 1)
        {
            using var gate = new SemaphoreSlim(maxParallel);
            var tasks = new Task[jobs.Count];
            for (var i = 0; i < jobs.Count; i++)
            {
                var index = i;
                gate.Wait();
                tasks[index] = Task.Run(() =>
                {
                    try
                    {
                        results[index] = BakeRegionToFile(jobs[index].RegionName, jobs[index].Source, outDir);
                    }
                    finally
                    {
                        gate.Release();
                    }
                });
            }

            Task.WaitAll(tasks);
        }
        else
        {
            for (var i = 0; i < jobs.Count; i++)
            {
                results[i] = BakeRegionToFile(jobs[i].RegionName, jobs[i].Source, outDir);
            }
        }

        var baked = 0;
        foreach (var (success, regionName, navMesh) in results)
        {
            if (!success)
            {
                continue;
            }

            baked++;
            if (root is not null && navMesh is not null)
            {
                var region = new NavigationRegion3D { Name = regionName, NavigationMesh = navMesh };
                root.AddChild(region);
                SetOwnerIfEditor(region);
            }

            if (baked % 200 == 0)
            {
                var elapsedSec = (Time.GetTicksMsec() - t0) / 1000.0;
                GD.Print($"TerrainNavigationBaker: baked {baked}/{considered} tiles considered ({elapsedSec:F1}s elapsed)...");
            }
        }

        GD.Print($"TerrainNavigationBaker: baked {baked} navigation mesh file(s)"
            + (PersistRegionsInScene ? "." : " (files only, not attached to scene)."));
        return baked;
    }

    /// <summary>
    ///     Bakes <paramref name="sourceGeometry" /> and saves the result to <paramref name="outDir" />/
    ///     <paramref name="regionName" />.res. Safe to call concurrently from a worker thread: touches only
    ///     the freshly-constructed <see cref="NavigationMesh" />/<see cref="NavigationMeshSourceGeometryData3D" />
    ///     passed in and file I/O on a path unique to this job - no scene tree access.
    /// </summary>
    private (bool Success, string RegionName, NavigationMesh? NavMesh) BakeRegionToFile(
        string regionName,
        NavigationMeshSourceGeometryData3D sourceGeometry,
        string outDir)
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
            FilterLedgeSpans = false,
            FilterWalkableLowHeightSpans = true
        };

        NavigationServer3D.BakeFromSourceGeometryData(navMesh, sourceGeometry);

        if (navMesh.GetPolygonCount() == 0)
        {
            GD.PushWarning($"TerrainNavigationBaker: empty bake for {regionName} (no walkable polygons).");
            return (false, regionName, null);
        }

        var outPath = $"{outDir}{regionName}.res";
        var navMeshToAssign = navMesh;
        var err = ResourceSaver.Save(navMesh, outPath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"TerrainNavigationBaker: failed to save navmesh ({err}): {outPath}");
        }
        else
        {
            var loaded = ResourceLoader.Load<NavigationMesh>(outPath, cacheMode: ResourceLoader.CacheMode.Ignore);
            if (loaded is not null)
            {
                navMeshToAssign = loaded;
            }
        }

        return (true, regionName, navMeshToAssign);
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

    /// <summary>
    ///     Appends world-space triangle corners (3 consecutive vertices per triangle) from <paramref name="mesh" />.
    /// </summary>
    private static void AppendMeshFacesWorldSpace(Mesh mesh, Transform3D transform, List<Vector3> faceAccumulator)
    {
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indexVariant = arrays[(int)Mesh.ArrayType.Index];
            if (indexVariant.VariantType == Variant.Type.Nil)
            {
                for (var i = 0; i + 2 < vertices.Length; i += 3)
                {
                    faceAccumulator.Add(transform * vertices[i]);
                    faceAccumulator.Add(transform * vertices[i + 1]);
                    faceAccumulator.Add(transform * vertices[i + 2]);
                }

                continue;
            }

            var indices = indexVariant.AsInt32Array();
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                faceAccumulator.Add(transform * vertices[indices[i]]);
                faceAccumulator.Add(transform * vertices[indices[i + 1]]);
                faceAccumulator.Add(transform * vertices[indices[i + 2]]);
            }
        }
    }

    /// <summary>
    ///     Converts dense object-collider triangle soup into projected obstructions Godot can carve from
    ///     the walk mesh. Collider triangles must never be fed through AddFaces on
    ///     <see cref="NavigationMeshSourceGeometryData3D" />.
    /// </summary>
    private static void AppendObjectObstructionsFromFaces(
        NavigationMeshSourceGeometryData3D sourceGeometry,
        IReadOnlyList<Vector3> faces,
        float cellSize)
    {
        if (faces.Count < 3)
        {
            return;
        }

        var cells = new Dictionary<(int Gx, int Gz), (Vector3 Min, Vector3 Max)>();
        for (var i = 0; i + 2 < faces.Count; i += 3)
        {
            var v0 = faces[i];
            var v1 = faces[i + 1];
            var v2 = faces[i + 2];
            var centerX = (v0.X + v1.X + v2.X) / 3f;
            var centerZ = (v0.Z + v1.Z + v2.Z) / 3f;
            var key = (Mathf.FloorToInt(centerX / cellSize), Mathf.FloorToInt(centerZ / cellSize));
            var triMin = new Vector3(
                Mathf.Min(v0.X, Mathf.Min(v1.X, v2.X)),
                Mathf.Min(v0.Y, Mathf.Min(v1.Y, v2.Y)),
                Mathf.Min(v0.Z, Mathf.Min(v1.Z, v2.Z)));
            var triMax = new Vector3(
                Mathf.Max(v0.X, Mathf.Max(v1.X, v2.X)),
                Mathf.Max(v0.Y, Mathf.Max(v1.Y, v2.Y)),
                Mathf.Max(v0.Z, Mathf.Max(v1.Z, v2.Z)));

            if (cells.TryGetValue(key, out var existing))
            {
                cells[key] = (
                    new Vector3(
                        Mathf.Min(existing.Min.X, triMin.X),
                        Mathf.Min(existing.Min.Y, triMin.Y),
                        Mathf.Min(existing.Min.Z, triMin.Z)),
                    new Vector3(
                        Mathf.Max(existing.Max.X, triMax.X),
                        Mathf.Max(existing.Max.Y, triMax.Y),
                        Mathf.Max(existing.Max.Z, triMax.Z)));
            }
            else
            {
                cells[key] = (triMin, triMax);
            }
        }

        foreach (var (_, bounds) in cells)
        {
            var mn = bounds.Min;
            var mx = bounds.Max;
            var rawHeight = mx.Y - mn.Y;
            if (rawHeight < 0.05f)
            {
                continue;
            }

            var outline = new[]
            {
                new Vector3(mn.X, 0f, mn.Z),
                new Vector3(mx.X, 0f, mn.Z),
                new Vector3(mx.X, 0f, mx.Z),
                new Vector3(mn.X, 0f, mx.Z),
            };
            // Object JSON Y is often hundreds of meters below the GridMap tile surface.
            // Projected carve only applies within [elevation, elevation+height], so expand
            // vertically to guarantee intersection with the walkable voxels on the tile.
            const float verticalPadding = 600f;
            var elevation = mn.Y - verticalPadding;
            var height = rawHeight + verticalPadding * 2f;
            sourceGeometry.AddProjectedObstruction(outline, elevation, height, carve: true);
        }
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
