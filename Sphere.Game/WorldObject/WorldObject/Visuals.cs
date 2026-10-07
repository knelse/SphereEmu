using Godot;
using SphServer.Shared.GameData.Enums;
using SphServer.Sphere.Game.Converters;

namespace SphServer.Sphere.Game.WorldObject;

public partial class WorldObject
{
    /// <summary>
    /// Short name + unique scene id so the tree shows e.g. <c>…#Glb</c> instead of a long duplicate
    /// name.
    /// </summary>
    private const string GlbModelChildName = "Glb";

    private const string GlbModelMetaKey = "_world_object_glb";
    private const string GlbModelMetaKeyLegacy = "_npc_interactable_glb";
    private const string GlbModelMetaKeyModelPath = "_world_object_glb_path";
    private const string GlbModelMetaKeyGrounded = "_world_object_glb_grounded";
    private const string PlaceholderCheckerDdsPath = "res://Godot/Textures/npc_placeholder_checker.dds";
    private const string PlaceholderMeshNodeName = "MeshInstance3D";

    /// <summary>
    /// Used when <see cref="ModelName" /> is empty and <see cref="ObjectType" /> has no mapped GLB.
    /// </summary>
    private const string DefaultModelNameForVisual = "pump1";

    /// <summary>
    /// CallDeferred only binds public methods, so this is the public forwarder
    /// </summary>
    public void RefreshModelVisualDeferred ()
    {
        if (Engine.IsEditorHint () && !ShouldRefreshModelVisual ())
        {
            return;
        }

        RefreshModelVisual ();
    }

    protected void ScheduleModelVisualRefreshIfNeeded ()
    {
        if (!IsInsideTree ())
        {
            return;
        }

        if (!Engine.IsEditorHint () || ShouldRefreshModelVisual ())
        {
            CallDeferred (nameof (RefreshModelVisualDeferred));
        }
    }

    /// <summary>
    /// Editor skips a refresh when the child already matches; runtime always refreshes
    /// </summary>
    protected bool ShouldRefreshModelVisual ()
    {
        if (!Engine.IsEditorHint ())
        {
            return true;
        }

        var trimmed = GetEffectiveModelNameForVisual ();
        if (string.IsNullOrEmpty (trimmed))
        {
            return GetNodeOrNull (PlaceholderMeshNodeName) is null && HasAnyGlbVisualChild ();
        }

        return !TryGetExistingGlbVisual (BuildModelGlbPath (trimmed), out _);
    }

    private static string BuildModelGlbPath (string modelName) => GlbModelPaths.ResolveOrFallback (modelName);

