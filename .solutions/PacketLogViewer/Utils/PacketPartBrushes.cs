using System.Collections.Generic;
using System.Windows.Media;

namespace PacketLogViewer;

internal static class PacketPartBrushes
{
    private static readonly Dictionary<(byte r, byte g, byte b, byte a), SolidColorBrush> Cache = new();

    public static SolidColorBrush Get(byte r, byte g, byte b, byte a)
    {
        var key = (r, g, b, a);
        if (Cache.TryGetValue(key, out var brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        Cache[key] = brush;
        return brush;
    }
}
