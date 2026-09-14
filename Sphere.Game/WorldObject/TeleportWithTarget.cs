using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class TeleportWithTarget : WorldObject
{
	public TeleportWithTarget()
	{
		ObjectType = ObjectType.Teleport_With_Target;
	}

	[Export] public int SubtypeID { get; set; }
}
