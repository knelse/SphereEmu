using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Editor tool: builds or updates a child <see cref="GridMap"/> named <c>Terrain</c>, a persisted
/// <see cref="MeshLibrary"/>, and fills cells from <c>map.bin</c>. Re-runs clear the mesh library and grid, then repopulate.
/// </summary>
[Tool]
public partial class TerrainGridFill : Node3D
{
	public const string TerrainNodeName = "Terrain";
	public const string DefaultMeshLibraryPath = "res://Godot/Terrain/TerrainMeshLibrary.tres";

	/// <summary>Path to the map.txt file. Has to be txt instead of bin because Godot will not 
	/// recognize it as a resource with unknown extension.</summary>
	[Export]
	public string MapBinPath { get; set; } = "res://Godot/Terrain/map.txt";

	[Export]
	public string TilesDirectory { get; set; } = "res://Godot/Terrain/Tiles/";

	[Export]
	public string TexturesDirectory { get; set; } = "res://Godot/Terrain/Textures/";

	/// <summary>Written after each build; loaded on the next run so the same resource is cleared and refilled.</summary>
	[Export]
	public string MeshLibraryResourcePath { get; set; } = DefaultMeshLibraryPath;

	[Export]
	public float TileSizeWorld { get; set; } = 100f;

	[Export]
	public int CellOrientation { get; set; }

	/// <summary>Inspector button: build or rebuild terrain from <c>map.bin</c> (editor only).</summary>
	[ExportToolButton("Rebuild terrain")]
	public Callable RebuildTerrainButton => Callable.From(RebuildTerrain);

	/// <summary>Clears and repopulates the mesh library and <c>Terrain</c> <see cref="GridMap"/>.</summary>
	public void RebuildTerrain()
	{
		var mapAbs = ProjectSettings.GlobalizePath(MapBinPath);
		if (!File.Exists(mapAbs))
		{
			GD.PushError($"TerrainGridFill: map not found: {MapBinPath}");
			return;
		}

		var cells = MapFill.ReadFullGrid(mapAbs);
		var terrain = GetOrCreateTerrainGridMap();
		// Avoid expensive navigation baking work during load/open; we want terrain to load fast.
		// Use dynamic property set so this works even if the C# API name differs across Godot versions.
		terrain.Set("bake_navigation", false);
		var meshLib = LoadOrCreateMeshLibrary();
		ClearMeshLibrary(meshLib);

		var uniqueNames = cells
			.Where(c => !c.IsEmpty)
			.Select(c => c.MasterName)
			.Distinct()
			.OrderBy(n => n)
			.ToList();

		var nameToItemId = new Dictionary<string, int>();
		var nextId = 0;
		foreach (var masterName in uniqueNames)
		{
			var mesh = TryBuildTexturedMesh(masterName);
			if (mesh is null)
			{
				GD.PushWarning($"TerrainGridFill: skipped tile (no mesh): {masterName}");
				continue;
			}

			meshLib.CreateItem(nextId);
			meshLib.SetItemMesh(nextId, mesh);
			meshLib.SetItemName(nextId, masterName);
			nameToItemId[masterName] = nextId;
			nextId++;
		}

		terrain.MeshLibrary = meshLib;
		SaveMeshLibrary(meshLib);

		var cellSize = new Vector3(TileSizeWorld, TileSizeWorld, TileSizeWorld);
		terrain.CellSize = cellSize;
		terrain.Clear();

		for (var i = 0; i < cells.Count; i++)
		{
			var cell = cells[i];
			if (cell.IsEmpty)
			{
				continue;
			}

			if (!nameToItemId.TryGetValue(cell.MasterName, out var itemId))
			{
				continue;
			}

			var gx = MapFill.GridWidth - (i % MapFill.GridWidth) - 1;
			var gz = i / MapFill.GridWidth;
			terrain.SetCellItem(new Vector3I(gx, 0, gz), itemId, CellOrientation);
		}
	}

	private GridMap GetOrCreateTerrainGridMap()
	{
		if (GetNodeOrNull(TerrainNodeName) is GridMap existing)
		{
			return existing;
		}

		var grid = new GridMap { Name = TerrainNodeName };
		AddChild(grid);
		SetOwnerIfEditor(grid);
		return grid;
	}

	private MeshLibrary LoadOrCreateMeshLibrary()
	{
		if (ResourceLoader.Exists(MeshLibraryResourcePath))
		{
			var loaded = ResourceLoader.Load<MeshLibrary>(MeshLibraryResourcePath);
			if (loaded is not null)
			{
				return loaded;
			}
		}

		return new MeshLibrary();
	}

	private static void ClearMeshLibrary(MeshLibrary meshLib)
	{
		var ids = meshLib.GetItemList();
		foreach (var id in ids)
		{
			meshLib.RemoveItem(id);
		}
	}

	private void SaveMeshLibrary(MeshLibrary meshLib)
	{
		var err = ResourceSaver.Save(meshLib, MeshLibraryResourcePath);
		if (err != Error.Ok)
		{
			GD.PushError($"TerrainGridFill: failed to save mesh library ({err}): {MeshLibraryResourcePath}");
		}
	}

