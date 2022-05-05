namespace emu.Helpers;

public class WorldCoords
{
    public int x;
    public int y;
    public int z;

    public WorldCoords(int x1, int y1, int z1)
    {
        x = x1;
        y = y1;
        z = z1;
    }

    public static WorldCoords ShipstoneCenter => new WorldCoords( 2614, 157, 1293 );
    public static WorldCoords UmradCenter => new WorldCoords( -1993, -106, 457 );
    public static WorldCoords Test => new WorldCoords( -7, 19, 2987 );
}
public static class CoordsHelper
{
    public static byte[] EncodeServerCoordinate(int a)
    {
        if (a == 0)
        {
            a = 2;
        }
        else if (Math.Abs(a) <= 1)
        {
            // Seems to be the only edge cases for this, I'll figure it out later, +1 step is good enough
            a = 2 * Math.Sign(a);
        }

        var a_normalized = Math.Abs(a);
        var scale = 0b101;
        var steps = 0;

        while (a_normalized < 2048)
        {
            a_normalized *= 2;
            steps += 1;
        }

        scale -= (steps + 1) / 2;

        if (scale < 0)
        {
            scale = 0;
        }
        
        var a_firstByte = (byte)(((a_normalized & 0b1111) << 4) + 0b0110);
        var a_secondByte = (byte)(((a_normalized >> 4) & 0b1111111) + (steps % 2 == 1 ? 0b10000000 : 0));
        var a_thirdbyte = (byte)(((a < 0 ? 1 : 0) << 7) + (((a_normalized >> 11) % 2 == 1 ? 1 : 0) << 6) + scale);
        return new[] { a_firstByte, a_secondByte, a_thirdbyte };
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
        return (((float)(((a[3] & 0b11111) << 18) + (a[2] << 10) + (a[1] << 2) +
                      ((a[0] & 0b11000000) >> 6))) / 0b100000000000000000000000 * baseCoord + baseCoord) * sign;
    }
}