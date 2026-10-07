using System;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

// 08 40 63 at bytes 13-15; the client retries about once a second until this is answered
public class DragItemOnGroundHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    // Item id is the spawned entity; the three floats track the player during the drag
    private const int ItemIdBitOffset = 141;
    private const int PositionBitOffset = 165;

    public async Task Handle (byte[] frame, double delta)
    {
        // Dragging arms g_6008, so this ack clears it or the client stays wedged
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));

        var buffer = frame;
        var stream = new BitStream (buffer);
        stream.ReadBits (ItemIdBitOffset);
        var itemId = stream.ReadUInt16 (16);

        stream = new BitStream (buffer);
        stream.ReadBits (PositionBitOffset);
        var x = ReadFloat (stream);
        var y = ReadFloat (stream);
        var z = ReadFloat (stream);

        var item = DbConnection.Items.FindById ((int) itemId);
        if (item is null)
        {
            SphLogger.Warning ($"Drag on ground: no item {itemId:X4}. Client ID: {localId:X4}");
            return;
        }

        // Client coords -> DB coords: Y and Z are negated, the same convention the spawn uses.
        item.X = x;
        item.Y = -y;
        item.Z = -z;
        DbConnection.Items.Update (item);

        SphLogger.Info ($"Drag on ground: {item.Localization.GetValueOrDefault (Locale.Russian, "?")} [{itemId:X4}] " +
                       $"to ({x:F2}, {y:F2}, {z:F2}). Client ID: {localId:X4}");
    }

    private static double ReadFloat (BitStream stream)
    {
        return BitConverter.Int32BitsToSingle ((int) stream.ReadUInt32 (32));
    }
}
