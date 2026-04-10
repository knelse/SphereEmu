using System.Collections.Generic;
using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleTeleport : WorldObject
{
    public CastleTeleport()
    {
        ObjectType = ObjectType.CastleTeleport;
    }
}
