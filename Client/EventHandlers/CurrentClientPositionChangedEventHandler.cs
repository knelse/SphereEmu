using SphServer.Shared.ClientEvents;

namespace SphServer.Client.EventHandlers;

public sealed class CurrentClientPositionChangedEventHandler : IClientEventHandler
{
    private readonly SphereClient sphereClient;

    public CurrentClientPositionChangedEventHandler (SphereClient sphereClient)
    {
        this.sphereClient = sphereClient;
    }

    Task IClientEventHandler.HandleAsync (ClientQueuedEvent clientEvent)
    {
        return HandleAsync((CurrentClientPositionChangedEvent) clientEvent);
    }

    public Task HandleAsync (CurrentClientPositionChangedEvent clientEvent)
    {
        ArgumentNullException.ThrowIfNull(clientEvent);

        var character = sphereClient.CurrentCharacter;
        if (character is null)
        {
            return Task.CompletedTask;
        }

        var area3D = sphereClient.BroadcastArea3D;
        if (area3D is null)
        {
            return Task.CompletedTask;
        }

        foreach (var body in area3D.GetOverlappingBodies())
        {
            var clientNode = body.GetParent();
            if (clientNode is not SphereClient recipient || recipient == sphereClient)
            {
                continue;
            }

            var entityId = recipient.GetLocalObjectId(sphereClient.ID);
            // this is a hack. Client couldn't do the full movement within a single tick, so objects won't be where they
            // actually are if we send only 1 packet. They'd be stuck somewhere in the middle, and only be updated when
            // the next event is raised (which looks dumb and breaks interactability)
            for (var i = 0; i < 4; i++)
            {
                recipient.EnqueueClientEvent(
                    new EntityPositionUpdateEvent(entityId, character.X, -character.Y, -character.Z, character.Angle));
            }
        }

        return Task.CompletedTask;
    }
}