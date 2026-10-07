using Godot;
using Godot.Collections;
using SphServer.Sphere.Game.WorldObject;
using System.IO;
using FileAccess = Godot.FileAccess;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
/// Source space is X right, Y down, Z forward. Positions map (x, y, z) to (x, -y, -z). Rotations
/// use R' = T R T so identity stays identity; T R_src alone flips GLBs 180 deg about X
/// </summary>
[Tool]
public partial class TerrainObjectsFill : Node3D
{
    public const string PlantsNodeName = "TerrainPlants";
    public const string RocksNodeName = "TerrainRocks";
    public const string OtherNodeName = "TerrainOther";
    public const string ExtraInstancedGroupsRootName = "ExtraInstancedGroups";

    /// <summary>
    /// Placements that miss every terrain grid cell
    /// </summary>
    public const string OutsideTerrainTileNodeName = "OutsideTerrain";

    /// <summary>
    /// Columns are source X, Y, Z in Godot: right, down, forward (-Z), so (x, y, z) maps to (x, -y,
    /// -z)
    /// </summary>
    private static readonly Basis SourceWorldToGodotWorldBasis = new (Vector3.Right, Vector3.Down, Vector3.Forward);

    [Export] public string ObjectDataDirectory { get; set; } = "res://Godot/Terrain/ObjectDataJson/";

    [Export] public string ModelsDirectory { get; set; } = "res://Godot/Models/";

    /// <summary>
    /// Faces stay in memory. Live physics shapes in the scene hang the editor
    /// </summary>
    [Export]
    public bool BuildObjectColliders { get; set; } = true;

    /// <summary>
    /// Same map as TerrainGridFill.MapBinPath, used to name per-tile collider groups
    /// </summary>
    [Export]
    public string MapBinPath { get; set; } = "res://Godot/Terrain/map.txt";

    [Export] public float TileSizeWorld { get; set; } = 100f;

    [Export] public Vector3 TerrainWorldOrigin { get; set; } = new (-4000f, 0f, -4000f);

    /// <summary>
    /// External .res instead of embedding MultiMeshes in the tscn, or scene load parses a huge
    /// sub-resource
    /// </summary>
    [Export]
    public bool SaveMultiMeshesAsExternalResources { get; set; } = true;

    [Export]
    public string MultiMeshResourcesDirectory { get; set; } = "";

    [ExportToolButton ("Rebuild terrain objects")]
    public Callable RebuildTerrainObjectsButton => Callable.From (RebuildTerrainObjects);

    /// <summary>
    /// Not persisted, and not used by the production nav bake. 3 verts per triangle, keyed like the
    /// object hierarchy
    /// </summary>
    public global::System.Collections.Generic.Dictionary<string, List<Vector3>> LastBuiltObjectColliderFacesByTile
    {
        get;
        private set;
    } = new ();

