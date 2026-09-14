using Godot;
using SphServer.Helpers;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleElixirPillar : WorldObject
{
	[Export] public Castles Castle { get; set; }

	public CastleElixirPillar()
	{
		ObjectType = ObjectType.Castle_Elixir_Pillar;
		ModelName = "cs_knot";
	}
}
