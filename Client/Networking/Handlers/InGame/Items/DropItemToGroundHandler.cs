using System.Threading.Tasks;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

public class DropItemToGroundHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        // TODO: tbd
    }
}