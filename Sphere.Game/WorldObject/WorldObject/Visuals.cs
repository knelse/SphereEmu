using Godot;
using SphServer.Shared.GameData.Enums;
using SphServer.Sphere.Game.Converters;

namespace SphServer.Sphere.Game.WorldObject;

public partial class WorldObject
{
	/// <summary>Short name + unique scene id so the tree shows e.g. <c>…#Glb</c> instead of a long duplicate name.</summary>
	private const string GlbModelChildName = "Glb";

	private const string GlbModelMetaKey = "_world_object_glb";
	private const string GlbModelMetaKeyLegacy = "_npc_interactable_glb";
	private const string GlbModelMetaKeyModelPath = "_world_object_glb_path";
	private const string GlbModelMetaKeyGrounded = "_world_object_glb_grounded";
	private const string PlaceholderCheckerDdsPath = "res://Godot/Textures/npc_placeholder_checker.dds";
	private const string PlaceholderMeshNodeName = "MeshInstance3D";

	/// <summary>Used when <see cref="ModelName" /> is empty and <see cref="ObjectType" /> has no mapped GLB.</summary>
	private const string DefaultModelNameForVisual = "pump1";

	/// <summary>
	///     Godot string-based <see cref="CallDeferred(string, Variant[])" /> only binds exported/public methods;
	///     this forwards to <see cref="RefreshModelVisual" /> when <see cref="ObjectType" /> / <see cref="ModelName" />
	///     change.
	/// </summary>
	public void RefreshModelVisualDeferred()
	{
		if (Engine.IsEditorHint() && !ShouldRefreshModelVisual())
		{
			return;
		}

		RefreshModelVisual();
	}

	protected void ScheduleModelVisualRefreshIfNeeded()
	{
		if (!IsInsideTree())
		{
			return;
		}

		if (!Engine.IsEditorHint() || ShouldRefreshModelVisual())
		{
			CallDeferred(nameof(RefreshModelVisualDeferred));
		}
	}

	/// <summary>
	///     Editor: skip refresh when an existing child GLB / placeholder already matches exports.
	///     Runtime: always allow refresh when invoked.
	/// </summary>
	protected bool ShouldRefreshModelVisual()
	{
		if (!Engine.IsEditorHint())
		{
			return true;
		}

		var trimmed = GetEffectiveModelNameForVisual();
		if (string.IsNullOrEmpty(trimmed))
		{
			return GetNodeOrNull(PlaceholderMeshNodeName) is null && HasAnyGlbVisualChild();
		}

		return !TryGetExistingGlbVisual(BuildModelGlbPath(trimmed), out _);
	}

	private static string BuildModelGlbPath(string modelName) => $"res://Godot/Models/{modelName}.glb";

