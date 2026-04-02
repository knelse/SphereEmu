using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Editor tool: loads object placements from <c>Godot/Terrain/ObjectData/*.json</c>, pulls meshes from GLB scenes under
/// <see cref="ModelsDirectory"/>, and draws instances via <see cref="MultiMeshInstance3D"/> (one multimesh per mesh per category).
/// JSON positions and rotations use source world space: X right, Y down, Z forward (right-handed). Godot: X right, Y up, forward = -Z;
/// <see cref="SourceWorldToGodotWorldBasis"/> maps positions (x, y, z) ↦ (x, -y, -z). Rotations use R' = T R T (T² = I) so identity source rotation stays identity in Godot; R = T R_src alone would flip GLB meshes 180° about X.
/// </summary>
[Tool]
public partial class TerrainObjectsFill : Node3D
{
	/// <summary>
	/// Columns = source X, Y, Z axes expressed in Godot: right, down, forward (Godot forward = -Z), so (x,y,z)_src ↦ (x,-y,-z).
	/// </summary>
	private static readonly Basis SourceWorldToGodotWorldBasis = new (Vector3.Right, Vector3.Down, Vector3.Forward);

	public const string PlantsNodeName = "TerrainPlants";
	public const string RocksNodeName = "TerrainRocks";
	public const string OtherNodeName = "TerrainOther";

	[Export]
	public string ObjectDataDirectory { get; set; } = "res://Godot/Terrain/ObjectData/";

	[Export]
	public string ModelsDirectory { get; set; } = "res://Godot/Models/";

	[ExportToolButton ("Rebuild terrain objects")]
	public Callable RebuildTerrainObjectsButton => Callable.From (RebuildTerrainObjects);

	/// <summary>Clears category children and repopulates from all JSON files in <see cref="ObjectDataDirectory"/>.</summary>
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

		ClearChildren (plants);
		ClearChildren (rocks);
		ClearChildren (other);

		// (category root, Mesh) -> instance transforms (world * mesh-local)
		var batches = new Dictionary<(Node3D Category, Mesh Mesh), List<Transform3D>> (new MeshBatchKeyComparer ());
		var meshPartsCache = new Dictionary<string, List<MeshPart>?> ();

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

			if (da.CurrentIsDir () || !name.EndsWith (".json", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var path = dir + name;
			var jsonText = global::Godot.FileAccess.GetFileAsString (path);
			if (string.IsNullOrEmpty (jsonText))
			{
				GD.PushWarning ($"TerrainObjectsFill: empty or unreadable: {path}");
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

			foreach (var rec in records)
			{
				if (rec is null || string.IsNullOrWhiteSpace (rec.ObjectName))
				{
					continue;
				}

				if (!meshPartsCache.TryGetValue (rec.ObjectName, out var parts))
				{
					var scene = GetOrLoadScene (rec.ObjectName);
					parts = scene is null ? null : ExtractMeshParts (scene);
					meshPartsCache[rec.ObjectName] = parts;
					if (parts is null || parts.Count == 0)
					{
						if (scene is not null)
						{
							GD.PushWarning ($"TerrainObjectsFill: no drawable mesh for '{rec.ObjectName}' (missing mesh or skinned-only)");
						}
						else
						{
							GD.PushWarning ($"TerrainObjectsFill: no model for '{rec.ObjectName}' (tried .glb / .gltf under {ModelsDirectory})");
						}

						continue;
					}
				}
				else if (parts is null || parts.Count == 0)
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

				foreach (var part in parts)
				{
					var key = (parent, part.Mesh);
					if (!batches.TryGetValue (key, out var list))
					{
						list = new List<Transform3D> ();
						batches[key] = list;
					}

					list.Add (world * part.LocalToRoot);
				}
			}
		}

		da.ListDirEnd ();
		da.Dispose ();

		var mmIndex = 0;
		foreach (var kv in batches)
		{
			var (category, mesh) = kv.Key;
			var transforms = kv.Value;
			if (transforms.Count == 0)
			{
				continue;
			}

			var mm = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = mesh,
				InstanceCount = transforms.Count,
			};

			for (var i = 0; i < transforms.Count; i++)
			{
				mm.SetInstanceTransform (i, transforms[i]);
			}

			var mmi = new MultiMeshInstance3D
			{
				Name = $"TerrainMM_{mmIndex++}",
				Multimesh = mm,
			};
			category.AddChild (mmi);
			SetOwnerIfEditor (mmi);
		}
	}

	private static Transform3D BuildPlacementTransform (Vector3 position, Vector3 rotationEuler)
	{
		var basis = Basis.FromEuler (rotationEuler, EulerOrder.Yxz);
		return new Transform3D (basis, position);
	}

	private sealed class MeshPart
	{
		public Mesh Mesh { get; }
		public Transform3D LocalToRoot { get; }

		public MeshPart (Mesh mesh, Transform3D localToRoot)
		{
			Mesh = mesh;
			LocalToRoot = localToRoot;
		}
	}

	/// <summary>Reference-equality for <see cref="Mesh"/> so batches merge identical resources.</summary>
	private sealed class MeshBatchKeyComparer : IEqualityComparer<(Node3D Category, Mesh Mesh)>
	{
		public bool Equals ((Node3D Category, Mesh Mesh) x, (Node3D Category, Mesh Mesh) y) =>
			ReferenceEquals (x.Category, y.Category) && ReferenceEquals (x.Mesh, y.Mesh);

		public int GetHashCode ((Node3D Category, Mesh Mesh) obj) =>
			HashCode.Combine (obj.Category.GetInstanceId (), obj.Mesh.GetInstanceId ());
	}

	private static List<MeshPart>? ExtractMeshParts (PackedScene scene)
	{
		var root = scene.Instantiate<Node3D> ();
		try
		{
			var list = new List<MeshPart> ();
			CollectMeshes (root, root, list);
			return list.Count == 0 ? null : list;
		}
		finally
		{
			root.QueueFree ();
		}
	}

	private static void CollectMeshes (Node node, Node3D root, List<MeshPart> list)
	{
		if (node is MeshInstance3D mi && mi.Mesh is { } mesh)
		{
			if (HasSkeletonAncestor (mi))
			{
				return;
			}

			var localToRoot = ComputeTransformRelativeToRoot (mi, root);
			list.Add (new MeshPart (mesh, localToRoot));
		}

		foreach (var child in node.GetChildren ())
		{
			CollectMeshes (child, root, list);
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

	/// <summary>Transform from <paramref name="root"/> space to <paramref name="node"/> space (node is typically a <see cref="MeshInstance3D"/>).</summary>
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

	private static void ClearChildren (Node3D node)
	{
		foreach (var child in node.GetChildren ())
		{
			child.QueueFree ();
		}
	}

	private PackedScene? GetOrLoadScene (string objectName)
	{
		var baseDir = ModelsDirectory.TrimEnd ('/') + "/";
		foreach (var ext in new[] { "glb", "gltf" })
		{
			var path = $"{baseDir}{objectName}.{ext}";
			if (ResourceLoader.Exists (path))
			{
				return ResourceLoader.Load<PackedScene> (path);
			}
		}

		return null;
	}

	private void SetOwnerIfEditor (Node node)
	{
		if (!Engine.IsEditorHint ())
		{
			return;
		}

		var root = GetTree ()?.EditedSceneRoot;
		node.Owner = root ?? this;
	}

	/// <summary>
	/// Uses Godot's <see cref="Json"/> parser (not Newtonsoft.Json) so collectible assemblies can unload in the editor.
	/// See https://github.com/godotengine/godot/issues/78513
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
			if (!d.TryGetValue ("object_name", out var nameVar))
			{
				continue;
			}

			var objectName = nameVar.AsString ();
			if (string.IsNullOrWhiteSpace (objectName))
			{
				continue;
			}

			var rec = new TerrainObjectRecord { ObjectName = objectName };

			if (d.TryGetValue ("coordinates", out var coordVar) && coordVar.VariantType == Variant.Type.Dictionary)
			{
				var cd = coordVar.AsGodotDictionary ();
				rec.Coordinates = new TerrainCoordinates {
					X = DictGetDouble (cd, "x"),
					Y = DictGetDouble (cd, "y"),
					Z = DictGetDouble (cd, "z"),
				};
			}

			if (d.TryGetValue ("rotation_euler", out var rotVar) && rotVar.VariantType == Variant.Type.Dictionary)
			{
				var rd = rotVar.AsGodotDictionary ();
				rec.RotationEuler = new TerrainRotationEuler {
					Yaw = DictGetDouble (rd, "yaw"),
					Pitch = DictGetDouble (rd, "pitch"),
					Roll = DictGetDouble (rd, "roll"),
				};
			}

			list.Add (rec);
		}

		return list;
	}

	private static double DictGetDouble (global::Godot.Collections.Dictionary d, StringName key) =>
		d.TryGetValue (key, out var v) ? v.AsDouble () : 0.0;

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

		public Vector3 ToVector3 () =>
			SourceWorldToGodotWorldBasis * new Vector3 ((float) X, (float) Y, (float) Z);
	}

	/// <summary>
	/// JSON uses yaw (Y), pitch (X), roll (Z) — same component order as <see cref="BuildPlacementTransform"/> / <see cref="EulerOrder.Yxz"/>.
	/// Euler is in <b>source</b> space (Y down, Z forward). Yaw is negated when building <see cref="Basis.FromEuler"/> so “yaw about down” matches Godot Y-up; then R_godot = T R_src T (conjugate — not T R_src, which leaves a 180° X flip on identity and inverts GLBs).
	/// </summary>
	private sealed class TerrainRotationEuler
	{
		public double Yaw { get; set; }
		public double Pitch { get; set; }
		public double Roll { get; set; }

		/// <summary>
		/// R_src from YXZ Euler with negated yaw; same physical orientation in Godot world as R' = T R_src T⁻¹ with T = <see cref="SourceWorldToGodotWorldBasis"/>.
		/// </summary>
		public Vector3 ToEulerRadians ()
		{
			var eulerForGodotBasis = new Vector3 ((float) Pitch, -(float) Yaw, (float) Roll);
			var basisSource = Basis.FromEuler (eulerForGodotBasis, EulerOrder.Yxz);
			var t = SourceWorldToGodotWorldBasis;
			var basisGodot = t * basisSource * t;
			return basisGodot.GetEuler (EulerOrder.Yxz);
		}
	}
}
