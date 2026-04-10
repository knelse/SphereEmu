using System.Collections.Generic;
using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class TeleportWild : WorldObject
{
    public TeleportWild()
    {
        ObjectType = ObjectType.TeleportWild;
    }
}