    public void RebuildTerrainObjects ()
    {
        var dir = ObjectDataDirectory.TrimEnd ('/') + "/";
        if (!DirAccess.DirExistsAbsolute (ProjectSettings.GlobalizePath (dir)))
        {
            GD.PushError ($"TerrainObjectsFill: directory not found: {ObjectDataDirectory}");
            return;
        }

        var plants = GetOrCreateCategory (PlantsNodeName);
        var rocks = GetOrCreateCategory (RocksNodeName);
        var other = GetOrCreateCategory (OtherNodeName);
        var terrainObjects = GetOrCreateCategory (ExtraInstancedGroupsRootName);

        // Editor-created children show up as @Class@id unless these roots are owned by the edited
        // scene
        if (Engine.IsEditorHint ())
        {
            var sceneOwner = GetTree ()?.EditedSceneRoot ?? Owner ?? this;
            plants.Owner = sceneOwner;
            rocks.Owner = sceneOwner;
            other.Owner = sceneOwner;
            terrainObjects.Owner = sceneOwner;
        }

        ClearChildren (plants);
        ClearChildren (rocks);
        ClearChildren (other);
        ClearChildren (terrainObjects);

        // Collider faces stay in memory under the same tile key as the object hierarchy. They are
        // not added to the scene
        ColliderBuildState? colliderState = null;
        if (BuildObjectColliders)
        {
            // Live GridMap origin, not the exported default (-4000). A GridMap at (0, 0, 0)
            // otherwise buckets every placement into the wrong cell
            var worldOrigin = ResolveTerrainWorldOrigin ();
            var tileIndex = TerrainTileGridIndex.TryBuild (MapBinPath, TileSizeWorld, worldOrigin);
            if (tileIndex is null)
            {
                GD.PushWarning (
                    $"TerrainObjectsFill: map not found ({MapBinPath}); grouping object colliders under "
                    + $"'{OutsideTerrainTileNodeName}' instead of per-tile.");
            }

            colliderState = new ColliderBuildState
            {
                TileIndex = tileIndex,
                ObjectsToGridLocal = GetObjectsToGridLocalTransform ()
            };
        }

        // (category root, source object name, Mesh) -> instance placements (world * mesh-local)
        // ObjectName stays on the key so the MultiMesh node can be named after the source object
        var batches =
            new global::System.Collections.Generic.Dictionary<(Node3D Category, string ObjectName, Mesh Mesh),
                List<InstancePlacement>> (new MeshBatchKeyComparer ());
        var objectPartsCache = new global::System.Collections.Generic.Dictionary<string, ObjectSceneParts?> ();

        var da = DirAccess.Open (dir);
        if (da is null)
        {
            GD.PushError ($"TerrainObjectsFill: could not open: {ObjectDataDirectory}");
            return;
        }

        da.ListDirBegin ();
        while (true)
        {
            var name = da.GetNext ();
            if (name == string.Empty)
            {
                break;
            }

            if (name is "." or "..")
            {
                continue;
            }

            // Top-level .json files stay on the multimesh batch path
            if (!da.CurrentIsDir () && name.EndsWith (".json", StringComparison.OrdinalIgnoreCase))
            {
                var path = dir + name;
                ProcessJsonForBatches (path, plants, rocks, other, batches, objectPartsCache, colliderState);
                continue;
            }

            // Subfolders are optional toggles: direct scene instances, no multimesh, under
            // TerrainObjects\<folder>\<file>
            if (da.CurrentIsDir ())
            {
                var subDir = dir + name;
                ProcessJsonFolderAsDirectInstances (subDir, name, terrainObjects);
            }
        }

        da.ListDirEnd ();
        da.Dispose ();

        LastBuiltObjectColliderFacesByTile = colliderState?.TileFaces
            ?? new global::System.Collections.Generic.Dictionary<string, List<Vector3>> ();

        var obstructionTris = 0L;
        foreach (var faces in LastBuiltObjectColliderFacesByTile.Values)
        {
            obstructionTris += faces.Count / 3;
        }

        GD.Print (
            $"TerrainObjectsFill: obstruction tile groups={LastBuiltObjectColliderFacesByTile.Count}, "
            + $"tris={obstructionTris} (origin={ResolveTerrainWorldOrigin ()})");

        var nextIndexByCategoryAndObjectName =
            new global::System.Collections.Generic.Dictionary<(ulong CategoryId, string ObjectName), int> ();
        foreach (var kv in batches)
        {
            var (category, objectName, mesh) = kv.Key;
            var placements = kv.Value;
            if (placements.Count == 0)
            {
                continue;
            }

            var indexKey = (category.GetInstanceId (), objectName);
            if (!nextIndexByCategoryAndObjectName.TryGetValue (indexKey, out var objectIndex))
            {
                objectIndex = 0;
            }

            // TransformFormat and UseCustomData reject changes once InstanceCount is non-zero.
            // Object initializers can set that first and error on load
            var mm = new MultiMesh ();
            mm.Mesh = mesh;
            mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            mm.UseCustomData = true;
            mm.InstanceCount = placements.Count;

            for (var i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                mm.SetInstanceTransform (i, p.Transform);
                // Source record index, up to 24 bits, packed into RGB 0..1 for editor tools that
                // map an instance back to the JSON record
                var idx = p.SourceRecordIndex;
                var r = (idx & 0xFF) / 255f;
                var g = ((idx >> 8) & 0xFF) / 255f;
                var b = ((idx >> 16) & 0xFF) / 255f;
                mm.SetInstanceCustomData (i, new Color (r, g, b, 0f));
            }

            // The headless dummy renderer ignores SetInstanceTransform. Saving that buffer writes a
            // ~9 KB .res with every instance at the origin
            if (placements.Count > 0)
            {
                var expectedOrigin = placements[0].Transform.Origin;
                var actualOrigin = mm.GetInstanceTransform (0).Origin;
                if (expectedOrigin.DistanceTo (actualOrigin) > 0.01f)
                {
                    GD.PushError (
                        "TerrainObjectsFill: MultiMesh instance transforms were not applied "
                        + $"(expected origin {expectedOrigin}, got {actualOrigin}). "
                        + "Do not run RebuildTerrainObjects with --headless; use the normal editor "
                        + "or a GPU-backed Godot process.");
                    continue;
                }
            }

            var mmToAssign = mm;

            var safeObjectName = SanitizeGodotNodeName (objectName);
            // Headless must write the .res too. An embedded MultiMesh sets instance_count before
            // transform_format and corrupts the buffer
            if (SaveMultiMeshesAsExternalResources)
            {
                var baseDir = string.IsNullOrWhiteSpace (MultiMeshResourcesDirectory)
                    ? TerrainBakePaths.GeneratedMultiMeshesDir.TrimEnd ('/') + "/"
                    : MultiMeshResourcesDirectory.TrimEnd ('/') + "/";
                var catDirName = SanitizeGodotNodeName (category.Name.ToString ());
                var outDir = $"{baseDir}{catDirName}/";
                var abs = outDir.Contains ("://", StringComparison.Ordinal)
                    ? ProjectSettings.GlobalizePath (outDir)
                    : outDir.Replace ('/', Path.DirectorySeparatorChar);
                // SanitizeGodotNodeName lowercases. Windows keeps the old PascalCase directory and
                // Godot warns on the case mismatch
                TerrainBakePaths.EnsureDirectoryExactCase (abs);

                var outPath = outDir.Contains ("://", StringComparison.Ordinal)
                    ? $"{outDir}{safeObjectName}_MM_{objectIndex}.res"
                    : TerrainBakePaths.Combine ("GeneratedMultiMeshes", catDirName,
                        $"{safeObjectName}_MM_{objectIndex}.res");
                var err = ResourceSaver.Save (mm, outPath);
                if (err != Error.Ok)
                {
                    GD.PushWarning ($"TerrainObjectsFill: failed to save multimesh ({err}): {outPath}");
                }
                else
                {
                    // The scene references the external resource only after this load. Otherwise
                    // the buffer stays embedded
                    var loaded = ResourceLoader.Load<MultiMesh> (outPath);
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
            category.AddChild (mmi);
            SetOwnerIfEditor (mmi);

            // Some editor contexts auto-name on add, after Name was already set
            mmi.Name = $"{safeObjectName}_MM_{objectIndex}";
        }

    }

    // TEMP DIAGNOSTIC - remove after use.
    public void DebugPrintColliderFaceStats ()
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

        GD.Print ($"[DEBUG] tile groups with faces: {LastBuiltObjectColliderFacesByTile.Count}, total face-verts: {totalFaces}");
        GD.Print ($"[DEBUG] largest group: {maxKey} with {maxCount} face-verts ({maxCount / 3} tris)");
        if (LastBuiltObjectColliderFacesByTile.TryGetValue (OutsideTerrainTileNodeName, out var outside))
        {
            GD.Print ($"[DEBUG] OutsideTerrain group: {outside.Count} face-verts ({outside.Count / 3} tris)");
        }

        if (LastBuiltObjectColliderFacesByTile.TryGetValue ("Cliffn_rd05_00_04", out var cliffn))
        {
            GD.Print ($"[DEBUG] Cliffn_rd05_00_04 face-verts: {cliffn.Count} ({cliffn.Count / 3} tris)");
        }
        else
        {
            GD.Print ("[DEBUG] Cliffn_rd05_00_04: NO obstruction faces");
        }

        GD.Print ($"[DEBUG] ResolveTerrainWorldOrigin={ResolveTerrainWorldOrigin ()}");
    }

    private static string SanitizeGodotNodeName (string name)
    {
        name = name.ToLower ();
        if (string.IsNullOrWhiteSpace (name))
        {
            return "Object";
        }

        // Godot node names are strings but certain characters can lead to confusing paths or
        // invalid NodePaths.
        // Keep common filename-ish characters; replace everything else with '_'.
        var chars = name.Trim ().ToCharArray ();
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

        var sanitized = new string (chars);
        return sanitized.Length == 0 ? "Object" : sanitized;
    }

    private void ProcessJsonForBatches (
        string path,
        Node3D plants,
        Node3D rocks,
        Node3D other,
        global::System.Collections.Generic.Dictionary<(Node3D Category, string ObjectName, Mesh Mesh),
            List<InstancePlacement>> batches,
        global::System.Collections.Generic.Dictionary<string, ObjectSceneParts?> objectPartsCache,
        ColliderBuildState? colliderState)
    {
        var jsonText = FileAccess.GetFileAsString (path);
        if (string.IsNullOrEmpty (jsonText))
        {
            GD.PushWarning ($"TerrainObjectsFill: empty or unreadable: {path}");
            return;
        }

        List<TerrainObjectRecord>? records;
        try
        {
            records = ParseTerrainRecords (jsonText);
        }
        catch (Exception ex)
        {
            GD.PushWarning ($"TerrainObjectsFill: JSON parse failed ({path}): {ex.Message}");
            return;
        }

        if (records is null || records.Count == 0)
        {
            return;
        }

        for (var recIndex = 0; recIndex < records.Count; recIndex++)
        {
            var rec = records[recIndex];
            if (string.IsNullOrWhiteSpace (rec.ObjectName))
            {
                continue;
            }

            if (!objectPartsCache.TryGetValue (rec.ObjectName, out var parts))
            {
                var scene = GetOrLoadScene (rec.ObjectName);
                parts = scene is null ? null : ExtractSceneParts (scene);
                objectPartsCache[rec.ObjectName] = parts;
                if (parts is null)
                {
                    if (scene is not null)
                    {
                        GD.PushWarning (
                            $"TerrainObjectsFill: no drawable mesh for '{rec.ObjectName}' (missing mesh or skinned-only)");
                    }
                    else
                    {
                        GD.PushWarning (
                            $"TerrainObjectsFill: no model for '{rec.ObjectName}' (tried .scn / .glb / .gltf under {ModelsDirectory})");
                    }

                    continue;
                }
            }
            else if (parts is null)
            {
                continue;
            }

            var pos = rec.Coordinates?.ToVector3 () ?? Vector3.Zero;
            var rot = rec.RotationEuler?.ToEulerRadians () ?? Vector3.Zero;
            var world = BuildPlacementTransform (pos, rot);

            var lower = rec.ObjectName.ToLowerInvariant ();
            Node3D parent;
            if (lower.Contains ("tree") || lower.Contains ("bush") || lower.Contains ("grass"))
            {
                parent = plants;
            }
            else if (lower.Contains ("rock") || lower.Contains ("stone"))
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
                if (!batches.TryGetValue (key, out var list))
                {
                    list = new List<InstancePlacement> ();
                    batches[key] = list;
                }

                list.Add (new InstancePlacement (world * part.LocalToRoot, recIndex));
            }

            // Grass and decorative props have no ColliderParts; the multimesh is unchanged. Trees
            // carve a trunk footprint even when leaf colliders exist for physics
            if (colliderState is not null && !ShouldSkipMeshObstruction (rec.ObjectName))
            {
                // Obstruction verts are in the Terrain GridMap frame. TerrainObjects and
                // TerrainGrid are offset siblings, so the visual `world` transform would rotate the
                // hole off the object. The multimesh still uses `world`
                var navWorld = colliderState.ObjectsToGridLocal * world;
                if (IsTreeObject (rec.ObjectName) && parts.MeshParts.Count > 0)
                {
                    AddTreeTrunkObstruction (colliderState, parts.MeshParts, navWorld);
                }
                else if (IsUndercroftObject (rec.ObjectName) && parts.MeshParts.Count > 0)
                {
                    // Near-ground tris only. An AABB seals arch and tower openings. Towers inflate
                    // in XZ because the wall planes are thin
                    var inflate = IsTowerObject (rec.ObjectName) ? TowerWallInflate : 0f;
                    AddNearGroundMeshObstruction (colliderState, parts.MeshParts, navWorld, inflate);
                    if (IsTowerObject (rec.ObjectName))
                    {
                        // Solid inset fill removes orphan walkable islands inside the hollow shell
                        AddTowerInteriorFillObstruction (colliderState, parts.MeshParts, navWorld);
                    }
                }
                else if (parts.ColliderParts.Count > 0)
                {
                    AddObjectCollider (colliderState, parts.ColliderParts, navWorld);
                }
                else if (parts.MeshParts.Count > 0)
                {
                    // A full AABB around a curved or L-shaped building covers walkable courtyard.
                    // Near-ground tris follow the real footprint
                    AddNearGroundMeshObstruction (colliderState, parts.MeshParts, navWorld, 0f);
                }
            }
        }
    }

