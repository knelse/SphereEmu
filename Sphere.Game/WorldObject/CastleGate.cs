using Godot;
using SphServer.Helpers;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleGate : WorldObject
{
	[Export] public Castles Castle { get; set; }
	[Export] public string ClanName { get; set; }

	public CastleGate()
	{
		ObjectType = ObjectType.Castle_Gate;
		ModelName = "cc103";
		ClanName = "Зеленый Слоник";
	}
}
