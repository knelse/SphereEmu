using System;
using System.Threading.Tasks;
using SphServer.Shared.ClientEvents;
using SphServer.Shared.Networking;

namespace SphServer.Client.EventHandlers;

public sealed class EntityPositionUpdateEventHandler : IClientEventHandler
{
    private readonly SphereClient sphereClient;

    public EntityPositionUpdateEventHandler (SphereClient sphereClient)
    {
        this.sphereClient = sphereClient;
    }

    public async Task HandleAsync (EntityPositionUpdateEvent clientEvent)
    {
        var packet = CommonPackets.BuildMoveObjectPacket (
            clientEvent.X,
            clientEvent.Y,
            clientEvent.Z,
            clientEvent.Angle,
            clientEvent.EntityId);
        sphereClient.MaybeQueueNetworkPacketSend (packet);
    }

    Task IClientEventHandler.HandleAsync (ClientQueuedEvent clientEvent) =>
        HandleAsync ((EntityPositionUpdateEvent) clientEvent);
}
