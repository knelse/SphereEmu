using Godot;
using SphServer.Client;
using SphServer.Server.Config;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Server.Simulation;

public sealed class MonsterActivity
{
	public const int GraceTicks = 60;

	private readonly Dictionary<Guid, int> awayTicks = new ();

	public void Step ()
	{
		if (Engine.IsEditorHint ())
		{
			return;
		}

		var radius = ServerConfig.AppConfig.ObjectVisibilityDistance;
		var radiusSq = radius * radius;
		var watchers = new List<Vector3> ();
		foreach (var client in ActiveClients.GetAll ().Values)
		{
			if (!GodotObject.IsInstanceValid (client) || !client.ClientStateManager.IsInGameState ())
			{
				continue;
			}

			watchers.Add (client.GlobalPosition);
		}

		var seen = new HashSet<Guid> ();
		foreach (var obj in ActiveWorldObjects.GetAll ().Values)
		{
			if (obj is not Monster monster || !GodotObject.IsInstanceValid (monster))
			{
				continue;
			}

			seen.Add (monster.EntityID);
			if (monster.IsDead)
			{
				awayTicks.Remove (monster.EntityID);
				continue;
			}

			var near = false;
			var pos = monster.GlobalPosition;
			for (var i = 0; i < watchers.Count; i++)
			{
				if (pos.DistanceSquaredTo (watchers[i]) <= radiusSq)
				{
					near = true;
					break;
				}
			}

			if (near)
			{
				awayTicks.Remove (monster.EntityID);
				if (!monster.IsPhysicsProcessing ())
				{
					monster.SetPhysicsProcess (true);
				}

				continue;
			}

			awayTicks.TryGetValue (monster.EntityID, out var ticks);
			if (ticks < GraceTicks)
			{
				ticks++;
			}

			awayTicks[monster.EntityID] = ticks;
			if (ticks >= GraceTicks && monster.IsPhysicsProcessing ())
			{
				monster.SetPhysicsProcess (false);
			}
		}

		if (awayTicks.Count == 0)
		{
			return;
		}

		List<Guid>? drop = null;
		foreach (var id in awayTicks.Keys)
		{
			if (seen.Contains (id))
			{
				continue;
			}

			drop ??= new List<Guid> ();
			drop.Add (id);
		}

		if (drop is null)
		{
			return;
		}

		foreach (var id in drop)
		{
			awayTicks.Remove (id);
		}
	}
}
