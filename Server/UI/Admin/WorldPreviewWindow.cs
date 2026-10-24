using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using SphServer.Client;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.World;
using SphServer.Server.Config;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Server.UI.Admin;

/// <summary>
/// Native 1920x1080 window rendering the live server world. The SubViewport shares the main World3D,
/// so terrain, placements, monsters and players are the server's own nodes. Collider overlays ride on
/// visibility layer 2, which only this camera's cull mask includes, and show only what the eye sees:
/// frustum test, then a ray against the blocking layer
/// </summary>
public partial class WorldPreviewWindow : Window
{
    /// <summary>
    /// Overlay geometry layer, so the overlays stay out of every other viewport
    /// </summary>
    private const uint OverlayVisibilityLayer = 1u << 1;

    private const uint WorldVisibilityLayer = 1u;

    // Physics layer the sweep masks, so the occlusion ray answers "can the eye reach this"
    private const uint BlockingPhysicsLayer = 1u;

    // PlayerMotion sweeps this box, so the player overlay is the swept box itself
    private const string PlayerShapeName = "ClientShape";

    private const float DefaultPitch = 0.5f;
    private const float DefaultDistance = 30f;
    private const float MinDistance = 1f;
    private const float MaxDistance = 600f;

    // Godot frames a focused node by AABB, so a 1.8 m body lands about this far out
    private const float FocusDistance = 3f;

    private const float FlightMetersPerSecond = 15f;
    private const float FlightTurbo = 4f;

    // Streaming moves shapes in and out, so the overlay set refreshes on a timer
    private const float OverlayRefreshSeconds = 1f;

    private const float WorldRefreshSeconds = 0.5f;

    // Frustum culling is cheap, and a lag of a quarter second on a moving camera reads as smooth
    private const float VisibilityRefreshSeconds = 0.25f;

    // Colliders past this are out of the streamed world anyway
    private const float OverlayMaxRangeMeters = 220f;

    // The pose goes to the log on this beat, and only once the eye has actually moved
    private const float CameraLogSeconds = 1f;
    private const float CameraLogMoveMeters = 0.25f;
    private const float CameraLogTurnRadians = 0.02f;

    // A pick walks the overlays the visibility pass already proved visible
    private const int PickMaxDistanceMeters = 500;

    // Shape3D.GetDebugMesh builds a new ArrayMesh from the shape's faces on every call, so one mesh
    // per shape is kept and reused across rebuilds
    private readonly Dictionary<Shape3D, Mesh> debugMeshByShape = [];

    // The pass costs go to the pose log, so a hitch names its phase
    private double lastGroundMs;
    private double lastObjectMs;
    private double lastAlphaMs;
    private double lastRebuildMs;
    private double lastVisibilityMs;
    private int lastDebugMeshBuilt;

    // 1.0 keeps the world in the opaque queue. Transparent geometry does not write depth and sorts
    // per frame, which flickers as the camera moves
    private const float WorldAlpha = 1f;

    // Collider fills stay light, so thousands of boxes do not wash out the geometry under them
    private const float ColliderAlpha = 0.2f;

    // A collider flush on the surface it mirrors z-fights with it, so it grows slightly past that
    // surface and the depth test passes on its near face
    private const float OverlayOutset = 1.02f;

    private SubViewport? previewViewport;
    private SubViewportContainer? viewportContainer;
    private Camera3D? camera;
    private Node3D? overlayRoot;
    private Label? statusLabel;
    private CheckButton? objectColliders;
    private CheckButton? groundColliders;
    private CheckButton? playerColliders;
    private CheckButton? actorColliders;
    private CheckButton? placementColliders;

    // Keyed by the source the overlay follows: a CollisionShape3D, or the object whose client box
    // it mirrors
    private readonly Dictionary<Node, OverlayEntry> overlays = [];
    private readonly HashSet<Node> seenThisPass = [];
    private readonly List<Node> stale = [];
    private readonly Dictionary<Vector3, BoxMesh> boxMeshBySize = [];

