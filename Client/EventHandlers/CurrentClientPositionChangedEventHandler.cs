using System;
using System.Threading.Tasks;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.World;
using SphServer.Server.Config;
using SphServer.Shared.ClientEvents;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.EventHandlers;

public sealed class CurrentClientPositionChangedEventHandler : IClientEventHandler
{
    private readonly SphereClient sphereClient;

    public CurrentClientPositionChangedEventHandler(SphereClient sphereClient)
    {
        this.sphereClient = sphereClient;
    }

    Task IClientEventHandler.HandleAsync(ClientQueuedEvent clientEvent)
    {
        return HandleAsync((CurrentClientPositionChangedEvent)clientEvent);
    }

    public Task HandleAsync(CurrentClientPositionChangedEvent clientEvent)
    {
        ArgumentNullException.ThrowIfNull(clientEvent);

        var character = sphereClient.CurrentCharacter;
        if (character is null)
        {
            return Task.CompletedTask;
        }

        sphereClient.UpdateCoordinatesInWorld();

        var visibilityRadius = ServerConfig.AppConfig.ObjectVisibilityDistance;
        var visibilityRadiusSq = visibilityRadius * visibilityRadius;
        var moverGodot = ClientWorldPosition.GetGodotWorldPosition(sphereClient);

        foreach (var recipient in ActiveClients.GetAll().Values)
        {
            if (recipient == sphereClient || recipient.IsAdminDebugDummy || recipient.CurrentCharacter is null)
            {
                continue;
            }

            if (!recipient.ClientStateManager.IsInGameState())
            {
                continue;
            }

            var recipientPos = ClientWorldPosition.GetGodotWorldPosition(recipient);
            if (moverGodot.DistanceSquaredTo(recipientPos) > visibilityRadiusSq)
            {
                continue;
            }

            var entityId = recipient.GetLocalObjectId(sphereClient.ID);
            // Client lerps movement across ticks; one packet leaves them mid-path until the next update.
            for (var i = 0; i < 4; i++)
            {
                recipient.EnqueueClientEvent(
                    new EntityPositionUpdateEvent(entityId, character.X, -character.Y, -character.Z, character.Angle));
            }
        }

        MonsterSpawnerActivationManager.NotifyClientPosition(sphereClient);
        AlchemyMaterialSpawnerActivationManager.NotifyClientPosition(sphereClient);
        WorldObjectVisibilityManager.NotifyClientPosition(sphereClient);
        SphServer.Server.SphereServer.ServerNode?.WorldChunks?.NotifyClientPosition(sphereClient);
        SphServer.Server.SphereServer.ServerNode?.TerrainGround?.NotifyClientPosition(sphereClient);

        return Task.CompletedTask;
    }
}
