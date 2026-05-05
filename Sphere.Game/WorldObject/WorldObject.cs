using Godot;
using SphServer.Client;
using SphServer.Packets;
using SphServer.Server.Config;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.Converters;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Marked as a Godot tool script so the editor runs export setters (angle, model, type) and <c>_Ready</c> for visuals;
///     server registration and visibility area are skipped in the editor.
/// </summary>
[Tool]
public partial class WorldObject : Node3D
{
	private const string PlaceholderMeshNodeName = "MeshInstance3D";

	/// <summary>Short name + unique scene id so the tree shows e.g. <c>…#Glb</c> instead of a long duplicate name.</summary>
	private const string GlbModelChildName = "Glb";

	private const string GlbModelMetaKey = "_world_object_glb";
	private const string GlbModelMetaKeyLegacy = "_npc_interactable_glb";
	private const string GlbModelMetaKeyModelPath = "_world_object_glb_path";
	private const string GlbModelMetaKeyGrounded = "_world_object_glb_grounded";
	private const string PlaceholderCheckerDdsPath = "res://Godot/Textures/npc_placeholder_checker.dds";

	/// <summary>Used when <see cref="ModelName" /> is empty and <see cref="ObjectType" /> has no mapped GLB.</summary>
	private const string DefaultModelNameForVisual = "pump1";

	/// <summary>
	///     Columns = source X, Y, Z axes in Godot (same as <see cref="SphServer.Godot.Scripts.Terrain.TerrainObjectsFill" />):
	///     <c>(x,y,z)_src ↦ (x,-y,-z)</c>. Used with <c>R' = T R T</c> for yaw from <see cref="Angle" />.
	/// </summary>
	private static readonly Basis SourceWorldToGodotWorldBasis = new(Vector3.Right, Vector3.Down, Vector3.Forward);

	private int _angle;

	private string _modelName = string.Empty;

	private ObjectType _objectType = ObjectType.Unknown;

	/// <summary>
	///     Game yaw: 0 = north; values increase counter-clockwise. Source yaw is <c>Angle * π / 64</c> radians; Godot
	///     <see cref="Rotation" /> uses the same <c>R' = T R_src T</c> / YXZ path as terrain JSON
	///     (<see cref="SphServer.Godot.Scripts.Terrain.TerrainObjectsFill" />; <c>NpcSpawnTscnWriter</c> position flip is
	///     separate).
	/// </summary>
	[Export]
	public int Angle
	{
		get => _angle;
		set
		{
			if (_angle == value)
			{
				return;
			}

			_angle = value;
			ApplyAngleToRotation();
		}
	}

	[Export] public ushort ID { get; set; }

	[Export]
	public ObjectType ObjectType
	{
		get => _objectType;
		set
		{
			if (_objectType == value)
			{
				return;
			}

			_objectType = value;
			if (IsInsideTree())
			{
				CallDeferred(nameof(RefreshModelVisualDeferred));
			}
		}
	}

	[Export]
	public string ModelName
	{
		get => _modelName;
		set
		{
			if (_modelName == value)
			{
				return;
			}

			_modelName = value;
			if (IsInsideTree())
			{
				CallDeferred(nameof(RefreshModelVisualDeferred));
			}
		}
	}

	/// <summary>
	///     When true, <see cref="Node._Ready" /> loads or refreshes the GLB / placeholder mesh before the rest of setup,
	///     and returns early in the editor (no collision / networking init).
	/// </summary>
	protected virtual bool RefreshModelVisualOnReady => false;

	/// <summary>
	///     If true, after loading a GLB we shift the visual child so its combined mesh bounds sit on Y=0 (feet on ground).
	///     This compensates for assets whose origin/pivot is centered instead of at the bottom.
	/// </summary>
	protected virtual bool AutoGroundGlbVisual => false;

