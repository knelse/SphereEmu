using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Headless check: baked parts land where TerrainObjectsFill draws the same placements. Per cell it
/// compares the baked part centroid to the placement centroid, in world. A frame rotation bug moves
/// these hundreds of meters apart, so 25 m is enough margin.
/// Run: <c>.\Tools\check_object_collision_bake.ps1</c>
/// </summary>
public partial class ObjectCollisionBakeCheck : Node
{
    private const string TerrainScenePath = "res://Godot/Scenes/terrain_scene.scn";

    /// <summary>
    /// gx % this == 0 picks cells spread over the map, without a random order
    /// </summary>
    private const int CellStride = 53;

    private const float MaxCentroidDistance = 25f;
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    public override void _Ready ()
    {
        // AddChild is refused while the tree root sets up children
        SetProcess (true);
    }

    public override void _Process (double delta)
    {
        SetProcess (false);
        GetTree ().CallDeferred (SceneTree.MethodName.Quit, Run ());
    }

    private int Run ()
    {
        using var frame = TerrainCollisionFrame.TryResolve (TerrainScenePath);
        if (frame is null)
        {
            GD.PrintErr ("BakeCheck: frame null");
            return ExitFailure;
        }

        var index = ObjectCollisionChunkIndex.Load (TerrainBakePaths.ObjectChunkIndexJson);
        if (index.CellCount == 0)
        {
            GD.PrintErr ("BakeCheck: index empty. Run Tools/pack_object_collision_chunks.ps1");
            return ExitFailure;
        }

        var visualOrigin = new Dictionary<(int Gx, int Gz), Vector3Sum> ();
        foreach (var placement in TerrainObjectPlacementSource.LoadAll ("res://Godot/Terrain/ObjectDataJson/"))
        {
            if (!TerrainObjectPlacementSource.IsCollidablePlacement (placement))
            {
                continue;
            }

            var cell = frame.GridMap.LocalToMap (frame.ToGridMapLocal * placement.WorldTransform.Origin);
            if (!visualOrigin.TryGetValue ((cell.X, cell.Z), out var sum))
            {
                sum = new Vector3Sum ();
                visualOrigin[(cell.X, cell.Z)] = sum;
            }

            sum.Add (frame.ToWorld * placement.WorldTransform.Origin);
        }

        var worst = 0f;
        (int, int) worstCell = default;
        var checkedCells = 0;
        foreach (var (gx, gz) in index.EnumerateCells ())
        {
            if (gx % CellStride != 0 || !index.TryGetCell (gx, gz, out var parts) || parts.Count == 0)
            {
                continue;
            }

            if (!visualOrigin.TryGetValue ((gx, gz), out var origins) || origins.Count == 0)
            {
                continue;
            }

            if (!TryBakedCentroid (frame, gx, gz, parts, out var bakedCenter, out var partCount) || partCount == 0)
            {
                continue;
            }

            var distance = bakedCenter.DistanceTo (origins.Center ());
            checkedCells++;
            if (distance > worst)
            {
                worst = distance;
                worstCell = (gx, gz);
            }
        }

        GD.Print (
            $"BakeCheck: cells={checkedCells} worstCell={worstCell} worstCentroid={worst:0.00} "
            + $"limit={MaxCentroidDistance:0.0}");
        return checkedCells > 0 && worst <= MaxCentroidDistance ? ExitSuccess : ExitFailure;
    }

    /// <summary>
    /// Cell parts reach world through GridGlobal * (cellOrigin + part), the same chain the runtime
    /// StaticBody3D gets from the GridMap-parented collision root
    /// </summary>
    private static bool TryBakedCentroid (
        TerrainCollisionFrame frame,
        int gx,
        int gz,
        IReadOnlyList<ObjectCollisionChunkIndex.CollisionPart> parts,
        out Vector3 centroid,
        out int count)
    {
        centroid = Vector3.Zero;
        count = 0;
        var cellOrigin = frame.GridMap.MapToLocal (new Vector3I (gx, 0, gz));
        foreach (var part in parts)
        {
            if (ResourceLoader.Load<Shape3D> (part.ShapePath) is not ConcavePolygonShape3D concave)
            {
                continue;
            }

            var shapeCenter = FaceCenter (concave);
            centroid += frame.GridGlobal * (cellOrigin + part.ToTransform () * shapeCenter);
            count++;
        }

        if (count == 0)
        {
            return false;
        }

        centroid /= count;
        return true;
    }

    private static Vector3 FaceCenter (ConcavePolygonShape3D shape)
    {
        var faces = shape.GetFaces ();
        if (faces.Length == 0)
        {
            return Vector3.Zero;
        }

        var sum = Vector3.Zero;
        foreach (var v in faces)
        {
            sum += v;
        }

        return sum / faces.Length;
    }

    private sealed class Vector3Sum
    {
        private Vector3 sum;

        public int Count { get; private set; }

        public void Add (Vector3 value)
        {
            sum += value;
            Count++;
        }

        public Vector3 Center () => Count > 0 ? sum / Count : Vector3.Zero;
    }
}