    private void ProcessJsonFolderAsDirectInstances (string folderPath, string folderName, Node3D terrainObjectsRoot)
    {
        var da = DirAccess.Open (folderPath);
        if (da is null)
        {
            return;
        }

        da.ListDirBegin ();
        while (true)
        {
            var name = da.GetNext ();
            if (name == string.Empty)
            {
                break;
            }

            if (name is "." or ".." || da.CurrentIsDir () || !name.EndsWith (".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = folderPath.TrimEnd ('/') + "/" + name;
            var jsonText = FileAccess.GetFileAsString (path);
            if (string.IsNullOrEmpty (jsonText))
            {
                continue;
            }

            List<TerrainObjectRecord>? records;
            try
            {
                records = ParseTerrainRecords (jsonText);
            }
            catch (Exception ex)
            {
                GD.PushWarning ($"TerrainObjectsFill: JSON parse failed ({path}): {ex.Message}");
                continue;
            }

            if (records is null || records.Count == 0)
            {
                continue;
            }

            var fileBase = name[..^5]; // trim ".json"
            var folderNode = GetOrCreateChildNodeUnder (terrainObjectsRoot, folderName);
            var fileNode = GetOrCreateChildNodeUnder (folderNode, fileBase);

            var instanceIndex = 0;
            foreach (var rec in records)
            {
                if (string.IsNullOrWhiteSpace (rec.ObjectName))
                {
                    continue;
                }

                var scene = GetOrLoadScene (rec.ObjectName);
                if (scene is null)
                {
                    continue;
                }

                var inst = scene.Instantiate<Node3D> ();
                inst.Name = $"{rec.ObjectName}_{instanceIndex++}";

                var pos = rec.Coordinates?.ToVector3 () ?? Vector3.Zero;
                var rot = rec.RotationEuler?.ToEulerRadians () ?? Vector3.Zero;
                inst.Transform = BuildPlacementTransform (pos, rot);

                fileNode.AddChild (inst);
                SetOwnerIfEditor (inst);
            }
        }

        da.ListDirEnd ();
        da.Dispose ();
    }

    private static Transform3D BuildPlacementTransform (Vector3 position, Vector3 rotationEuler)
    {
        var basis = Basis.FromEuler (rotationEuler);
        return new Transform3D (basis, position);
    }

    /// <summary>
    /// null only when no mesh was found. An empty ColliderParts list is normal for models without
    /// '-col' shapes
    /// </summary>
    private static ObjectSceneParts? ExtractSceneParts (PackedScene scene)
    {
        var root = scene.Instantiate<Node3D> ();
        try
        {
            var meshParts = new List<MeshPart> ();
            var colliderParts = new List<ColliderPart> ();
            CollectParts (root, root, meshParts, colliderParts);
            return meshParts.Count == 0
                ? null
                : new ObjectSceneParts { MeshParts = meshParts, ColliderParts = colliderParts };
        }
        finally
        {
            root.QueueFree ();
        }
    }

    private static void CollectParts (Node node, Node3D root, List<MeshPart> meshList, List<ColliderPart> colliderList)
    {
        if (node is MeshInstance3D mi && mi.Mesh is { } mesh)
        {
            if (HasSkeletonAncestor (mi))
            {
                return;
            }

            meshList.Add (new MeshPart (mesh, ComputeTransformRelativeToRoot (mi, root)));
        }
        else if (node is StaticBody3D body)
        {
            foreach (var child in body.GetChildren ())
            {
                if (child is CollisionShape3D { Shape: { } shape } collisionShape)
                {
                    colliderList.Add (new ColliderPart (shape, ComputeTransformRelativeToRoot (collisionShape, root)));
                }
            }
        }

        foreach (var child in node.GetChildren ())
        {
            CollectParts (child, root, meshList, colliderList);
        }
    }

    private static bool HasSkeletonAncestor (Node node)
    {
        var p = node.GetParent ();
        while (p is not null)
        {
            if (p is Skeleton3D)
            {
                return true;
            }

            p = p.GetParent ();
        }

        return false;
    }

    private static Transform3D ComputeTransformRelativeToRoot (Node3D node, Node3D root)
    {
        var t = Transform3D.Identity;
        var cur = node;
        while (!ReferenceEquals (cur, root) && cur is not null)
        {
            t = cur.Transform * t;
            cur = cur.GetParent () as Node3D;
        }

        return t;
    }

    private Node3D GetOrCreateCategory (string nodeName)
    {
        if (GetNodeOrNull (nodeName) is Node3D existing)
        {
            return existing;
        }

        var n = new Node3D { Name = nodeName };
        AddChild (n);
        SetOwnerIfEditor (n);
        return n;
    }

    private Node3D GetOrCreateChildNode (string nodeName)
    {
        if (GetNodeOrNull (nodeName) is Node3D existing)
        {
            return existing;
        }

        var n = new Node3D { Name = nodeName };
        AddChild (n);
        SetOwnerIfEditor (n);
        return n;
    }

    private Node3D GetOrCreateChildNodeUnder (Node3D parent, string nodeName)
    {
        if (parent.GetNodeOrNull (nodeName) is Node3D existing)
        {
            return existing;
        }

        var n = new Node3D { Name = nodeName };
        parent.AddChild (n);
        SetOwnerIfEditor (n);
        return n;
    }

    /// <summary>
    /// Nothing is added to the scene. worldTransform is the visual placement so the faces line up
    /// with that multimesh
    /// </summary>
    private void AddObjectCollider (
        ColliderBuildState state,
        List<ColliderPart> colliderParts,
        Transform3D worldTransform)
    {
        var tileGroupKey = OutsideTerrainTileNodeName;
        if (state.TileIndex is not null
            && state.TileIndex.TryGetTile (worldTransform.Origin, out var masterName, out var occurrence))
        {
            tileGroupKey = TerrainTileGridIndex.BuildTileGroupKey (masterName, occurrence);
        }

        if (!state.TileFaces.TryGetValue (tileGroupKey, out var faces))
        {
            faces = new List<Vector3> ();
            state.TileFaces[tileGroupKey] = faces;
        }

        foreach (var part in colliderParts)
        {
            AppendShapeFacesWorldSpace (part.Shape, worldTransform * part.LocalToRoot, faces);
        }
    }

    /// <summary>
    /// Nav only. Small-XZ parts, clamped to TreeTrunkMaxRadius, or a cylinder at the origin.
    /// Physics colliders are untouched
    /// </summary>
    private void AddTreeTrunkObstruction (
        ColliderBuildState state,
        List<MeshPart> meshParts,
        Transform3D worldTransform)
    {
        var aabb = ResolveTreeTrunkAabb (meshParts, worldTransform);
        if (aabb.Size.Y < 0.05f)
        {
            return;
        }

        AppendObstructionAabb (state, worldTransform.Origin, aabb);
    }

    private void AppendObstructionAabb (ColliderBuildState state, Vector3 worldOrigin, Aabb aabb)
    {
        var tileGroupKey = OutsideTerrainTileNodeName;
        if (state.TileIndex is not null
            && state.TileIndex.TryGetTile (worldOrigin, out var masterName, out var occurrence))
        {
            tileGroupKey = TerrainTileGridIndex.BuildTileGroupKey (masterName, occurrence);
        }

        if (!state.TileFaces.TryGetValue (tileGroupKey, out var faces))
        {
            faces = new List<Vector3> ();
            state.TileFaces[tileGroupKey] = faces;
        }

        AppendAabbBoxFaces (aabb, faces);
    }

    private const float TreeTrunkFallbackRadius = 0.4f;
    private const float TreeTrunkMaxRadius = 1.0f;

    /// <summary>
    /// Tris whose lowest vertex is above placement origin Y plus this are ignored, so lintels and
    /// roofs do not seal an opening
    /// </summary>
    private const float NearGroundCarveMaxHeight = 2.0f;

    /// <summary>
    /// Tower meshes are thin planes. Without this the projected carve is about one cell thick
    /// </summary>
    private const float TowerWallInflate = 0.45f;

    /// <summary>
    /// Inset from the outer mesh AABB. Keeps the doorway notch and removes the hollow-center island
    /// </summary>
    private const float TowerInteriorInset = 0.9f;

    private static bool IsTreeObject (string objectName) =>
        objectName.Contains ("tree", StringComparison.OrdinalIgnoreCase);

    private static bool IsTowerObject (string objectName) =>
        objectName.Contains ("tower", StringComparison.OrdinalIgnoreCase);

    private static bool IsUndercroftObject (string objectName)
    {
        var lower = objectName.ToLowerInvariant ();
        return lower.Contains ("tower", StringComparison.Ordinal)
               || lower.Contains ("gate", StringComparison.Ordinal)
               || lower.Contains ("arch", StringComparison.Ordinal)
               || lower.Contains ("_arc", StringComparison.Ordinal)
               || lower.StartsWith ("cem_arc", StringComparison.Ordinal)
               || lower.StartsWith ("cem_door", StringComparison.Ordinal);
    }

    /// <summary>
    /// Nav carve only. Physics colliders are untouched
    /// </summary>
    private void AddTowerInteriorFillObstruction (
        ColliderBuildState state,
        List<MeshPart> meshParts,
        Transform3D worldTransform)
    {
        if (!TryComputePartsWorldAabb (meshParts, worldTransform, out var outer) || outer.Size.Y < 0.05f)
        {
            return;
        }

        var inset = TowerInteriorInset;
        var innerSize = new Vector3 (
            outer.Size.X - inset * 2f,
            outer.Size.Y,
            outer.Size.Z - inset * 2f);
        if (innerSize.X < 0.4f || innerSize.Z < 0.4f)
        {
            return;
        }

        var inner = new Aabb (outer.Position + new Vector3 (inset, 0f, inset), innerSize);
        AppendObstructionAabb (state, worldTransform.Origin, inner);
    }

    /// <summary>
    /// Nav only. Near-ground tris follow the real footprint. An AABB seals openings and inflates
    /// curved buildings. Physics colliders are untouched
    /// </summary>
    private void AddNearGroundMeshObstruction (
        ColliderBuildState state,
        List<MeshPart> meshParts,
        Transform3D worldTransform,
        float xzInflate)
    {
        var tileGroupKey = OutsideTerrainTileNodeName;
        if (state.TileIndex is not null
            && state.TileIndex.TryGetTile (worldTransform.Origin, out var masterName, out var occurrence))
        {
            tileGroupKey = TerrainTileGridIndex.BuildTileGroupKey (masterName, occurrence);
        }

        if (!state.TileFaces.TryGetValue (tileGroupKey, out var faces))
        {
            faces = new List<Vector3> ();
            state.TileFaces[tileGroupKey] = faces;
        }

        var maxY = worldTransform.Origin.Y + NearGroundCarveMaxHeight;
        foreach (var part in meshParts)
        {
            AppendMeshFacesNearGround (
                part.Mesh,
                worldTransform * part.LocalToRoot,
                maxY,
                xzInflate,
                faces);
        }
    }

    private static void AppendMeshFacesNearGround (
        Mesh mesh,
        Transform3D transform,
        float maxVertexY,
        float xzInflate,
        List<Vector3> faceAccumulator)
    {
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount (); surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays (surfaceIndex);
            var vertices = arrays[(int) Mesh.ArrayType.Vertex].AsVector3Array ();
            var indexVariant = arrays[(int) Mesh.ArrayType.Index];

            void ConsiderTri (Vector3 a, Vector3 b, Vector3 c)
            {
                var wa = transform * a;
                var wb = transform * b;
                var wc = transform * c;
                if (Mathf.Min (wa.Y, Mathf.Min (wb.Y, wc.Y)) > maxVertexY)
                {
                    return;
                }

                if (xzInflate <= 0f)
                {
                    faceAccumulator.Add (wa);
                    faceAccumulator.Add (wb);
                    faceAccumulator.Add (wc);
                    return;
                }

                // Emit an inflated XZ box for this tri so thin wall planes carve with real
                // thickness.
                var mn = new Vector3 (
                    Mathf.Min (wa.X, Mathf.Min (wb.X, wc.X)) - xzInflate,
                    Mathf.Min (wa.Y, Mathf.Min (wb.Y, wc.Y)),
                    Mathf.Min (wa.Z, Mathf.Min (wb.Z, wc.Z)) - xzInflate);
                var mx = new Vector3 (
                    Mathf.Max (wa.X, Mathf.Max (wb.X, wc.X)) + xzInflate,
                    Mathf.Max (wa.Y, Mathf.Max (wb.Y, wc.Y)),
                    Mathf.Max (wa.Z, Mathf.Max (wb.Z, wc.Z)) + xzInflate);
                AppendAabbBoxFaces (new Aabb (mn, mx - mn), faceAccumulator);
            }

            if (indexVariant.VariantType == Variant.Type.Nil)
            {
                for (var i = 0; i + 2 < vertices.Length; i += 3)
                {
                    ConsiderTri (vertices[i], vertices[i + 1], vertices[i + 2]);
                }

                continue;
            }

            var indices = indexVariant.AsInt32Array ();
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                ConsiderTri (vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
            }
        }
    }
    /// <summary>
    /// Smaller-XZ parts only, clamped around the placement origin so the canopy does not dominate
    /// the hole
    /// </summary>
    private static Aabb ResolveTreeTrunkAabb (List<MeshPart> meshParts, Transform3D worldTransform)
    {
        var partAabbs = new List<(Aabb Aabb, float Xz)> ();
        foreach (var part in meshParts)
        {
            if (!TryComputePartWorldAabb (part, worldTransform, out var partAabb) || partAabb.Size.Y < 0.05f)
            {
                continue;
            }

            var xz = Mathf.Max (partAabb.Size.X, partAabb.Size.Z);
            partAabbs.Add ((partAabb, xz));
        }

        Aabb trunk;
        if (partAabbs.Count == 0)
        {
            trunk = MakeTrunkCylinderAabb (worldTransform.Origin, TreeTrunkFallbackRadius, 2f);
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
                minXz = Mathf.Min (minXz, xz);
                maxXz = Mathf.Max (maxXz, xz);
            }

            // Keep parts that look trunk-like vs the widest canopy part.
            var threshold = Mathf.Max (minXz * 1.75f, maxXz * 0.45f);
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
                    trunk = trunk.Merge (aabb);
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

        return ClampAabbXzAroundOrigin (trunk, worldTransform.Origin, TreeTrunkMaxRadius);
    }

    private static Aabb MakeTrunkCylinderAabb (Vector3 origin, float radius, float height)
    {
        return new Aabb (
            new Vector3 (origin.X - radius, origin.Y, origin.Z - radius),
            new Vector3 (radius * 2f, height, radius * 2f));
    }

    private static Aabb ClampAabbXzAroundOrigin (Aabb aabb, Vector3 origin, float maxRadius)
    {
        var halfX = Mathf.Min (aabb.Size.X * 0.5f, maxRadius);
        var halfZ = Mathf.Min (aabb.Size.Z * 0.5f, maxRadius);
        // Prefer object origin on XZ; keep original Y range for vertical carve padding.
        var y0 = aabb.Position.Y;
        var y1 = aabb.Position.Y + aabb.Size.Y;
        if (aabb.Size.Y < 0.05f)
        {
            y0 = origin.Y;
            y1 = origin.Y + 2f;
        }

        return new Aabb (
            new Vector3 (origin.X - halfX, y0, origin.Z - halfZ),
            new Vector3 (halfX * 2f, y1 - y0, halfZ * 2f));
    }

    private static bool TryComputePartsWorldAabb (
        List<MeshPart> meshParts,
        Transform3D worldTransform,
        out Aabb aabb)
    {
        aabb = default;
        var has = false;
        foreach (var part in meshParts)
        {
            if (!TryComputePartWorldAabb (part, worldTransform, out var partAabb))
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
                aabb = aabb.Merge (partAabb);
            }
        }

        return has;
    }