	public override void _Ready()
	{
		ApplyAngleToRotation();

		if (RefreshModelVisualOnReady)
		{
			RefreshModelVisual();
			if (Engine.IsEditorHint())
			{
				return;
			}
		}

		if (!Engine.IsEditorHint())
		{
			if (ID == 0)
			{
				ID = WorldObjectIndex.New();
			}

			Name = Name + $"_{ID}";

			ActiveNodes.Add(GetInstanceId(), this);
			ActiveWorldObjects.Add(ID, this);

			var area3D = new Area3D();
			area3D.CollisionLayer = 1;
			area3D.CollisionMask = 2;

			var collisionShape3d = new CollisionShape3D();
			collisionShape3d.Shape = new SphereShape3D
			{
				Radius = ServerConfig.AppConfig.ObjectVisibilityDistance
			};

			area3D.AddChild(collisionShape3d);
			area3D.BodyEntered += body =>
			{
				// layer mask should only allow clients here, so we assume it's a client

				var clientNode = body.GetParent();
				if (clientNode is not SphereClient client)
				{
					SphLogger.Info($"WorldObject {Name}: collision enter by {body.Name} which is not a SphereClient");
					return;
				}

				ShowForClient(client);
			};

			area3D.BodyExited += body =>
			{
				// layer mask should only allow clients here, so we assume it's a client

				var clientNode = body.GetParent();
				if (clientNode is not SphereClient client)
				{
					SphLogger.Info($"WorldObject {Name}: collision exit by {body.Name} which is not a SphereClient");
					return;
				}

				client.MaybeQueueNetworkPacketSend(CommonPackets.DespawnEntity(ID));
			};
			AddChild(area3D);
		}

		// Instance overrides (e.g. ObjectType = Workshop on MainServer placements) are applied after the base
		// scene's defaults; refreshing on the next frame avoids resolving Unknown/empty map and a stuck placeholder.
		if (!RefreshModelVisualOnReady)
		{
			CallDeferred(nameof(RefreshModelVisualDeferred));
		}
	}

	/// <summary>
	///     Sets <see cref="Rotation" /> Y from <see cref="Angle" /> using <c>t0 = -Angle * π / 64</c>.
	/// </summary>
	private void ApplyAngleToRotation()
	{
		var t0 = (float)(_angle * Math.PI / 128.0);
		// Avoid forcing a transform change on load if the scene already has the correct rotation.
		if (Mathf.Abs(Rotation.Y - t0) < 0.0001f && Mathf.Abs(Rotation.X) < 0.0001f && Mathf.Abs(Rotation.Z) < 0.0001f)
		{
			return;
		}

		Rotation = new Vector3(0f, t0, 0f);
	}

	protected virtual void ShowForClient(SphereClient client)
	{
		var packetParts = GetPacketPartsAndUpdateCoordsAndID(client);
		packetParts = ModifyPacketParts(packetParts);
		var packet = PostprocessPacketBytes(PacketPart.GetBytesToWrite(packetParts));
		client.MaybeQueueNetworkPacketSend(packet);
	}

	protected virtual List<PacketPart> GetPacketParts()
	{
		return PacketPart.LoadDefinedPartsFromFile(ObjectType);
	}

	public List<PacketPart> GetPacketPartsAndUpdateCoordsAndID(SphereClient client)
	{
		var packetParts = GetPacketParts();
		PacketPart.UpdateCoordinates(packetParts, GlobalTransform.Origin.X, -GlobalTransform.Origin.Y,
			-GlobalTransform.Origin.Z, Angle);
		var localId = client.GetLocalObjectId(ID);
		PacketPart.UpdateEntityId(packetParts, localId);
		return packetParts;
	}

