using System.Collections.Generic;
using Godot;
using SphServer.Helpers;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleTeleport : WorldObject
{
    [Export] public Castles Castle { get; set; }
    public CastleTeleport()
    {
        ObjectType = ObjectType.CastleTeleport;
    }
}