    private bool HasAnyGlbVisualChild ()
    {
        foreach (var child in GetChildren ())
        {
            if (child is not Node3D)
            {
                continue;
            }

            if (child.HasMeta (GlbModelMetaKey) || child.HasMeta (GlbModelMetaKeyLegacy))
            {
                return true;
            }

            var name = child.Name.ToString ();
            if (name == GlbModelChildName || IsGlbRenamedDuplicateName (name)
                || name.StartsWith ("NpcGlbModel", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A missing asset shows the checkered placeholder cube
    /// </summary>
    protected virtual void RefreshModelVisual ()
    {
        var trimmed = GetEffectiveModelNameForVisual ();
        if (string.IsNullOrEmpty (trimmed))
        {
            RemoveGlbModelChild ();
            ShowPlaceholderCube ();
            return;
        }

        var glbPath = BuildModelGlbPath (trimmed);
        // A matching child stays, because re-instancing every load is expensive
        if (TryGetExistingGlbVisual (glbPath, out var existingGlb))
        {
            RemovePlaceholderMeshChild ();
            if (AutoGroundGlbVisual)
            {
                TryAutoGroundGlbVisualOnce (existingGlb);
            }

            return;
        }

        RemoveGlbModelChild ();
        RemovePlaceholderMeshChild ();
        if (!ResourceLoader.Exists (glbPath))
        {
            GD.PushWarning ($"WorldObject: GLB not found: {glbPath}");
            ShowPlaceholderCube ();
            return;
        }

        var packed = ResourceLoader.Load<PackedScene> (glbPath);
        if (packed is null)
        {
            GD.PushWarning ($"WorldObject: failed to load scene: {glbPath}");
            ShowPlaceholderCube ();
            return;
        }

        var root = InstantiateGlbRoot (packed);
        if (root is null)
        {
            GD.PushWarning ($"WorldObject: GLB root is not a Node3D: {glbPath}");
            ShowPlaceholderCube ();
            return;
        }

        root.Name = GlbModelChildName;
        root.UniqueNameInOwner = true;
        root.SetMeta (GlbModelMetaKey, true);
        root.SetMeta (GlbModelMetaKeyModelPath, glbPath);
        AddChild (root);
        SetOwnerForEditedScene (root);

        if (AutoGroundGlbVisual)
        {
            TryAutoGroundGlbVisualOnce (root);
        }
    }

    private bool TryGetExistingGlbVisual (string desiredGlbPath, out Node3D glbRoot)
    {
        glbRoot = null!;

        // Prefer meta-tagged nodes (created by this script), but also accept pre-authored "Glb"
        // from .tscn files.
        foreach (var child in GetChildren ())
        {
            if (child is not Node3D n3)
            {
                continue;
            }

            if (!child.HasMeta (GlbModelMetaKey) && child.Name != GlbModelChildName)
            {
                continue;
            }

            var metaPath = child.GetMeta (GlbModelMetaKeyModelPath, Variant.CreateFrom (string.Empty)).AsString ();
            if (!string.IsNullOrEmpty (metaPath)
                && string.Equals (metaPath, desiredGlbPath, StringComparison.OrdinalIgnoreCase))
            {
                glbRoot = n3;
                return true;
            }

            // For scene-authored instances, SceneFilePath should point to the packed scene.
            var scenePath = n3.SceneFilePath ?? string.Empty;
            if (!string.IsNullOrEmpty (scenePath)
                && string.Equals (scenePath, desiredGlbPath, StringComparison.OrdinalIgnoreCase))
            {
                glbRoot = n3;
                // Tag it so future refreshes can be O(1) without relying on SceneFilePath.
                n3.SetMeta (GlbModelMetaKey, true);
                n3.SetMeta (GlbModelMetaKeyModelPath, desiredGlbPath);
                return true;
            }
        }

        return false;
    }

    private void TryAutoGroundGlbVisualOnce (Node3D glbRoot)
    {
        if (glbRoot.HasMeta (GlbModelMetaKeyGrounded))
        {
            return;
        }

        TryAutoGroundGlbVisual (glbRoot);
        glbRoot.SetMeta (GlbModelMetaKeyGrounded, true);
    }

    private void TryAutoGroundGlbVisual (Node3D glbRoot)
    {
        // Only the GLB child moves, so this node's network transform stays put
        GlbVisualGrounding.ApplyGroundOffset (glbRoot);
    }

    /// <summary>
    /// Trimmed ModelName wins over the object-type map
    /// </summary>
    protected string GetEffectiveModelNameForVisual ()
    {
        var explicitName = ModelName?.Trim () ?? string.Empty;
        if (!string.IsNullOrEmpty (explicitName))
        {
            return explicitName;
        }

        return ResolveModelNameFromObjectTypeFallback ();
    }

    protected virtual string ResolveModelNameFromObjectTypeFallback ()
    {
        if (TryGetMappedModelName (ObjectType, out var mappedName))
        {
            return mappedName;
        }

        return DefaultModelNameForVisual;
    }

    private static bool TryGetMappedModelName (ObjectType objectType, out string modelName)
    {
        modelName = string.Empty;
        var mapped = ObjectTypeToModelNameMap.Get (objectType).Trim ();
        if (string.IsNullOrEmpty (mapped))
        {
            return false;
        }

        modelName = mapped;
        return true;
    }

    /// <summary>
    /// GLB scenes usually root at <see cref="Node3D" />; otherwise wrap in a <see cref="Node3D" />.
    /// </summary>
    private static Node3D? InstantiateGlbRoot (PackedScene packed)
    {
        var inst = packed.Instantiate ();
        if (inst is Node3D n3)
        {
            return n3;
        }

        if (inst is Node n)
        {
            var wrap = new Node3D ();
            wrap.AddChild (n);
            return wrap;
        }

        inst?.Free ();
        return null;
    }

    /// <summary>
    /// Also Godot-renamed duplicates (Glb2 and the rest)
    /// </summary>
    protected void ClearLocalModelVisuals ()
    {
        RemoveGlbModelChild ();
        RemovePlaceholderMeshChild ();
    }

    private void RemoveGlbModelChild ()
    {
        var toRemove = new List<Node> ();
        foreach (var child in GetChildren ())
        {
            if (child.HasMeta (GlbModelMetaKey) || child.HasMeta (GlbModelMetaKeyLegacy))
            {
                toRemove.Add (child);
                continue;
            }

            // A matching pre-authored Glb is kept earlier; this runs only after that check fails
            var nm = child.Name.ToString ();
            if (nm == GlbModelChildName || IsGlbRenamedDuplicateName (nm) || nm.StartsWith ("NpcGlbModel", StringComparison.Ordinal))
            {
                toRemove.Add (child);
            }
        }

        foreach (var n in toRemove)
        {
            n.Free ();
        }
    }

    private static bool IsGlbRenamedDuplicateName (string nodeName)
    {
        if (!nodeName.StartsWith (GlbModelChildName, StringComparison.Ordinal))
        {
            return false;
        }

        if (nodeName.Length == GlbModelChildName.Length)
        {
            return true;
        }

        for (var i = GlbModelChildName.Length; i < nodeName.Length; i++)
        {
            if (!char.IsDigit (nodeName[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void RemovePlaceholderMeshChild ()
    {
        var n = GetNodeOrNull (PlaceholderMeshNodeName);
        n?.Free ();
    }

    private void ShowPlaceholderCube ()
    {
        RemoveGlbModelChild ();

        MeshInstance3D meshInst;
        if (GetNodeOrNull (PlaceholderMeshNodeName) is MeshInstance3D existing)
        {
            meshInst = existing;
        }
        else
        {
            meshInst = new MeshInstance3D ();
            meshInst.Name = PlaceholderMeshNodeName;
            AddChild (meshInst);
            SetOwnerForEditedScene (meshInst);
        }

        var box = new BoxMesh { Size = Vector3.One };
        meshInst.Mesh = box;
        var mat = new StandardMaterial3D ();
        mat.AlbedoTexture = LoadPlaceholderCheckerTexture ();
        meshInst.MaterialOverride = mat;
    }

    private static Texture2D LoadPlaceholderCheckerTexture ()
    {
        if (ResourceLoader.Exists (PlaceholderCheckerDdsPath))
        {
            var tex = ResourceLoader.Load<Texture2D> (PlaceholderCheckerDdsPath);
            if (tex is not null)
            {
                return tex;
            }
        }

        return CreateFallbackPinkCheckerTexture ();
    }

    /// <summary>
    /// Procedural tileable fallback if the DDS resource is missing.
    /// </summary>
    private static Texture2D CreateFallbackPinkCheckerTexture ()
    {
        const int w = 64;
        const int h = 64;
        const int tile = 8;
        var img = Image.CreateEmpty (w, h, false, Image.Format.Rgba8);
        var light = new Color (1f, 0.6f, 0.8f);
        var dark = new Color (1f, 0.25f, 0.55f);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = ((x / tile + y / tile) & 1) == 0 ? light : dark;
                img.SetPixel (x, y, c);
            }
        }

        return ImageTexture.CreateFromImage (img);
    }

    /// <summary>
    /// Owner is this instance; the main scene root would save these nodes as its direct children
    /// </summary>
    private void SetOwnerForEditedScene (Node node)
    {
        if (!Engine.IsEditorHint ())
        {
            return;
        }

        node.Owner = this;
    }
}
