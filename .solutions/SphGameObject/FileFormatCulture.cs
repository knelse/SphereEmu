using System.Globalization;

namespace SphServer.Helpers;

/// <summary>
///     en-US parse/format helpers for tab-separated and other text file I/O.
/// </summary>
public static class FileFormatCulture
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");

    public static bool TryParseInt(string s, out int value) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, Culture, out value);

    public static int ParseInt(string s) =>
        int.Parse(s.Trim(), NumberStyles.Integer, Culture);

    public static bool TryParseUInt(string s, out uint value) =>
        uint.TryParse(s.Trim(), NumberStyles.Integer, Culture, out value);

    public static uint ParseUInt(string s) =>
        uint.Parse(s.Trim(), NumberStyles.Integer, Culture);

    public static bool TryParseUShort(string s, out ushort value) =>
        ushort.TryParse(s.Trim(), NumberStyles.Integer, Culture, out value);

    public static ushort ParseUShort(string s) =>
        ushort.Parse(s.Trim(), NumberStyles.Integer, Culture);

    public static bool TryParseByte(string s, out byte value) =>
        byte.TryParse(s.Trim(), NumberStyles.Integer, Culture, out value);

    public static byte ParseByte(string s) =>
        byte.Parse(s.Trim(), NumberStyles.Integer, Culture);

    public static bool TryParseHexInt(string s, out int value)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }

        return int.TryParse(s, NumberStyles.HexNumber, Culture, out value);
    }

    public static int ParseHexInt(string s)
    {
        if (!TryParseHexInt(s, out var value))
        {
            throw new FormatException($"Could not parse hex integer '{s}'.");
        }

        return value;
    }

    public static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, Culture, out value);

    public static double ParseDouble(string s) =>
        double.Parse(s.Trim(), NumberStyles.Float, Culture);

    public static bool TryParseFloat(string s, out float value) =>
        float.TryParse(s.Trim(), NumberStyles.Float, Culture, out value);

    public static float ParseFloat(string s) =>
        float.Parse(s.Trim(), NumberStyles.Float, Culture);

    public static bool TryParseAngle(string s, out int angle)
    {
        if (TryParseInt(s, out angle))
        {
            return true;
        }

        if (TryParseDouble(s, out var d))
        {
            angle = (int)Math.Round(d);
            return true;
        }

        angle = 0;
        return false;
    }

    public static string FormatInt(int value) => value.ToString(Culture);

    public static string FormatUInt(uint value) => value.ToString(Culture);

    public static string FormatDouble(double value, string? format = null) =>
        format is null ? value.ToString(Culture) : value.ToString(format, Culture);

    public static string FormatFloat(float value, string? format = null) =>
        format is null ? value.ToString(Culture) : value.ToString(format, Culture);

    public static string FormatFileField(object? value, string? format = null)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            double d => FormatDouble(d, format),
            float f => FormatFloat(f, format),
            int i => format is null ? FormatInt(i) : i.ToString(format, Culture),
            uint u => format is null ? FormatUInt(u) : u.ToString(format, Culture),
            long l => format is null ? l.ToString(Culture) : l.ToString(format, Culture),
            _ => Convert.ToString(value, Culture) ?? string.Empty
        };
    }

    public static string JoinFields(char separator, params object?[] fields)
    {
        var formatted = new string[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            formatted[i] = FormatFileField(fields[i]);
        }

        return string.Join(separator, formatted);
    }
}