    private static bool TryComputePartWorldAabb (MeshPart part, Transform3D worldTransform, out Aabb aabb)
    {
        aabb = default;
        var localAabb = part.Mesh.GetAabb ();
        var xform = worldTransform * part.LocalToRoot;
        var has = false;
        for (var i = 0; i < 8; i++)
        {
            var worldCorner = xform * localAabb.GetEndpoint (i);
            if (!has)
            {
                aabb = new Aabb (worldCorner, Vector3.Zero);
                has = true;
            }
            else
            {
                aabb = aabb.Expand (worldCorner);
            }
        }

        return has;
    }

    private static void AppendAabbBoxFaces (Aabb aabb, List<Vector3> faces)
    {
        var mn = aabb.Position;
        var mx = aabb.Position + aabb.Size;
        // 6 faces × 2 tris × 3 verts. Order does not matter for projected-obstruction clustering.
        void Quad (Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            faces.Add (a);
            faces.Add (b);
            faces.Add (c);
            faces.Add (a);
            faces.Add (c);
            faces.Add (d);
        }

        Quad (new Vector3 (mn.X, mn.Y, mn.Z), new Vector3 (mx.X, mn.Y, mn.Z), new Vector3 (mx.X, mx.Y, mn.Z),
            new Vector3 (mn.X, mx.Y, mn.Z));
        Quad (new Vector3 (mn.X, mn.Y, mx.Z), new Vector3 (mn.X, mx.Y, mx.Z), new Vector3 (mx.X, mx.Y, mx.Z),
            new Vector3 (mx.X, mn.Y, mx.Z));
        Quad (new Vector3 (mn.X, mn.Y, mn.Z), new Vector3 (mn.X, mx.Y, mn.Z), new Vector3 (mn.X, mx.Y, mx.Z),
            new Vector3 (mn.X, mn.Y, mx.Z));
        Quad (new Vector3 (mx.X, mn.Y, mn.Z), new Vector3 (mx.X, mn.Y, mx.Z), new Vector3 (mx.X, mx.Y, mx.Z),
            new Vector3 (mx.X, mx.Y, mn.Z));
        Quad (new Vector3 (mn.X, mn.Y, mn.Z), new Vector3 (mn.X, mn.Y, mx.Z), new Vector3 (mx.X, mn.Y, mx.Z),
            new Vector3 (mx.X, mn.Y, mn.Z));
        Quad (new Vector3 (mn.X, mx.Y, mn.Z), new Vector3 (mx.X, mx.Y, mn.Z), new Vector3 (mx.X, mx.Y, mx.Z),
            new Vector3 (mn.X, mx.Y, mx.Z));
    }

