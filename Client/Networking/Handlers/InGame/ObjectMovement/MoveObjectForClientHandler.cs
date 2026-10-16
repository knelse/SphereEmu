using System.Threading.Tasks;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Client.Networking.Handlers.InGame.ObjectMovement;

public class MoveObjectForClientHandler (ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
    }

    public async Task HandleObjectMovement (double x0, double y0, double z0, double t0, Guid entityId,
        ushort moduleTag)
    {
        var movePacket = CommonPackets.BuildMoveObjectPacket (x0, y0, z0, t0, entityId, moduleTag);
        clientConnection.MaybeScheduleNetworkPacketSend (movePacket);
    }
}
