using System.Threading.Tasks;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame.ObjectMovement;

public class MoveObjectForClientHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
    }

    public async Task HandleObjectMovement (double x0, double y0, double z0, double t0, ushort entityId)
    {
        var movePacket = CommonPackets.BuildMoveObjectPacket(x0, y0, z0, t0, entityId);
        clientConnection.MaybeScheduleNetworkPacketSend(movePacket);
    }
}