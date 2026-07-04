using System.Collections.Generic;
using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class Teleport : WorldObject
{
	public Teleport()
	{
		ObjectType = ObjectType.Teleport;
	}
}
