using SphServer.Shared.Db.DataModels;

namespace SphServer;

public partial class ItemWorldObject : WorldObject
{
    public ItemDbEntry ItemDbEntry { get; set; }
}