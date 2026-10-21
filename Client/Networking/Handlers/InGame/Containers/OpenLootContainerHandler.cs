using System.Threading.Tasks;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;

namespace SphServer.Client.Networking.Handlers.InGame.Containers;

public class OpenLootContainerHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        var containerId = frame[11] + frame[12] * 0x100;
        var bag = DbConnection.ItemContainers.FindById (containerId);
        if (bag is null)
        {
            SphLogger.Warning ($"Open loot bag: {containerId:X4} not found. Client ID: {localId:X4}");
            return;
        }

        foreach (var packet in bag.GetContentsPackets ())
        {
            clientConnection.MaybeScheduleNetworkPacketSend (packet);
        }
    }
}
