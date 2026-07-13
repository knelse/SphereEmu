using System.Threading.Tasks;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class UseItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        var itemId = (ushort) (clientConnection.ReceiveBuffer[11] + clientConnection.ReceiveBuffer[12] * 0x100);
        // Attack-wedge fix: item use arms the same client use-lock (g_6008) as an attack, so clear it here
        // too. Without this the client wedges permanently after using an item. See CommonPackets.ClearUseToutAck.
        clientConnection.MaybeScheduleNetworkPacketSend(CommonPackets.ClearUseToutAck(localId));
        return;
    }
}