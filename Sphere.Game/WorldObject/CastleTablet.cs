using Godot;
using SphServer.Helpers;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleTablet : WorldObject
{
	public CastleTablet()
	{
		ObjectType = ObjectType.CastleTablet;
		ModelName = "cs_table";
		ClanName = "Зеленый Слоник";
	}

	[Export] public Castles Castle { get; set; }
	[Export] public string ClanName { get; set; }
}
