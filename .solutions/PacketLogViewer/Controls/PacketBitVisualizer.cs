using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BitStreams;
using SpherePacketVisualEditor;

namespace PacketLogViewer.Controls;

public class PacketBitVisualizer : FrameworkElement
{
    private static readonly Typeface Typeface = new("Hack");
    private static readonly Brush CaretBrush = CreateFrozenBrush(Colors.Black);
    private static readonly Brush DefaultTextBrush = CreateFrozenBrush(Colors.Black);

    private Bit[] bits = [];
    private PacketPart?[] coverage = [];
    private int? selectionStart;
    private int? selectionEnd;
    private int caretBit;
    private double charWidth;
    private ScrollViewerInvalidateHook? scrollHook;
    private bool metricsReady;
    private bool draggingSelection;

    public Brush SelectionOverlayBrush { get; set; } = CreateFrozenBrush(Color.FromArgb(140, 51, 153, 255));

    public int CaretBit => caretBit;
    public int? SelectionStart => selectionStart;
    public int? SelectionEnd => selectionEnd;
    public int BitCount => bits.Length;
    public event EventHandler? BitSelectionChanged;

    public PacketBitVisualizer()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ClipToBounds = true;
        Cursor = Cursors.IBeam;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    public void SetContent(Bit[] packetBits, IEnumerable<PacketPart> parts)
    {
        bits = packetBits ?? [];
        coverage = new PacketPart?[bits.Length];
        foreach (var part in parts)
        {
            var start = Math.Max(0, part.BitOffset);
            var end = Math.Min(bits.Length, part.BitOffsetEnd);
            for (var i = start; i < end; i++)
            {
                coverage[i] ??= part;
            }
        }

        caretBit = Math.Clamp(caretBit, 0, bits.Length);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetSelection(int? startBit, int? endBit, int caret)
    {
        selectionStart = startBit;
        selectionEnd = endBit;
        caretBit = bits.Length == 0 ? 0 : Math.Clamp(caret, 0, bits.Length);
        InvalidateVisual();
    }

    public void BringBitIntoView(int bit)
    {
        var scrollViewer = scrollHook?.ScrollViewer;
        if (scrollViewer is null || bits.Length == 0)
        {
            return;
        }

        var line = Math.Clamp(bit, 0, bits.Length) / PacketVisualizerLayout.BitsPerLine;
        var y = line * PacketVisualizerLayout.LineHeight;
        if (y < scrollViewer.VerticalOffset)
        {
            scrollViewer.ScrollToVerticalOffset(y);
        }
        else if (y + PacketVisualizerLayout.LineHeight > scrollViewer.VerticalOffset + scrollViewer.ViewportHeight)
        {
            scrollViewer.ScrollToVerticalOffset(y + PacketVisualizerLayout.LineHeight - scrollViewer.ViewportHeight);
        }
    }

    public int HitTestBit(Point point)
    {
        EnsureMetrics();
        if (bits.Length == 0 || charWidth <= 0)
        {
            return 0;
        }

        var lineCount = (bits.Length + PacketVisualizerLayout.BitsPerLine - 1) / PacketVisualizerLayout.BitsPerLine;
        var line = (int)Math.Floor(point.Y / PacketVisualizerLayout.LineHeight);
        line = Math.Clamp(line, 0, Math.Max(0, lineCount - 1));
        var bitStart = line * PacketVisualizerLayout.BitsPerLine;
        var bitsOnLine = Math.Min(PacketVisualizerLayout.BitsPerLine, bits.Length - bitStart);
        if (bitsOnLine <= 0)
        {
            return bits.Length;
        }

        // Original FlowDocument: LTR stream order, right-aligned in an 80px page with 10px left pad.
        var originX = GetLineOriginX(bitsOnLine);
        var col = (int)Math.Floor((point.X - originX) / charWidth);
        col = Math.Clamp(col, 0, bitsOnLine);
        return Math.Clamp(bitStart + col, 0, bits.Length);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureMetrics();
        var lines = bits.Length == 0
            ? 0
            : (bits.Length + PacketVisualizerLayout.BitsPerLine - 1) / PacketVisualizerLayout.BitsPerLine;
        var width = double.IsInfinity(availableSize.Width)
            ? PacketVisualizerLayout.BitColumnWidth
            : availableSize.Width;
        return new Size(width, lines * PacketVisualizerLayout.LineHeight);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        draggingSelection = true;
        var hit = HitTestBit(e.GetPosition(this));
        caretBit = hit;
        selectionStart = hit;
        selectionEnd = hit;
        InvalidateVisual();
        RaiseSelectionChanged();
        e.Handled = true;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!draggingSelection)
        {
            base.OnMouseMove(e);
            return;
        }

        var scrollViewer = scrollHook?.ScrollViewer;
        if (scrollViewer is not null)
        {
            AutoScrollToward(e.GetPosition(scrollViewer));
        }
        var hit = HitTestBit(e.GetPosition(this));
        if (hit != caretBit || hit != selectionEnd)
        {
            caretBit = hit;
            selectionEnd = hit;
            InvalidateVisual();
            RaiseSelectionChanged();
        }

        e.Handled = true;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (draggingSelection)
        {
            EndMouseSelection(HitTestBit(e.GetPosition(this)));
            e.Handled = true;
        }

        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (draggingSelection)
        {
            EndMouseSelection(caretBit);
        }

        base.OnLostMouseCapture(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0)
        {
            base.OnKeyDown(e);
            return;
        }

        var delta = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -PacketVisualizerLayout.BitsPerLine,
            Key.Down => PacketVisualizerLayout.BitsPerLine,
            Key.Home => -caretBit,
            Key.End => bits.Length - caretBit,
            _ => 0
        };

