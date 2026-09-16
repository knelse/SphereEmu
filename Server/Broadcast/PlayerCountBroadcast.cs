using System.Linq;
using SphServer.Client;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Broadcast;

/// <summary>
///     Mirrors _stat.Server / EInit: publish online player count on module tag 4.
///     Region 61 once per client (EInit), then region 0 whenever the in-game count changes.
/// </summary>
public static class PlayerCountBroadcast
{
    private static int lastPublished = -1;

    public static int CurrentInGameCount() =>
        ActiveClients.GetAll().Values.Count(c =>
            !c.IsAdminDebugDummy && c.ClientStateManager.IsInGameState());

    /// <summary>
    ///     Client just reached in-game: EInit (r61) to them, then region-0 update to everyone.
    /// </summary>
    public static void OnClientEnteredWorld(SphereClient client)
    {
        var count = CurrentInGameCount();
        client.MaybeQueueNetworkPacketSend(CommonPackets.PublishPlayerCount(count, eInit: true));
        PublishRegion0(count, force: true);
    }

    public static void OnClientLeftWorld()
    {
        PublishRegion0(CurrentInGameCount(), force: false);
    }

    private static void PublishRegion0(int count, bool force)
    {
        if (!force && count == lastPublished)
        {
            return;
        }

        lastPublished = count;
        var packet = CommonPackets.PublishPlayerCount(count, eInit: false);
        foreach (var client in ActiveClients.GetAll().Values.ToList())
        {
            if (client.IsAdminDebugDummy || !client.ClientStateManager.IsInGameState())
            {
                continue;
            }

            client.MaybeQueueNetworkPacketSend(packet);
        }
    }
}
