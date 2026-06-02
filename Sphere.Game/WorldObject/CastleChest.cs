using Godot;
using SphServer.Helpers;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleChest : WorldObject
{
    [Export] public Castles Castle { get; set; }
    public CastleChest()
    {
        ObjectType = ObjectType.CastleChest;
        ModelName = "cs_chest";
    }
}