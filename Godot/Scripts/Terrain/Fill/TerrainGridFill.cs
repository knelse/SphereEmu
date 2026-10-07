using System.Collections.Generic;
using System.IO;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.Fill;

[Tool]
public partial class TerrainGridFill : Node3D
{
	public const string TerrainNodeName = "Terrain";

	/// <summary>
	/// Legacy path. An empty export uses TerrainBakePaths.MeshLibraryTres
	/// </summary>
	public const string DefaultMeshLibraryPath = "";

	/// <summary>
	/// txt, not bin. Godot will not import an unknown extension as a resource
	/// </summary>
	[Export]
	public string MapBinPath { get; set; } = "res://Godot/Terrain/map.txt";

	[Export]
	public string TilesDirectory { get; set; } = "res://Godot/Terrain/Tiles/";

	[Export]
	public string TexturesDirectory { get; set; } = "res://Godot/Terrain/Textures/";

	/// <summary>
	/// Empty uses TerrainBakePaths.MeshLibraryTres, outside Godot import. The same resource is
	/// cleared and refilled on the next build
	/// </summary>
	[Export]
	public string MeshLibraryResourcePath { get; set; } = DefaultMeshLibraryPath;

	[Export]
	public float TileSizeWorld { get; set; } = 100f;

	/// <summary>
	/// 80x80 map at 100 m is 8000x8000 m, centered, so cell (0, 0) sits near (-4000, -4000)
	/// </summary>
	[Export]
	public Vector3 TerrainWorldOrigin { get; set; } = new (-4000f, 0f, -4000f);

	[Export]
	public int CellOrientation { get; set; }

	[ExportToolButton ("Rebuild terrain")]
	public Callable RebuildTerrainButton => Callable.From (RebuildTerrain);

	private readonly HashSet<(int Gx, int Gz)> editorWaterCells = [];
	private double editorWaterWait;
	private int editorWaterShown;
	private bool editorWaterProbed;
	private bool editorWaterCameraWarned;

	public override void _EnterTree ()
	{
		// Tool _Process stays off in the editor until this is set
		if (Engine.IsEditorHint ())
		{
			SetProcess (true);
		}
	}

	public override void _Process (double delta)
	{
		if (!Engine.IsEditorHint ())
		{
			return;
		}

		editorWaterWait += delta;
		if (editorWaterWait < 0.4)
		{
			return;
		}

		editorWaterWait = 0;
		var camera = EditorCamera ();
		if (camera is null || GetNodeOrNull (TerrainNodeName) is not GridMap grid)
		{
			if (!editorWaterCameraWarned)
			{
				editorWaterCameraWarned = true;
				GD.Print ("TerrainGridFill: editor water waiting on the 3D camera or Terrain GridMap");
			}

			return;
		}

		var center = grid.LocalToMap (grid.ToLocal (camera.GlobalPosition));
		var root = EditorWaterRoot (grid);
		// Script reload keeps the previous water nodes, which still have the old material
		if (editorWaterCells.Count == 0)
		{
			foreach (var child in root.GetChildren ())
			{
				child.Free ();
			}
		}

		var index = TerrainGroundIndex.GetOrLoad (MapBinPath);
		if (!editorWaterProbed)
		{
			editorWaterProbed = true;
			index.TryGetMasterName (center.X, center.Z, out var centerMaster);
			var hasWater = centerMaster is not null && TerrainWater.GetOrBuildMesh (centerMaster) is not null;
			GD.Print (
				$"TerrainGridFill: editor camera cell {center.X},{center.Z} "
				+ $"master={centerMaster ?? "none"} water={hasWater}");
		}
		var added = 0;
		for (var gz = center.Z - 3; gz <= center.Z + 3; gz++)
		{
			for (var gx = center.X - 3; gx <= center.X + 3; gx++)
			{
				if (!editorWaterCells.Add ((gx, gz)))
				{
					continue;
				}

				if (!index.TryGetMasterName (gx, gz, out var masterName))
				{
					continue;
				}

				var water = TerrainWater.GetOrBuildMesh (masterName);
				if (water is null)
				{
					continue;
				}

				root.AddChild (new MeshInstance3D
				{
					Name = $"Water_{gx}_{gz}",
					Mesh = water,
					Position = grid.MapToLocal (new Vector3I (gx, 0, gz)),
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				});
				added++;
			}
		}

		if (added == 0)
		{
			return;
		}

		editorWaterShown += added;
		GD.Print ($"TerrainGridFill: editor water meshes {editorWaterShown}");
	}

	private static Camera3D? EditorCamera ()
	{
#if TOOLS
		var viewport = EditorInterface.Singleton?.GetEditorViewport3D ();
		return viewport?.GetCamera3D ();
#else
		return null;
#endif
	}

	private static Node3D EditorWaterRoot (GridMap grid)
	{
		if (grid.GetNodeOrNull ("TerrainWater") is Node3D existing)
		{
			return existing;
		}

		var root = new Node3D { Name = "TerrainWater" };
		grid.AddChild (root);
		root.Owner = null;
		return root;
	}