	protected virtual List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "object_type", (int)ObjectType, 10);
		return packetParts;
	}

	protected virtual byte[] PostprocessPacketBytes(byte[] packet)
	{
		return packet;
	}

	protected virtual void ClientInteract(ushort clientID,
		ClientInteractionType interactionType = ClientInteractionType.Unknown)
	{
		SphLogger.Info($"Client [{clientID:X4}] interacts with [{ID}] {ObjectType} -- {interactionType}");
	}

	/// <summary>
	///     Godot string-based <see cref="CallDeferred(string, Variant[])" /> only binds exported/public methods;
	///     this forwards to <see cref="RefreshModelVisual" /> when <see cref="ObjectType" /> / <see cref="ModelName" />
	///     change.
	/// </summary>
	public void RefreshModelVisualDeferred()
	{
		RefreshModelVisual();
	}

	/// <summary>
	///     Loads a GLB from <c>res://Godot/Models/{name}.glb</c> using <see cref="ModelName" /> if set, otherwise
	///     <see cref="ObjectTypeToModelNameMap" /> for <see cref="ObjectType" />, otherwise
	///     <see cref="DefaultModelNameForVisual" />;
	///     if the asset is missing, shows the checkered placeholder cube.
	/// </summary>
	protected void RefreshModelVisual()
	{
		var trimmed = GetEffectiveModelNameForVisual();
		if (string.IsNullOrEmpty(trimmed))
		{
			RemoveGlbModelChild();
			ShowPlaceholderCube();
			return;
		}

		var glbPath = $"res://Godot/Models/{trimmed}.glb";
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
			if (!string.IsNullOrEmpty(metaPath) && string.Equals(metaPath, desiredGlbPath, StringComparison.Ordinal))
			{
				glbRoot = n3;
				return true;
			}

			// For scene-authored instances, SceneFilePath should point to the packed scene.
			var scenePath = n3.SceneFilePath ?? string.Empty;
			if (!string.IsNullOrEmpty(scenePath) && string.Equals(scenePath, desiredGlbPath, StringComparison.Ordinal))
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
		var aabbFound = false;
		var combined = new Aabb();

		foreach (var child in glbRoot.FindChildren("*", "MeshInstance3D", recursive: true, owned: false))
		{
			if (child is not MeshInstance3D mi)
			{
				continue;
			}

			var mesh = mi.Mesh;
			if (mesh is null)
			{
				continue;
			}

			var localAabb = mi.GetAabb();
			if (localAabb.Size == Vector3.Zero)
			{
				continue;
			}

			// Transform AABB corners from mesh-local → glbRoot-local and merge.
			var toGlb = glbRoot.GlobalTransform.AffineInverse() * mi.GlobalTransform;
			var transformed = TransformAabb(toGlb, localAabb);

			if (!aabbFound)
			{
				combined = transformed;
				aabbFound = true;
			}
			else
			{
				combined = combined.Merge(transformed);
			}
		}

		if (!aabbFound)
		{
			return;
		}

		var minY = combined.Position.Y;
		// If the mesh already has its bottom at Y=0, minY will be ~0 and this is a no-op.
		// If the pivot is centered, minY will be negative and we shift up by -minY.
		if (Mathf.Abs(minY) < 0.0001f)
		{
			return;
		}

		glbRoot.Position += new Vector3(0f, -minY, 0f);
	}

	private static Aabb TransformAabb(Transform3D xform, Aabb aabb)
	{
		var p = aabb.Position;
		var s = aabb.Size;

		var corners = new[]
		{
			new Vector3(p.X,       p.Y,       p.Z),
			new Vector3(p.X + s.X, p.Y,       p.Z),
			new Vector3(p.X,       p.Y + s.Y, p.Z),
			new Vector3(p.X,       p.Y,       p.Z + s.Z),
			new Vector3(p.X + s.X, p.Y + s.Y, p.Z),
			new Vector3(p.X + s.X, p.Y,       p.Z + s.Z),
			new Vector3(p.X,       p.Y + s.Y, p.Z + s.Z),
			new Vector3(p.X + s.X, p.Y + s.Y, p.Z + s.Z),
		};

		var first = xform * corners[0];
		var min = first;
		var max = first;

		for (var i = 1; i < corners.Length; i++)
		{
			var v = xform * corners[i];
			min = new Vector3(Mathf.Min(min.X, v.X), Mathf.Min(min.Y, v.Y), Mathf.Min(min.Z, v.Z));
			max = new Vector3(Mathf.Max(max.X, v.X), Mathf.Max(max.Y, v.Y), Mathf.Max(max.Z, v.Z));
		}

		return new Aabb(min, max - min);
	}

	/// <summary>
	///     Non-empty <see cref="ModelName" /> (trimmed) wins; otherwise <see cref="ResolveModelNameFromObjectTypeFallback" />.
	/// </summary>
	private string GetEffectiveModelNameForVisual()
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

	/// <summary>
	///     Resolves the map by enum; also matches by underlying <see cref="ushort" /> so packed-scene instance values
	///     still line up if Godot's enum binding is off by member name.
	/// </summary>
	private static bool TryGetMappedModelName(ObjectType objectType, out string modelName)
	{
		modelName = string.Empty;
		if (ObjectTypeToModelNameMap.Map.TryGetValue(objectType, out var mapped))
		{
			var m = mapped?.Trim() ?? string.Empty;
			if (!string.IsNullOrEmpty(m))
			{
				modelName = m;
				return true;
			}
		}

		var raw = (ushort)objectType;
		foreach (var kv in ObjectTypeToModelNameMap.Map)
		{
			if ((ushort)kv.Key != raw)
			{
				continue;
			}

			var m = kv.Value?.Trim() ?? string.Empty;
			if (!string.IsNullOrEmpty(m))
			{
				modelName = m;
				return true;
			}

			return false;
		}

		return false;
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
