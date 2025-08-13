using System;
using System.Threading.Tasks;
using Godot;

namespace SphServer.Client.Networking.Handlers.InGame.ObjectMovement;

public class MoveObjectForClientHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        return;
    }

    public async Task HandleObjectMovement (double x0, double y0, double z0, double t0, ushort entityId)
    {
        // best guess for X and Z: decimal value in packet = 4095 - coord_value, where coord_value is in 0..63 range
        // for Y max value becomes 2047 with the same formula
        // technically, it's not even decimal, as it's possible to move by ~50 units if 0 is sent instead of 4095 
        var xDec = 4095 - (1 - (int) Math.Truncate(x0 - Math.Truncate(x0)) * 64);
        var yDec = 2047 - (int) Math.Truncate((y0 - Math.Truncate(y0)) * 64);
        var zDec = 4095 - (1 - (int) Math.Truncate(z0 - Math.Truncate(z0)) * 64);
        var x = 32768 + (int) x0;
        var y = 1200 + (int) y0;
        var z = 32768 + (int) z0;
        var x_1 = (byte) (((x & 0b1111111) << 1) + 1);
        var x_2 = (byte) ((x & 0b111111110000000) >> 7);
        var y_1 = (byte) (((y & 0b1111111) << 1) + ((x & 0b1000000000000000) >> 15));
        var z_1 = (byte) (((z & 0b11) << 6) + ((y & 0b1111110000000) >> 7));
        var z_2 = (byte) ((z & 0b1111111100) >> 2);
        var z_3 = (byte) ((z & 0b1111110000000000) >> 10);
        var id_1 = (byte) (((entityId & 0b111) << 5) + 0b10001);
        var id_2 = (byte) ((entityId & 0b11111111000) >> 3);
        var id_3 = (byte) ((entityId & 0b1111100000000000) >> 11);
        var xdec_1 = (byte) ((xDec & 0b111111) << 2);
        var ydec_1 = (byte) (((yDec & 0b11) << 6) + ((xDec & 0b111111000000) >> 6));
        var ydec_2 = (byte) ((yDec & 0b1111111100) >> 2);
        var zdec_1 = (byte) (((zDec & 0b111111) << 2) + ((yDec & 0b110000000000) >> 10));
        while (Math.Abs(t0) > 2 * Mathf.Pi)
        {
            t0 -= Math.Sign(t0) * 2 * Mathf.Pi;
        }

        var turn = (int) (t0 * 256 / 2 / Mathf.Pi);

        var turn_1 = (byte) (((turn & 0b11) << 6) + ((zDec & 0b111111000000) >> 6));
        var turn_2 = (byte) ((turn & 0b11111100) >> 2);
        var movePacket = new byte[]
        {
            0x17, 0x00, 0x2c, 0x01, 0x00, x_1, x_2, y_1, z_1, z_2, z_3, 0x2D, id_1, id_2, id_3, 0x6A, 0x10, xdec_1,
            ydec_1, ydec_2, zdec_1, turn_1, turn_2
        };

        clientConnection.MaybeQueueNetworkPacketSend(movePacket);
    }
}