using System;
using System.Threading.Tasks;
using Godot;
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

        foreach (var body in area3D.GetOverlappingBodies ())
        {
            var clientNode = body.GetParent ();
            if (clientNode is not SphereClient recipient || recipient == sphereClient)
            {
                continue;
            }

            var entityId = recipient.GetLocalObjectId (sphereClient.ID);
            recipient.EnqueueClientEvent (
                new EntityPositionUpdateEvent (entityId, character.X, character.Y, character.Z, character.Angle));
        }

        return Task.CompletedTask;
    }

    Task IClientEventHandler.HandleAsync (ClientQueuedEvent clientEvent) =>
        HandleAsync ((CurrentClientPositionChangedEvent) clientEvent);
}
