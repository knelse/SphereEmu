using Godot;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
/// Tool script so the editor runs export setters and _Ready for visuals, and skips server
/// registration
/// </summary>
[Tool]
public partial class WorldObject : Node3D
{
    private int _angle;

    private string _modelName = string.Empty;

    private ObjectType _objectType = ObjectType.Unknown;

    /// <summary>
    /// Game yaw 0 is north, counter-clockwise; Godot Y follows the terrain YXZ conjugate of Angle *
    /// pi / 64
    /// </summary>
    [Export]
    public int Angle
    {
        get => _angle;
        set
        {
            if (_angle == value)
            {
                return;
            }

            _angle = value;
            ApplyAngleToRotation ();
        }
    }

    [Export] public ushort ID { get; set; }

    /// <summary>
    /// Fill rebuild keeps this placement and treats its source coordinates as already occupied
    /// </summary>
    [Export (PropertyHint.None, "Do Not Rebuild")]
    public bool DoNotRebuild { get; set; }

    [Export]
    public ObjectType ObjectType
    {
        get => _objectType;
        set
        {
            if (_objectType == value)
            {
                return;
            }

            _objectType = value;
            ScheduleModelVisualRefreshIfNeeded ();
        }
    }

    [Export]
    public string ModelName
    {
        get => _modelName;
        set
        {
            if (_modelName == value)
            {
                return;
            }

            _modelName = value;
            ScheduleModelVisualRefreshIfNeeded ();
        }
    }

    /// <summary>
    /// Loads the mesh before the rest of setup, and the editor returns before collision and
    /// networking
    /// </summary>
    protected virtual bool RefreshModelVisualOnReady => false;

    /// <summary>
    /// Editor _Ready skips the model refresh (monsters use MultiMesh)
    /// </summary>
    protected virtual bool SkipModelVisualRefreshOnEditorReady => false;

    /// <summary>
    /// Asset origin sits at the center, not the bottom
    /// </summary>
    protected virtual bool AutoGroundGlbVisual => false;

    /// <summary>
    /// MBC module tag for region 1 TransformUpdate, the same tag used at spawn
    /// </summary>
    protected virtual ushort GetMoveModuleTag () => (ushort) ObjectType;

    internal bool HasVisibilityArea => _visibilityArea is not null;

    public override void _ExitTree ()
    {
        if (!Engine.IsEditorHint ())
        {
            WorldObjectVisibilityManager.Unregister (this);
        }

        base._ExitTree ();
    }

    /// <summary>
    /// Collapses stacked _{ID} suffixes on names already baked into World/Chunks
    /// </summary>
    public void CompactDuplicatedIdNameSuffix ()
    {
        if (ID == 0)
        {
            return;
        }

        var suffix = $"_{ID}";
        var current = Name.ToString ();
        while (current.EndsWith (suffix, StringComparison.Ordinal))
        {
            current = current[..^suffix.Length];
        }

        Name = string.IsNullOrEmpty (current) ? $"WO{suffix}" : current + suffix;
    }

    public override void _Ready ()
    {
        ApplyAngleToRotation ();

        if (RefreshModelVisualOnReady)
        {
            if (!Engine.IsEditorHint ())
            {
                RefreshModelVisual ();
            }
            else if (!SkipModelVisualRefreshOnEditorReady && ShouldRefreshModelVisual ())
            {
                RefreshModelVisual ();
            }

            if (Engine.IsEditorHint ())
            {
                return;
            }
        }

        if (!Engine.IsEditorHint ())
        {
            if (ID == 0)
            {
                ID = WorldObjectIndex.New ();
            }
            else if (!WorldObjectIndex.TryReserve (ID))
            {
                var previous = ID;
                ID = WorldObjectIndex.New ();
                SphLogger.Warning (
                    $"World object {Name} baked id {previous:X4} already in use, reassigned to {ID:X4}");
            }

            // Re-entering the tree stacks _{ID} onto names already baked into World/Chunks
            CompactDuplicatedIdNameSuffix ();

            ActiveNodes.Add (GetInstanceId (), this);
            ActiveWorldObjects.Add (ID, this);
            WorldObjectVisibilityManager.Register (this);
        }

        // Runtime only: instance overrides are applied after the base scene defaults; defer one
        // frame so exports settle.
        if (!RefreshModelVisualOnReady && !Engine.IsEditorHint ())
        {
            CallDeferred (nameof (RefreshModelVisualDeferred));
        }
    }

    /// <summary>
    /// Godot Y from Angle, t0 = Angle * pi / 128
    /// </summary>
    private void ApplyAngleToRotation ()
    {
        var t0 = DecodeAngleToYawRadians (_angle);
        // Avoid forcing a transform change on load if the scene already has the correct rotation.
        if (Mathf.Abs (Rotation.Y - t0) < 0.0001f && Mathf.Abs (Rotation.X) < 0.0001f && Mathf.Abs (Rotation.Z) < 0.0001f)
        {
            return;
        }

        Rotation = new Vector3 (0f, t0, 0f);
    }

    /// <summary>
    /// Game yaw in radians from encoded <see cref="Angle" />.
    /// </summary>
    public static float DecodeAngleToYawRadians (int angle) => (float) (angle * Math.PI / 128.0);

    /// <summary>
    /// Encodes Godot Y yaw radians into game <see cref="Angle" /> units.
    /// </summary>
    public static int EncodeYawRadiansToAngle (float yawRadians) =>
        (int) Mathf.Round (yawRadians * 128f / Mathf.Pi);

    /// <summary>
    /// Uniform random facing for spawned world objects (256 discrete yaw steps).
    /// </summary>
    public static int CreateRandomSpawnAngle () => GD.RandRange (0, 255);
}