    /// <summary>
    /// Same skip list as Tools/generate_colliders.py. These meshes do not block nav
    /// </summary>
    private static bool ShouldSkipMeshObstruction (string objectName)
    {
        var lower = objectName.ToLowerInvariant ();
        return lower.Contains ("bush")
               || lower.Contains ("grass")
               || lower.StartsWith ("fl_", StringComparison.Ordinal)
               || lower.StartsWith ("flower", StringComparison.Ordinal)
               || lower.StartsWith ("kamysh", StringComparison.Ordinal)
               || lower.StartsWith ("pyram", StringComparison.Ordinal)
               || lower.StartsWith ("vine", StringComparison.Ordinal)
               || lower is "cam_cube" or "treeput"
               || lower.StartsWith ("tn2_fl", StringComparison.Ordinal);
    }

    /// <summary>
    /// Live Terrain GridMap position, including (0, 0, 0). The -4000 default buckets every object
    /// into the wrong tile
    /// </summary>
    private Vector3 ResolveTerrainWorldOrigin ()
    {
        Node? walk = this;
        while (walk is not null)
        {
            if (walk.FindChild (TerrainGridFill.TerrainNodeName, recursive: true, owned: false) is GridMap terrain)
            {
                return terrain.Position;
            }

            walk = walk.GetParent ();
        }

        var gridFill = GetNodeOrNull<TerrainGridFill> ("../TerrainGrid")
                       ?? GetParent ()?.GetNodeOrNull<TerrainGridFill> ("TerrainGrid");
        var gridTerrain = gridFill?.GetNodeOrNull<GridMap> (TerrainGridFill.TerrainNodeName);
        if (gridTerrain is not null)
        {
            return gridTerrain.Position;
        }

        return TerrainWorldOrigin;
    }

