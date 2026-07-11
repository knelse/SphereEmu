using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class TeleportWithTarget : WorldObject
{
	public TeleportWithTarget()
	{
		ObjectType = ObjectType.TeleportWithTarget;
	}

	[Export] public int SubtypeID { get; set; }
}
