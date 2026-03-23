using System;
using System.Threading.Tasks;
using SphServer.Shared.ClientEvents;

namespace SphServer.Client.EventHandlers;

public sealed class EntityPositionUpdateEventHandler : IClientEventHandler
{
    private readonly SphereClient sphereClient;

    public EntityPositionUpdateEventHandler (SphereClient sphereClient)
    {
        this.sphereClient = sphereClient;
    }

    public Task HandleAsync (EntityPositionUpdateEvent clientEvent)
    {
        ArgumentNullException.ThrowIfNull (clientEvent);
        return Task.CompletedTask;
    }

    Task IClientEventHandler.HandleAsync (ClientQueuedEvent clientEvent) =>
        HandleAsync ((EntityPositionUpdateEvent) clientEvent);
}
