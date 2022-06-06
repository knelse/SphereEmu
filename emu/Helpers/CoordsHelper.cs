namespace emu.Helpers;

public class WorldCoords
{
    public double x;
    public double y;
    public double z;
    public double turn;

    public WorldCoords(double x1, double y1, double z1, double turn1 = 0)
    {
        x = x1;
        y = y1;
        z = z1;
        turn = turn1;
    }

    public static WorldCoords ShipstoneCenter => new WorldCoords( 2614, 157, 1293 );
    public static WorldCoords UmradCenter => new WorldCoords( -1993, -106, 457 );
    public static WorldCoords Test => new WorldCoords( -7, 19, 2987 );

    public string ToDebugString()
    {
        return "X: " + (double) x + " Y: " + (double) y +  " Z: " + (double) z + " Turn: " + (int) turn;
    }
}
public static class CoordsHelper
{
    public static byte[] EncodeServerCoordinate(double a)
    {
        var scale = 69;
        
        var a_abs = Math.Abs(a);
        var a_temp = a_abs;
        
        var steps = 0;

        if (((int)a_abs) == 0)
        {
            scale = 58;
        }

        else if (a_temp < 2048)
        {
            while (a_temp < 2048)
            {
                a_temp *= 2;
                steps += 1;
            }

            scale -= (steps + 1) / 2;

            if (scale < 0)
            {
                scale = 58;
            }
        }
        else
        {
            while (a_temp > 4096)
            {
                a_temp /= 2;
                steps += 1;
            }

            scale += steps / 2;
        }

        var a_3 = (byte) (((a < 0 ? 1 : 0) << 7) + scale);
        var mul = Math.Pow(2, ((int)Math.Log2(a_abs)));
        var numToEncode = (int)(0b100000000000000000000000 * (a_abs / mul + 1));

        var a_2 = (byte) (((numToEncode & 0b111111111111111100000000) >> 16) + (steps % 2 == 1 ? 0b10000000 : 0));
        var a_1 = (byte)((numToEncode & 0b1111111100000000) >> 8);
        var a_0 = (byte) (numToEncode & 0b11111111);

        return new [] { a_0, a_1, a_2, a_3};
    }
    public static int DecodeServerCoordinate(byte[] a)
    {
        var steps = (5 - a[2] & 0b111) * 2;

        if ((a[1] & 0b10000000) > 0)
        {
            steps -= 1;
        }

        var a_last4 = (a[0] & 0b11110000) >> 4;
        var a_next7 = (a[1] & 0b01111111) << 4;
        var a_first1 = (a[2] & 0b01000000) << 5;

        var mul = (int) (Math.Pow(2, steps));

        return (a_first1 + a_next7 + a_last4) / mul * ((a[2] & 0b10000000) > 0 ? -1 : 1);
    }

    public static double DecodeClientCoordinate(byte[] a)
    {
        var x_scale = ((a[4] & 0b11111) << 3) + ((a[3] & 0b11100000) >> 5);

        if (x_scale == 126)
        {
            return 0.0;
        }
        
        var baseCoord = Math.Pow(2, x_scale - 127);
        var sign = (a[4] & 0b100000) > 0 ? -1 : 1;
        return ((1 + ((float)(((a[3] & 0b11111) << 18) + (a[2] << 10) + (a[1] << 2) +
                      ((a[0] & 0b11000000) >> 6))) / 0b100000000000000000000000) * baseCoord) * sign;
    }

    public static WorldCoords GetCoordsFromPingBytes(byte[] rcvBuffer)
    {
        var x = CoordsHelper.DecodeClientCoordinate(rcvBuffer[21..26]);
        var y = CoordsHelper.DecodeClientCoordinate(rcvBuffer[25..30]);
        var z = CoordsHelper.DecodeClientCoordinate(rcvBuffer[29..34]);
        var turn = CoordsHelper.DecodeClientCoordinate(rcvBuffer[33..38]);

        return new WorldCoords(x, y, z, turn);
    }
}