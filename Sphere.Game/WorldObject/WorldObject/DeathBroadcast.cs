using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

public partial class WorldObject
{
	/// <summary>
	///     Builds the frame per client so client-local ids resolve. Private on purpose: subclasses
	///     get only the specific lifecycle broadcasts below, not an arbitrary-frame sender.
	/// </summary>
	private void BroadcastToVisibleClients (Func<SphereClient, byte[]> buildFrameForClient)
	{
		if (_visibleClients.Count == 0)
		{
			return;
		}

		var staleClients = new List<SphereClient>();
		foreach (var client in _visibleClients)
		{
			if (!GodotObject.IsInstanceValid(client))
			{
				staleClients.Add(client);
				continue;
			}

			client.MaybeQueueNetworkPacketSend(buildFrameForClient(client));
		}

		foreach (var client in staleClients)
		{
			_visibleClients.Remove(client);
		}
	}

	/// <summary>Sends entity_killed to every viewer so all of them play the death animation, not just the killer.</summary>
	protected void BroadcastDeathSignalToVisibleClients (ushort killerGlobalId)
	{
		BroadcastToVisibleClients(client =>
			CommonPackets.EntityKilled(client.GetLocalObjectId(ID), client.GetLocalObjectId(killerGlobalId)));
	}

	/// <summary>
	///     Required on death: <see cref="WorldObjectVisibilityManager.Unregister" /> never despawns,
	///     so a freed node would otherwise leave a ghost on every viewer until relog.
	/// </summary>
	protected void BroadcastDespawnToVisibleClients ()
	{
		BroadcastToVisibleClients(client => CommonPackets.DespawnEntity(client.GetLocalObjectId(ID)));
		_visibleClients.Clear();
	}

	/// <summary>
	///     <see cref="_ExitTree" /> only unregisters visibility; without this the registry entries
	///     would dangle at a freed node. Call before QueueFree.
	/// </summary>
	protected void RemoveFromWorldRegistry ()
	{
		ActiveWorldObjects.Remove(ID);
		ActiveNodes.Remove(GetInstanceId());
	}
}
