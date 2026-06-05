using System.Collections.Generic;
using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class TeleportInDungeon : WorldObject
{
	public TeleportInDungeon()
	{
		ObjectType = ObjectType.TeleportInDungeon;
	}
}
