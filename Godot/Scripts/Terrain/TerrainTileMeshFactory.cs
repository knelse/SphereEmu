using Godot;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Builds textured terrain tile meshes + trimesh collision shapes (same basis/texture
///     rules as <see cref="TerrainGridFill"/>), cached by master tile name for ground streaming.
/// </summary>
public static class TerrainTileMeshFactory
{
	public const string TilesDirectory = "res://Godot/Terrain/Tiles/";
	public const string TexturesDirectory = "res://Godot/Terrain/Textures/";

	public static string SharedMeshDirectory => TerrainBakePaths.GroundMeshesDir.TrimEnd('/') + "/";
	public static string SharedShapeDirectory => TerrainBakePaths.GroundShapesDir.TrimEnd('/') + "/";

	private static readonly Dictionary<string, Mesh> MeshCache = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, Shape3D> ShapeCache = new(StringComparer.Ordinal);

	public static Mesh? GetOrBuildMesh(string masterName)
	{
		if (MeshCache.TryGetValue(masterName, out var cached))
		{
			return cached;
		}

		var sharedPath = TerrainBakePaths.GroundMeshRes(masterName);
		if (ResourceLoader.Exists(sharedPath))
		{
			var loaded = ResourceLoader.Load<Mesh>(sharedPath);
			if (loaded is not null)
			{
				MeshCache[masterName] = loaded;
				return loaded;
			}
		}

		var built = BuildTexturedMesh(masterName);
		if (built is null)
		{
			return null;
		}

		MeshCache[masterName] = built;
		return built;
	}

	public static Shape3D? GetOrBuildShape(string masterName)
	{
		if (ShapeCache.TryGetValue(masterName, out var cached))
		{
			return cached;
		}

		var sharedPath = TerrainBakePaths.GroundShapeRes(masterName);
		if (ResourceLoader.Exists(sharedPath))
		{
			var loaded = ResourceLoader.Load<Shape3D>(sharedPath);
			if (loaded is not null)
			{
				ShapeCache[masterName] = loaded;
				return loaded;
			}
		}

		var mesh = GetOrBuildMesh(masterName);
		if (mesh is null)
		{
			return null;
		}

		var shape = mesh.CreateTrimeshShape();
		if (shape is null)
		{
			return null;
		}

		ShapeCache[masterName] = shape;
		return shape;
	}

	public static void ClearCache()
	{
		MeshCache.Clear();
		ShapeCache.Clear();
	}

	public static Mesh? BuildTexturedMesh(string masterName)
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

	private static Basis TileMeshBasisAfterImport()
	{
		var dr = MapFill.DefaultRotation;
		var euler = new Vector3(dr.X, dr.Y, dr.Z);
		var basis = Basis.FromEuler(euler, EulerOrder.Yxz);
		var reflectZ = new Basis(Vector3.Right, Vector3.Up, new Vector3(0f, 0f, -1f));
		return reflectZ * basis * reflectZ;
	}

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

	private static PackedScene? LoadTileScene(string baseName)
	{
		// On-disk tile/texture stems are lowercase; map master names use Patch* casing.
		var fileStem = baseName.ToLowerInvariant();
		foreach (var ext in new[] { "blend", "glb", "gltf" })
		{
			var path = $"{TilesDirectory.TrimEnd('/')}/{fileStem}.{ext}";
			if (!ResourceLoader.Exists(path))
			{
				continue;
			}

			return ResourceLoader.Load<PackedScene>(path);
		}

		return null;
	}

	private static Texture2D? TryLoadTexture(string baseName)
	{
		var path = $"{TexturesDirectory.TrimEnd('/')}/{baseName.ToLowerInvariant()}.dds";
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
}
