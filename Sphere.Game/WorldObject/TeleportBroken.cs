using System.Collections.Generic;
using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class TeleportBroken : WorldObject
{
    public TeleportBroken()
    {
        ObjectType = ObjectType.TeleportBroken;
    }
}
