using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Headless check: the frame ObjectCollisionPackHeadless baked with must equal the pair
/// WorldObjectCollisionStreamer reads from the live scene under MainServer's TerrainScene transform.
/// The shared parent cancels in gridGlobalInv * objectsGlobal, and this asserts it.
/// Run: <c>.\Tools\check_object_collision_frame.ps1</c>
/// </summary>
public partial class ObjectCollisionFrameCheck : Node
{
    private const string TerrainScenePath = "res://Godot/Scenes/terrain_scene.scn";
    private const string MainScenePath = "res://Godot/Scenes/MainServer.tscn";
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    public override void _Ready ()
    {
        // AddChild is refused while the tree root sets up children, so the check runs on frame 1
        SetProcess (true);
    }

    public override void _Process (double delta)
    {
        SetProcess (false);
        GetTree ().CallDeferred (SceneTree.MethodName.Quit, Run ());
    }

    private int Run ()
    {
        if (ResourceLoader.Load<PackedScene> (TerrainScenePath) is not { } packed)
        {
            GD.PrintErr ("FrameCheck: terrain scene missing");
            return ExitFailure;
        }

        if (ResourceLoader.Load<PackedScene> (MainScenePath) is not { } mainScene
            || !SceneStateTransforms.TryGetInstance (mainScene, TerrainScenePath, out var sceneParent))
        {
            GD.PrintErr ("FrameCheck: MainServer TerrainScene instance not found");
            return ExitFailure;
        }

        Print ("mainTerrainScene", sceneParent);

        // The live game: terrain scene under MainServer's rotated TerrainScene node
        var live = packed.Instantiate<Node3D> ();
        live.Transform = sceneParent * live.Transform;
        AddChild (live);

        if (live.FindChild (Fill.TerrainGridFill.TerrainNodeName, true, false) is not GridMap grid
            || live.FindChild (Fill.TerrainObjectsFill.NodeName, true, false) is not Node3D objects)
        {
            GD.PrintErr ("FrameCheck: live GridMap/TerrainObjects missing");
            return ExitFailure;
        }

        var runtime = grid.GlobalTransform.AffineInverse () * objects.GlobalTransform;
        using var baked = TerrainCollisionFrame.TryResolve (TerrainScenePath);
        if (baked is null)
        {
            GD.PrintErr ("FrameCheck: bake frame null");
            return ExitFailure;
        }

        Print ("live", runtime);
        Print ("baked", baked.ToGridMapLocal);
        var delta = runtime.Origin.DistanceTo (baked.ToGridMapLocal.Origin);
        GD.Print ($"FrameCheck: originDelta={delta:0.000}");

        // The nav bake documents Ry(-90) about world origin, then (4000, 0, 4000)
        var documented = new Transform3D (new Basis (Vector3.Up, -Mathf.Pi / 2f), new Vector3 (4000f, 0f, 4000f));
        Print ("navDoc", documented);
        GD.Print ($"FrameCheck: docOriginDelta={documented.Origin.DistanceTo (runtime.Origin):0.000}");

        return delta < 0.01f ? ExitSuccess : ExitFailure;
    }

    private static void Print (string label, Transform3D transform)
    {
        var b = transform.Basis;
        GD.Print (
            $"FrameCheck {label}: x=({b.X.X:0.000},{b.X.Y:0.000},{b.X.Z:0.000}) "
            + $"y=({b.Y.X:0.000},{b.Y.Y:0.000},{b.Y.Z:0.000}) "
            + $"z=({b.Z.X:0.000},{b.Z.Y:0.000},{b.Z.Z:0.000}) "
            + $"o=({transform.Origin.X:0.00},{transform.Origin.Y:0.00},{transform.Origin.Z:0.00})");
    }
}