    private Vector3 focus;
    private Vector3 wantedFocus;
    private float yaw;
    private float pitch = DefaultPitch;
    private float distance = DefaultDistance;
    private float wantedDistance = DefaultDistance;
    private Guid focusClientId;
    private bool orbiting;
    private bool panning;
    private float overlayElapsed = OverlayRefreshSeconds;
    private float visibilityElapsed = VisibilityRefreshSeconds;
    private float worldElapsed = WorldRefreshSeconds;
    private float cameraLogElapsed = CameraLogSeconds;
    private Vector3 loggedEye = Vector3.Zero;
    private float loggedYaw;
    private float loggedPitch;
    private int lastShown;

    /// <summary>
    /// Show the window and start rendering
    /// </summary>
    public void Open ()
    {
        Show ();
    }

    /// <summary>
    /// Pull the camera onto this player and keep following it
    /// </summary>
    public void FocusClient (Guid clientId)
    {
        focusClientId = clientId;
    }

    public override void _Ready ()
    {
        Title = "World Preview";
        Size = new Vector2I (1920, 1080);
        MinSize = new Vector2I (640, 480);
        InitialPosition = WindowInitialPosition.CenterScreenWithMouseFocus;
        Unresizable = false;
        Exclusive = false;
        Transient = true;
        Visible = false;
        CloseRequested += Hide;
        VisibilityChanged += () => SetRendering (Visible);

        BuildUi ();
        BuildPreviewWorld ();
        focus = wantedFocus = SpawnWorldPosition ();
    }

    public override void _ExitTree ()
    {
        var collision = SphereServer.ServerNode?.ObjectCollision;
        if (collision is not null)
        {
            collision.KeepLoadedPositions.Clear ();
        }
    }

    public override void _Process (double delta)
    {
        if (!Visible)
        {
            return;
        }

        var seconds = (float) delta;
        TrackFocusedClient ();

        // An eased focus keeps the eye direction and only closes distance, like editor focus
        var t = 1f - Mathf.Exp (-9f * seconds);
        focus = focus.Lerp (wantedFocus, t);
        distance = Mathf.Lerp (distance, wantedDistance, t);
        Fly (seconds);
        UpdateCamera ();

        worldElapsed += seconds;
        if (worldElapsed >= WorldRefreshSeconds)
        {
            worldElapsed = 0f;
            // The streams build and upload meshes, the alpha walks the whole tree, and the log
            // names which one stalls the tick
            var groundStart = Time.GetTicksMsec ();
            SphereServer.ServerNode?.TerrainGround?.EnsureAroundWorldPosition (focus);
            lastGroundMs = Time.GetTicksMsec () - groundStart;

            var objectStart = Time.GetTicksMsec ();
            var collision = SphereServer.ServerNode?.ObjectCollision;
            if (collision is not null)
            {
                collision.KeepLoadedPositions.Clear ();
                collision.KeepLoadedPositions.Add (focus);
                collision.EnsureAtWorldPosition (focus);
            }

            lastObjectMs = Time.GetTicksMsec () - objectStart;

            // Streaming brings in cells with materials never seen, and those need the alpha too
            var alphaStart = Time.GetTicksMsec ();
            ApplyWorldAlpha ();
            lastAlphaMs = Time.GetTicksMsec () - alphaStart;
        }

        overlayElapsed += seconds;
        if (overlayElapsed >= OverlayRefreshSeconds)
        {
            overlayElapsed = 0f;
            visibilityElapsed = VisibilityRefreshSeconds;
            var rebuildStart = Time.GetTicksMsec ();
            RebuildColliderOverlays ();
            lastRebuildMs = Time.GetTicksMsec () - rebuildStart;
        }

        cameraLogElapsed += seconds;
        if (cameraLogElapsed >= CameraLogSeconds)
        {
            cameraLogElapsed = 0f;
            LogCameraPose ();
        }

        visibilityElapsed += seconds;
        if (visibilityElapsed >= VisibilityRefreshSeconds)
        {
            visibilityElapsed = 0f;
            UpdateOverlayVisibility ();
        }
    }