        if (delta == 0 || bits.Length == 0)
        {
            base.OnKeyDown(e);
            return;
        }

        caretBit = Math.Clamp(caretBit + delta, 0, bits.Length);
        BringBitIntoView(caretBit);
        InvalidateVisual();
        e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        EnsureMetrics();
        if (bits.Length == 0 || charWidth <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var lineCount = (bits.Length + PacketVisualizerLayout.BitsPerLine - 1) / PacketVisualizerLayout.BitsPerLine;
        var firstLine = 0;
        var lastLine = lineCount;
        var scrollViewer = scrollHook?.ScrollViewer;
        if (scrollViewer is not null)
        {
            firstLine = Math.Max(0, (int)(scrollViewer.VerticalOffset / PacketVisualizerLayout.LineHeight) - 1);
            lastLine = Math.Min(lineCount,
                (int)Math.Ceiling((scrollViewer.VerticalOffset + scrollViewer.ViewportHeight) /
                                  PacketVisualizerLayout.LineHeight) + 1);
        }

        var bitChars = new char[PacketVisualizerLayout.BitsPerLine];
        for (var line = firstLine; line < lastLine; line++)
        {
            var bitStart = line * PacketVisualizerLayout.BitsPerLine;
            var bitsOnLine = Math.Min(PacketVisualizerLayout.BitsPerLine, bits.Length - bitStart);
            if (bitsOnLine <= 0)
            {
                break;
            }

            var y = line * PacketVisualizerLayout.LineHeight;
            var originX = GetLineOriginX(bitsOnLine);
            DrawLineBackgrounds(drawingContext, bitStart, bitsOnLine, y, originX);

            for (var col = 0; col < bitsOnLine; col++)
            {
                bitChars[col] = bits[bitStart + col].AsInt() == 0 ? '0' : '1';
            }

            var text = new string(bitChars, 0, bitsOnLine);
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface, PacketVisualizerLayout.BitFontSize, DefaultTextBrush, dpi);
            drawingContext.DrawText(formatted, new Point(originX, y));

            if (caretBit >= bitStart && caretBit <= bitStart + bitsOnLine)
            {
                var caretX = originX + (caretBit - bitStart) * charWidth;
                drawingContext.DrawRectangle(CaretBrush, null,
                    new Rect(caretX, y, 1, PacketVisualizerLayout.LineHeight));
            }
        }
    }

    private void DrawLineBackgrounds(DrawingContext drawingContext, int bitStart, int bitsOnLine, double y,
        double originX)
    {
        var runStart = 0;
        var runPart = coverage[bitStart];
        var runSelected = IsSelected(bitStart);
        for (var col = 1; col <= bitsOnLine; col++)
        {
            var bit = bitStart + col;
            PacketPart? part = null;
            var selected = false;
            if (col < bitsOnLine)
            {
                part = coverage[bit];
                selected = IsSelected(bit);
            }

            if (col == bitsOnLine || !ReferenceEquals(part, runPart) || selected != runSelected)
            {
                var brush = runSelected
                    ? SelectionOverlayBrush
                    : runPart is null
                        ? null
                        : PacketPartBrushes.Get(runPart.HighlightColorR, runPart.HighlightColorG,
                            runPart.HighlightColorB, runPart.HighlightColorA);
                if (brush is not null)
                {
                    var visualLeft = originX + runStart * charWidth;
                    drawingContext.DrawRectangle(brush, null,
                        new Rect(visualLeft, y, (col - runStart) * charWidth, PacketVisualizerLayout.LineHeight));
                }

                runStart = col;
                runPart = part;
                runSelected = selected;
            }
        }
    }

    private void EndMouseSelection(int hit)
    {
        if (!draggingSelection)
        {
            return;
        }

        draggingSelection = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        caretBit = hit;
        selectionEnd = hit;
        if (selectionStart == selectionEnd)
        {
            selectionStart = null;
            selectionEnd = null;
        }

        InvalidateVisual();
        RaiseSelectionChanged();
    }

    private void AutoScrollToward(Point positionInScrollViewer)
    {
        var scrollViewer = scrollHook?.ScrollViewer;
        if (scrollViewer is null)
        {
            return;
        }

        const double edge = 12;
        if (positionInScrollViewer.Y < edge)
        {
            scrollViewer.ScrollToVerticalOffset(Math.Max(0,
                scrollViewer.VerticalOffset - PacketVisualizerLayout.LineHeight));
        }
        else if (positionInScrollViewer.Y > scrollViewer.ViewportHeight - edge)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + PacketVisualizerLayout.LineHeight);
        }
    }

    private void RaiseSelectionChanged()
    {
        BitSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private double GetLineOriginX(int bitsOnLine)
    {
        EnsureMetrics();
        var contentWidth = PacketVisualizerLayout.BitPageWidth - PacketVisualizerLayout.BitPadLeft;
        var clusterWidth = bitsOnLine * charWidth;
        return PacketVisualizerLayout.BitPadLeft + Math.Max(0, contentWidth - clusterWidth);
    }

    private bool IsSelected(int bit)
    {
        if (selectionStart is not int start || selectionEnd is not int end)
        {
            return false;
        }

        var min = Math.Min(start, end);
        var max = Math.Max(start, end);
        return bit >= min && bit < max;
    }

    private void EnsureMetrics()
    {
        if (metricsReady && charWidth > 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface, PacketVisualizerLayout.BitFontSize, DefaultTextBrush, dpi);
        charWidth = formatted.WidthIncludingTrailingWhitespace;
        if (charWidth <= 0)
        {
            charWidth = 8.4;
        }

        metricsReady = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        scrollHook?.Detach();
        scrollHook = ScrollViewerInvalidateHook.Attach(this, () => InvalidateVisual());
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        scrollHook?.Detach();
        scrollHook = null;
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
