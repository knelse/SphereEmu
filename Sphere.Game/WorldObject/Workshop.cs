using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class Workshop : WorldObject
{
	public Workshop()
	{
		ObjectType = ObjectType.Workshop;
		ModelName = "edoor";
	}
}
