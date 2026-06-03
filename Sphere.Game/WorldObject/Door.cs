using Godot;
using SphServer.Godot.Scripts.Objects;
using SphServer.Packets;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class Door : WorldObject
{
	[Export] public int DoorID { get; set; }
	[Export] public bool HasTarget { get; set; }
	[Export] public double TargetX { get; set; }
	[Export] public double TargetY { get; set; }
	[Export] public double TargetZ { get; set; }

	[ExportToolButton("Jump to target")]
	public Callable JumpToTargetButton => Callable.From(JumpToTarget);

	protected override List<PacketPart> GetPacketParts()
	{
		return HasTarget ? PacketPart.LoadDefinedWithOverride("door_entrance_tp") : base.GetPacketParts();
	}

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "door_id", DoorID, 7);
		if (HasTarget)
		{
			PacketPart.UpdateTargetCoordinates(packetParts, TargetX, TargetY, TargetZ);
		}

		return packetParts;
	}

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
