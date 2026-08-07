using System;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

// 37-byte frame, marker 08 40 63 at bytes 13-15. The client sends it about once a second while an
// item on the ground is being dragged, and keeps retrying until the server answers. Nothing routed
// it before, so the client eventually gave up and discarded the item.
public class DragItemOnGroundHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    // Offsets decoded from live captures (2026-07-31, logs/packets_20260731_174827.log): the item id
    // reads back as the entity the server had spawned, and the three floats track the player as the
    // drag moves.
    private const int ItemIdBitOffset = 141;
    private const int PositionBitOffset = 165;

    public async Task Handle (double delta)
    {
        // Dragging arms the same client use-lock as an attack or an item use; without the ack the
        // client stays wedged. See CommonPackets.ClearUseToutAck.
        clientConnection.MaybeScheduleNetworkPacketSend(CommonPackets.ClearUseToutAck(localId));

        var buffer = clientConnection.ReceiveBuffer;
        var stream = new BitStream(buffer);
        stream.ReadBits(ItemIdBitOffset);
        var itemId = stream.ReadUInt16(16);

        stream = new BitStream(buffer);
        stream.ReadBits(PositionBitOffset);
        var x = ReadFloat(stream);
        var y = ReadFloat(stream);
        var z = ReadFloat(stream);

        var item = DbConnection.Items.FindById((int) itemId);
        if (item is null)
        {
            SphLogger.Warning($"Drag on ground: no item {itemId:X4}. Client ID: {localId:X4}");
            return;
        }

        // Client coords -> DB coords: Y and Z are negated, the same convention the spawn uses.
        item.X = x;
        item.Y = -y;
        item.Z = -z;
        DbConnection.Items.Update(item);

        SphLogger.Info($"Drag on ground: {item.Localization[Locale.Russian]} [{itemId:X4}] " +
                       $"to ({x:F2}, {y:F2}, {z:F2}). Client ID: {localId:X4}");
    }

    private static double ReadFloat (BitStream stream)
    {
        return BitConverter.Int32BitsToSingle((int) stream.ReadUInt32(32));
    }
}
