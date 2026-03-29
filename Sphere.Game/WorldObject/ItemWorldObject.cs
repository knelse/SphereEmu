using Godot;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class ItemWorldObject : WorldObject
{
    public ItemDbEntry ItemDbEntry { get; set; }
}