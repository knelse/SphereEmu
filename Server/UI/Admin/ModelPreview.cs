using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Server.UI.Admin;

/// <summary>
/// Orbit-camera model viewer with an animation picker. LMB orbits, RMB zooms, MMB pans
/// </summary>
public partial class ModelPreview : VBoxContainer
{
    private const float DefaultYaw = Mathf.Pi;
    private const float DefaultPitch = 0.2f;

    private OptionButton? animationSelect;
    private Label? previewStatus;
    private SubViewport? previewViewport;
    private Node3D? modelHolder;
    private MeshInstance3D? floorGrid;
    private Camera3D? camera;

    private readonly List<AnimationPlayer> animationPlayers = [];
    private Node3D? modelInstance;
    private string playingClip = "";
    private bool suppressAnimationCallback;

    private float yaw = DefaultYaw;
    private float pitch = DefaultPitch;
    private float distance = 5f;
    private Vector3 focus = Vector3.Zero;
    private bool orbiting;
    private bool zooming;
    private bool panning;

    public bool HasModel => modelInstance is not null;

    public override void _Ready ()
    {
        AddThemeConstantOverride ("separation", 4);

        var viewportContainer = new SubViewportContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Stretch = true,
            MouseFilter = MouseFilterEnum.Stop
        };
        viewportContainer.GuiInput += OnPreviewGuiInput;
        AddChild (viewportContainer);

        previewViewport = new SubViewport
        {
            OwnWorld3D = true,
            HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Size = new Vector2I (640, 480)
        };
        viewportContainer.AddChild (previewViewport);

        var animationRow = new HBoxContainer ();
        animationRow.AddThemeConstantOverride ("separation", 8);
        animationRow.AddChild (new Label { Text = "Animation" });
        animationSelect = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        animationSelect.ItemSelected += _ =>
        {
            if (!suppressAnimationCallback)
            {
                PlaySelectedAnimation ();
            }
        };
        animationRow.AddChild (animationSelect);
        AddChild (animationRow);

        previewStatus = new Label ();
        AddChild (previewStatus);