    public override void _UnhandledKeyInput (InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && Visible)
        {
            Hide ();
        }
    }

    private void BuildUi ()
    {
        var body = new VBoxContainer ();
        body.SetAnchorsAndOffsetsPreset (Control.LayoutPreset.FullRect);
        body.AddThemeConstantOverride ("separation", 4);
        AddChild (body);

        var toolbar = new HBoxContainer ();
        toolbar.AddThemeConstantOverride ("separation", 12);
        body.AddChild (toolbar);

        objectColliders = new CheckButton { Text = "Object colliders", ButtonPressed = true };
        placementColliders = new CheckButton { Text = "Placement colliders", ButtonPressed = true };
        actorColliders = new CheckButton { Text = "Actor colliders", ButtonPressed = true };
        playerColliders = new CheckButton { Text = "Player colliders", ButtonPressed = true };
        groundColliders = new CheckButton { Text = "Ground colliders" };
        foreach (var box in new[] { objectColliders, placementColliders, actorColliders, playerColliders,
                 groundColliders })
        {
            box.Toggled += _ =>
            {
                RebuildColliderOverlays ();
                UpdateOverlayVisibility ();
            };
            toolbar.AddChild (box);
        }

        toolbar.AddChild (new Label
        {
            Text = "LMB pick names the overlay, RMB orbit, MMB pan, wheel zoom, WASD + QE fly, Shift turbo, Esc hides"
        });

        viewportContainer = new SubViewportContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        viewportContainer.GuiInput += OnPreviewGuiInput;
        body.AddChild (viewportContainer);

        statusLabel = new Label ();
        body.AddChild (statusLabel);
    }

    private void BuildPreviewWorld ()
    {
        if (viewportContainer is null)
        {
            return;
        }

        previewViewport = new SubViewport
        {
            // The server's own world, so the preview shows the nodes the server simulates
            World3D = GetTree ().Root.GetWorld3D (),
            HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Msaa3D = Viewport.Msaa.Msaa4X
        };
        viewportContainer.AddChild (previewViewport);

        var scene = new Node3D { Name = "Scene" };
        previewViewport.AddChild (scene);

        // MainServer ships no light, so the preview brings its own
        scene.AddChild (new DirectionalLight3D
        {
            Rotation = new Vector3 (-0.8f, 0.6f, 0f),
            ShadowEnabled = true,
            LightEnergy = 0.9f
        });
        scene.AddChild (new DirectionalLight3D
        {
            Rotation = new Vector3 (-0.2f, 2.4f, 0f),
            LightEnergy = 0.3f
        });

        overlayRoot = new Node3D { Name = "ColliderOverlays" };
        scene.AddChild (overlayRoot);

        camera = new Camera3D
        {
            Current = true,
            Fov = 55f,
            Near = 0.1f,
            Far = 1200f,
            CullMask = WorldVisibilityLayer | OverlayVisibilityLayer,
            Environment = PreviewEnvironment ()
        };
        scene.AddChild (camera);
    }

    private static global::Godot.Environment PreviewEnvironment ()
    {
        return new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = new Color (0.16f, 0.17f, 0.2f),
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color (0.55f, 0.55f, 0.58f),
            AmbientLightEnergy = 0.7f
        };
    }

    private void SetRendering (bool rendering)
    {
        if (previewViewport is null)
        {
            return;
        }

        previewViewport.RenderTargetUpdateMode = rendering
            ? SubViewport.UpdateMode.Always
            : SubViewport.UpdateMode.Disabled;

        // The materials are shared with the main window, so the world is only translucent while
        // this window renders
        if (rendering)
        {
            ApplyWorldAlpha ();
        }
        else
        {
            WorldTransparency.Restore ();
        }
    }

    private void ApplyWorldAlpha ()
    {
        if (GetTree ()?.Root is { } root)
        {
            // The overlays carry their own material, and thousands of them are not the world
            WorldTransparency.Apply (root, WorldAlpha, overlayRoot);
        }
    }

    private void OnPreviewGuiInput (InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left && button.Pressed)
            {
                PickOverlay (button.Position);
                GetViewport ().SetInputAsHandled ();
                return;
            }

            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
            {
                wantedDistance = Mathf.Max (MinDistance, wantedDistance * 0.85f);
            }
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
            {
                wantedDistance = Mathf.Min (MaxDistance, wantedDistance * 1.15f);
            }
            else if (button.ButtonIndex == MouseButton.Right)
            {
                orbiting = button.Pressed;
            }
            else if (button.ButtonIndex == MouseButton.Middle)
            {
                panning = button.Pressed;
            }
            else
            {
                return;
            }

            GetViewport ().SetInputAsHandled ();
            return;
        }

        if (inputEvent is not InputEventMouseMotion motion)
        {
            return;
        }

        if (orbiting)
        {
            yaw -= motion.Relative.X * 0.005f;
            pitch = Mathf.Clamp (pitch + motion.Relative.Y * 0.005f, -1.4f, 1.4f);
        }
        else if (panning)
        {
            Pan (motion.Relative);
        }
        else
        {
            return;
        }

        GetViewport ().SetInputAsHandled ();
    }

    private void Pan (Vector2 relative)
    {
        if (camera is null)
        {
            return;
        }

        var basis = camera.GlobalTransform.Basis;
        wantedFocus += (-basis.X * relative.X + basis.Y * relative.Y) * (distance * 0.0015f);
    }

    private void Fly (float seconds)
    {
        if (camera is null || !MouseOverViewport ())
        {
            return;
        }

        var wish = Vector3.Zero;
        if (Input.IsKeyPressed (Key.W))
        {
            wish += Vector3.Forward;
        }

        if (Input.IsKeyPressed (Key.S))
        {
            wish -= Vector3.Forward;
        }

        if (Input.IsKeyPressed (Key.D))
        {
            wish += Vector3.Right;
        }

        if (Input.IsKeyPressed (Key.A))
        {
            wish -= Vector3.Right;
        }

        if (Input.IsKeyPressed (Key.E))
        {
            wish += Vector3.Up;
        }

        if (Input.IsKeyPressed (Key.Q))
        {
            wish -= Vector3.Up;
        }

        if (wish == Vector3.Zero)
        {
            return;
        }

        // Yaw only, so W follows the eye direction without inheriting its pitch
        var basis = Basis.FromEuler (new Vector3 (0f, yaw, 0f));
        var speed = FlightMetersPerSecond * (Input.IsKeyPressed (Key.Shift) ? FlightTurbo : 1f);
        wantedFocus = focus + (basis * wish).Normalized () * (speed * seconds);
        // Flying must not get pulled back by the focus easing
        focus = wantedFocus;
    }

    private bool MouseOverViewport ()
    {
        return viewportContainer is not null
               && viewportContainer.GetGlobalRect ().HasPoint (viewportContainer.GetGlobalMousePosition ());
    }

    private void UpdateCamera ()
    {
        if (camera is null)
        {
            return;
        }

        var horizontal = distance * Mathf.Cos (pitch);
        camera.Position = focus + new Vector3 (
            horizontal * Mathf.Sin (yaw),
            distance * Mathf.Sin (pitch),
            horizontal * Mathf.Cos (yaw));
        camera.LookAt (focus, Vector3.Up);
    }

    private void TrackFocusedClient ()
    {
        if (focusClientId == Guid.Empty)
        {
            return;
        }

        var client = ActiveClients.Get (focusClientId);
        // The admin dummy has no session and never gets a pose, so it sits at the origin
        if (!GodotObject.IsInstanceValid (client) || client.IsAdminDebugDummy || client.CurrentCharacter is null)
        {
            focusClientId = Guid.Empty;
            return;
        }

        // The pose sits at the model center, so framing the pose centers the body
        wantedFocus = client.GlobalPosition;
        wantedDistance = FocusDistance;
    }

    private void RebuildColliderOverlays ()
    {
        if (overlayRoot is null)
        {
            return;
        }

        seenThisPass.Clear ();

        AddOverlays (FindStreamerRoot (WorldObjectCollisionStreamer.CollisionRootName), ColliderKind.Object);
        AddOverlays (FindStreamerRoot (TerrainGroundStreamer.StreamedRootName), ColliderKind.Ground);
        AddPlacementOverlays ();
        AddPlayerOverlays ();
        AddActorOverlays ();

        stale.Clear ();
        foreach (var source in overlays.Keys)
        {
            if (!seenThisPass.Contains (source))
            {
                stale.Add (source);
            }
        }

        foreach (var source in stale)
        {
            // A streamer cell free()s its shapes, so the source node can already be gone
            if (GodotObject.IsInstanceValid (overlays[source].Overlay))
            {
                overlays[source].Overlay.QueueFree ();
            }

            overlays.Remove (source);
        }
    }

    private static Node? FindStreamerRoot (string rootName)
    {
        var tree = Engine.GetMainLoop () as SceneTree;
        return tree?.Root.FindChild (rootName, recursive: true, owned: false);
    }

    private void AddOverlays (Node? root, ColliderKind kind)
    {
        if (root is null)
        {
            return;
        }

        foreach (var node in root.FindChildren ("*", nameof (CollisionShape3D), recursive: true, owned: false))
        {
            if (node is CollisionShape3D { Shape: not null } shape)
            {
                AddOverlay (shape, kind);
            }
        }
    }

    private void AddPlayerOverlays ()
    {
        foreach (var client in ActiveClients.GetAll ().Values)
        {
            // The admin dummy has no session and no pose, so it stands at the origin
            if (!GodotObject.IsInstanceValid (client)
                || client.IsAdminDebugDummy
                || client.FindChild (PlayerShapeName, recursive: true, owned: false) is not CollisionShape3D
                {
                    Shape: not null
                } shape)
            {
                continue;
            }

            AddOverlay (shape, ColliderKind.Player);
        }
    }

    /// <summary>
    /// Monsters, loot bags and other bodies on the blocking physics layer, the live blockers the
    /// baked cells do not cover
    /// </summary>
    private void AddActorOverlays ()
    {
        foreach (var node in ActiveNodes.GetAll ().Values)
        {
            // Players get their swept box, and a streamer StaticBody3D is already drawn
            if (!GodotObject.IsInstanceValid (node)
                || node is SphereClient
                || node is not PhysicsBody3D body
                || (body.CollisionLayer & BlockingPhysicsLayer) == 0u)
            {
                continue;
            }

            foreach (var child in body.GetChildren ())
            {
                if (child is CollisionShape3D { Shape: not null, Disabled: false } shape)
                {
                    AddOverlay (shape, ColliderKind.Actor);
                }
            }
        }
    }

    /// <summary>
    /// The model bounds box the client bounds-tests against. The wall a sweep hits is the magenta
    /// ObjectChunks part, so a box with no part under it is the passable-on-server gap
    /// </summary>
    private void AddPlacementOverlays ()
    {
        foreach (var node in ActiveWorldObjects.GetAll ().Values)
        {
            if (!GodotObject.IsInstanceValid (node)
                || !node.IsInsideTree ()
                || node is SphereClient)
            {
                continue;
            }

            if (node.TryGetModelBoundsBox (out var size, out var center))
            {
                AddBoxOverlay (node, size, center, ColliderKind.Placement);
            }
        }
    }

    /// <summary>
    /// The shape's debug mesh, kept per shape. A trimesh GetDebugMesh rebuilds the whole ArrayMesh
    /// from its faces, which on thousands of streamed cells stalls the tick
    /// </summary>
    private Mesh DebugMesh (Shape3D shape)
    {
        if (!debugMeshByShape.TryGetValue (shape, out var mesh))
        {
            mesh = shape.GetDebugMesh ();
            debugMeshByShape[shape] = mesh;
            lastDebugMeshBuilt++;
        }

        return mesh;
    }

    private void AddOverlay (CollisionShape3D shape, ColliderKind kind)
    {
        // A ground trimesh debug mesh is the whole ground mesh, so an off toggle never builds one
        if (!IsToggleOn (kind))
        {
            return;
        }

        var mesh = DebugMesh (shape.Shape!);
        var posed = OutsetTransform (shape.GlobalTransform, mesh.GetAabb ());
        var label = OverlayLabel (shape, kind);
        if (overlays.TryGetValue (shape, out var entry))
        {
            entry.Kind = kind;
            entry.Label = label;
            // A monster capsule moves, so the overlay follows its shape
            entry.Overlay.GlobalTransform = posed;
            seenThisPass.Add (shape);
            return;
        }

        var overlay = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = OverlayMaterial (kind),
            Layers = OverlayVisibilityLayer,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        overlayRoot?.AddChild (overlay);
        overlay.GlobalTransform = posed;
        overlays[shape] = new OverlayEntry
        {
            Source = shape,
            Overlay = overlay,
            Kind = kind,
            Label = label,
            MeshAabb = mesh.GetAabb ()
        };
        seenThisPass.Add (shape);
    }

    private void AddBoxOverlay (Node3D source, Vector3 size, Vector3 center, ColliderKind kind)
    {
        if (!IsToggleOn (kind))
        {
            return;
        }

        var boxCenter = new Transform3D (source.GlobalBasis, source.GlobalPosition)
                        * new Transform3D (Basis.Identity, center);
        var label = $"{kind} {source.Name} size {Form (size.X)} x {Form (size.Y)} x {Form (size.Z)}";
        if (overlays.TryGetValue (source, out var entry))
        {
            entry.Kind = kind;
            entry.Label = label;
            entry.Overlay.GlobalTransform = boxCenter;
            seenThisPass.Add (source);
            return;
        }

        var overlay = new MeshInstance3D
        {
            Mesh = BoxMesh (size * OverlayOutset),
            MaterialOverride = OverlayMaterial (kind),
            Layers = OverlayVisibilityLayer,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        overlayRoot?.AddChild (overlay);
        overlays[source] = new OverlayEntry
        {
            Source = source,
            Overlay = overlay,
            Kind = kind,
            Label = label,
            MeshAabb = overlay.Mesh!.GetAabb ()
        };
        // The bounds are model space, so the box turns with the placement
        overlay.GlobalTransform = boxCenter;
        seenThisPass.Add (source);
    }

    /// <summary>
    /// What a pick reports for this overlay. A streamed cell part carries the baked model stem on the
    /// shape, so an empty box names the model it came from
    /// </summary>
    private string OverlayLabel (CollisionShape3D shape, ColliderKind kind)
    {
        // The streamer bodies are anonymous, the cell node above them is named
        var body = shape.GetParent ();
        var cell = body?.GetParent () ?? body;
        var size = DebugMesh (shape.Shape!).GetAabb ().Size;
        return $"{kind} shape={shape.Name} body={body?.Name} cell={cell?.Name}"
               + $" size {Form (size.X)} x {Form (size.Y)} x {Form (size.Z)}";
    }

    /// <summary>
    /// Name the overlay under a left click: the eye ray against each overlay's world AABB, nearest
    /// first. Only overlays the visibility pass left visible can be hit
    /// </summary>
    private void PickOverlay (Vector2 controlPosition)
    {
        if (camera is null || overlayRoot is null)
        {
            return;
        }

        var from = camera.ProjectRayOrigin (controlPosition);
        var dir = camera.ProjectRayNormal (controlPosition);
        var best = -1f;
        OverlayEntry? picked = null;
        foreach (var entry in overlays.Values)
        {
            if (!entry.Overlay.Visible)
            {
                continue;
            }

            var aabb = TransformAabb (entry.Overlay.Mesh!.GetAabb (), entry.Overlay.GlobalTransform);
            var t = RayAabb (from, dir, aabb);
            if (t < 0f || t > PickMaxDistanceMeters)
            {
                continue;
            }

            if (picked is null || t < best)
            {
                best = t;
                picked = entry;
            }
        }

        if (picked is null)
        {
            SphLogger.Info ($"PICK no overlay at ({Form (controlPosition.X)}, {Form (controlPosition.Y)})"
                + $"  eye=({Form (from.X)}, {Form (from.Y)}, {Form (from.Z)})");
            return;
        }

        var center = TransformAabb (picked.MeshAabb, picked.Overlay.GlobalTransform).GetCenter ();
        SphLogger.Info (
            $"PICK {picked.Label}  t={Form (best)}  world=({Form (center.X)}, {Form (center.Y)}, {Form (center.Z)})"
            + $"  shape={picked.Source.GetPath ()}");
    }

    /// <summary>
    /// Slab test: distance from the ray origin to the near hit, -1 when the ray misses
    /// </summary>
    private static float RayAabb (Vector3 from, Vector3 dir, Aabb aabb)
    {
        var min = aabb.Position;
        var max = aabb.End;
        var tNear = 0f;
        var tFar = float.MaxValue;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = axis == 0 ? from.X : axis == 1 ? from.Y : from.Z;
            var direction = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
            var lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            var hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (Mathf.Abs (direction) < 1e-6f)
            {
                if (origin < lo || origin > hi)
                {
                    return -1f;
                }

                continue;
            }

            var inv = 1f / direction;
            var t1 = (lo - origin) * inv;
            var t2 = (hi - origin) * inv;
            tNear = Mathf.Max (tNear, Mathf.Min (t1, t2));
            tFar = Mathf.Min (tFar, Mathf.Max (t1, t2));
            if (tNear > tFar)
            {
                return -1f;
            }
        }

        return tFar < 0f ? -1f : tNear;
    }

    /// <summary>
    /// The shape pose, with the mesh scaled about its own center so the overlay sits just outside
    /// the surface it mirrors. The scale runs before the pose, so a rotated placement grows evenly
    /// instead of shearing along its world offset
    /// </summary>
    private static Transform3D OutsetTransform (Transform3D shapeTransform, Aabb meshAabb)
    {
        var center = meshAabb.GetCenter ();
        var toCenter = new Transform3D (Basis.Identity, center);
        var scale = new Transform3D (Basis.FromScale (Vector3.One * OverlayOutset), Vector3.Zero);
        return shapeTransform * toCenter * scale * toCenter.AffineInverse ();
    }

    /// <summary>
    /// Drop overlays fully outside the frustum. A big box whose center sits off to the side, a
    /// 100 m ground tile under the eye, stays visible through its corners, which a center point test
    /// alone drops
    /// </summary>
    private void UpdateOverlayVisibility ()
    {
        if (camera is null)
        {
            return;
        }

        var visibilityStart = Time.GetTicksMsec ();
        var eye = camera.GlobalPosition;
        var shown = 0;
        foreach (var entry in overlays.Values)
        {
            if (!IsToggleOn (entry.Kind))
            {
                entry.Overlay.Visible = false;
                continue;
            }

            // The overlay mesh AABB is static, so only the pose moves it
            var aabb = TransformAabb (entry.MeshAabb, entry.Overlay.GlobalTransform);
            var inRange = aabb.GetCenter ().DistanceTo (eye)
                          <= OverlayMaxRangeMeters + aabb.Size.Length () * 0.5f;
            entry.Overlay.Visible = inRange && InFrustum (camera, aabb);
            if (entry.Overlay.Visible)
            {
                shown++;
            }
        }

        lastShown = shown;
        lastVisibilityMs = Time.GetTicksMsec () - visibilityStart;
        UpdateStatus ();
    }

    private static bool InFrustum (Camera3D camera, Aabb aabb)
    {
        if (camera.IsPositionInFrustum (aabb.GetCenter ()))
        {
            return true;
        }

        for (var index = 0; index < 8; index++)
        {
            if (camera.IsPositionInFrustum (aabb.GetEndpoint (index)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsToggleOn (ColliderKind kind)
    {
        return kind switch
        {
            ColliderKind.Object => objectColliders?.ButtonPressed == true,
            ColliderKind.Placement => placementColliders?.ButtonPressed == true,
            ColliderKind.Actor => actorColliders?.ButtonPressed == true,
            ColliderKind.Player => playerColliders?.ButtonPressed == true,
            ColliderKind.Ground => groundColliders?.ButtonPressed == true,
            _ => false
        };
    }

    private void UpdateStatus ()
    {
        if (statusLabel is null)
        {
            return;
        }

        var name = "";
        var client = focusClientId == Guid.Empty ? null : ActiveClients.Get (focusClientId);
        if (client?.CurrentCharacter is { } character)
        {
            name = "  " + character.Name;
        }

        statusLabel.Text =
            $"focus ({Form (focus.X)}, {Form (focus.Y)}, {Form (focus.Z)})"
            + $"  dist {Form (distance)}"
            + $"  overlays {overlays.Count} shown {lastShown}"
            + name;
    }

    /// <summary>
    /// Eye pose once a second, and only after the eye moved. Game axes are right, down, forward,
    /// the appsettings Spawn_X/Y/Z frame, so a pasted coordinate lands where the camera looks
    /// </summary>
    private void LogCameraPose ()
    {
        if (camera is null)
        {
            return;
        }

        var eye = camera.GlobalPosition;
        if (eye.DistanceTo (loggedEye) < CameraLogMoveMeters
            && Mathf.Abs (yaw - loggedYaw) < CameraLogTurnRadians
            && Mathf.Abs (pitch - loggedPitch) < CameraLogTurnRadians)
        {
            return;
        }

        loggedEye = eye;
        loggedYaw = yaw;
        loggedPitch = pitch;

        SphLogger.Info (
            $"CAMERA eye=({Form (eye.X)}, {Form (eye.Y)}, {Form (eye.Z)}) "
            + $"game=({Form (eye.X)}, {Form (-eye.Y)}, {Form (-eye.Z)}) "
            + $"focus=({Form (focus.X)}, {Form (focus.Y)}, {Form (focus.Z)}) "
            + $"dist={Form (distance)} yaw={Form (Mathf.RadToDeg (yaw))} pitch={Form (Mathf.RadToDeg (pitch))} "
            + $"overlays {overlays.Count} shown {lastShown} "
            + $"ms ground {lastGroundMs:0} objects {lastObjectMs:0} alpha {lastAlphaMs:0}"
            + $" rebuild {lastRebuildMs:0} vis {lastVisibilityMs:0}"
            + $" meshes {debugMeshByShape.Count} built {lastDebugMeshBuilt}");
    }

    private static string Form (float value) => value.ToString ("0.0", CultureInfo.InvariantCulture);

    private Mesh BoxMesh (Vector3 size)
    {
        // Placements of one model share the size, so the mesh is shared too
        if (boxMeshBySize.TryGetValue (size, out var cached))
        {
            return cached;
        }

        var mesh = new BoxMesh { Size = size };
        boxMeshBySize[size] = mesh;
        return mesh;
    }

    private static Aabb TransformAabb (Aabb aabb, Transform3D transform)
    {
        var merged = new Aabb (transform * aabb.GetEndpoint (0), Vector3.Zero);
        for (var index = 1; index < 8; index++)
        {
            merged = merged.Expand (transform * aabb.GetEndpoint (index));
        }

        return merged;
    }

    private static StandardMaterial3D OverlayMaterial (ColliderKind kind)
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            // The depth test hides the colliders behind walls, so the scene stays readable
            NoDepthTest = false,
            // The overlay is transparent, so it must draw after the opaque world to blend on it
            RenderPriority = 10,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DisableReceiveShadows = true,
            AlbedoColor = kind switch
            {
                ColliderKind.Player => new Color (1f, 0.85f, 0.2f, ColliderAlpha),
                ColliderKind.Actor => new Color (1f, 0.4f, 0.15f, ColliderAlpha),
                ColliderKind.Placement => new Color (0.35f, 0.65f, 1f, ColliderAlpha),
                ColliderKind.Ground => new Color (0.2f, 0.85f, 0.6f, ColliderAlpha),
                _ => new Color (1f, 0.35f, 0.75f, ColliderAlpha)
            }
        };
    }

    private static Vector3 SpawnWorldPosition ()
    {
        var config = ServerConfig.AppConfig;
        return new Vector3 (config.Spawn_X, -config.Spawn_Y, -config.Spawn_Z);
    }

    private enum ColliderKind
    {
        Object,
        Actor,
        Placement,
        Ground,
        Player
    }

    private sealed class OverlayEntry
    {
        public required Node Source { get; init; }
        public required MeshInstance3D Overlay { get; init; }
        public ColliderKind Kind { get; set; }

        // What a pick prints, refreshed each rebuild
        public string Label { get; set; } = "";

        // The mesh bounds in mesh space, taken once. The visibility pass transforms this per overlay
        public Aabb MeshAabb { get; init; }
    }
}
