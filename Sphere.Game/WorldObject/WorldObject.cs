using Godot;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Marked as a Godot tool script so the editor runs export setters (angle, model, type) and <c>_Ready</c> for visuals;
///     server registration and visibility area are skipped in the editor.
/// </summary>
[Tool]
public partial class WorldObject : Node3D
{
	private int _angle;

	private string _modelName = string.Empty;

	private ObjectType _objectType = ObjectType.Unknown;

	/// <summary>
	///     Game yaw: 0 = north; values increase counter-clockwise. Source yaw is <c>Angle * π / 64</c> radians; Godot
	///     <see cref="Rotation" /> uses the same <c>R' = T R_src T</c> / YXZ path as terrain JSON
	///     (<see cref="SphServer.Godot.Scripts.Terrain.Fill.TerrainObjectsFill" />; <c>NpcSpawnTscnWriter</c> position flip is
	///     separate).
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
			ApplyAngleToRotation();
		}
	}

	[Export] public ushort ID { get; set; }

	/// <summary>
	///     When set in the editor, rebuild Fill tools keep this placement and treat its source coordinates as occupied
	///     so dump rows at the same position are not spawned again.
	/// </summary>
	[Export(PropertyHint.None, "Do Not Rebuild")]
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
			ScheduleModelVisualRefreshIfNeeded();
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
			ScheduleModelVisualRefreshIfNeeded();
		}
	}

	/// <summary>
	///     When true, <see cref="Node._Ready" /> loads or refreshes the GLB / placeholder mesh before the rest of setup,
	///     and returns early in the editor (no collision / networking init).
	/// </summary>
	protected virtual bool RefreshModelVisualOnReady => false;

	/// <summary>
	///     When true, editor <see cref="Node._Ready" /> does not refresh models (e.g. monsters use MultiMesh instead).
	/// </summary>
	protected virtual bool SkipModelVisualRefreshOnEditorReady => false;

	/// <summary>
	///     If true, after loading a GLB we shift the visual child so its combined mesh bounds sit on Y=0 (feet on ground).
	///     This compensates for assets whose origin/pivot is centered instead of at the bottom.
	/// </summary>
	protected virtual bool AutoGroundGlbVisual => false;

	internal bool HasVisibilityArea => _visibilityArea is not null;

	public override void _ExitTree()
	{
		if (!Engine.IsEditorHint())
		{
			WorldObjectVisibilityManager.Unregister(this);
		}

		base._ExitTree();
	}

	public override void _Ready()
	{
		ApplyAngleToRotation();

		if (RefreshModelVisualOnReady)
		{
			if (!Engine.IsEditorHint())
			{
				RefreshModelVisual();
			}
			else if (!SkipModelVisualRefreshOnEditorReady && ShouldRefreshModelVisual())
			{
				RefreshModelVisual();
			}

			if (Engine.IsEditorHint())
			{
				return;
			}
		}

		if (!Engine.IsEditorHint())
		{
			if (ID == 0)
			{
				ID = WorldObjectIndex.New();
			}

			Name = Name + $"_{ID}";

			ActiveNodes.Add(GetInstanceId(), this);
			ActiveWorldObjects.Add(ID, this);
			WorldObjectVisibilityManager.Register(this);
		}

		// Runtime only: instance overrides are applied after the base scene defaults; defer one frame so exports settle.
		if (!RefreshModelVisualOnReady && !Engine.IsEditorHint())
		{
			CallDeferred(nameof(RefreshModelVisualDeferred));
		}
	}

	/// <summary>
	///     Sets <see cref="Rotation" /> Y from <see cref="Angle" /> using <c>t0 = Angle * π / 128</c>.
	/// </summary>
	private void ApplyAngleToRotation()
	{
		var t0 = DecodeAngleToYawRadians(_angle);
		// Avoid forcing a transform change on load if the scene already has the correct rotation.
		if (Mathf.Abs(Rotation.Y - t0) < 0.0001f && Mathf.Abs(Rotation.X) < 0.0001f && Mathf.Abs(Rotation.Z) < 0.0001f)
		{
			return;
		}

		Rotation = new Vector3(0f, t0, 0f);
	}

	/// <summary>Game yaw in radians from encoded <see cref="Angle" />.</summary>
	public static float DecodeAngleToYawRadians(int angle) => (float)(angle * Math.PI / 128.0);

	/// <summary>Encodes Godot Y yaw radians into game <see cref="Angle" /> units.</summary>
	public static int EncodeYawRadiansToAngle(float yawRadians) =>
		(int)Mathf.Round(yawRadians * 128f / Mathf.Pi);

	/// <summary>Uniform random facing for spawned world objects (256 discrete yaw steps).</summary>
	public static int CreateRandomSpawnAngle() => GD.RandRange(0, 255);
}
