using Godot;
using SphServer.Client;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Server.Config;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Server.Simulation;

public sealed class WorldSimulation
{
	public const int RegenPeriodTicks = 360;

	public const int WorldDatagramPeriodTicks = 1;

	private readonly Dictionary<Guid, PlayerInputSlot> players = new ();

	private readonly Dictionary<Guid, PlayerCombatState> combatStates = new ();

	private readonly Dictionary<Guid, CharacterVitalRegen> regenByPlayer = new ();

	private readonly Dictionary<Guid, byte[]> worldDatagrams = new ();

	private readonly List<CombatResult> combatResults = new ();

	private readonly PersistenceQueue writer = new ();

	private readonly MonsterActivity monsters = new ();

	private readonly Random attackRng = new ();

	private int regenTicks;

	public ulong Tick { get; private set; }

	public double SimulatedTime { get; private set; }

	public void Enqueue (Guid playerId, PlayerCommand command)
	{
		if (!players.TryGetValue (playerId, out var slot))
		{
			slot = new PlayerInputSlot ();
			players.Add (playerId, slot);
		}

		PlayerInputBuffer.Enqueue (slot, Tick, command);
	}

	public bool AcceptCommandDatagram (Guid playerId, byte[] payload)
	{
		var decoded = new List<PlayerCommand> ();
		if (!CommandDatagram.TryDecode (payload, decoded))
		{
			return false;
		}

		if (!players.TryGetValue (playerId, out var slot))
		{
			slot = new PlayerInputSlot ();
			players.Add (playerId, slot);
		}

		PlayerInputBuffer.ApplyLatest (slot, Tick, decoded);
		return true;
	}

	public bool TryGetWorldDatagram (Guid playerId, out byte[] datagram)
	{
		if (worldDatagrams.TryGetValue (playerId, out var stored) && stored is not null)
		{
			datagram = stored;
			return true;
		}

		datagram = [];
		return false;
	}

	public void EnqueueWriter (WriterAction action)
	{
		writer.Enqueue (action);
	}

	public void Step ()
	{
		var tick = Tick;
		var time = SimulatedTime;
		SimulationClock.Advance (ref tick, ref time);
		Tick = tick;
		SimulatedTime = time;

		foreach (var slot in players.Values)
		{
			PlayerInputBuffer.Step (slot, Tick);
		}

		StepPlayers ();
		StepRegen ();
		monsters.Step ();
		if (Tick % WorldDatagramPeriodTicks == 0)
		{
			WriteWorldDatagrams ();
		}
	}

	public void DrainWriter ()
	{
		var actions = writer.Take ();
		var hits = TakeCombatResults ();
		PersistenceQueue.ApplyBatch (actions, hits, Tick);
	}

	private void StepPlayers ()
	{
		foreach (var client in ActiveClients.GetAll ().Values)
		{
			if (!PlayerMotion.IsInGame (client))
			{
				continue;
			}

			if (!players.TryGetValue (client.EntityID, out var slot))
			{
				continue;
			}

			PlayerMotion.Apply (client, slot);
			var body = PlayerMotion.FindModel (client);
			if (body is null)
			{
				continue;
			}

			if (!combatStates.TryGetValue (client.EntityID, out var combat))
			{
				combat = new PlayerCombatState ();
				combatStates.Add (client.EntityID, combat);
			}

			PlayerCombat.Step (client, body, slot, combat, attackRng, combatResults);
		}
	}

