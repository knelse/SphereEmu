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
    /// Per client so local ids resolve, and subclasses only get the lifecycle broadcasts below
    /// </summary>
    private void BroadcastToVisibleClients (Func<SphereClient, byte[]> buildFrameForClient)
    {
        if (_visibleClients.Count == 0)
        {
            return;
        }

        var staleClients = new List<SphereClient> ();
        foreach (var client in _visibleClients)
        {
            if (!GodotObject.IsInstanceValid (client))
            {
                staleClients.Add (client);
                continue;
            }

            client.MaybeQueueNetworkPacketSend (buildFrameForClient (client));
        }

        foreach (var client in staleClients)
        {
            _visibleClients.Remove (client);
        }
    }

    /// <summary>
    /// Classic entity_killed (INTERACT+0x040D); MBC EKill is not this frame, and despawn follows
    /// next frame
    /// </summary>
    protected void BroadcastDeathSignalToVisibleClients (ushort killerGlobalId)
    {
        BroadcastToVisibleClients (client =>
            CommonPackets.EntityKilled (client.GetLocalObjectId (ID), client.GetLocalObjectId (killerGlobalId)));
    }

    /// <summary>
    /// Unregister never despawns, so a freed node would leave a ghost until relog
    /// </summary>
    protected void BroadcastDespawnToVisibleClients ()
    {
        BroadcastToVisibleClients (client => CommonPackets.DespawnEntity (client.GetLocalObjectId (ID)));
        _visibleClients.Clear ();
    }

    /// <summary>
    /// _ExitTree only drops visibility, so the registry would keep a freed node
    /// </summary>
    protected void RemoveFromWorldRegistry ()
    {
        ActiveWorldObjects.Remove (ID);
        ActiveNodes.Remove (GetInstanceId ());
    }
}