	private Mesh? TryBuildTexturedMesh(string masterName)
	{
		var scene = LoadTileScene(masterName);
		if (scene is null)
		{
			return null;
		}

		var root = scene.Instantiate<Node>();
		var meshInstance = FindFirstMeshInstance(root);
		var sourceMesh = meshInstance?.Mesh;
		if (sourceMesh is null)
		{
			root.QueueFree();
			return null;
		}

		var mesh = (Mesh)sourceMesh.Duplicate();
		root.QueueFree();

		mesh = ApplyBasisRotationAfterImport(mesh, TileMeshBasisAfterImport());

		var texture = TryLoadTexture(masterName);
		var surfaceCount = mesh.GetSurfaceCount();
		for (var s = 0; s < surfaceCount; s++)
		{
			var mat = new StandardMaterial3D();
			if (texture is not null)
			{
				mat.AlbedoTexture = texture;
			}

			mesh.SurfaceSetMaterial(s, mat);
		}

		return mesh;
	}

	/// <summary>
	/// Same right-handed → left-handed mapping as <c>TerrainRotationEuler.ToEulerRadians</c> in
	/// <see cref="TerrainObjectsFill"/>: <c>euler = (Pitch, -π + Yaw, Roll)</c> with
	/// <see cref="EulerOrder.Yxz"/>, then <c>reflectZ * basis * reflectZ</c> for Godot (Y-up, mirror forward).
	/// <see cref="MapFill.DefaultRotation"/> supplies pitch/yaw/roll as <c>X</c>/<c>Y</c>/<c>Z</c>.
	/// </summary>
	private static Basis TileMeshBasisAfterImport()
	{
		var dr = MapFill.DefaultRotation;
		var euler = new Vector3(dr.X, dr.Y, dr.Z);
		var basis = Basis.FromEuler(euler, EulerOrder.Yxz);
		var reflectZ = new Basis(Vector3.Right, Vector3.Up, new Vector3(0f, 0f, -1f));
		return reflectZ * basis * reflectZ;
	}

	/// <summary>
	/// Applies <paramref name="basis"/> to mesh arrays (vertices, normals, tangents).
	/// </summary>
	private static Mesh ApplyBasisRotationAfterImport(Mesh mesh, Basis basis)
	{
		if (mesh.GetSurfaceCount() == 0)
		{
			return mesh;
		}

		var outMesh = new ArrayMesh();
		for (var s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			var arrays = mesh.SurfaceGetArrays(s);
			var verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
			if (verts is not null)
			{
				for (var i = 0; i < verts.Length; i++)
				{
					verts[i] = basis * verts[i];
				}

				arrays[(int)Mesh.ArrayType.Vertex] = verts;
			}

			var normals = (Vector3[])arrays[(int)Mesh.ArrayType.Normal];
			if (normals is not null)
			{
				for (var i = 0; i < normals.Length; i++)
				{
					normals[i] = (basis * normals[i]).Normalized();
				}

				arrays[(int)Mesh.ArrayType.Normal] = normals;
			}

			var tangents = (float[])arrays[(int)Mesh.ArrayType.Tangent];
			if (tangents is not null)
			{
				for (var i = 0; i < tangents.Length; i += 4)
				{
					var tv = basis * new Vector3(tangents[i], tangents[i + 1], tangents[i + 2]);
					tangents[i] = tv.X;
					tangents[i + 1] = tv.Y;
					tangents[i + 2] = tv.Z;
				}

				arrays[(int)Mesh.ArrayType.Tangent] = tangents;
			}

			var prim = mesh is ArrayMesh am ? am.SurfaceGetPrimitiveType(s) : Mesh.PrimitiveType.Triangles;
			outMesh.AddSurfaceFromArrays(prim, arrays);
		}

		return outMesh;
	}

	private PackedScene? LoadTileScene(string baseName)
	{
		foreach (var ext in new[] { "blend", "glb", "gltf" })
		{
			var path = $"{TilesDirectory.TrimEnd('/')}/{baseName}.{ext}";
			if (ResourceLoader.Exists(path))
			{
				return ResourceLoader.Load<PackedScene>(path);
			}
		}

		return null;
	}

	private Texture2D? TryLoadTexture(string baseName)
	{
		var path = $"{TexturesDirectory.TrimEnd('/')}/{baseName}.dds";
		if (!ResourceLoader.Exists(path))
		{
			return null;
		}

		return ResourceLoader.Load<Texture2D>(path);
	}

	private static MeshInstance3D? FindFirstMeshInstance(Node node)
	{
		if (node is MeshInstance3D mi)
		{
			return mi;
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && FindFirstMeshInstance(childNode) is { } found)
			{
				return found;
			}
		}

		return null;
	}

	/// <summary>
	/// Editor: new nodes must be owned by the opened scene root or they do not show in the Scene dock and are not saved.
	/// </summary>
	private void SetOwnerIfEditor(Node node)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		var root = GetTree()?.EditedSceneRoot;
		if (root is not null)
		{
			node.Owner = root;
		}
		else
		{
			node.Owner = this;
		}
	}
}
