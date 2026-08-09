using System.Threading.Tasks;
using SphServer.Shared.Logger;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Communities;

public class GroupActionsHandler(ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        var clientlocalId = (ushort) ((frame[11] << 8) + frame[12]);
        var action = frame[17];
        switch (action)
        {
            case 0x00:
            {
                // create
                SphLogger.Info($"[{clientlocalId:X}] Create group");
                var createResponse = new byte[]
                {
                    0x13, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(clientlocalId),
                    MinorByte(clientlocalId), 0x08, 0x40, 0x23, 0x23, 0xA0, 0xA0, 0x91, 0x11, 0x90, 0x00
                };
                clientConnection.MaybeScheduleNetworkPacketSend(createResponse);
                break;
            }
            case 0x80:
                // leave or disband
                SphLogger.Info($"[{clientlocalId:X}] Leave group");
                var leaveResponse = new byte[]
                {
                    0x0F, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(clientlocalId),
                    MinorByte(clientlocalId), 0x08, 0x40, 0x23, 0x23, 0xA0, 0x01
                };
                clientConnection.MaybeScheduleNetworkPacketSend(leaveResponse);
                break;
        }
    }
}