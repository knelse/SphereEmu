using Godot;
using Godot.Collections;
using FileAccess = Godot.FileAccess;

namespace SphServer.Godot.Scripts.Terrain;

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

    /// <summary>
    ///     Columns = source X, Y, Z axes expressed in Godot: right, down, forward (Godot forward = -Z), so (x,y,z)_src ↦
    ///     (x,-y,-z).
    /// </summary>
    private static readonly Basis SourceWorldToGodotWorldBasis = new(Vector3.Right, Vector3.Down, Vector3.Forward);

    [Export] public string ObjectDataDirectory { get; set; } = "res://Godot/Terrain/ObjectDataJson/";

    [Export] public string ModelsDirectory { get; set; } = "res://Godot/Models/";

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

        // (category root, source object name, Mesh) -> instance placements (world * mesh-local)
        // We keep ObjectName so MultiMesh nodes can be named after their source object.
        var batches =
            new global::System.Collections.Generic.Dictionary<(Node3D Category, string ObjectName, Mesh Mesh),
                List<InstancePlacement>>(new MeshBatchKeyComparer());
        var meshPartsCache = new global::System.Collections.Generic.Dictionary<string, List<MeshPart>?>();

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
                ProcessJsonForBatches(path, plants, rocks, other, batches, meshPartsCache);
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

            var mmToAssign = mm;

            var safeObjectName = SanitizeGodotNodeName(objectName);
            if (Engine.IsEditorHint() && SaveMultiMeshesAsExternalResources)
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
        global::System.Collections.Generic.Dictionary<string, List<MeshPart>?> meshPartsCache)
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

            if (!meshPartsCache.TryGetValue(rec.ObjectName, out var parts))
            {
                var scene = GetOrLoadScene(rec.ObjectName);
                parts = scene is null ? null : ExtractMeshParts(scene);
                meshPartsCache[rec.ObjectName] = parts;
                if (parts is null || parts.Count == 0)
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
            else if (parts is null || parts.Count == 0)
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

            foreach (var part in parts)
            {
                var key = (parent, rec.ObjectName, part.Mesh);
                if (!batches.TryGetValue(key, out var list))
                {
                    list = new List<InstancePlacement>();
                    batches[key] = list;
                }

                list.Add(new InstancePlacement(world * part.LocalToRoot, recIndex));
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

    private static List<MeshPart>? ExtractMeshParts(PackedScene scene)
    {
        var root = scene.Instantiate<Node3D>();
        try
        {
            var list = new List<MeshPart>();
            CollectMeshes(root, root, list);
            return list.Count == 0 ? null : list;
        }
        finally
        {
            root.QueueFree();
        }
    }

    private static void CollectMeshes(Node node, Node3D root, List<MeshPart> list)
    {
        if (node is MeshInstance3D mi && mi.Mesh is { } mesh)
        {
            if (HasSkeletonAncestor(mi))
            {
                return;
            }

            var localToRoot = ComputeTransformRelativeToRoot(mi, root);
            list.Add(new MeshPart(mesh, localToRoot));
        }

        foreach (var child in node.GetChildren())
        {
            CollectMeshes(child, root, list);
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

    private static void ClearChildren(Node3D node)
    {
        foreach (var child in node.GetChildren())
        {
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