    /// <summary>
    /// TerrainObjects local into the Terrain GridMap frame. The siblings are offset independently,
    /// so skipping this rotates the hole off the object
    /// </summary>
    private Transform3D GetObjectsToGridLocalTransform ()
    {
        var gridFill = GetNodeOrNull<TerrainGridFill> ("../TerrainGrid")
                       ?? GetParent ()?.GetNodeOrNull<TerrainGridFill> ("TerrainGrid");
        if (gridFill is null)
        {
            GD.PushWarning (
                "TerrainObjectsFill: TerrainGrid sibling not found; nav obstruction geometry will use the "
                + "raw (un-rotated) placement transform, which is almost certainly wrong.");
            return Transform3D.Identity;
        }

        return gridFill.Transform.AffineInverse () * Transform;
    }

    /// <summary>
    /// ConcavePolygonShape3D is read as triangles. Any other Shape3D uses its debug mesh
    /// </summary>
    private static void AppendShapeFacesWorldSpace (Shape3D shape, Transform3D transform, List<Vector3> faceAccumulator)
    {
        if (shape is ConcavePolygonShape3D concave)
        {
            foreach (var v in concave.GetFaces ())
            {
                faceAccumulator.Add (transform * v);
            }

            return;
        }

        var debugMesh = shape.GetDebugMesh ();
        for (var surfaceIndex = 0; surfaceIndex < debugMesh.GetSurfaceCount (); surfaceIndex++)
        {
            var arrays = debugMesh.SurfaceGetArrays (surfaceIndex);
            var vertices = arrays[(int) Mesh.ArrayType.Vertex].AsVector3Array ();
            var indexVariant = arrays[(int) Mesh.ArrayType.Index];
            if (indexVariant.VariantType == Variant.Type.Nil)
            {
                foreach (var v in vertices)
                {
                    faceAccumulator.Add (transform * v);
                }

                continue;
            }

            foreach (var index in indexVariant.AsInt32Array ())
            {
                faceAccumulator.Add (transform * vertices[index]);
            }
        }
    }

