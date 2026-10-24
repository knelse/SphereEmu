using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Terrain.Fill;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Headless: shared Shape3D per model in ObjectShapes/, and ObjectChunks/index.json keyed by
///     terrain cell. Cells match TerrainGroundIndex so the ground and object radii line up.
///     Run: <c>.\Tools\pack_object_collision_chunks.ps1</c>
/// </summary>
public partial class ObjectCollisionPackHeadless : Node
{
    public const string DefaultTerrainScenePath = "res://Godot/Scenes/terrain_scene.scn";
    public const string DefaultObjectDataDirectory = "res://Godot/Terrain/ObjectDataJson/";

    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    public static bool IsActive { get; private set; }

    public override async void _Ready ()
    {
        var options = ParseOptions ();
        if (options.ShowHelp)
        {
            PrintHelp ();
            Quit (ExitSuccess);
            return;
        }

        IsActive = true;
        try
        {
            // GlobalTransform needs the tree, and _Ready runs while the root sets up children
            await ToSignal (GetTree (), SceneTree.SignalName.ProcessFrame);
            Quit (await RunAsync (options));
        }
        catch (Exception ex)
        {
            GD.PushError ($"ObjectCollisionPackHeadless: failed: {ex}");
            Quit (ExitFailure);
        }
        finally
        {
            IsActive = false;
        }
    }

    private async Task<int> RunAsync (Options options)
    {
        TerrainBakePaths.EnsureBakeRoot ();
        TerrainBakePaths.EnsureDirectoryExactCase (TerrainBakePaths.ObjectShapesDir);
        TerrainBakePaths.EnsureDirectoryExactCase (TerrainBakePaths.ObjectChunksDir);

        using var frame = TerrainCollisionFrame.TryResolve (options.TerrainScenePath);
        if (frame is null)
        {
            GD.PushError ("ObjectCollisionPackHeadless: Terrain GridMap/TerrainObjects missing; cannot key cells.");
            return ExitFailure;
        }

        var placements = TerrainObjectPlacementSource.LoadAll (options.ObjectDataDirectory);
        GD.Print ($"ObjectCollisionPackHeadless: {placements.Count} placements, tileSize={frame.TileSize}");
        var frameBasis = frame.ToGridMapLocal.Basis;
        GD.Print (
            $"ObjectCollisionPackHeadless: objectsToGridMap "
            + $"x=({frameBasis.X.X:0.000},{frameBasis.X.Y:0.000},{frameBasis.X.Z:0.000}) "
            + $"y=({frameBasis.Y.X:0.000},{frameBasis.Y.Y:0.000},{frameBasis.Y.Z:0.000}) "
            + $"z=({frameBasis.Z.X:0.000},{frameBasis.Z.Y:0.000},{frameBasis.Z.Z:0.000}) "
            + $"o=({frame.ToGridMapLocal.Origin.X:0.00},{frame.ToGridMapLocal.Origin.Y:0.00},"
            + $"{frame.ToGridMapLocal.Origin.Z:0.00})");

        var shapes = new BakeShapes ();
        var cells = new SortedDictionary<(int Gx, int Gz), List<ObjectCollisionChunkIndex.CollisionPart>> ();
        var keptParts = 0;
        var noCollider = 0;
        var markerSkips = 0;

        foreach (var placement in placements)
        {
            if (!TerrainObjectPlacementSource.IsBakePlacement (placement))
            {
                continue;
            }

            // A char*/edoor/pyram1 model the client only lists as a marker, and a spot that is a
            // marker of a model spawned elsewhere, spawn no scene object on the client, so a box
            // baked from the .mdl blocks ground that is walkable there
            if (ClientPlacementMarkers.IsMarkerOnly (placement.ObjectName, placement.WorldTransform.Origin))
            {
                markerSkips++;
                continue;
            }

            // Cell key and part transforms both use the GridMap-local frame, the frame the runtime
            // parents cells in
            var gridPlacement = frame.ToGridMapLocal * placement.WorldTransform;

            // The client blocks kind 0 on model bounds added to the object position, so the
            // placement rotation stays out, and kind 1 on a box that turns with the placement. The
            // baked trimesh turns for both, so a kind 0 tree blocks where the client does not
            var kind = ClientModelCollision.Kind (placement.ObjectName);
            var wantsBox = kind is ClientModelCollision.AxisAlignedBounds or ClientModelCollision.OrientedBounds;
            List<(string Stem, Transform3D Local)> modelParts;
            Transform3D placementFrame;
            if (wantsBox && shapes.GetBoxPart (placement.ObjectName, out var boxLocal) is { } boxStem)
            {
                modelParts = [(boxStem, boxLocal)];
                placementFrame = kind == ClientModelCollision.AxisAlignedBounds
                    ? new Transform3D (Basis.Identity, gridPlacement.Origin)
                    : gridPlacement;
            }
            else
            {
                modelParts = shapes.GetModelParts (placement.ObjectName).Parts;
                placementFrame = gridPlacement;
                if (modelParts.Count == 0)
                {
                    noCollider++;
                    continue;
                }
            }

            var cell = frame.GridMap.LocalToMap (gridPlacement.Origin);
            // The runtime puts the cell node at MapToLocal (gx, 0, gz), so parts are stored
            // relative to that origin
            var cellOrigin = frame.GridMap.MapToLocal (new Vector3I (cell.X, 0, cell.Z));
            var toCell = new Transform3D (Basis.Identity, -cellOrigin);
            if (!cells.TryGetValue ((cell.X, cell.Z), out var list))
            {
                list = [];
                cells[(cell.X, cell.Z)] = list;
            }

            foreach (var (stem, local) in modelParts)
            {
                var partTransform = placementFrame * local;
                list.Add (new ObjectCollisionChunkIndex.CollisionPart
                {
                    ShapeName = stem,
                    Transform = ObjectCollisionChunkIndex.CollisionPart.FromTransform (
                        toCell * partTransform),
                });
                keptParts++;
            }
        }

        var shapesWritten = shapes.WriteShapes ();
        var indexPath = TerrainBakePaths.ObjectChunkIndexJson;
        ObjectCollisionChunkIndex.Save (indexPath, frame.TileSize, cells);

        GD.Print (
            $"ObjectCollisionPackHeadless: shapes={shapesWritten}/{shapes.Names.Count} cells={cells.Count} "
            + $"parts={keptParts} modelsNoCollider={noCollider} markerPlacements={markerSkips}");
        GD.Print ($"ObjectCollisionPackHeadless: index={indexPath}");

        await Task.CompletedTask;
        return ExitSuccess;
    }

