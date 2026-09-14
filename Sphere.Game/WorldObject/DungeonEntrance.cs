using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class DungeonEntrance : WorldObject
{
	public DungeonEntrance()
	{
		ObjectType = ObjectType.Dungeon_Entrance;
		ModelName = "edoor";
	}
}