    private static string CapitalizeFirstLetter (string value)
    {
        return string.IsNullOrEmpty (value) ? value : char.ToUpperInvariant (value[0]) + value[1..];
    }

    /// <summary>
    /// RemoveChild runs now. QueueFree alone leaves a child that a same-call lookup grafts onto,
    /// then the deferred free deletes the new content
    /// </summary>
    private static void ClearChildren (Node3D node)
    {
        foreach (var child in node.GetChildren ())
        {
            node.RemoveChild (child);
            child.QueueFree ();
        }
    }

    private PackedScene? GetOrLoadScene (string objectName)
    {
        // A lowercase path on Windows rewrites the NTFS name and Godot warns on sibling textures
        var path = GlbModelPaths.Resolve (objectName, ModelsDirectory);
        if (path is null || !ResourceLoader.Exists (path))
        {
            return null;
        }

        return ResourceLoader.Load<PackedScene> (path);
    }

    private void SetOwnerIfEditor (Node node)
    {
        if (!Engine.IsEditorHint ())
        {
            return;
        }

        // Scene dock and save need Owner = edited scene root. EditedSceneRoot can be null, so this
        // falls back along the owner chain
        var root = GetTree ()?.EditedSceneRoot ?? Owner ?? this;
        node.Owner = root;
    }

