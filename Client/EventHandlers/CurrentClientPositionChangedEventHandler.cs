using System;
using System.Threading.Tasks;
using SphServer.Shared.ClientEvents;

namespace SphServer.Client.EventHandlers;

public sealed class CurrentClientPositionChangedEventHandler : IClientEventHandler
{
    private readonly SphereClient sphereClient;

    public CurrentClientPositionChangedEventHandler (SphereClient sphereClient)
    {
        this.sphereClient = sphereClient;
    }

    public Task HandleAsync (CurrentClientPositionChangedEvent clientEvent)
    {
        ArgumentNullException.ThrowIfNull (clientEvent);
        return Task.CompletedTask;
    }

    Task IClientEventHandler.HandleAsync (ClientQueuedEvent clientEvent) =>
        HandleAsync ((CurrentClientPositionChangedEvent) clientEvent);
}
