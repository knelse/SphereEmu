using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class DoorWithKey : WorldObject
{
	[Export] public int SubtypeID { get; set; }
}