        BuildPreviewWorld ();
    }

    /// <summary>
    /// Renders only while the owning window is shown
    /// </summary>
    public void SetRendering (bool rendering)
    {
        if (previewViewport is null)
        {
            return;
        }

        previewViewport.RenderTargetUpdateMode = rendering
            ? SubViewport.UpdateMode.Always
            : SubViewport.UpdateMode.Disabled;
    }

    public override void _UnhandledInput (InputEvent inputEvent)
    {
        if (!orbiting && !zooming && !panning)
        {
            return;
        }

        if (inputEvent is InputEventMouseButton { Pressed: false } released)
        {
            if (released.ButtonIndex == MouseButton.Left)
            {
                orbiting = false;
            }

            if (released.ButtonIndex == MouseButton.Right)
            {
                zooming = false;
            }

            if (released.ButtonIndex == MouseButton.Middle)
            {
                panning = false;
            }

            return;
        }

        if (inputEvent is not InputEventMouseMotion motion)
        {
            return;
        }

        if (orbiting)
        {
            yaw -= motion.Relative.X * 0.01f;
            pitch = Mathf.Clamp (pitch + motion.Relative.Y * 0.01f, -1.2f, 1.2f);
        }

        if (zooming)
        {
            distance = Mathf.Clamp (distance * (1f + motion.Relative.Y * 0.01f), 0.35f, 80f);
        }

        if (panning)
        {
            Pan (motion.Relative);
        }

        UpdateCamera ();
        GetViewport ().SetInputAsHandled ();
    }

    private void BuildPreviewWorld ()
    {
        if (previewViewport is null)
        {
            return;
        }

        var world = new Node3D { Name = "PreviewWorld" };
        previewViewport.AddChild (world);

        var environment = new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = new Color (0.15f, 0.15f, 0.17f),
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color (0.55f, 0.55f, 0.58f),
            AmbientLightEnergy = 0.85f
        };
        world.AddChild (new WorldEnvironment { Environment = environment });

        var key = new DirectionalLight3D
        {
            Rotation = new Vector3 (-0.9f, 0.6f, 0f),
            ShadowEnabled = true
        };
        world.AddChild (key);
        world.AddChild (new DirectionalLight3D
        {
            Rotation = new Vector3 (-0.2f, 2.6f, 0f),
            LightEnergy = 0.35f
        });

        floorGrid = new MeshInstance3D
        {
            Mesh = BuildGridMesh (),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color (0.38f, 0.38f, 0.42f)
            }
        };
        world.AddChild (floorGrid);

        modelHolder = new Node3D { Name = "Model" };
        world.AddChild (modelHolder);

        camera = new Camera3D
        {
            Current = true,
            Fov = 50f,
            Near = 0.05f,
            Far = 200f
        };
        world.AddChild (camera);
        UpdateCamera ();
    }

    private static ImmediateMesh BuildGridMesh ()
    {
        var mesh = new ImmediateMesh ();
        mesh.SurfaceBegin (Mesh.PrimitiveType.Lines);
        for (var i = -4; i <= 4; i++)
        {
            mesh.SurfaceAddVertex (new Vector3 (i, 0f, -4f));
            mesh.SurfaceAddVertex (new Vector3 (i, 0f, 4f));
            mesh.SurfaceAddVertex (new Vector3 (-4f, 0f, i));
            mesh.SurfaceAddVertex (new Vector3 (4f, 0f, i));
        }

        mesh.SurfaceEnd ();
        return mesh;
    }

    private void OnPreviewGuiInput (InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
            {
                distance = Mathf.Max (0.35f, distance * 0.9f);
            }
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
            {
                distance = Mathf.Min (80f, distance * 1.1f);
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                orbiting = button.Pressed;
            }
            else if (button.ButtonIndex == MouseButton.Right)
            {
                zooming = button.Pressed;
            }
            else if (button.ButtonIndex == MouseButton.Middle)
            {
                panning = button.Pressed;
            }
            else
            {
                return;
            }

            UpdateCamera ();
            GetViewport ().SetInputAsHandled ();
            return;
        }

        if (inputEvent is not InputEventMouseMotion motion)
        {
            return;
        }

        if (orbiting)
        {
            yaw -= motion.Relative.X * 0.01f;
            pitch = Mathf.Clamp (pitch + motion.Relative.Y * 0.01f, -1.2f, 1.2f);
        }
        else if (zooming)
        {
            distance = Mathf.Clamp (distance * (1f + motion.Relative.Y * 0.01f), 0.35f, 80f);
        }
        else if (panning)
        {
            Pan (motion.Relative);
        }
        else
        {
            return;
        }

        UpdateCamera ();
        GetViewport ().SetInputAsHandled ();
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

    private void Pan (Vector2 relative)
    {
        if (camera is null)
        {
            return;
        }

        var basis = camera.GlobalTransform.Basis;
        var scale = distance * 0.0015f;
        focus += (-basis.X * relative.X + basis.Y * relative.Y) * scale;
    }

    public void LoadModel (string? modelName)
    {
        ClearModel ();
        if (modelHolder is null)
        {
            return;
        }

        var trimmed = modelName?.Trim () ?? string.Empty;
        var path = string.IsNullOrEmpty (trimmed) ? null : GlbModelPaths.Resolve (trimmed);
        if (path is null)
        {
            if (previewStatus is not null)
            {
                previewStatus.Text = string.IsNullOrEmpty (trimmed)
                    ? "No model"
                    : $"Model not found: {trimmed}";
            }

            FillAnimations ([]);
            return;
        }

        var packed = ResourceLoader.Load<PackedScene> (path);
        if (packed?.Instantiate () is not Node3D root)
        {
            if (previewStatus is not null)
            {
                previewStatus.Text = $"Failed to load {trimmed}";
            }

            FillAnimations ([]);
            return;
        }

        modelInstance = root;
        modelHolder.AddChild (root);
        CollectAnimationPlayers (root, animationPlayers);
        foreach (var player in animationPlayers)
        {
            player.AnimationFinished += OnAnimationFinished;
        }

        FrameModel (root);
        FillAnimations (animationPlayers);
        if (previewStatus is not null)
        {
            previewStatus.Text = trimmed;
        }
    }

    private void ClearModel ()
    {
        foreach (var player in animationPlayers)
        {
            if (GodotObject.IsInstanceValid (player))
            {
                player.AnimationFinished -= OnAnimationFinished;
            }
        }

        animationPlayers.Clear ();
        if (modelInstance is not null && GodotObject.IsInstanceValid (modelInstance))
        {
            modelInstance.QueueFree ();
        }

        modelInstance = null;
        playingClip = "";
    }

    private static void CollectAnimationPlayers (Node node, List<AnimationPlayer> into)
    {
        if (node is AnimationPlayer player)
        {
            into.Add (player);
        }

        foreach (var child in node.GetChildren ())
        {
            CollectAnimationPlayers (child, into);
        }
    }

    private void FrameModel (Node3D root)
    {
        yaw = DefaultYaw;
        pitch = DefaultPitch;
        focus = Vector3.Zero;
        if (!TryGetCombinedAabb (root, out var bounds))
        {
            distance = 5f;
            UpdateCamera ();
            return;
        }

        root.Position -= bounds.GetCenter ();
        if (floorGrid is not null)
        {
            floorGrid.Position = new Vector3 (0f, -bounds.Size.Y * 0.5f, 0f);
        }

        distance = Mathf.Clamp (bounds.Size.Length () * 1.45f, 1.2f, 40f);
        UpdateCamera ();
    }

    private static bool TryGetCombinedAabb (Node3D root, out Aabb bounds)
    {
        bounds = default;
        var any = false;
        MergeAabb (root, root, ref bounds, ref any);
        return any;
    }

    private static void MergeAabb (Node3D node, Node3D space, ref Aabb bounds, ref bool any)
    {
        if (node is MeshInstance3D { Mesh: not null } mesh)
        {
            var transformed = TransformAabb (
                mesh.GetAabb (),
                space.GlobalTransform.AffineInverse () * mesh.GlobalTransform);
            bounds = any ? bounds.Merge (transformed) : transformed;
            any = true;
        }

        foreach (var child in node.GetChildren ())
        {
            if (child is Node3D child3D)
            {
                MergeAabb (child3D, space, ref bounds, ref any);
            }
        }
    }

    private static Aabb TransformAabb (Aabb aabb, Transform3D transform)
    {
        var origin = aabb.Position;
        var size = aabb.Size;
        Vector3 Corner (int index) => transform * (origin + new Vector3 (
            (index & 1) == 0 ? 0f : size.X,
            (index & 2) == 0 ? 0f : size.Y,
            (index & 4) == 0 ? 0f : size.Z));

        var merged = new Aabb (Corner (0), Vector3.Zero);
        for (var index = 1; index < 8; index++)
        {
            merged = merged.Expand (Corner (index));
        }

        return merged;
    }

    private void FillAnimations (List<AnimationPlayer> players)
    {
        if (animationSelect is null)
        {
            return;
        }

        var names = new SortedSet<string> (StringComparer.OrdinalIgnoreCase);
        foreach (var player in players)
        {
            foreach (var name in player.GetAnimationList ())
            {
                if (!string.IsNullOrEmpty (name) && !name.Equals ("RESET", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add (name);
                }
            }
        }

        var idleClip = names.FirstOrDefault (name => name.EndsWith ("_00", StringComparison.OrdinalIgnoreCase)
                          || name.Equals ("00", StringComparison.OrdinalIgnoreCase))
                      ?? names.FirstOrDefault (IsIdleName)
                      ?? "";
        suppressAnimationCallback = true;
        animationSelect.Clear ();
        animationSelect.AddItem ("Idle");
        animationSelect.SetItemMetadata (0, idleClip);
        var itemIndex = 1;
        foreach (var name in names)
        {
            if (IsIdleName (name))
            {
                continue;
            }

            animationSelect.AddItem (name);
            animationSelect.SetItemMetadata (itemIndex, name);
            itemIndex++;
        }

        animationSelect.Select (0);
        suppressAnimationCallback = false;
        PlaySelectedAnimation ();
    }

    private static bool IsIdleName (string name)
    {
        var lower = name.ToLowerInvariant ();
        return lower is "idle" or "stay" or "stand" or "00"
               || lower.Contains ("idle")
               || lower.EndsWith ("_00", StringComparison.Ordinal);
    }

    private void PlaySelectedAnimation ()
    {
        if (animationSelect is null || animationSelect.Selected < 0)
        {
            return;
        }

        playingClip = animationSelect.GetItemMetadata (animationSelect.Selected).AsString ();
        foreach (var player in animationPlayers)
        {
            if (!GodotObject.IsInstanceValid (player))
            {
                continue;
            }

            if (string.IsNullOrEmpty (playingClip))
            {
                if (player.HasAnimation ("RESET"))
                {
                    player.Play ("RESET");
                }
                else
                {
                    player.Stop ();
                }

                continue;
            }

            if (player.HasAnimation (playingClip))
            {
                player.Play (playingClip);
            }
        }
    }

    private void OnAnimationFinished (StringName name)
    {
        if (string.IsNullOrEmpty (playingClip) || !name.ToString ().Equals (playingClip, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var player in animationPlayers)
        {
            if (GodotObject.IsInstanceValid (player) && player.HasAnimation (playingClip))
            {
                player.Play (playingClip);
            }
        }
    }
}
