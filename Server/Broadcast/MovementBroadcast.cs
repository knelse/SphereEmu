using Godot;
using SphServer.Client;
using SphServer.Shared.Logger;

namespace SphServer.Server.Broadcast;

public static class MovementBroadcast
{
    public static void BroadcastMovementToNearbyClients (SphereClient movingClient, Area3D visibilityArea)
    {
        if (movingClient.CurrentCharacter is null)
        {
            return;
        }

        var nearbyBodies = visibilityArea.GetOverlappingBodies();

        foreach (var body in nearbyBodies)
        {
            var clientNode = body?.GetParent();
            if (clientNode is not SphereClient targetClient)
            {
                continue;
            }

            // Skip broadcasting to self
            if (targetClient.GetInstanceId() == movingClient.GetInstanceId())
            {
                continue;
            }

            var movingCharacter = movingClient.CurrentCharacter;
            var newPosition = movingCharacter.Origin;

            targetClient.ScheduleObjectMovement(newPosition.X, newPosition.Y, newPosition.Z, movingCharacter.Angle,
                targetClient.localId);

            SphLogger.Info(
                $"Movement broadcast: Client {movingClient.localId:X4} moved -> broadcasting to Client {targetClient.localId:X4}. "
                + $"New position: {newPosition.X:F1} | {newPosition.Y:F1} | {newPosition.Z:F1} | {movingClient.Angle}");
        }
    }
}