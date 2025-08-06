using SphServer.Shared.Db.DataModels;

namespace SphServer.Sphere.Game.WorldObject;

public partial class ItemWorldObject : WorldObject
{
    public ItemDbEntry ItemDbEntry { get; set; }
}