	public void RebuildTerrain ()
	{
		RebuildTerrainGrid ();
	}

	public bool RebuildTerrainGrid ()
	{
		if (!SphServer.Godot.Scripts.Util.ResPathIO.TryReadAllBytes (MapBinPath, out var mapBytes))
		{
			GD.PushError ($"TerrainGridFill: map not found: {MapBinPath}");
			return false;
		}

		var cells = MapFill.ReadFullGrid (mapBytes);
		var terrain = GetOrCreateTerrainGridMap ();
		// Navigation bake during open is slow. Set by name because the C# property differs across
		// Godot versions
		terrain.Set ("bake_navigation", false);
		var meshLib = LoadOrCreateMeshLibrary ();
		ClearMeshLibrary (meshLib);

		var uniqueNames = cells
			.Where (c => !c.IsEmpty)
			.Select (c => c.MasterName)
			.Distinct ()
			.OrderBy (n => n)
			.ToList ();

		var nameToItemId = new Dictionary<string, int> ();
		var nextId = 0;
		foreach (var masterName in uniqueNames)
		{
			var mesh = TryBuildTexturedMesh (masterName);
			if (mesh is null)
			{
				GD.PushWarning ($"TerrainGridFill: skipped tile (no mesh): {masterName}");
				continue;
			}

			meshLib.CreateItem (nextId);
			meshLib.SetItemMesh (nextId, mesh);
			meshLib.SetItemName (nextId, masterName);

			// The collider is the visual mesh, already basis-rotated, so cells collide where the
			// terrain is drawn. SetItemShapes wants [shape, transform] pairs
			var shape = mesh.CreateTrimeshShape ();
			if (shape is not null)
			{
				meshLib.SetItemShapes (nextId, [shape, Transform3D.Identity]);
			}
			else
			{
				GD.PushWarning ($"TerrainGridFill: failed to build collision shape for tile: {masterName}");
			}

			nameToItemId[masterName] = nextId;
			nextId++;
		}

		terrain.MeshLibrary = meshLib;
		SaveMeshLibrary (meshLib);

		// Cell corner origin (CellCenter* false). CellSize.Y stays small: CellCenterY lifts cells
		// by Y/2, about 50 m
		var cellSize = new Vector3 (TileSizeWorld, 1f, TileSizeWorld);
		terrain.CellSize = cellSize;
		terrain.CellCenterX = false;
		terrain.CellCenterY = false;
		terrain.CellCenterZ = false;
		terrain.Position = TerrainWorldOrigin;
		terrain.Clear ();

		for (var i = 0; i < cells.Count; i++)
		{
			var cell = cells[i];
			if (cell.IsEmpty)
			{
				continue;
			}

			if (!nameToItemId.TryGetValue (cell.MasterName, out var itemId))
			{
				continue;
			}

			var gx = MapFill.GridWidth - (i % MapFill.GridWidth) - 1;
			var gz = i / MapFill.GridWidth;
			terrain.SetCellItem (new Vector3I (gx, 0, gz), itemId, CellOrientation);
		}

		return true;
	}

	private GridMap GetOrCreateTerrainGridMap ()
	{
		if (GetNodeOrNull (TerrainNodeName) is GridMap existing)
		{
			return existing;
		}

		var grid = new GridMap { Name = TerrainNodeName };
		AddChild (grid);
		SetOwnerIfEditor (grid);
		return grid;
	}

	private MeshLibrary LoadOrCreateMeshLibrary ()
	{
		var path = ResolveMeshLibraryPath ();
		if (ResourceLoader.Exists (path) || File.Exists (path.Replace ('/', Path.DirectorySeparatorChar)))
		{
			var loaded = ResourceLoader.Load<MeshLibrary> (path);
			if (loaded is not null)
			{
				return loaded;
			}
		}

		return new MeshLibrary ();
	}

	private static void ClearMeshLibrary (MeshLibrary meshLib)
	{
		var ids = meshLib.GetItemList ();
		foreach (var id in ids)
		{
			meshLib.RemoveItem (id);
		}
	}

	private void SaveMeshLibrary (MeshLibrary meshLib)
	{
		var path = ResolveMeshLibraryPath ();
		var dir = Path.GetDirectoryName (path.Replace ('/', Path.DirectorySeparatorChar));
		if (!string.IsNullOrEmpty (dir))
		{
			Directory.CreateDirectory (dir);
		}

		var err = ResourceSaver.Save (meshLib, path);
		if (err != Error.Ok)
		{
			GD.PushError ($"TerrainGridFill: failed to save mesh library ({err}): {path}");
		}
	}

	private string ResolveMeshLibraryPath ()
	{
		if (string.IsNullOrWhiteSpace (MeshLibraryResourcePath)
			|| MeshLibraryResourcePath == "res://Godot/Terrain/TerrainMeshLibrary.tres")
		{
			return TerrainBakePaths.MeshLibraryTres;
		}

		if (MeshLibraryResourcePath.StartsWith ("res://"))
		{
			return ProjectSettings.GlobalizePath (MeshLibraryResourcePath).Replace ('\\', '/');
		}

		return MeshLibraryResourcePath.Replace ('\\', '/');
	}