    private static Options ParseOptions ()
    {
        var options = new Options ();
        var args = OS.GetCmdlineUserArgs ();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--terrain-scene":
                    if (i + 1 < args.Length)
                    {
                        options.TerrainScenePath = args[++i];
                    }

                    break;
                case "--object-data":
                    if (i + 1 < args.Length)
                    {
                        options.ObjectDataDirectory = args[++i];
                    }

                    break;
            }
        }

        return options;
    }

    private static void PrintHelp ()
    {
        GD.Print (
            """
			ObjectCollisionPackHeadless options (after --):
			  --terrain-scene <path>   Default res://Godot/Scenes/terrain_scene.scn
			  --object-data <dir>      Default res://Godot/Terrain/ObjectDataJson/
			  --help
			""");
    }

    private void Quit (int code) => GetTree ().Quit (code);

    private sealed class Options
    {
        public string TerrainScenePath { get; set; } = DefaultTerrainScenePath;
        public string ObjectDataDirectory { get; set; } = DefaultObjectDataDirectory;
        public bool ShowHelp { get; set; }
    }

    /// <summary>
    /// One shape file per model part, plus the model name -> parts map
    /// </summary>
    private sealed class BakeShapes
    {
        public List<string> Names { get; } = [];

        private readonly List<Shape3D> shapes = [];
        private readonly Dictionary<string, List<(string Stem, Transform3D Local)>> byModel = new (StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> boxByModel = new (StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> tried = new (StringComparer.OrdinalIgnoreCase);

        public record struct Model (List<(string Stem, Transform3D Local)> Parts);

        public Model GetModelParts (string objectName)
        {
            if (byModel.TryGetValue (objectName, out var cached))
            {
                return new Model (cached);
            }

            if (tried.Contains (objectName))
            {
                return new Model ([]);
            }

            tried.Add (objectName);
            var parts = BuildModelParts (objectName);
            byModel[objectName] = parts;
            return new Model (parts);
        }

        public int WriteShapes ()
        {
            var written = 0;
            for (var i = 0; i < Names.Count; i++)
            {
                var path = TerrainBakePaths.ObjectShapeRes (Names[i]);
                var err = ResourceSaver.Save (shapes[i], path);
                if (err != Error.Ok)
                {
                    GD.PushWarning ($"ObjectCollisionPackHeadless: save shape failed ({err}): {path}");
                    continue;
                }

                written++;
            }

            return written;
        }

        /// <summary>
        /// BoxShape3D stem for a model the client blocks on with a box, the box in the model's own
        /// frame, from the .mdl. Null when the model has no box there
        /// </summary>
        public string? GetBoxPart (string objectName, out Transform3D local)
        {
            local = Transform3D.Identity;
            if (boxByModel.TryGetValue (objectName, out var stem))
            {
                return stem;
            }

            // The client's own box, not the visual AABB: char*.mdl is 0.6 by 1.8 by 0.6, a capsule,
            // while the tree visual AABB is a 10 by 13 by 10 block
            if (!ClientModelCollision.TryGetBox (objectName, out var size, out var center))
            {
                return null;
            }

            var boxStem = $"{objectName}_box";
            boxByModel[objectName] = boxStem;
            Names.Add (boxStem);
            shapes.Add (new BoxShape3D { Size = size });
            local = new Transform3D (Basis.Identity, center);
            return boxStem;
        }

        /// <summary>
        /// Authored StaticBody3D shapes, else a trimesh of the visual mesh. The 90 models with no
        /// authored shape are all decorative names ShouldSkipMeshObstruction drops
        /// </summary>
        private List<(string Stem, Transform3D Local)> BuildModelParts (string objectName)
        {
            var path = GlbModelPaths.Resolve (objectName);
            if (path is null || !ResourceLoader.Exists (path))
            {
                return [];
            }

            var scene = ResourceLoader.Load<PackedScene> (path);
            if (scene is null)
            {
                return [];
            }

            var root = scene.Instantiate<Node3D> ();
            try
            {
                var parts = CollectAuthoredShapes (root, root);
                if (parts.Count == 0)
                {
                    parts = CollectMeshTrimeshes (root, root);
                }

                for (var i = 0; i < parts.Count; i++)
                {
                    var stem = parts.Count == 1 ? objectName : $"{objectName}_{i}";
                    Names.Add (stem);
                    shapes.Add (parts[i].Shape);
                }

                var stems = new List<(string, Transform3D)> ();
                var offset = Names.Count - parts.Count;
                for (var i = 0; i < parts.Count; i++)
                {
                    stems.Add ((Names[offset + i], parts[i].Local));
                }

                return stems;
            }
            finally
            {
                root.Free ();
            }
        }

        private static List<(Shape3D Shape, Transform3D Local)> CollectAuthoredShapes (Node node, Node3D root)
        {
            var parts = new List<(Shape3D, Transform3D)> ();
            if (node is StaticBody3D)
            {
                foreach (var child in node.GetChildren ())
                {
                    if (child is CollisionShape3D { Shape: { } shape } collision)
                    {
                        parts.Add ((shape, RelativeToRoot (collision, root)));
                    }
                }
            }

            foreach (var child in node.GetChildren ())
            {
                parts.AddRange (CollectAuthoredShapes (child, root));
            }

            return parts;
        }

        private static List<(Shape3D Shape, Transform3D Local)> CollectMeshTrimeshes (Node node, Node3D root)
        {
            var parts = new List<(Shape3D, Transform3D)> ();
            if (node is MeshInstance3D { Mesh: { } mesh } meshInstance && !HasSkeletonAncestor (meshInstance))
            {
                var shape = mesh.CreateTrimeshShape ();
                if (shape is not null)
                {
                    parts.Add ((shape, RelativeToRoot (meshInstance, root)));
                }
            }

            foreach (var child in node.GetChildren ())
            {
                parts.AddRange (CollectMeshTrimeshes (child, root));
            }

            return parts;
        }

        private static bool HasSkeletonAncestor (Node node)
        {
            var parent = node.GetParent ();
            while (parent is not null)
            {
                if (parent is Skeleton3D)
                {
                    return true;
                }

                parent = parent.GetParent ();
            }

            return false;
        }

        private static Transform3D RelativeToRoot (Node3D node, Node3D root)
        {
            var transform = Transform3D.Identity;
            var current = node;
            while (!ReferenceEquals (current, root) && current is not null)
            {
                transform = current.Transform * transform;
                current = current.GetParent () as Node3D;
            }

            return transform;
        }
    }
}
