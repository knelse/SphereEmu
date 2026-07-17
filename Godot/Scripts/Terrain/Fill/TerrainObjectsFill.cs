using Godot;
using Godot.Collections;
using SphServer.Godot.Scripts.Terrain.WalkSurface;
using FileAccess = Godot.FileAccess;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
///     Editor tool: loads object placements from <c>Godot/Terrain/ObjectDataJson</c>, pulls meshes from GLB scenes under
///     <see cref="ModelsDirectory" />, and draws instances via <see cref="MultiMeshInstance3D" /> (one multimesh per mesh
///     per category).
///     JSON positions and rotations use source world space: X right, Y down, Z forward (right-handed). Godot: X right, Y
///     up, forward = -Z;
///     <see cref="SourceWorldToGodotWorldBasis" /> maps positions (x, y, z) ↦ (x, -y, -z). Rotations use R' = T R T (T² =
///     I) so identity source rotation stays identity in Godot; R = T R_src alone would flip GLB meshes 180° about X.
/// </summary>
[Tool]
public partial class TerrainObjectsFill : Node3D
{
    public const string PlantsNodeName = "TerrainPlants";
    public const string RocksNodeName = "TerrainRocks";
    public const string OtherNodeName = "TerrainOther";
    public const string ExtraInstancedGroupsRootName = "ExtraInstancedGroups";

    /// <summary>Tile node for object placements that do not fall on any terrain grid cell.</summary>
    public const string OutsideTerrainTileNodeName = "OutsideTerrain";

    /// <summary>
    ///     Columns = source X, Y, Z axes expressed in Godot: right, down, forward (Godot forward = -Z), so (x,y,z)_src ↦
    ///     (x,-y,-z).
    /// </summary>
    private static readonly Basis SourceWorldToGodotWorldBasis = new(Vector3.Right, Vector3.Down, Vector3.Forward);

    [Export] public string ObjectDataDirectory { get; set; } = "res://Godot/Terrain/ObjectDataJson/";

    [Export] public string ModelsDirectory { get; set; } = "res://Godot/Models/";

    /// <summary>
    ///     When enabled, every placement whose model scene contains collision shapes (models imported with the
    ///     '-col' suffix) has its triangle geometry accumulated (in world space, per terrain tile group) into
    ///     <see cref="LastBuiltObjectColliderFacesByTile" /> for diagnostics / legacy tooling. Production nav
    ///     meshes are baked by <see cref="TerrainNavigationBaker" /> via
    ///     <c>Tools/bake_and_export_single_nav.gd</c> (ObjectDataJson + checkpoint), not from these faces.
    ///     Nothing is added to the scene itself: an earlier version persisted live physics shapes and hung the
    ///     editor; keeping only baked <see cref="NavigationMesh" /> resources avoids that.
    /// </summary>
    [Export]
    public bool BuildObjectColliders { get; set; } = true;

    /// <summary>Same map source as <see cref="TerrainGridFill.MapBinPath" />; used to name per-tile collider groups.</summary>
    [Export]
    public string MapBinPath { get; set; } = "res://Godot/Terrain/map.txt";

    [Export] public float TileSizeWorld { get; set; } = 100f;

    [Export] public Vector3 TerrainWorldOrigin { get; set; } = new(-4000f, 0f, -4000f);

    /// <summary>
    ///     When enabled (editor rebuild), generated MultiMeshes are saved as external binary resources instead of
    ///     being embedded as huge sub-resources in the .tscn. This dramatically reduces scene load/parse time.
    /// </summary>
    [Export]
    public bool SaveMultiMeshesAsExternalResources { get; set; } = true;

    /// <summary>
    ///     Directory where MultiMesh .res files are written when <see cref="SaveMultiMeshesAsExternalResources" /> is enabled.
    /// </summary>
    [Export]
    public string MultiMeshResourcesDirectory { get; set; } = "res://Godot/Terrain/GeneratedMultiMeshes/";

    [Export]
    public string WalkSurfaceDataDirectory { get; set; } = WalkSurfaceAtlasBuilder.DefaultOutputDirectory;

    [Export]
    public bool UpdateWalkSurfaceObjectFootprintsOnRebuild { get; set; } = true;

    [ExportToolButton("Rebuild terrain objects")]
    public Callable RebuildTerrainObjectsButton => Callable.From(RebuildTerrainObjects);

    /// <summary>
    ///     World-space object-collider triangle faces (3 consecutive entries per triangle) accumulated by the most
    ///     recent <see cref="RebuildTerrainObjects" /> call, keyed by the same <c>{TileName}_{TileIndex}</c> group
    ///     used for the object hierarchy. Not consumed by the production nav bake path; nothing here is persisted
    ///     to the scene.
    /// </summary>
    public global::System.Collections.Generic.Dictionary<string, List<Vector3>> LastBuiltObjectColliderFacesByTile
    {
        get;
        private set;
    } = new();