	private void StepRegen ()
	{
		regenTicks++;
		if (regenTicks < RegenPeriodTicks)
		{
			return;
		}

		regenTicks = 0;
		var live = new HashSet<Guid> ();
		foreach (var client in ActiveClients.GetAll ().Values)
		{
			if (!PlayerMotion.IsInGame (client) || client.CurrentCharacter is not { } character)
			{
				continue;
			}

			var id = client.EntityID;
			live.Add (id);
			if (!regenByPlayer.TryGetValue (id, out var regen))
			{
				regen = new CharacterVitalRegen ();
				regenByPlayer.Add (id, regen);
			}

			if (regen.ApplyOnce (character))
			{
				writer.Enqueue (new WriterAction (WriterActionKind.SaveCharacter, id, "regen"));
			}
		}

		List<Guid>? drop = null;
		foreach (var id in regenByPlayer.Keys)
		{
			if (live.Contains (id))
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
			regenByPlayer.Remove (id);
		}
	}

	private void WriteWorldDatagrams ()
	{
		var radius = ServerConfig.AppConfig.ObjectVisibilityDistance;
		var radiusSq = radius * radius;
		var live = new HashSet<Guid> ();
		foreach (var client in ActiveClients.GetAll ().Values)
		{
			if (!PlayerMotion.IsInGame (client))
			{
				continue;
			}

			var body = PlayerMotion.FindModel (client);
			if (body is null)
			{
				continue;
			}

			live.Add (client.EntityID);
			var origin = body.GlobalPosition;
			var ack = players.TryGetValue (client.EntityID, out var slot) ? slot.LastApplied : 0u;
			var entities = new List<WorldEntity> ();
			foreach (var obj in ActiveWorldObjects.GetAll ().Values)
			{
				if (!GodotObject.IsInstanceValid (obj) || obj.EntityID == client.EntityID)
				{
					continue;
				}

				if (!TryEntity (obj, origin, radiusSq, out var entity))
				{
					continue;
				}

				entities.Add (entity);
			}

			var velocity = body.Velocity;
			var owner = new WorldOwner
			{
				PosX = origin.X,
				// The client draws the glb 0.065 lower than the swept box base, so the pose it
				// receives is lifted and the box it never draws keeps the floor line
				PosY = SphereClient.ClientGodotY (origin.Y),
				PosZ = origin.Z,
				Yaw = body.Rotation.Y,
				VelX = velocity.X,
				VelY = velocity.Y,
				VelZ = velocity.Z
			};
			worldDatagrams[client.EntityID] = WorldDatagram.Write (Tick, ack, owner, entities);
		}

		List<Guid>? drop = null;
		foreach (var id in worldDatagrams.Keys)
		{
			if (live.Contains (id))
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
			worldDatagrams.Remove (id);
		}
	}

	private static bool TryEntity (WorldObject obj, Vector3 origin, float radiusSq, out WorldEntity entity)
	{
		entity = default;
		if (obj is Monster monster)
		{
			var pos = monster.GlobalPosition;
			if (pos.DistanceSquaredTo (origin) > radiusSq)
			{
				return false;
			}

			entity = new WorldEntity
			{
				Id = monster.EntityID,
				PosX = pos.X,
				PosY = pos.Y,
				PosZ = pos.Z,
				Yaw = monster.Rotation.Y,
				Hp = monster.CurrentHp,
				Anim = 0
			};
			return true;
		}

		if (obj is SphereClient other && PlayerMotion.IsInGame (other) && other.CurrentCharacter is { } character)
		{
			var otherBody = PlayerMotion.FindModel (other);
			var pos = otherBody?.GlobalPosition ?? other.GlobalPosition;
			if (pos.DistanceSquaredTo (origin) > radiusSq)
			{
				return false;
			}

			entity = new WorldEntity
			{
				Id = other.EntityID,
				PosX = pos.X,
				PosY = pos.Y,
				PosZ = pos.Z,
				Yaw = otherBody?.Rotation.Y ?? (float) character.Angle,
				Hp = character.CurrentHP,
				Anim = 0
			};
			return true;
		}

		return false;
	}

	private List<CombatResult> TakeCombatResults ()
	{
		if (combatResults.Count == 0)
		{
			return new List<CombatResult> ();
		}

		var batch = new List<CombatResult> (combatResults);
		combatResults.Clear ();
		return batch;
	}
}