    /// <summary>
    /// Godot Json, not Newtonsoft. Newtonsoft keeps collectible assemblies loaded in the editor
    /// (godotengine/godot#78513)
    /// </summary>
    private static List<TerrainObjectRecord>? ParseTerrainRecords (string jsonText)
    {
        var json = new Json ();
        if (json.Parse (jsonText) != Error.Ok)
        {
            return null;
        }

        var root = json.Data;
        if (root.VariantType != Variant.Type.Array)
        {
            return null;
        }

        var arr = root.AsGodotArray ();
        var list = new List<TerrainObjectRecord> ();
        foreach (var item in arr)
        {
            if (item.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            var d = item.AsGodotDictionary ();
            // Legacy ObjectData JSON (object_name, coordinates, rotation_euler) and MbdConverter
            // JSON (name, x, y, z, pitch, yaw, roll)
            string objectName;
            if (d.TryGetValue ("object_name", out var nameVar))
            {
                objectName = nameVar.AsString ().ToLower ();
            }
            else if (d.TryGetValue ("name", out var mbdNameVar))
            {
                objectName = mbdNameVar.AsString ().ToLower ();
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace (objectName))
            {
                continue;
            }

            var rec = new TerrainObjectRecord { ObjectName = objectName };

            if (d.TryGetValue ("coordinates", out var coordVar) && coordVar.VariantType == Variant.Type.Dictionary)
            {
                var cd = coordVar.AsGodotDictionary ();
                rec.Coordinates = new TerrainCoordinates
                {
                    X = DictGetDouble (cd, "x"),
                    Y = DictGetDouble (cd, "y"),
                    Z = DictGetDouble (cd, "z")
                };
            }
            else if (d.ContainsKey ("x") || d.ContainsKey ("y") || d.ContainsKey ("z"))
            {
                rec.Coordinates = new TerrainCoordinates
                {
                    X = DictGetDouble (d, "x"),
                    Y = DictGetDouble (d, "y"),
                    Z = DictGetDouble (d, "z")
                };
            }

            if (d.TryGetValue ("rotation_euler", out var rotVar) && rotVar.VariantType == Variant.Type.Dictionary)
            {
                var rd = rotVar.AsGodotDictionary ();
                rec.RotationEuler = new TerrainRotationEuler
                {
                    Yaw = DictGetDouble (rd, "yaw"),
                    Pitch = DictGetDouble (rd, "pitch"),
                    Roll = DictGetDouble (rd, "roll")
                };
            }
            else if (d.ContainsKey ("pitch") || d.ContainsKey ("yaw") || d.ContainsKey ("roll"))
            {
                rec.RotationEuler = new TerrainRotationEuler
                {
                    Yaw = DictGetDouble (d, "yaw"),
                    Pitch = DictGetDouble (d, "pitch"),
                    Roll = DictGetDouble (d, "roll")
                };
            }

            list.Add (rec);
        }

        return list;
    }

    private static double DictGetDouble (Dictionary d, StringName key)
    {
        return d.TryGetValue (key, out var v) ? v.AsDouble () : 0.0;
    }

    private readonly struct InstancePlacement
    {
        public InstancePlacement (Transform3D transform, int sourceRecordIndex)
        {
            Transform = transform;
            SourceRecordIndex = sourceRecordIndex;
        }

        public Transform3D Transform { get; }
        public int SourceRecordIndex { get; }
    }

    private sealed class MeshPart
    {
        public MeshPart (Mesh mesh, Transform3D localToRoot)
        {
            Mesh = mesh;
            LocalToRoot = localToRoot;
        }

        public Mesh Mesh { get; }
        public Transform3D LocalToRoot { get; }
    }

    private sealed class ColliderPart
    {
        public ColliderPart (Shape3D shape, Transform3D localToRoot)
        {
            Shape = shape;
            LocalToRoot = localToRoot;
        }

        public Shape3D Shape { get; }
        public Transform3D LocalToRoot { get; }
    }

    private sealed class ObjectSceneParts
    {
        public required List<MeshPart> MeshParts { get; init; }
        public required List<ColliderPart> ColliderParts { get; init; }
    }

    private sealed class ColliderBuildState
    {
        public required TerrainTileGridIndex? TileIndex { get; init; }

        public required Transform3D ObjectsToGridLocal { get; init; }

        /// <summary>
        /// Not turned into scene nodes
        /// </summary>
        public global::System.Collections.Generic.Dictionary<string, List<Vector3>> TileFaces { get; } = new ();
    }

    /// <summary>
    /// Same row-major layout as TerrainGridFill: gx = GridWidth - (i % GridWidth) - 1. Occurrence
    /// counts repeats of a master tile in that order
    /// </summary>
    internal sealed class TerrainTileGridIndex
    {
        private readonly global::System.Collections.Generic.Dictionary<(int Gx, int Gz), (string MasterName, int Occurrence)>
            cellsByCoord;

        private readonly float tileSize;
        private readonly Vector3 worldOrigin;

        private TerrainTileGridIndex (
            global::System.Collections.Generic.Dictionary<(int Gx, int Gz), (string MasterName, int Occurrence)> cellsByCoord,
            float tileSize,
            Vector3 worldOrigin)
        {
            this.cellsByCoord = cellsByCoord;
            this.tileSize = tileSize;
            this.worldOrigin = worldOrigin;
        }

        public static TerrainTileGridIndex? TryBuild (string mapBinResourcePath, float tileSize, Vector3 worldOrigin)
        {
            if (!SphServer.Godot.Scripts.Util.ResPathIO.TryReadAllBytes (mapBinResourcePath, out var fileContents))
            {
                return null;
            }

            var cells = MapFill.ReadFullGrid (fileContents);
            var cellsByCoord =
                new global::System.Collections.Generic.Dictionary<(int Gx, int Gz), (string MasterName, int Occurrence)> ();
            var nextOccurrenceByMaster = new global::System.Collections.Generic.Dictionary<string, int> ();
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell.IsEmpty)
                {
                    continue;
                }

                if (!nextOccurrenceByMaster.TryGetValue (cell.MasterName, out var occurrence))
                {
                    occurrence = 0;
                }

                nextOccurrenceByMaster[cell.MasterName] = occurrence + 1;

                var gx = MapFill.GridWidth - (i % MapFill.GridWidth) - 1;
                var gz = i / MapFill.GridWidth;
                cellsByCoord[(gx, gz)] = (cell.MasterName, occurrence);
            }

            return new TerrainTileGridIndex (cellsByCoord, tileSize, worldOrigin);
        }

        public bool TryGetTile (Vector3 worldPosition, out string masterName, out int occurrence)
        {
            var local = worldPosition - worldOrigin;
            var gx = Mathf.FloorToInt (local.X / tileSize);
            var gz = Mathf.FloorToInt (local.Z / tileSize);
            if (cellsByCoord.TryGetValue ((gx, gz), out var found))
            {
                masterName = found.MasterName;
                occurrence = found.Occurrence;
                return true;
            }

            masterName = string.Empty;
            occurrence = 0;
            return false;
        }

        public IEnumerable<((int Gx, int Gz) Coord, string MasterName, int Occurrence)> EnumerateCells ()
        {
            foreach (var kv in cellsByCoord)
            {
                yield return (kv.Key, kv.Value.MasterName, kv.Value.Occurrence);
            }
        }

        public static string BuildTileGroupKey (string masterName, int occurrence)
        {
            return $"{CapitalizeFirstLetter (SanitizeGodotNodeName (masterName))}_{occurrence:D2}";
        }
    }

    /// <summary>
    /// Reference equality on Mesh, so batches merge the same resource
    /// </summary>
    private sealed class MeshBatchKeyComparer : IEqualityComparer<(Node3D Category, string ObjectName, Mesh Mesh)>
    {
        public bool Equals ((Node3D Category, string ObjectName, Mesh Mesh) x,
            (Node3D Category, string ObjectName, Mesh Mesh) y)
        {
            return ReferenceEquals (x.Category, y.Category)
                   && string.Equals (x.ObjectName, y.ObjectName, StringComparison.Ordinal)
                   && ReferenceEquals (x.Mesh, y.Mesh);
        }

        public int GetHashCode ((Node3D Category, string ObjectName, Mesh Mesh) obj)
        {
            return HashCode.Combine (obj.Category.GetInstanceId (), obj.ObjectName, obj.Mesh.GetInstanceId ());
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

        public Vector3 ToVector3 ()
        {
            return SourceWorldToGodotWorldBasis * new Vector3 ((float) X, (float) Y, (float) Z);
        }
    }

    /// <summary>
    /// JSON yaw, pitch, roll is source space (Y down, Z forward), YXZ order. Yaw is negated for
    /// Godot Y-up. R_godot = T R_src T. T R_src alone leaves a 180 deg X flip on identity
    /// </summary>
    private sealed class TerrainRotationEuler
    {
        public double Yaw { get; set; }
        public double Pitch { get; set; }
        public double Roll { get; set; }

        /// <summary>
        /// YXZ Euler with negated yaw. Same orientation as R' = T R_src T^-1
        /// </summary>
        public Vector3 ToEulerRadians ()
        {
            var eulerForGodotBasis = new Vector3 ((float) Pitch, -(float) Yaw, (float) Roll);
            var basisSource = Basis.FromEuler (eulerForGodotBasis);
            var t = SourceWorldToGodotWorldBasis;
            var basisGodot = t * basisSource * t;
            return basisGodot.GetEuler ();
        }
    }
}