    /// <summary>Clears category children and repopulates from all JSON files in <see cref="ObjectDataDirectory" />.</summary>
    public void RebuildTerrainObjects()
    {
        var dir = ObjectDataDirectory.TrimEnd('/') + "/";
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(dir)))
        {
            GD.PushError($"TerrainObjectsFill: directory not found: {ObjectDataDirectory}");
            return;
        }

        var plants = GetOrCreateCategory(PlantsNodeName);
        var rocks = GetOrCreateCategory(RocksNodeName);
        var other = GetOrCreateCategory(OtherNodeName);
        var terrainObjects = GetOrCreateCategory(ExtraInstancedGroupsRootName);

        // Ensure these roots are owned by the edited scene root, otherwise editor-created children can show up
        // with auto-generated "@Class@id" labels even when Name is assigned.
        if (Engine.IsEditorHint())
        {
            var sceneOwner = GetTree()?.EditedSceneRoot ?? Owner ?? this;
            plants.Owner = sceneOwner;
            rocks.Owner = sceneOwner;
            other.Owner = sceneOwner;
            terrainObjects.Owner = sceneOwner;
        }

        ClearChildren(plants);
        ClearChildren(rocks);
        ClearChildren(other);
        ClearChildren(terrainObjects);

        // Object colliders are never added to the scene (see BuildObjectColliders doc) - just accumulated in
        // memory, keyed by the same {TileName}_{TileIndex} grouping the object hierarchy uses, for
        // TerrainNavigationBaker to consume right after this call.
        ColliderBuildState? colliderState = null;
        if (BuildObjectColliders)
        {
            // Prefer the live GridMap origin so object→tile assignment matches where ground meshes
            // are baked (terrain.Position). The exported TerrainWorldOrigin default (-4000) drifts
            // from the saved scene GridMap at (0,0,0) and sent every placement to the wrong cell.
            var worldOrigin = ResolveTerrainWorldOrigin();
            var tileIndex = TerrainTileGridIndex.TryBuild(MapBinPath, TileSizeWorld, worldOrigin);
            if (tileIndex is null)
            {
                GD.PushWarning(
                    $"TerrainObjectsFill: map not found ({MapBinPath}); grouping object colliders under "
                    + $"'{OutsideTerrainTileNodeName}' instead of per-tile.");
            }

            colliderState = new ColliderBuildState
            {
                TileIndex = tileIndex,
                ObjectsToGridLocal = GetObjectsToGridLocalTransform()
            };
        }

        // (category root, source object name, Mesh) -> instance placements (world * mesh-local)
        // We keep ObjectName so MultiMesh nodes can be named after their source object.
        var batches =
            new global::System.Collections.Generic.Dictionary<(Node3D Category, string ObjectName, Mesh Mesh),
                List<InstancePlacement>>(new MeshBatchKeyComparer());
        var objectPartsCache = new global::System.Collections.Generic.Dictionary<string, ObjectSceneParts?>();

        var da = DirAccess.Open(dir);
        if (da is null)
        {
            GD.PushError($"TerrainObjectsFill: could not open: {ObjectDataDirectory}");
            return;
        }

        da.ListDirBegin();
        while (true)
        {
            var name = da.GetNext();
            if (name == string.Empty)
            {
                break;
            }

            if (name is "." or "..")
            {
                continue;
            }

            // Top-level .json files keep existing multimesh batching behavior.
            if (!da.CurrentIsDir() && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var path = dir + name;
                ProcessJsonForBatches(path, plants, rocks, other, batches, objectPartsCache, colliderState);
                continue;
            }

            // Anything in subfolders is optional toggles: instance scenes directly (no multimesh),
            // under TerrainObjects\<folder>\<file>.
            if (da.CurrentIsDir())
            {
                var subDir = dir + name;
                ProcessJsonFolderAsDirectInstances(subDir, name, terrainObjects);
            }
        }

        da.ListDirEnd();
        da.Dispose();

        LastBuiltObjectColliderFacesByTile = colliderState?.TileFaces
            ?? new global::System.Collections.Generic.Dictionary<string, List<Vector3>>();

        var obstructionTris = 0L;
        foreach (var faces in LastBuiltObjectColliderFacesByTile.Values)
        {
            obstructionTris += faces.Count / 3;
        }

        GD.Print(
            $"TerrainObjectsFill: obstruction tile groups={LastBuiltObjectColliderFacesByTile.Count}, "
            + $"tris={obstructionTris} (origin={ResolveTerrainWorldOrigin()})");

        var nextIndexByCategoryAndObjectName =
            new global::System.Collections.Generic.Dictionary<(ulong CategoryId, string ObjectName), int>();
        foreach (var kv in batches)
        {
            var (category, objectName, mesh) = kv.Key;
            var placements = kv.Value;
            if (placements.Count == 0)
            {
                continue;
            }

            var indexKey = (category.GetInstanceId(), objectName);
            if (!nextIndexByCategoryAndObjectName.TryGetValue(indexKey, out var objectIndex))
            {
                objectIndex = 0;
            }

            // Godot requires InstanceCount == 0 when changing TransformFormat / UseCustomData.
            // Avoid object initializers here because property set order can vary and trigger editor errors on load.
            var mm = new MultiMesh();
            mm.Mesh = mesh;
            mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            mm.UseCustomData = true;
            mm.InstanceCount = placements.Count;

            for (var i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                mm.SetInstanceTransform(i, p.Transform);
                // Encode source record index (up to 24-bit) into RGB (0..1) so editor tools can map an instance back to JSON record.
                var idx = p.SourceRecordIndex;
                var r = (idx & 0xFF) / 255f;
                var g = ((idx >> 8) & 0xFF) / 255f;
                var b = ((idx >> 16) & 0xFF) / 255f;
                mm.SetInstanceCustomData(i, new Color(r, g, b, 0f));
            }

            // Godot's headless dummy renderer silently ignores SetInstanceTransform; saving those
            // buffers produces tiny .res files (~9 KB) with every instance at the origin.
            if (placements.Count > 0)
            {
                var expectedOrigin = placements[0].Transform.Origin;
                var actualOrigin = mm.GetInstanceTransform(0).Origin;
                if (expectedOrigin.DistanceTo(actualOrigin) > 0.01f)
                {
                    GD.PushError(
                        "TerrainObjectsFill: MultiMesh instance transforms were not applied "
                        + $"(expected origin {expectedOrigin}, got {actualOrigin}). "
                        + "Do not run RebuildTerrainObjects with --headless; use the normal editor "
                        + "or a GPU-backed Godot process.");
                    continue;
                }
            }

            var mmToAssign = mm;

            var safeObjectName = SanitizeGodotNodeName(objectName);
            // Not gated on Engine.IsEditorHint(): saving a resource to disk is plain file I/O, not an
            // editor-only concept. Gating this on the editor hint meant headless (`-s script.gd`) runs of
            // this [Tool] silently skipped it, leaving every MultiMesh in-memory only; packing/saving the
            // scene afterwards then embedded ~1000 MultiMesh sub-resources instead of referencing external
            // .res files. Godot's binary serializer applies an embedded MultiMesh's saved properties in
            // registration order (instance_count before transform_format/use_custom_data), and
            // MultiMesh::set_transform_format()/set_use_custom_data() both reject changes once
            // instance_count != 0 - so every reloaded embedded MultiMesh silently kept its default 2D
            // transform format while instance_count was set for 3D+custom-data data, corrupting the
            // instance buffer and crashing the engine (out-of-bounds access) as soon as anything touched it.
            if (SaveMultiMeshesAsExternalResources)
            {
                var baseDir = MultiMeshResourcesDirectory.TrimEnd('/') + "/";
                var catDirName = SanitizeGodotNodeName(category.Name.ToString());
                var outDir = $"{baseDir}{catDirName}/";
                var abs = ProjectSettings.GlobalizePath(outDir);
                DirAccess.MakeDirRecursiveAbsolute(abs);

                var outPath = $"{outDir}{safeObjectName}_MM_{objectIndex}.res";
                var err = ResourceSaver.Save(mm, outPath);
                if (err != Error.Ok)
                {
                    GD.PushWarning($"TerrainObjectsFill: failed to save multimesh ({err}): {outPath}");
                }
                else
                {
                    // Load back to ensure the scene references an external resource instead of embedding the buffer.
                    var loaded = ResourceLoader.Load<MultiMesh>(outPath);
                    if (loaded is not null)
                    {
                        mmToAssign = loaded;
                    }
                }
            }

            var mmi = new MultiMeshInstance3D
            {
                Name = $"{safeObjectName}_MM_{objectIndex}",
                Multimesh = mmToAssign
            };
            nextIndexByCategoryAndObjectName[indexKey] = objectIndex + 1;
            category.AddChild(mmi);
            SetOwnerIfEditor(mmi);

            // Some editor contexts may still auto-name on add; re-assign after parenting to be safe.
            mmi.Name = $"{safeObjectName}_MM_{objectIndex}";
        }

        if (UpdateWalkSurfaceObjectFootprintsOnRebuild)
        {
            WalkSurfaceAtlasBuilder.ApplyObjectFootprintsToSavedChunks(
                WalkSurfaceDataDirectory,
                new WalkSurfaceAtlasBuilder.ObjectFootprintSettings
                {
                    ObjectDataDirectory = ObjectDataDirectory,
                    ModelsDirectory = ModelsDirectory,
                    Enabled = true,
                });
            WalkSurfaceCache.Invalidate();
        }
    }

    // TEMP DIAGNOSTIC - remove after use.
    public void DebugPrintColliderFaceStats()
    {
        var totalFaces = 0L;
        var maxKey = string.Empty;
        var maxCount = 0;
        foreach (var (key, faces) in LastBuiltObjectColliderFacesByTile)
        {
            totalFaces += faces.Count;
            if (faces.Count > maxCount)
            {
                maxCount = faces.Count;
                maxKey = key;
            }
        }

        GD.Print($"[DEBUG] tile groups with faces: {LastBuiltObjectColliderFacesByTile.Count}, total face-verts: {totalFaces}");
        GD.Print($"[DEBUG] largest group: {maxKey} with {maxCount} face-verts ({maxCount / 3} tris)");
        if (LastBuiltObjectColliderFacesByTile.TryGetValue(OutsideTerrainTileNodeName, out var outside))
        {
            GD.Print($"[DEBUG] OutsideTerrain group: {outside.Count} face-verts ({outside.Count / 3} tris)");
        }

        if (LastBuiltObjectColliderFacesByTile.TryGetValue("Cliffn_rd05_00_04", out var cliffn))
        {
            GD.Print($"[DEBUG] Cliffn_rd05_00_04 face-verts: {cliffn.Count} ({cliffn.Count / 3} tris)");
        }
        else
        {
            GD.Print("[DEBUG] Cliffn_rd05_00_04: NO obstruction faces");
        }

        GD.Print($"[DEBUG] ResolveTerrainWorldOrigin={ResolveTerrainWorldOrigin()}");
    }

    private static string SanitizeGodotNodeName(string name)
    {
        name = name.ToLower();
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Object";
        }

        // Godot node names are strings but certain characters can lead to confusing paths or invalid NodePaths.
        // Keep common filename-ish characters; replace everything else with '_'.
        var chars = name.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var ok =
                (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c is '_' or '-' or '.';
            if (!ok)
            {
                chars[i] = '_';
            }
        }

        var sanitized = new string(chars);
        return sanitized.Length == 0 ? "Object" : sanitized;
    }

    private void ProcessJsonForBatches(
        string path,
        Node3D plants,
        Node3D rocks,
        Node3D other,
        global::System.Collections.Generic.Dictionary<(Node3D Category, string ObjectName, Mesh Mesh),
            List<InstancePlacement>> batches,
        global::System.Collections.Generic.Dictionary<string, ObjectSceneParts?> objectPartsCache,
        ColliderBuildState? colliderState)
    {
        var jsonText = FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(jsonText))
        {
            GD.PushWarning($"TerrainObjectsFill: empty or unreadable: {path}");
            return;
        }

        List<TerrainObjectRecord>? records;
        try
        {
            records = ParseTerrainRecords(jsonText);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"TerrainObjectsFill: JSON parse failed ({path}): {ex.Message}");
            return;
        }

        if (records is null || records.Count == 0)
        {
            return;
        }

        for (var recIndex = 0; recIndex < records.Count; recIndex++)
        {
            var rec = records[recIndex];
            if (string.IsNullOrWhiteSpace(rec.ObjectName))
            {
                continue;
            }

            if (!objectPartsCache.TryGetValue(rec.ObjectName, out var parts))
            {
                var scene = GetOrLoadScene(rec.ObjectName);
                parts = scene is null ? null : ExtractSceneParts(scene);
                objectPartsCache[rec.ObjectName] = parts;
                if (parts is null)
                {
                    if (scene is not null)
                    {
                        GD.PushWarning(
                            $"TerrainObjectsFill: no drawable mesh for '{rec.ObjectName}' (missing mesh or skinned-only)");
                    }
                    else
                    {
                        GD.PushWarning(
                            $"TerrainObjectsFill: no model for '{rec.ObjectName}' (tried .glb / .gltf under {ModelsDirectory})");
                    }

                    continue;
                }
            }
            else if (parts is null)
            {
                continue;
            }

            var pos = rec.Coordinates?.ToVector3() ?? Vector3.Zero;
            var rot = rec.RotationEuler?.ToEulerRadians() ?? Vector3.Zero;
            var world = BuildPlacementTransform(pos, rot);

            var lower = rec.ObjectName.ToLowerInvariant();
            Node3D parent;
            if (lower.Contains("tree") || lower.Contains("bush") || lower.Contains("grass"))
            {
                parent = plants;
            }
            else if (lower.Contains("rock") || lower.Contains("stone"))
            {
                parent = rocks;
            }
            else
            {
                parent = other;
            }

            foreach (var part in parts.MeshParts)
            {
                var key = (parent, rec.ObjectName, part.Mesh);
                if (!batches.TryGetValue(key, out var list))
                {
                    list = new List<InstancePlacement>();
                    batches[key] = list;
                }

                list.Add(new InstancePlacement(world * part.LocalToRoot, recIndex));
            }

            // Models filtered out of collider generation (grass, decorative-only props) simply have no
            // ColliderParts here; the multimesh visual above is unaffected either way.
            // When no imported '-col' shapes exist (common until generate_colliders.py has been run),
            // fall back to the drawable mesh AABB so nav bake still carves object footprints.
            // Trees always carve from a trunk-only footprint (mesh heuristic), even if full leaf
            // colliders exist later for physics — those are not fed into nav obstruction faces.
            if (colliderState is not null && !ShouldSkipMeshObstruction(rec.ObjectName))
            {
                // Obstruction geometry (and the tile it gets bucketed into) must be expressed in the
                // "Terrain" GridMap's local frame — the same frame TerrainNavigationBaker positions
                // ground tiles in — not in this node's (TerrainObjects) own local frame. TerrainObjects
                // and TerrainGrid are independently offset/rotated siblings under TerrainScene, so
                // reusing the raw visual placement transform here silently baked every obstruction
                // hole rotated/offset away from the object that actually carved it. The visual
                // multimesh placement below is unaffected: it inherits this node's Transform via the
                // normal scene graph, so it must keep using the raw `world` transform.
                var navWorld = colliderState.ObjectsToGridLocal * world;
                if (IsTreeObject(rec.ObjectName) && parts.MeshParts.Count > 0)
                {
                    AddTreeTrunkObstruction(colliderState, parts.MeshParts, navWorld);
                }
                else if (IsUndercroftObject(rec.ObjectName) && parts.MeshParts.Count > 0)
                {
                    // Near-ground mesh tris only — AABB would seal arch/tower openings.
                    // Towers get XZ inflate so thin wall planes block like the visual shell.
                    var inflate = IsTowerObject(rec.ObjectName) ? TowerWallInflate : 0f;
                    AddNearGroundMeshObstruction(colliderState, parts.MeshParts, navWorld, inflate);
                    if (IsTowerObject(rec.ObjectName))
                    {
                        // Solid inset fill removes orphan walkable islands inside the hollow shell.
                        AddTowerInteriorFillObstruction(colliderState, parts.MeshParts, navWorld);
                    }
                }
                else if (parts.ColliderParts.Count > 0)
                {
                    AddObjectCollider(colliderState, parts.ColliderParts, navWorld);
                }
                else if (parts.MeshParts.Count > 0)
                {
                    // Near-ground mesh tris rather than a single full-object AABB: a curved/L-shaped/
                    // diagonal building's bounding box can enclose a lot of genuinely walkable space
                    // around it (see the Town4 "snow01b"/courtyard investigation) — tracing the real
                    // near-ground silhouette keeps the carve hugging the actual footprint instead.
                    AddNearGroundMeshObstruction(colliderState, parts.MeshParts, navWorld, 0f);
                }
            }
        }
    }

    private void ProcessJsonFolderAsDirectInstances(string folderPath, string folderName, Node3D terrainObjectsRoot)
    {
        var da = DirAccess.Open(folderPath);
        if (da is null)
        {
            return;
        }

        da.ListDirBegin();
        while (true)
        {
            var name = da.GetNext();
            if (name == string.Empty)
            {
                break;
            }

            if (name is "." or ".." || da.CurrentIsDir() || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = folderPath.TrimEnd('/') + "/" + name;
            var jsonText = FileAccess.GetFileAsString(path);
            if (string.IsNullOrEmpty(jsonText))
            {
                continue;
            }

            List<TerrainObjectRecord>? records;
            try
            {
                records = ParseTerrainRecords(jsonText);
            }
            catch (Exception ex)
            {
                GD.PushWarning($"TerrainObjectsFill: JSON parse failed ({path}): {ex.Message}");
                continue;
            }

            if (records is null || records.Count == 0)
            {
                continue;
            }

            var fileBase = name[..^5]; // trim ".json"
            var folderNode = GetOrCreateChildNodeUnder(terrainObjectsRoot, folderName);
            var fileNode = GetOrCreateChildNodeUnder(folderNode, fileBase);

            var instanceIndex = 0;
            foreach (var rec in records)
            {
                if (string.IsNullOrWhiteSpace(rec.ObjectName))
                {
                    continue;
                }

                var scene = GetOrLoadScene(rec.ObjectName);
                if (scene is null)
                {
                    continue;
                }

                var inst = scene.Instantiate<Node3D>();
                inst.Name = $"{rec.ObjectName}_{instanceIndex++}";

                var pos = rec.Coordinates?.ToVector3() ?? Vector3.Zero;
                var rot = rec.RotationEuler?.ToEulerRadians() ?? Vector3.Zero;
                inst.Transform = BuildPlacementTransform(pos, rot);

                fileNode.AddChild(inst);
                SetOwnerIfEditor(inst);
            }
        }

        da.ListDirEnd();
        da.Dispose();
    }

    private static Transform3D BuildPlacementTransform(Vector3 position, Vector3 rotationEuler)
    {
        var basis = Basis.FromEuler(rotationEuler);
        return new Transform3D(basis, position);
    }

    /// <summary>
    ///     Instantiates <paramref name="scene" /> once and collects both drawable mesh parts and, when the source
    ///     model was processed with the collider generator's '-col' import suffix, the StaticBody3D/CollisionShape3D
    ///     pairs Godot's importer attached next to each mesh. Returns <c>null</c> only when no mesh was found;
    ///     an empty <see cref="ObjectSceneParts.ColliderParts" /> list is expected for models without colliders.
    /// </summary>
    private static ObjectSceneParts? ExtractSceneParts(PackedScene scene)
    {
        var root = scene.Instantiate<Node3D>();
        try
        {
            var meshParts = new List<MeshPart>();
            var colliderParts = new List<ColliderPart>();
            CollectParts(root, root, meshParts, colliderParts);
            return meshParts.Count == 0
                ? null
                : new ObjectSceneParts { MeshParts = meshParts, ColliderParts = colliderParts };
        }
        finally
        {
            root.QueueFree();
        }
    }

    private static void CollectParts(Node node, Node3D root, List<MeshPart> meshList, List<ColliderPart> colliderList)
    {
        if (node is MeshInstance3D mi && mi.Mesh is { } mesh)
        {
            if (HasSkeletonAncestor(mi))
            {
                return;
            }

            meshList.Add(new MeshPart(mesh, ComputeTransformRelativeToRoot(mi, root)));
        }
        else if (node is StaticBody3D body)
        {
            foreach (var child in body.GetChildren())
            {
                if (child is CollisionShape3D { Shape: { } shape } collisionShape)
                {
                    colliderList.Add(new ColliderPart(shape, ComputeTransformRelativeToRoot(collisionShape, root)));
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            CollectParts(child, root, meshList, colliderList);
        }
    }

    private static bool HasSkeletonAncestor(Node node)
    {
        var p = node.GetParent();
        while (p is not null)
        {
            if (p is Skeleton3D)
            {
                return true;
            }

            p = p.GetParent();
        }

        return false;
    }

    /// <summary>
    ///     Transform from <paramref name="root" /> space to <paramref name="node" /> space (node is typically a
    ///     <see cref="MeshInstance3D" />).
    /// </summary>
    private static Transform3D ComputeTransformRelativeToRoot(Node3D node, Node3D root)
    {
        var t = Transform3D.Identity;
        var cur = node;
        while (!ReferenceEquals(cur, root) && cur is not null)
        {
            t = cur.Transform * t;
            cur = cur.GetParent() as Node3D;
        }

        return t;
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

    private Node3D GetOrCreateChildNode(string nodeName)
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

    private Node3D GetOrCreateChildNodeUnder(Node3D parent, string nodeName)
    {
        if (parent.GetNodeOrNull(nodeName) is Node3D existing)
        {
            return existing;
        }

        var n = new Node3D { Name = nodeName };
        parent.AddChild(n);
        SetOwnerIfEditor(n);
        return n;
    }

    /// <summary>
    ///     Accumulates one object placement's collider geometry (in world space) into
    ///     <see cref="ColliderBuildState.TileFaces" />, keyed by the tile group it falls on (the terrain grid cell,
    ///     by master tile name + occurrence index), or <see cref="OutsideTerrainTileNodeName" /> when it falls
    ///     outside the grid / map is unavailable. Nothing is added to the scene here - see
    ///     <see cref="LastBuiltObjectColliderFacesByTile" />. <paramref name="worldTransform" /> is reused verbatim
    ///     from the visual placement so the accumulated geometry lines up with the multimesh instance it belongs to.
    /// </summary>
    private void AddObjectCollider(
        ColliderBuildState state,
        List<ColliderPart> colliderParts,
        Transform3D worldTransform)
    {
        var tileGroupKey = OutsideTerrainTileNodeName;
        if (state.TileIndex is not null
            && state.TileIndex.TryGetTile(worldTransform.Origin, out var masterName, out var occurrence))
        {
            tileGroupKey = TerrainTileGridIndex.BuildTileGroupKey(masterName, occurrence);
        }

        if (!state.TileFaces.TryGetValue(tileGroupKey, out var faces))
        {
            faces = new List<Vector3>();
            state.TileFaces[tileGroupKey] = faces;
        }

        foreach (var part in colliderParts)
        {
            AppendShapeFacesWorldSpace(part.Shape, worldTransform * part.LocalToRoot, faces);
        }
    }

    /// <summary>
    ///     Nav-only trunk footprint for trees: prefer small-XZ mesh parts (trunk), ignore canopy-sized parts,
    ///     clamp to <see cref="TreeTrunkMaxRadius"/>, fall back to a cylinder at the placement origin.
    ///     Does not touch physics colliders.
    /// </summary>
    private void AddTreeTrunkObstruction(
        ColliderBuildState state,
        List<MeshPart> meshParts,
        Transform3D worldTransform)
    {
        var aabb = ResolveTreeTrunkAabb(meshParts, worldTransform);
        if (aabb.Size.Y < 0.05f)
        {
            return;
        }

        AppendObstructionAabb(state, worldTransform.Origin, aabb);
    }

    private void AppendObstructionAabb(ColliderBuildState state, Vector3 worldOrigin, Aabb aabb)
    {
        var tileGroupKey = OutsideTerrainTileNodeName;
        if (state.TileIndex is not null
            && state.TileIndex.TryGetTile(worldOrigin, out var masterName, out var occurrence))
        {
            tileGroupKey = TerrainTileGridIndex.BuildTileGroupKey(masterName, occurrence);
        }

        if (!state.TileFaces.TryGetValue(tileGroupKey, out var faces))
        {
            faces = new List<Vector3>();
            state.TileFaces[tileGroupKey] = faces;
        }

        AppendAabbBoxFaces(aabb, faces);
    }

    private const float TreeTrunkFallbackRadius = 0.4f;
    private const float TreeTrunkMaxRadius = 1.0f;

    /// <summary>
    ///     Tris whose lowest vertex is above placement.origin.y + this are ignored for the near-ground nav
    ///     carve (arch lintels / upper floors / roof overhangs must not project-seal a walkable opening or
    ///     inflate a building's footprint beyond its walls).
    /// </summary>
    private const float NearGroundCarveMaxHeight = 2.0f;

    /// <summary>
    ///     Extra XZ half-extent added around each near-ground tower wall tri when building nav faces.
    ///     Tower meshes are thin planes; without this the projected carve is ~one cell thick.
    /// </summary>
    private const float TowerWallInflate = 0.45f;

    /// <summary>
    ///     Inset from the tower's outer mesh AABB for a solid interior projected carve.
    ///     Keeps the doorway notch from wall-mesh gaps while removing hollow-center islands.
    /// </summary>
    private const float TowerInteriorInset = 0.9f;

    private static bool IsTreeObject(string objectName) =>
        objectName.Contains("tree", StringComparison.OrdinalIgnoreCase);

    private static bool IsTowerObject(string objectName) =>
        objectName.Contains("tower", StringComparison.OrdinalIgnoreCase);

    private static bool IsUndercroftObject(string objectName)
    {
        var lower = objectName.ToLowerInvariant();
        return lower.Contains("tower", StringComparison.Ordinal)
               || lower.Contains("gate", StringComparison.Ordinal)
               || lower.Contains("arch", StringComparison.Ordinal)
               || lower.Contains("_arc", StringComparison.Ordinal)
               || lower.StartsWith("cem_arc", StringComparison.Ordinal)
               || lower.StartsWith("cem_door", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Solid-fills the hollow tower interior (inset from outer mesh bounds) for nav carve only.
    /// </summary>
    private void AddTowerInteriorFillObstruction(
        ColliderBuildState state,
        List<MeshPart> meshParts,
        Transform3D worldTransform)
    {
        if (!TryComputePartsWorldAabb(meshParts, worldTransform, out var outer) || outer.Size.Y < 0.05f)
        {
            return;
        }

        var inset = TowerInteriorInset;
        var innerSize = new Vector3(
            outer.Size.X - inset * 2f,
            outer.Size.Y,
            outer.Size.Z - inset * 2f);
        if (innerSize.X < 0.4f || innerSize.Z < 0.4f)
        {
            return;
        }

        var inner = new Aabb(outer.Position + new Vector3(inset, 0f, inset), innerSize);
        AppendObstructionAabb(state, worldTransform.Origin, inner);
    }

    /// <summary>
    ///     Nav-only: append near-ground drawable mesh triangles (instead of a single full-object AABB) so
    ///     the projected carve follows the object's real footprint — pillars/walls/curved or L-shaped
    ///     buildings carve their actual silhouette, and arch/tower openings stay walkable, rather than an
    ///     inflated bounding rectangle. Does not change physics colliders. Used both for keyword-tagged
    ///     undercroft objects (tower/gate/arch, with <paramref name="xzInflate" /> for thin tower walls) and
    ///     as the general fallback for any other object with no imported collision shapes.
    /// </summary>
    private void AddNearGroundMeshObstruction(
        ColliderBuildState state,
        List<MeshPart> meshParts,
        Transform3D worldTransform,
        float xzInflate)
    {
        var tileGroupKey = OutsideTerrainTileNodeName;
        if (state.TileIndex is not null
            && state.TileIndex.TryGetTile(worldTransform.Origin, out var masterName, out var occurrence))
        {
            tileGroupKey = TerrainTileGridIndex.BuildTileGroupKey(masterName, occurrence);
        }

        if (!state.TileFaces.TryGetValue(tileGroupKey, out var faces))
        {
            faces = new List<Vector3>();
            state.TileFaces[tileGroupKey] = faces;
        }

        var maxY = worldTransform.Origin.Y + NearGroundCarveMaxHeight;
        foreach (var part in meshParts)
        {
            AppendMeshFacesNearGround(
                part.Mesh,
                worldTransform * part.LocalToRoot,
                maxY,
                xzInflate,
                faces);
        }
    }

    private static void AppendMeshFacesNearGround(
        Mesh mesh,
        Transform3D transform,
        float maxVertexY,
        float xzInflate,
        List<Vector3> faceAccumulator)
    {
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indexVariant = arrays[(int)Mesh.ArrayType.Index];

            void ConsiderTri(Vector3 a, Vector3 b, Vector3 c)
            {
                var wa = transform * a;
                var wb = transform * b;
                var wc = transform * c;
                if (Mathf.Min(wa.Y, Mathf.Min(wb.Y, wc.Y)) > maxVertexY)
                {
                    return;
                }

                if (xzInflate <= 0f)
                {
                    faceAccumulator.Add(wa);
                    faceAccumulator.Add(wb);
                    faceAccumulator.Add(wc);
                    return;
                }

                // Emit an inflated XZ box for this tri so thin wall planes carve with real thickness.
                var mn = new Vector3(
                    Mathf.Min(wa.X, Mathf.Min(wb.X, wc.X)) - xzInflate,
                    Mathf.Min(wa.Y, Mathf.Min(wb.Y, wc.Y)),
                    Mathf.Min(wa.Z, Mathf.Min(wb.Z, wc.Z)) - xzInflate);
                var mx = new Vector3(
                    Mathf.Max(wa.X, Mathf.Max(wb.X, wc.X)) + xzInflate,
                    Mathf.Max(wa.Y, Mathf.Max(wb.Y, wc.Y)),
                    Mathf.Max(wa.Z, Mathf.Max(wb.Z, wc.Z)) + xzInflate);
                AppendAabbBoxFaces(new Aabb(mn, mx - mn), faceAccumulator);
            }

            if (indexVariant.VariantType == Variant.Type.Nil)
            {
                for (var i = 0; i + 2 < vertices.Length; i += 3)
                {
                    ConsiderTri(vertices[i], vertices[i + 1], vertices[i + 2]);
                }

                continue;
            }

            var indices = indexVariant.AsInt32Array();
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                ConsiderTri(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
            }
        }
    }    /// <summary>
         ///     Builds a trunk-sized world AABB for nav carve. Multi-part models keep only the smaller-XZ parts;
         ///     XZ is always clamped around the placement origin so canopy never dominates the hole.
         /// </summary>
    private static Aabb ResolveTreeTrunkAabb(List<MeshPart> meshParts, Transform3D worldTransform)
    {
        var partAabbs = new List<(Aabb Aabb, float Xz)>();
        foreach (var part in meshParts)
        {
            if (!TryComputePartWorldAabb(part, worldTransform, out var partAabb) || partAabb.Size.Y < 0.05f)
            {
                continue;
            }

            var xz = Mathf.Max(partAabb.Size.X, partAabb.Size.Z);
            partAabbs.Add((partAabb, xz));
        }

        Aabb trunk;
        if (partAabbs.Count == 0)
        {
            trunk = MakeTrunkCylinderAabb(worldTransform.Origin, TreeTrunkFallbackRadius, 2f);
        }
        else if (partAabbs.Count == 1)
        {
            trunk = partAabbs[0].Aabb;
        }
        else
        {
            var minXz = partAabbs[0].Xz;
            var maxXz = partAabbs[0].Xz;
            foreach (var (_, xz) in partAabbs)
            {
                minXz = Mathf.Min(minXz, xz);
                maxXz = Mathf.Max(maxXz, xz);
            }

            // Keep parts that look trunk-like vs the widest canopy part.
            var threshold = Mathf.Max(minXz * 1.75f, maxXz * 0.45f);
            var has = false;
            trunk = default;
            foreach (var (aabb, xz) in partAabbs)
            {
                if (xz > threshold)
                {
                    continue;
                }

                if (!has)
                {
                    trunk = aabb;
                    has = true;
                }
                else
                {
                    trunk = trunk.Merge(aabb);
                }
            }

            if (!has)
            {
                // Degenerate: pick the narrowest part.
                trunk = partAabbs[0].Aabb;
                var best = partAabbs[0].Xz;
                for (var i = 1; i < partAabbs.Count; i++)
                {
                    if (partAabbs[i].Xz < best)
                    {
                        best = partAabbs[i].Xz;
                        trunk = partAabbs[i].Aabb;
                    }
                }
            }
        }

        return ClampAabbXzAroundOrigin(trunk, worldTransform.Origin, TreeTrunkMaxRadius);
    }

    private static Aabb MakeTrunkCylinderAabb(Vector3 origin, float radius, float height)
    {
        return new Aabb(
            new Vector3(origin.X - radius, origin.Y, origin.Z - radius),
            new Vector3(radius * 2f, height, radius * 2f));
    }

    private static Aabb ClampAabbXzAroundOrigin(Aabb aabb, Vector3 origin, float maxRadius)
    {
        var halfX = Mathf.Min(aabb.Size.X * 0.5f, maxRadius);
        var halfZ = Mathf.Min(aabb.Size.Z * 0.5f, maxRadius);
        // Prefer object origin on XZ; keep original Y range for vertical carve padding.
        var y0 = aabb.Position.Y;
        var y1 = aabb.Position.Y + aabb.Size.Y;
        if (aabb.Size.Y < 0.05f)
        {
            y0 = origin.Y;
            y1 = origin.Y + 2f;
        }

        return new Aabb(
            new Vector3(origin.X - halfX, y0, origin.Z - halfZ),
            new Vector3(halfX * 2f, y1 - y0, halfZ * 2f));
    }

    private static bool TryComputePartsWorldAabb(
        List<MeshPart> meshParts,
        Transform3D worldTransform,
        out Aabb aabb)
    {
        aabb = default;
        var has = false;
        foreach (var part in meshParts)
        {
            if (!TryComputePartWorldAabb(part, worldTransform, out var partAabb))
            {
                continue;
            }

            if (!has)
            {
                aabb = partAabb;
                has = true;
            }
            else
            {
                aabb = aabb.Merge(partAabb);
            }
        }

        return has;
    }

    private static bool TryComputePartWorldAabb(MeshPart part, Transform3D worldTransform, out Aabb aabb)
    {
        aabb = default;
        var localAabb = part.Mesh.GetAabb();
        var xform = worldTransform * part.LocalToRoot;
        var has = false;
        for (var i = 0; i < 8; i++)
        {
            var worldCorner = xform * localAabb.GetEndpoint(i);
            if (!has)
            {
                aabb = new Aabb(worldCorner, Vector3.Zero);
                has = true;
            }
            else
            {
                aabb = aabb.Expand(worldCorner);
            }
        }

        return has;
    }

    private static void AppendAabbBoxFaces(Aabb aabb, List<Vector3> faces)
    {
        var mn = aabb.Position;
        var mx = aabb.Position + aabb.Size;
        // 6 faces × 2 tris × 3 verts. Order does not matter for projected-obstruction clustering.
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            faces.Add(a);
            faces.Add(b);
            faces.Add(c);
            faces.Add(a);
            faces.Add(c);
            faces.Add(d);
        }

        Quad(new Vector3(mn.X, mn.Y, mn.Z), new Vector3(mx.X, mn.Y, mn.Z), new Vector3(mx.X, mx.Y, mn.Z),
            new Vector3(mn.X, mx.Y, mn.Z));
        Quad(new Vector3(mn.X, mn.Y, mx.Z), new Vector3(mn.X, mx.Y, mx.Z), new Vector3(mx.X, mx.Y, mx.Z),
            new Vector3(mx.X, mn.Y, mx.Z));
        Quad(new Vector3(mn.X, mn.Y, mn.Z), new Vector3(mn.X, mx.Y, mn.Z), new Vector3(mn.X, mx.Y, mx.Z),
            new Vector3(mn.X, mn.Y, mx.Z));
        Quad(new Vector3(mx.X, mn.Y, mn.Z), new Vector3(mx.X, mn.Y, mx.Z), new Vector3(mx.X, mx.Y, mx.Z),
            new Vector3(mx.X, mx.Y, mn.Z));
        Quad(new Vector3(mn.X, mn.Y, mn.Z), new Vector3(mn.X, mn.Y, mx.Z), new Vector3(mx.X, mn.Y, mx.Z),
            new Vector3(mx.X, mn.Y, mn.Z));
        Quad(new Vector3(mn.X, mx.Y, mn.Z), new Vector3(mx.X, mx.Y, mn.Z), new Vector3(mx.X, mx.Y, mx.Z),
            new Vector3(mn.X, mx.Y, mx.Z));
    }

    /// <summary>
    ///     Same vegetation / decorative skip list as <c>Tools/generate_colliders.py</c> — these should not block nav.
    /// </summary>
    private static bool ShouldSkipMeshObstruction(string objectName)
    {
        var lower = objectName.ToLowerInvariant();
        return lower.Contains("bush")
               || lower.Contains("grass")
               || lower.StartsWith("fl_", StringComparison.Ordinal)
               || lower.StartsWith("flower", StringComparison.Ordinal)
               || lower.StartsWith("kamysh", StringComparison.Ordinal)
               || lower.StartsWith("pyram", StringComparison.Ordinal)
               || lower.StartsWith("vine", StringComparison.Ordinal)
               || lower is "cam_cube" or "treeput"
               || lower.StartsWith("tn2_fl", StringComparison.Ordinal);
    }

    /// <summary>
    ///     World origin for object→tile assignment, matching the exact frame
    ///     <see cref="TerrainNavigationBaker" /> positions ground tiles in (<c>gridWorldOrigin + gx*tileSize</c>,
    ///     where <c>gridWorldOrigin</c> is the live "Terrain" GridMap's own local <see cref="GridMap.Position" />).
    ///     No longer falls back to the Hyperion-centered <see cref="TerrainWorldOrigin" /> default when the
    ///     GridMap sits at (0,0,0): that used to look like a "never rebuilt" placeholder, but (0,0,0) is the
    ///     GridMap's real, current position (verified against the committed baked nav meshes) — silently
    ///     substituting -4000 here bucketed every object into the wrong tile relative to where ground tiles
    ///     are actually baked.
    /// </summary>
    private Vector3 ResolveTerrainWorldOrigin()
    {
        Node? walk = this;
        while (walk is not null)
        {
            if (walk.FindChild(TerrainGridFill.TerrainNodeName, recursive: true, owned: false) is GridMap terrain)
            {
                return terrain.Position;
            }

            walk = walk.GetParent();
        }

        var gridFill = GetNodeOrNull<TerrainGridFill>("../TerrainGrid")
                       ?? GetParent()?.GetNodeOrNull<TerrainGridFill>("TerrainGrid");
        var gridTerrain = gridFill?.GetNodeOrNull<GridMap>(TerrainGridFill.TerrainNodeName);
        if (gridTerrain is not null)
        {
            return gridTerrain.Position;
        }

        return TerrainWorldOrigin;
    }

    /// <summary>
    ///     Transform from this node's (TerrainObjects) own local space into the "Terrain" GridMap's local
    ///     space — the frame <see cref="TerrainNavigationBaker" /> positions ground tiles in. TerrainObjects
    ///     and TerrainGrid are independently offset/rotated siblings under TerrainScene (e.g. TerrainObjects
    ///     currently sits at a -90° Y rotation plus its own translation, while TerrainGrid has an unrelated
    ///     translation of its own), so nav obstruction geometry accumulated from object placements must go
    ///     through this transform before being combined with ground-tile faces, or every baked obstruction
    ///     hole ends up rotated/offset away from the object that actually produced it. The visual multimesh
    ///     placement is unaffected — it inherits this node's Transform automatically via the scene graph.
    /// </summary>
    private Transform3D GetObjectsToGridLocalTransform()
    {
        var gridFill = GetNodeOrNull<TerrainGridFill>("../TerrainGrid")
                       ?? GetParent()?.GetNodeOrNull<TerrainGridFill>("TerrainGrid");
        if (gridFill is null)
        {
            GD.PushWarning(
                "TerrainObjectsFill: TerrainGrid sibling not found; nav obstruction geometry will use the "
                + "raw (un-rotated) placement transform, which is almost certainly wrong.");
            return Transform3D.Identity;
        }

        return gridFill.Transform.AffineInverse() * Transform;
    }

    /// <summary>
    ///     Appends <paramref name="shape" />'s triangle geometry, transformed by <paramref name="transform" />, to
    ///     <paramref name="faceAccumulator" /> (3 consecutive entries per triangle). Reads triangle faces directly
    ///     for <see cref="ConcavePolygonShape3D" /> (the common case for '-col'-imported meshes); any other
    ///     <see cref="Shape3D" /> type falls back to its debug mesh representation, which every shape provides.
    /// </summary>
    private static void AppendShapeFacesWorldSpace(Shape3D shape, Transform3D transform, List<Vector3> faceAccumulator)
    {
        if (shape is ConcavePolygonShape3D concave)
        {
            foreach (var v in concave.GetFaces())
            {
                faceAccumulator.Add(transform * v);
            }

            return;
        }

        var debugMesh = shape.GetDebugMesh();
        for (var surfaceIndex = 0; surfaceIndex < debugMesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = debugMesh.SurfaceGetArrays(surfaceIndex);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indexVariant = arrays[(int)Mesh.ArrayType.Index];
            if (indexVariant.VariantType == Variant.Type.Nil)
            {
                foreach (var v in vertices)
                {
                    faceAccumulator.Add(transform * v);
                }

                continue;
            }

            foreach (var index in indexVariant.AsInt32Array())
            {
                faceAccumulator.Add(transform * vertices[index]);
            }
        }
    }

    private static string CapitalizeFirstLetter(string value)
    {
        return string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    /// <summary>
    ///     Removes and queues deletion of every child of <paramref name="node" />. <see cref="Node.RemoveChild" /> is
    ///     called synchronously (not just <see cref="Node.QueueFree" />) so a same-call lookup-or-create right after
    ///     (e.g. <see cref="GetOrCreateChildNodeUnder" />) can't find a "zombie" node that is still a child (because
    ///     its deferred free hasn't run yet) and graft fresh content onto it, only for that content to disappear when
    ///     the deferred free eventually fires.
    /// </summary>
    private static void ClearChildren(Node3D node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private PackedScene? GetOrLoadScene(string objectName)
    {
        var baseDir = ModelsDirectory.TrimEnd('/') + "/";
        foreach (var ext in new[] { "glb", "gltf" })
        {
            var path = $"{baseDir}{objectName}.{ext}";
            if (ResourceLoader.Exists(path))
            {
                return ResourceLoader.Load<PackedScene>(path);
            }
        }

        return null;
    }

    private void SetOwnerIfEditor(Node node)
    {
        if (!Engine.IsEditorHint())
        {
            return;
        }

        // Nodes only show up properly in the Scene dock / get saved if their Owner is the edited scene root.
        // `EditedSceneRoot` can be null in some editor tool execution contexts, so fall back to this node's owner chain.
        var root = GetTree()?.EditedSceneRoot ?? Owner ?? this;
        node.Owner = root;
    }

    /// <summary>
    ///     Uses Godot's <see cref="Json" /> parser (not Newtonsoft.Json) so collectible assemblies can unload in the editor.
    ///     See https://github.com/godotengine/godot/issues/78513
    /// </summary>
    private static List<TerrainObjectRecord>? ParseTerrainRecords(string jsonText)
    {
        var json = new Json();
        if (json.Parse(jsonText) != Error.Ok)
        {
            return null;
        }

        var root = json.Data;
        if (root.VariantType != Variant.Type.Array)
        {
            return null;
        }

        var arr = root.AsGodotArray();
        var list = new List<TerrainObjectRecord>();
        foreach (var item in arr)
        {
            if (item.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            var d = item.AsGodotDictionary();
            // Accept both legacy ObjectData JSON (object_name/coordinates/rotation_euler)
            // and MbdConverter JSON (name/x/y/z/pitch/yaw/roll).
            string objectName;
            if (d.TryGetValue("object_name", out var nameVar))
            {
                objectName = nameVar.AsString().ToLower();
            }
            else if (d.TryGetValue("name", out var mbdNameVar))
            {
                objectName = mbdNameVar.AsString().ToLower();
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(objectName))
            {
                continue;
            }

            var rec = new TerrainObjectRecord { ObjectName = objectName };

            if (d.TryGetValue("coordinates", out var coordVar) && coordVar.VariantType == Variant.Type.Dictionary)
            {
                var cd = coordVar.AsGodotDictionary();
                rec.Coordinates = new TerrainCoordinates
                {
                    X = DictGetDouble(cd, "x"),
                    Y = DictGetDouble(cd, "y"),
                    Z = DictGetDouble(cd, "z")
                };
            }
            else if (d.ContainsKey("x") || d.ContainsKey("y") || d.ContainsKey("z"))
            {
                rec.Coordinates = new TerrainCoordinates
                {
                    X = DictGetDouble(d, "x"),
                    Y = DictGetDouble(d, "y"),
                    Z = DictGetDouble(d, "z")
                };
            }

            if (d.TryGetValue("rotation_euler", out var rotVar) && rotVar.VariantType == Variant.Type.Dictionary)
            {
                var rd = rotVar.AsGodotDictionary();
                rec.RotationEuler = new TerrainRotationEuler
                {
                    Yaw = DictGetDouble(rd, "yaw"),
                    Pitch = DictGetDouble(rd, "pitch"),
                    Roll = DictGetDouble(rd, "roll")
                };
            }
            else if (d.ContainsKey("pitch") || d.ContainsKey("yaw") || d.ContainsKey("roll"))
            {
                rec.RotationEuler = new TerrainRotationEuler
                {
                    Yaw = DictGetDouble(d, "yaw"),
                    Pitch = DictGetDouble(d, "pitch"),
                    Roll = DictGetDouble(d, "roll")
                };
            }

            list.Add(rec);
        }

        return list;
    }

    private static double DictGetDouble(Dictionary d, StringName key)
    {
        return d.TryGetValue(key, out var v) ? v.AsDouble() : 0.0;
    }

    private readonly struct InstancePlacement
    {
        public InstancePlacement(Transform3D transform, int sourceRecordIndex)
        {
            Transform = transform;
            SourceRecordIndex = sourceRecordIndex;
        }

        public Transform3D Transform { get; }
        public int SourceRecordIndex { get; }
    }

    private sealed class MeshPart
    {
        public MeshPart(Mesh mesh, Transform3D localToRoot)
        {
            Mesh = mesh;
            LocalToRoot = localToRoot;
        }

        public Mesh Mesh { get; }
        public Transform3D LocalToRoot { get; }
    }

    private sealed class ColliderPart
    {
        public ColliderPart(Shape3D shape, Transform3D localToRoot)
        {
            Shape = shape;
            LocalToRoot = localToRoot;
        }

        public Shape3D Shape { get; }
        public Transform3D LocalToRoot { get; }
    }

    /// <summary>Cached per-object-name extraction result: drawable mesh parts plus any collider parts found.</summary>
    private sealed class ObjectSceneParts
    {
        public required List<MeshPart> MeshParts { get; init; }
        public required List<ColliderPart> ColliderParts { get; init; }
    }

    /// <summary>Mutable state threaded through one <see cref="RebuildTerrainObjects" /> run for collider building.</summary>
    private sealed class ColliderBuildState
    {
        public required TerrainTileGridIndex? TileIndex { get; init; }

        /// <summary>See <see cref="GetObjectsToGridLocalTransform" />.</summary>
        public required Transform3D ObjectsToGridLocal { get; init; }

        /// <summary>
        ///     World-space triangle faces (flat list, 3 consecutive entries per triangle) accumulated per tile
        ///     group key. Read back via <see cref="LastBuiltObjectColliderFacesByTile" /> - nothing here is ever
        ///     turned into scene nodes (see that property's doc for why).
        /// </summary>
        public global::System.Collections.Generic.Dictionary<string, List<Vector3>> TileFaces { get; } = new();
    }

    /// <summary>
    ///     Maps a world position to the terrain grid cell it falls on, using the exact same cell layout
    ///     <see cref="TerrainGridFill.RebuildTerrainGrid" /> uses to fill the GridMap (<see cref="MapFill" />, row-major,
    ///     <c>gx = GridWidth - (i % GridWidth) - 1</c>). "Occurrence" numbers repeated uses of the same master tile
    ///     (0, 1, 2, ...) in that same row-major order, so tile group names stay stable across rebuilds.
    ///     Internal (not private) so <see cref="TerrainNavigationBaker" /> can reuse the exact same tile-group
    ///     keys and cell enumeration when it builds ground + object source geometry per tile.
    /// </summary>
    internal sealed class TerrainTileGridIndex
    {
        private readonly global::System.Collections.Generic.Dictionary<(int Gx, int Gz), (string MasterName, int Occurrence)>
            cellsByCoord;

        private readonly float tileSize;
        private readonly Vector3 worldOrigin;

        private TerrainTileGridIndex(
            global::System.Collections.Generic.Dictionary<(int Gx, int Gz), (string MasterName, int Occurrence)> cellsByCoord,
            float tileSize,
            Vector3 worldOrigin)
        {
            this.cellsByCoord = cellsByCoord;
            this.tileSize = tileSize;
            this.worldOrigin = worldOrigin;
        }

        public static TerrainTileGridIndex? TryBuild(string mapBinResourcePath, float tileSize, Vector3 worldOrigin)
        {
            var abs = ProjectSettings.GlobalizePath(mapBinResourcePath);
            if (!File.Exists(abs))
            {
                return null;
            }

            var cells = MapFill.ReadFullGrid(abs);
            var cellsByCoord =
                new global::System.Collections.Generic.Dictionary<(int Gx, int Gz), (string MasterName, int Occurrence)>();
            var nextOccurrenceByMaster = new global::System.Collections.Generic.Dictionary<string, int>();
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell.IsEmpty)
                {
                    continue;
                }

                if (!nextOccurrenceByMaster.TryGetValue(cell.MasterName, out var occurrence))
                {
                    occurrence = 0;
                }

                nextOccurrenceByMaster[cell.MasterName] = occurrence + 1;

                var gx = MapFill.GridWidth - (i % MapFill.GridWidth) - 1;
                var gz = i / MapFill.GridWidth;
                cellsByCoord[(gx, gz)] = (cell.MasterName, occurrence);
            }

            return new TerrainTileGridIndex(cellsByCoord, tileSize, worldOrigin);
        }

        public bool TryGetTile(Vector3 worldPosition, out string masterName, out int occurrence)
        {
            var local = worldPosition - worldOrigin;
            var gx = Mathf.FloorToInt(local.X / tileSize);
            var gz = Mathf.FloorToInt(local.Z / tileSize);
            if (cellsByCoord.TryGetValue((gx, gz), out var found))
            {
                masterName = found.MasterName;
                occurrence = found.Occurrence;
                return true;
            }

            masterName = string.Empty;
            occurrence = 0;
            return false;
        }

        /// <summary>All occupied cells with their grid coordinate, master tile name, and occurrence index.</summary>
        public IEnumerable<((int Gx, int Gz) Coord, string MasterName, int Occurrence)> EnumerateCells()
        {
            foreach (var kv in cellsByCoord)
            {
                yield return (kv.Key, kv.Value.MasterName, kv.Value.Occurrence);
            }
        }

        /// <summary>World-space tile-group key (matching <see cref="TerrainObjectsFill" />'s naming) for a cell.</summary>
        public static string BuildTileGroupKey(string masterName, int occurrence)
        {
            return $"{CapitalizeFirstLetter(SanitizeGodotNodeName(masterName))}_{occurrence:D2}";
        }
    }

    /// <summary>Reference-equality for <see cref="Mesh" /> so batches merge identical resources.</summary>
    private sealed class MeshBatchKeyComparer : IEqualityComparer<(Node3D Category, string ObjectName, Mesh Mesh)>
    {
        public bool Equals((Node3D Category, string ObjectName, Mesh Mesh) x,
            (Node3D Category, string ObjectName, Mesh Mesh) y)
        {
            return ReferenceEquals(x.Category, y.Category)
                   && string.Equals(x.ObjectName, y.ObjectName, StringComparison.Ordinal)
                   && ReferenceEquals(x.Mesh, y.Mesh);
        }

        public int GetHashCode((Node3D Category, string ObjectName, Mesh Mesh) obj)
        {
            return HashCode.Combine(obj.Category.GetInstanceId(), obj.ObjectName, obj.Mesh.GetInstanceId());
        }
    }

    private sealed class TerrainObjectRecord
    {
        public string ObjectName { get; set; } = string.Empty;
        public TerrainCoordinates? Coordinates { get; set; }
        public TerrainRotationEuler? RotationEuler { get; set; }
    }

    private sealed class TerrainCoordinates
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public Vector3 ToVector3()
        {
            return SourceWorldToGodotWorldBasis * new Vector3((float)X, (float)Y, (float)Z);
        }
    }

    /// <summary>
    ///     JSON uses yaw (Y), pitch (X), roll (Z) — same component order as <see cref="BuildPlacementTransform" /> /
    ///     <see cref="EulerOrder.Yxz" />.
    ///     Euler is in <b>source</b> space (Y down, Z forward). Yaw is negated when building <see cref="Basis.FromEuler" /> so
    ///     “yaw about down” matches Godot Y-up; then R_godot = T R_src T (conjugate — not T R_src, which leaves a 180° X flip
    ///     on identity and inverts GLBs).
    /// </summary>
    private sealed class TerrainRotationEuler
    {
        public double Yaw { get; set; }
        public double Pitch { get; set; }
        public double Roll { get; set; }

        /// <summary>
        ///     R_src from YXZ Euler with negated yaw; same physical orientation in Godot world as R' = T R_src T⁻¹ with T =
        ///     <see cref="SourceWorldToGodotWorldBasis" />.
        /// </summary>
        public Vector3 ToEulerRadians()
        {
            var eulerForGodotBasis = new Vector3((float)Pitch, -(float)Yaw, (float)Roll);
            var basisSource = Basis.FromEuler(eulerForGodotBasis);
            var t = SourceWorldToGodotWorldBasis;
            var basisGodot = t * basisSource * t;
            return basisGodot.GetEuler();
        }
    }
}