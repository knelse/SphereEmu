using System.Threading.Tasks;
using SphServer.Shared.Db;

namespace SphServer.Client.Networking.Handlers.InGame.Containers;

public class OpenLootContainerHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        var containerId = clientConnection.ReceiveBuffer[11] + clientConnection.ReceiveBuffer[12] * 0x100;
        var bag = DbConnection.ItemContainers.Include(x => x.Contents).FindById(containerId);
        if (bag is not null)
        {
            bag.ShowItemListForClient(localId);
            var packet = bag.GetContentsPacket(localId);
            packet[6] = 0x04;
            clientConnection.MaybeQueueNetworkPacketSend(packet);
        }
    }
}