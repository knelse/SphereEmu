using System;
using System.Threading.Tasks;
using SphereHelpers.Extensions;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Logger;

namespace SphServer.Client.Networking.Handlers.InGame.Communities;

public class GroupActionsHandler (ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        if (frame.Length < 46)
        {
            return;
        }

        var entityId = new Guid (frame.AsSpan (11, 16));
        var action = SphServer.Helpers.Networking.ClientPacketClassifier.ShiftedByte (frame, 31);
        switch (action)
        {
            case 0x00:
                {
                    // create
                    SphLogger.Info ($"[{entityId}] Create group");
                    clientConnection.MaybeScheduleNetworkPacketSend (BuildGroupPacket (entityId,
                    [
                        0x08, 0x40, 0x23, 0x23, 0xA0, 0xA0, 0x91, 0x11, 0x90, 0x00
                    ]));
                    break;
                }
            case 0x80:
                // leave or disband
                SphLogger.Info ($"[{entityId}] Leave group");
                clientConnection.MaybeScheduleNetworkPacketSend (BuildGroupPacket (entityId,
                [
                    0x08, 0x40, 0x23, 0x23, 0xA0, 0x01
                ]));
                break;
        }
    }

    private static byte[] BuildGroupPacket (Guid entityId, byte[] tail)
    {
        var stream = SphBitStream.GetWriteBitStream ();
        stream.WriteByte (0, 1);
        stream.WriteUInt16 (0, 15);
        stream.WriteGuid (entityId);
        stream.WriteBytes (tail, tail.Length, true);
        return Packet.ToByteArray (stream.GetStreamData (), 1);
    }
}
