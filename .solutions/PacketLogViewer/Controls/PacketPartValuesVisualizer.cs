using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SpherePacketVisualEditor;

namespace PacketLogViewer.Controls;

public class PacketPartValuesVisualizer : FrameworkElement
{
    private static readonly Typeface Typeface = new("Hack");
    private static readonly Typeface BoldTypeface = new(new FontFamily("Hack"), FontStyles.Normal, FontWeights.Bold,
        FontStretches.Normal);
    private static readonly Brush TextBrush = CreateFrozenBrush(Colors.Black);
    private static readonly Brush GrayBrush = CreateFrozenBrush(Colors.Gray);

    private PacketPart[] parts = [];
    private int totalBits;
    private ScrollViewerInvalidateHook? scrollHook;

    public PacketPartValuesVisualizer()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ClipToBounds = true;
        Loaded += (_, _) =>
        {
            scrollHook?.Detach();
            scrollHook = ScrollViewerInvalidateHook.Attach(this, () => InvalidateVisual());
        };
        Unloaded += (_, _) =>
        {
            scrollHook?.Detach();
            scrollHook = null;
        };
    }

    public void SetContent(int bitCount, IEnumerable<PacketPart> packetParts)
    {
        totalBits = Math.Max(0, bitCount);
        if (packetParts is PacketPart[] array)
        {
            parts = array;
        }
        else
        {
            parts = [.. packetParts];
        }

        Array.Sort(parts, (a, b) => a.BitOffset.CompareTo(b.BitOffset));
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var lines = totalBits == 0
            ? 0
            : (totalBits + PacketVisualizerLayout.BitsPerLine - 1) / PacketVisualizerLayout.BitsPerLine;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var width = Math.Max(200, MeasureContentWidth(dpi) + 8);
        return new Size(width, lines * PacketVisualizerLayout.LineHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (parts.Length == 0 || totalBits == 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        GetVisibleLines(out var firstLine, out var lastLine);
        var firstBit = firstLine * PacketVisualizerLayout.BitsPerLine;
        var lastBit = lastLine * PacketVisualizerLayout.BitsPerLine;
        var xByLine = new Dictionary<int, double>();

        foreach (var part in parts)
        {
            if (part.BitOffsetEnd <= firstBit)
            {
                continue;
            }

            if (part.BitOffset >= lastBit)
            {
                break;
            }

            var line = part.BitOffset / PacketVisualizerLayout.BitsPerLine;
            if (line < firstLine || line >= lastLine)
            {
                continue;
            }

            var y = line * PacketVisualizerLayout.LineHeight;
            if (!xByLine.TryGetValue(line, out var x))
            {
                x = 0;
            }

            xByLine[line] = DrawPart(drawingContext, part, x, y, dpi);
        }
    }

    private double MeasureContentWidth(double dpi)
    {
        var xByLine = new Dictionary<int, double>();
        var maxX = 0.0;
        foreach (var part in parts)
        {
            var line = part.BitOffset / PacketVisualizerLayout.BitsPerLine;
            if (!xByLine.TryGetValue(line, out var x))
            {
                x = 0;
            }

            x = MeasurePart(part, x, dpi);
            xByLine[line] = x;
            if (x > maxX)
            {
                maxX = x;
            }
        }

        return maxX;
    }

    private void GetVisibleLines(out int firstLine, out int lastLine)
    {
        var lineCount = totalBits == 0
            ? 0
            : (totalBits + PacketVisualizerLayout.BitsPerLine - 1) / PacketVisualizerLayout.BitsPerLine;
        firstLine = 0;
        lastLine = lineCount;
        var scrollViewer = scrollHook?.ScrollViewer;
        if (scrollViewer is null)
        {
            return;
        }

        firstLine = Math.Max(0, (int)(scrollViewer.VerticalOffset / PacketVisualizerLayout.LineHeight) - 1);
        lastLine = Math.Min(lineCount,
            (int)Math.Ceiling((scrollViewer.VerticalOffset + scrollViewer.ViewportHeight) /
                              PacketVisualizerLayout.LineHeight) + 1);
    }

    private static double DrawPart(DrawingContext drawingContext, PacketPart part, double x, double y, double dpi)
    {
        var nameBrush = PacketPartBrushes.Get(part.HighlightColorR, part.HighlightColorG, part.HighlightColorB,
            part.HighlightColorA);
        var nameText = CreateText(part.Name, Typeface, TextBrush, dpi);
        drawingContext.DrawRectangle(nameBrush, null,
            new Rect(x, y, nameText.WidthIncludingTrailingWhitespace, PacketVisualizerLayout.LineHeight));
        drawingContext.DrawText(nameText, new Point(x, y));
        x += nameText.WidthIncludingTrailingWhitespace;

        var colon = CreateText(": ", Typeface, TextBrush, dpi);
        drawingContext.DrawText(colon, new Point(x, y));
        x += colon.WidthIncludingTrailingWhitespace;

        if (!string.IsNullOrEmpty(part.ListValuePrimary))
        {
            var primary = CreateText(part.ListValuePrimary, BoldTypeface, TextBrush, dpi);
            drawingContext.DrawText(primary, new Point(x, y));
            x += primary.WidthIncludingTrailingWhitespace;
        }

        if (!string.IsNullOrEmpty(part.ListValueSecondary))
        {
            var secondary = CreateText(part.ListValueSecondary, Typeface, GrayBrush, dpi);
            drawingContext.DrawText(secondary, new Point(x, y));
            x += secondary.WidthIncludingTrailingWhitespace;
        }

        if (!string.IsNullOrEmpty(part.ListRangeDisplay))
        {
            var range = CreateText(part.ListRangeDisplay, Typeface, GrayBrush, dpi);
            drawingContext.DrawText(range, new Point(x, y));
            x += range.WidthIncludingTrailingWhitespace;
        }

        return x;
    }

    private static double MeasurePart(PacketPart part, double x, double dpi)
    {
        x += CreateText(part.Name, Typeface, TextBrush, dpi).WidthIncludingTrailingWhitespace;
        x += CreateText(": ", Typeface, TextBrush, dpi).WidthIncludingTrailingWhitespace;
        if (!string.IsNullOrEmpty(part.ListValuePrimary))
        {
            x += CreateText(part.ListValuePrimary, BoldTypeface, TextBrush, dpi).WidthIncludingTrailingWhitespace;
        }

        if (!string.IsNullOrEmpty(part.ListValueSecondary))
        {
            x += CreateText(part.ListValueSecondary, Typeface, GrayBrush, dpi).WidthIncludingTrailingWhitespace;
        }

        if (!string.IsNullOrEmpty(part.ListRangeDisplay))
        {
            x += CreateText(part.ListRangeDisplay, Typeface, GrayBrush, dpi).WidthIncludingTrailingWhitespace;
        }

        return x;
    }

    private static FormattedText CreateText(string text, Typeface typeface, Brush brush, double dpi)
    {
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface,
            PacketVisualizerLayout.ValueFontSize, brush, dpi);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
