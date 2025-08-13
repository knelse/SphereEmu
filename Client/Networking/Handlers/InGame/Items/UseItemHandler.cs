using System.Threading.Tasks;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class UseItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        var itemId = (ushort) (clientConnection.ReceiveBuffer[11] + clientConnection.ReceiveBuffer[12] * 0x100);
        return;
    }
}