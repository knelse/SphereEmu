using System.Collections.Generic;
using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class TeleportTournament : WorldObject
{
	public TeleportTournament()
	{
		ObjectType = ObjectType.TournamentTeleport;
	}
}
