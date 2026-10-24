using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Headless check of the runtime chain: the streamer finds the GridMap, parents cells at
/// MapToLocal, and puts the baked shapes into the physics world. Queries the space state for
/// layer-1 bodies near a drawn placement. Run: <c>.\Tools\check_object_collision_stream.ps1</c>
/// </summary>
public partial class ObjectCollisionStreamCheck : Node
{
    private const string TerrainScenePath = "res://Godot/Scenes/terrain_scene.scn";

    /// <summary>
    /// Building shape centroids sit in courtyards, so the probe has to reach the walls
    /// </summary>
    private const float ProbeRadius = 25f;

    /// <summary>
    /// Cells near this GridMap cell are sampled, the middle of the map
    /// </summary>
    private const int SampleCell = 40;

    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    public override void _Ready ()
    {
        // The streamer and terrain scene need the tree for GlobalTransform and World3D
        SetProcess (true);
    }

    public override void _Process (double delta)
    {
        SetProcess (false);
        GetTree ().CallDeferred (SceneTree.MethodName.Quit, Run ());
    }

    private int Run ()
    {
        if (ResourceLoader.Load<PackedScene> (TerrainScenePath) is not { } packed
            || packed.Instantiate<Node3D> () is not { } terrainScene)
        {
            GD.PrintErr ("StreamCheck: terrain scene missing");
            return ExitFailure;
        }

        AddChild (terrainScene);
        var streamer = new WorldObjectCollisionStreamer { Name = "StreamCheckStreamer" };
        AddChild (streamer);

        var index = ObjectCollisionChunkIndex.Load (TerrainBakePaths.ObjectChunkIndexJson);
        if (index.CellCount == 0)
        {
            GD.PrintErr ("StreamCheck: index empty. Run Tools/pack_object_collision_chunks.ps1");
            return ExitFailure;
        }

        if (!TryProbePoint (index, out var probe))
        {
            GD.PrintErr ("StreamCheck: no trimesh placement near the sample cell");
            return ExitFailure;
        }

        streamer.EnsureAtWorldPosition (probe);

        // The streamer parents its cells under the GridMap, so count from the terrain scene
        var bodies = CountStaticBodies (terrainScene);
        var space = GetTree ().Root.GetWorld3D ()?.DirectSpaceState;
        if (space is null)
        {
            GD.PrintErr ("StreamCheck: no DirectSpaceState");
            return ExitFailure;
        }

        var hits = space.IntersectShape (
            new PhysicsShapeQueryParameters3D
            {
                Shape = new SphereShape3D { Radius = ProbeRadius },
                Transform = new Transform3D (Basis.Identity, probe),
                CollisionMask = 1,
            },
            8);

        GD.Print (
            $"StreamCheck: cells={streamer.LoadedCellCount} bodies={bodies} hits={hits.Count} "
            + $"probe=({probe.X:0.0},{probe.Y:0.0},{probe.Z:0.0})");
        return bodies > 0 && hits.Count > 0 ? ExitSuccess : ExitFailure;
    }

    /// <summary>
    /// Center of a baked trimesh near the sample cell, in world, where a layer-1 body must exist
    /// </summary>
    private static bool TryProbePoint (ObjectCollisionChunkIndex index, out Vector3 probe)
    {
        probe = Vector3.Zero;
        using var frame = TerrainCollisionFrame.TryResolve (TerrainScenePath);
        if (frame is null)
        {
            return false;
        }

        foreach (var placement in TerrainObjectPlacementSource.LoadAll ("res://Godot/Terrain/ObjectDataJson/"))
        {
            if (!TerrainObjectPlacementSource.IsCollidablePlacement (placement))
            {
                continue;
            }

            var cell = frame.GridMap.LocalToMap (frame.ToGridMapLocal * placement.WorldTransform.Origin);
            if (Mathf.Abs (cell.X - SampleCell) > 1 || Mathf.Abs (cell.Z - SampleCell) > 1)
            {
                continue;
            }

            if (!index.TryGetCell (cell.X, cell.Z, out var parts))
            {
                continue;
            }

            var cellOrigin = frame.GridMap.MapToLocal (new Vector3I (cell.X, 0, cell.Z));
            foreach (var part in parts)
            {
                if (ResourceLoader.Load<Shape3D> (part.ShapePath) is not ConcavePolygonShape3D concave)
                {
                    continue;
                }

                var faces = concave.GetFaces ();
                if (faces.Length == 0)
                {
                    continue;
                }

                probe = frame.GridGlobal * (cellOrigin + part.ToTransform () * FaceCenter (faces));
                return true;
            }
        }

        return false;
    }

    private static Vector3 FaceCenter (Vector3[] faces)
    {
        var sum = Vector3.Zero;
        foreach (var v in faces)
        {
            sum += v;
        }

        return sum / faces.Length;
    }

    private static int CountStaticBodies (Node root)
    {
        var count = 0;
        var stack = new Stack<Node> ();
        stack.Push (root);
        while (stack.Count > 0)
        {
            var node = stack.Pop ();
            if (node is StaticBody3D)
            {
                count++;
            }

            foreach (var child in node.GetChildren ())
            {
                stack.Push (child);
            }
        }

        return count;
    }
}
