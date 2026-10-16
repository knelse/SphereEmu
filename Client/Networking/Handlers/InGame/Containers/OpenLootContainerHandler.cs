using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Shared.Db;

namespace SphServer.Client.Networking.Handlers.InGame.Containers;

public class OpenLootContainerHandler (Guid entityId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        var containerId = ReadGuid (frame, 11 * 8);
        var bag = DbConnection.ItemContainers.Include (x => x.Contents).FindById (containerId);
        if (bag is not null)
        {
            bag.ShowItemListForClient (entityId);
            var packet = bag.GetContentsPacket (entityId);
            packet[6] = 0x04;
            clientConnection.MaybeScheduleNetworkPacketSend (packet);
        }
    }

    private static Guid ReadGuid (byte[] frame, int bit)
    {
        if (frame.Length * 8 < bit + 128)
        {
            return Guid.Empty;
        }

        var stream = new BitStream (frame);
        stream.ReadBits (bit);
        return stream.ReadGuid ();
    }
}
