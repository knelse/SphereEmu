using Godot;
using SphServer.Godot.Scripts.Objects;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Door
{
	private void JumpToTarget()
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		if (ObjectType is not (ObjectType.DoorEntrance or ObjectType.DoorExit))
		{
			GD.PushWarning($"{Name}: Jump to target is only for door entrance/exit.");
			return;
		}

		if (!HasTarget)
		{
			GD.PushWarning($"{Name}: no target coordinates.");
			return;
		}

		var sceneTarget = new Vector3((float)TargetX, -(float)TargetY, -(float)TargetZ);
		var worldTarget = GetParent() is Node3D parent ? parent.ToGlobal(sceneTarget) : sceneTarget;
		EditorSceneCamera.JumpToWorldPosition(worldTarget);
	}
}