	private Mesh? TryBuildTexturedMesh (string masterName)
	{
		var scene = LoadTileScene (masterName);
		if (scene is null)
		{
			return null;
		}

		var root = scene.Instantiate<Node> ();
		var meshInstance = FindFirstMeshInstance (root);
		var sourceMesh = meshInstance?.Mesh;
		if (sourceMesh is null)
		{
			root.QueueFree ();
			return null;
		}

		var mesh = (Mesh) sourceMesh.Duplicate ();
		root.QueueFree ();

		mesh = ApplyBasisRotationAfterImport (mesh, TileMeshBasisAfterImport ());

		var texture = TryLoadTexture (masterName);
		var surfaceCount = mesh.GetSurfaceCount ();
		for (var s = 0; s < surfaceCount; s++)
		{
			var mat = new StandardMaterial3D ();
			if (texture is not null)
			{
				mat.AlbedoTexture = texture;
			}

			mesh.SurfaceSetMaterial (s, mat);
		}

		return mesh;
	}

	/// <summary>
	/// Same as TerrainObjectsFill: euler (Pitch, -pi + Yaw, Roll), YXZ, then reflectZ * basis *
	/// reflectZ. MapFill.DefaultRotation is pitch, yaw, roll as X, Y, Z
	/// </summary>
	private static Basis TileMeshBasisAfterImport ()
	{
		var dr = MapFill.DefaultRotation;
		var euler = new Vector3 (dr.X, dr.Y, dr.Z);
		var basis = Basis.FromEuler (euler, EulerOrder.Yxz);
		var reflectZ = new Basis (Vector3.Right, Vector3.Up, new Vector3 (0f, 0f, -1f));
		return reflectZ * basis * reflectZ;
	}

	private static Mesh ApplyBasisRotationAfterImport (Mesh mesh, Basis basis)
	{
		if (mesh.GetSurfaceCount () == 0)
		{
			return mesh;
		}

		var outMesh = new ArrayMesh ();
		for (var s = 0; s < mesh.GetSurfaceCount (); s++)
		{
			var arrays = mesh.SurfaceGetArrays (s);
			var verts = (Vector3[]) arrays[(int) Mesh.ArrayType.Vertex];
			if (verts is not null)
			{
				for (var i = 0; i < verts.Length; i++)
				{
					verts[i] = basis * verts[i];
				}

				arrays[(int) Mesh.ArrayType.Vertex] = verts;
			}

			var normals = (Vector3[]) arrays[(int) Mesh.ArrayType.Normal];
			if (normals is not null)
			{
				for (var i = 0; i < normals.Length; i++)
				{
					normals[i] = (basis * normals[i]).Normalized ();
				}

				arrays[(int) Mesh.ArrayType.Normal] = normals;
			}

			var tangents = (float[]) arrays[(int) Mesh.ArrayType.Tangent];
			if (tangents is not null)
			{
				for (var i = 0; i < tangents.Length; i += 4)
				{
					var tv = basis * new Vector3 (tangents[i], tangents[i + 1], tangents[i + 2]);
					tangents[i] = tv.X;
					tangents[i + 1] = tv.Y;
					tangents[i + 2] = tv.Z;
				}

				arrays[(int) Mesh.ArrayType.Tangent] = tangents;
			}

			var prim = mesh is ArrayMesh am ? am.SurfaceGetPrimitiveType (s) : Mesh.PrimitiveType.Triangles;
			outMesh.AddSurfaceFromArrays (prim, arrays);
		}

		return outMesh;
	}

	private PackedScene? LoadTileScene (string baseName)
	{
		// On-disk tile and texture stems are lowercase. Map master names use Patch* casing
		var fileStem = baseName.ToLowerInvariant ();
		foreach (var ext in new[] { "scn", "blend", "glb", "gltf" })
		{
			var path = $"{TilesDirectory.TrimEnd ('/')}/{fileStem}.{ext}";
			if (!ResourceLoader.Exists (path))
			{
				continue;
			}

			return ResourceLoader.Load<PackedScene> (path);
		}

		return null;
	}

	private Texture2D? TryLoadTexture (string baseName)
	{
		var path = $"{TexturesDirectory.TrimEnd ('/')}/{baseName.ToLowerInvariant ()}.dds";
		if (!ResourceLoader.Exists (path))
		{
			return null;
		}

		return ResourceLoader.Load<Texture2D> (path);
	}

	private static MeshInstance3D? FindFirstMeshInstance (Node node)
	{
		if (node is MeshInstance3D mi)
		{
			return mi;
		}

		foreach (var child in node.GetChildren ())
		{
			if (child is Node childNode && FindFirstMeshInstance (childNode) is { } found)
			{
				return found;
			}
		}

		return null;
	}

	/// <summary>
	/// Editor-created nodes stay out of the Scene dock and the save unless Owner is the opened
	/// scene root
	/// </summary>
	private void SetOwnerIfEditor (Node node)
	{
		if (!Engine.IsEditorHint ())
		{
			return;
		}

		var root = GetTree ()?.EditedSceneRoot;
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
