using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PacketLogViewer.Controls;

internal sealed class ScrollViewerInvalidateHook
{
    private readonly Action onScroll;
    public ScrollViewer? ScrollViewer { get; private set; }

    private ScrollViewerInvalidateHook(ScrollViewer scrollViewer, Action onScroll)
    {
        ScrollViewer = scrollViewer;
        this.onScroll = onScroll;
        scrollViewer.ScrollChanged += OnScrollChanged;
    }

    public static ScrollViewerInvalidateHook? Attach(DependencyObject from, Action onScroll)
    {
        var current = from;
        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is ScrollViewer scrollViewer)
            {
                return new ScrollViewerInvalidateHook(scrollViewer, onScroll);
            }
        }

        return null;
    }

    public void Detach()
    {
        if (ScrollViewer is not null)
        {
            ScrollViewer.ScrollChanged -= OnScrollChanged;
            ScrollViewer = null;
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ViewportHeightChange != 0)
        {
            onScroll();
        }
    }
}