	private bool HasAnyGlbVisualChild()
	{
		foreach (var child in GetChildren())
		{
			if (child is not Node3D)
			{
				continue;
			}

			if (child.HasMeta(GlbModelMetaKey) || child.HasMeta(GlbModelMetaKeyLegacy))
			{
				return true;
			}

			var name = child.Name.ToString();
			if (name == GlbModelChildName || IsGlbRenamedDuplicateName(name)
				|| name.StartsWith("NpcGlbModel", StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     Loads a GLB from <c>res://Godot/Models/{name}.glb</c> using <see cref="ModelName" /> if set, otherwise
	///     <see cref="ObjectTypeToModelNameMap" /> for <see cref="ObjectType" />, otherwise
	///     <see cref="DefaultModelNameForVisual" />;
	///     if the asset is missing, shows the checkered placeholder cube.
	/// </summary>
	protected virtual void RefreshModelVisual()
	{
		var trimmed = GetEffectiveModelNameForVisual();
		if (string.IsNullOrEmpty(trimmed))
		{
			RemoveGlbModelChild();
			ShowPlaceholderCube();
			return;
		}

		var glbPath = BuildModelGlbPath(trimmed);
		// If a GLB is already instanced under this node and matches the desired model, keep it.
		// This avoids removing/re-instancing visuals on every scene load (which gets expensive with many objects).
		if (TryGetExistingGlbVisual(glbPath, out var existingGlb))
		{
			RemovePlaceholderMeshChild();
			if (AutoGroundGlbVisual)
			{
				TryAutoGroundGlbVisualOnce(existingGlb);
			}

			return;
		}

		RemoveGlbModelChild();
		RemovePlaceholderMeshChild();
		if (!ResourceLoader.Exists(glbPath))
		{
			GD.PushWarning($"WorldObject: GLB not found: {glbPath}");
			ShowPlaceholderCube();
			return;
		}

		var packed = ResourceLoader.Load<PackedScene>(glbPath);
		if (packed is null)
		{
			GD.PushWarning($"WorldObject: failed to load scene: {glbPath}");
			ShowPlaceholderCube();
			return;
		}

		var root = InstantiateGlbRoot(packed);
		if (root is null)
		{
			GD.PushWarning($"WorldObject: GLB root is not a Node3D: {glbPath}");
			ShowPlaceholderCube();
			return;
		}

		root.Name = GlbModelChildName;
		root.UniqueNameInOwner = true;
		root.SetMeta(GlbModelMetaKey, true);
		root.SetMeta(GlbModelMetaKeyModelPath, glbPath);
		AddChild(root);
		SetOwnerForEditedScene(root);

		if (AutoGroundGlbVisual)
		{
			TryAutoGroundGlbVisualOnce(root);
		}
	}

	private bool TryGetExistingGlbVisual(string desiredGlbPath, out Node3D glbRoot)
	{
		glbRoot = null!;

		// Prefer meta-tagged nodes (created by this script), but also accept pre-authored "Glb" from .tscn files.
		foreach (var child in GetChildren())
		{
			if (child is not Node3D n3)
			{
				continue;
			}

			if (!child.HasMeta(GlbModelMetaKey) && child.Name != GlbModelChildName)
			{
				continue;
			}

			var metaPath = child.GetMeta(GlbModelMetaKeyModelPath, Variant.CreateFrom(string.Empty)).AsString();
			if (!string.IsNullOrEmpty(metaPath)
				&& string.Equals(metaPath, desiredGlbPath, StringComparison.OrdinalIgnoreCase))
			{
				glbRoot = n3;
				return true;
			}

			// For scene-authored instances, SceneFilePath should point to the packed scene.
			var scenePath = n3.SceneFilePath ?? string.Empty;
			if (!string.IsNullOrEmpty(scenePath)
				&& string.Equals(scenePath, desiredGlbPath, StringComparison.OrdinalIgnoreCase))
			{
				glbRoot = n3;
				// Tag it so future refreshes can be O(1) without relying on SceneFilePath.
				n3.SetMeta(GlbModelMetaKey, true);
				n3.SetMeta(GlbModelMetaKeyModelPath, desiredGlbPath);
				return true;
			}
		}

		return false;
	}

	private void TryAutoGroundGlbVisualOnce(Node3D glbRoot)
	{
		if (glbRoot.HasMeta(GlbModelMetaKeyGrounded))
		{
			return;
		}

		TryAutoGroundGlbVisual(glbRoot);
		glbRoot.SetMeta(GlbModelMetaKeyGrounded, true);
	}

	private void TryAutoGroundGlbVisual(Node3D glbRoot)
	{
		// We want the *visual* to sit on the ground while keeping this WorldObject's transform (network coords) intact.
		// So we adjust only the GLB child local position.
		GlbVisualGrounding.ApplyGroundOffset(glbRoot);
	}

	/// <summary>
	///     Non-empty <see cref="ModelName" /> (trimmed) wins; otherwise <see cref="ResolveModelNameFromObjectTypeFallback" />.
	/// </summary>
	protected string GetEffectiveModelNameForVisual()
	{
		var explicitName = ModelName?.Trim() ?? string.Empty;
		if (!string.IsNullOrEmpty(explicitName))
		{
			return explicitName;
		}

		return ResolveModelNameFromObjectTypeFallback();
	}

	/// <summary>
	///     Map entry for <see cref="ObjectType" />; if the map has no model for that type,
	///     <see cref="DefaultModelNameForVisual" />.
	/// </summary>
	protected virtual string ResolveModelNameFromObjectTypeFallback()
	{
		if (TryGetMappedModelName(ObjectType, out var mappedName))
		{
			return mappedName;
		}

		return DefaultModelNameForVisual;
	}

	private static bool TryGetMappedModelName(ObjectType objectType, out string modelName)
	{
		modelName = string.Empty;
		var mapped = ObjectTypeToModelNameMap.Get(objectType).Trim();
		if (string.IsNullOrEmpty(mapped))
		{
			return false;
		}

		modelName = mapped;
		return true;
	}

	/// <summary>GLB scenes usually root at <see cref="Node3D" />; otherwise wrap in a <see cref="Node3D" />.</summary>
	private static Node3D? InstantiateGlbRoot(PackedScene packed)
	{
		var inst = packed.Instantiate();
		if (inst is Node3D n3)
		{
			return n3;
		}

		if (inst is Node n)
		{
			var wrap = new Node3D();
			wrap.AddChild(n);
			return wrap;
		}

		inst?.Free();
		return null;
	}

	/// <summary>
	///     Removes prior GLB roots (meta-tagged or name <c>Glb</c>), including Godot-renamed duplicates (<c>Glb2</c>, …).
	/// </summary>
	protected void ClearLocalModelVisuals()
	{
		RemoveGlbModelChild();
		RemovePlaceholderMeshChild();
	}

	private void RemoveGlbModelChild()
	{
		var toRemove = new List<Node>();
		foreach (var child in GetChildren())
		{
			if (child.HasMeta(GlbModelMetaKey) || child.HasMeta(GlbModelMetaKeyLegacy))
			{
				toRemove.Add(child);
				continue;
			}

			// Don't aggressively remove a pre-authored "Glb" child: if it exists in the .tscn and matches the
			// desired model, we want to keep it (handled by TryGetExistingGlbVisual). If it's wrong, RefreshModelVisual
			// will call this after TryGetExistingGlbVisual fails, and at that point we do want it gone.
			var nm = child.Name.ToString();
			if (nm == GlbModelChildName || IsGlbRenamedDuplicateName(nm) || nm.StartsWith("NpcGlbModel", StringComparison.Ordinal))
			{
				toRemove.Add(child);
			}
		}

		foreach (var n in toRemove)
		{
			n.Free();
		}
	}

	private static bool IsGlbRenamedDuplicateName(string nodeName)
	{
		if (!nodeName.StartsWith(GlbModelChildName, StringComparison.Ordinal))
		{
			return false;
		}

		if (nodeName.Length == GlbModelChildName.Length)
		{
			return true;
		}

		for (var i = GlbModelChildName.Length; i < nodeName.Length; i++)
		{
			if (!char.IsDigit(nodeName[i]))
			{
				return false;
			}
		}

		return true;
	}

	private void RemovePlaceholderMeshChild()
	{
		var n = GetNodeOrNull(PlaceholderMeshNodeName);
		n?.Free();
	}

	private void ShowPlaceholderCube()
	{
		RemoveGlbModelChild();

		MeshInstance3D meshInst;
		if (GetNodeOrNull(PlaceholderMeshNodeName) is MeshInstance3D existing)
		{
			meshInst = existing;
		}
		else
		{
			meshInst = new MeshInstance3D();
			meshInst.Name = PlaceholderMeshNodeName;
			AddChild(meshInst);
			SetOwnerForEditedScene(meshInst);
		}

		var box = new BoxMesh { Size = Vector3.One };
		meshInst.Mesh = box;
		var mat = new StandardMaterial3D();
		mat.AlbedoTexture = LoadPlaceholderCheckerTexture();
		meshInst.MaterialOverride = mat;
	}

	private static Texture2D LoadPlaceholderCheckerTexture()
	{
		if (ResourceLoader.Exists(PlaceholderCheckerDdsPath))
		{
			var tex = ResourceLoader.Load<Texture2D>(PlaceholderCheckerDdsPath);
			if (tex is not null)
			{
				return tex;
			}
		}

		return CreateFallbackPinkCheckerTexture();
	}

	/// <summary>Procedural tileable fallback if the DDS resource is missing.</summary>
	private static Texture2D CreateFallbackPinkCheckerTexture()
	{
		const int w = 64;
		const int h = 64;
		const int tile = 8;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var light = new Color(1f, 0.6f, 0.8f);
		var dark = new Color(1f, 0.25f, 0.55f);
		for (var y = 0; y < h; y++)
		{
			for (var x = 0; x < w; x++)
			{
				var c = ((x / tile + y / tile) & 1) == 0 ? light : dark;
				img.SetPixel(x, y, c);
			}
		}

		return ImageTexture.CreateFromImage(img);
	}

	/// <summary>
	///     Owns editor-created visuals under this instance root only. Do not use <see cref="SceneTree.EditedSceneRoot" />;
	///     assigning the main scene root as owner can persist those nodes as direct children of the main node in the .tscn.
	/// </summary>
	private void SetOwnerForEditedScene(Node node)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		node.Owner = this;
	}
}
