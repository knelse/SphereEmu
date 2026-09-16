using System;

namespace SphServer.Shared.Networking.Mbc;

/// <summary>
///     Client <c>SferaMbcBitStream::encodeCoordinate</c> / angle format <c>'l'</c>
///     (<c>semantic_classes.cpp</c>). Used for msg300 TransformUpdate (region 1).
/// </summary>
public static class MbcCoordEncoding
{
    private const double Inverse40 = 1.0 / 40.0;
    // Exact float literal from the client: 0.0062500000931322575f
    private const double MinimumInverse = 0.0062500000931322575;
    // 256 / (2π), client writeField('l') scale
    private const double AngleScale = 40.7436637878418;
    private const double TwoPi = 6.2831854820251465;

    /// <summary>
    ///     12-bit nonlinear delta from integer origin. Magnitude must be &lt; 120.
    ///     Bit 11 set when coordinate &lt; origin.
    /// </summary>
    public static ushort EncodeCoordinate(int origin, float coordinate)
    {
        var magnitude = Math.Abs((double)coordinate - origin);
        if (!double.IsFinite(magnitude) || magnitude >= 120.0)
        {
            // At-origin code: client decode of 2047 is distance 0
            return 2047;
        }

        var inverse = 1.0 / (magnitude + 40.0);
        var normalized = (inverse - MinimumInverse) / (Inverse40 - MinimumInverse);
        var code = (ushort)Math.Truncate(normalized * 2047.0);
        if (coordinate < origin)
        {
            code |= 0x800;
        }

        return code;
    }

    /// <summary>
    ///     Angle as raw u8: trunc(radians * 256 / 2π) &amp; 0xFF after wrapping negatives.
    /// </summary>
    public static byte EncodeAngle(double angleRadians)
    {
        var angle = (float)angleRadians;
        if (!float.IsFinite(angle) || angle is < -1000f or > 1000f)
        {
            angle = 0;
        }

        while (angle < 0)
        {
            angle = (float)(angle + TwoPi);
        }

        return (byte)((uint)Math.Truncate(angle * AngleScale) & 0xFF);
    }
}
