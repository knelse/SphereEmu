using SphServer.Helpers;

namespace PacketLogViewer;

/// <summary>
/// Builds the server→client teleport subpacket bytes (same layout as CharacterDbEntrySerializer.GetTeleportByteArray).
/// </summary>
public static class TeleportPacketBuilder
{
    public static byte[] BuildTeleportPacket(ushort clientIndex, WorldCoords coords)
    {
        var x = CoordsHelper.EncodeServerCoordinate(coords.x);
        var y = CoordsHelper.EncodeServerCoordinate(coords.y);
        var z = CoordsHelper.EncodeServerCoordinate(coords.z);
        var t = CoordsHelper.EncodeServerCoordinate(coords.turn);
        var x_1 = ((x[0] & 0b111) << 5) + 0b00010;
        var x_2 = ((x[1] & 0b111) << 5) + ((x[0] & 0b11111000) >> 3);
        var x_3 = ((x[2] & 0b111) << 5) + ((x[1] & 0b11111000) >> 3);
        var x_4 = ((x[3] & 0b111) << 5) + ((x[2] & 0b11111000) >> 3);
        var y_1 = ((y[0] & 0b111) << 5) + ((x[3] & 0b11111000) >> 3);
        var y_2 = ((y[1] & 0b111) << 5) + ((y[0] & 0b11111000) >> 3);
        var y_3 = ((y[2] & 0b111) << 5) + ((y[1] & 0b11111000) >> 3);
        var y_4 = ((y[3] & 0b111) << 5) + ((y[2] & 0b11111000) >> 3);
        var z_1 = ((z[0] & 0b111) << 5) + ((y[3] & 0b11111000) >> 3);
        var z_2 = ((z[1] & 0b111) << 5) + ((z[0] & 0b11111000) >> 3);
        var z_3 = ((z[2] & 0b111) << 5) + ((z[1] & 0b11111000) >> 3);
        var z_4 = ((z[3] & 0b111) << 5) + ((z[2] & 0b11111000) >> 3);
        var t_1 = ((t[0] & 0b111) << 5) + ((z[3] & 0b11111000) >> 3);
        var t_2 = ((t[1] & 0b111) << 5) + ((t[0] & 0b11111000) >> 3);
        var t_3 = ((t[2] & 0b111) << 5) + ((t[1] & 0b11111000) >> 3);
        var t_4 = ((t[3] & 0b111) << 5) + ((t[2] & 0b11111000) >> 3);
        var t_5 = 0b10100000 + ((t[3] & 0b11111000) >> 3);

        return new byte[]
        {
            0x1F, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(clientIndex),
            MinorByte(clientIndex), 0x08, 0x40, 0xE3,
            0x01,
            (byte) x_1, (byte) x_2, (byte) x_3, (byte) x_4, (byte) y_1, (byte) y_2, (byte) y_3, (byte) y_4, (byte) z_1,
            (byte) z_2, (byte) z_3, (byte) z_4, (byte) t_1, (byte) t_2, (byte) t_3, (byte) t_4, (byte) t_5, 0x00
        };
    }

    private static byte MinorByte(ushort input) => (byte)(input & 0xFF);

    private static byte MajorByte(ushort input) => (byte)(input >> 8);
}
