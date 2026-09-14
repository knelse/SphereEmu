using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpherePacketVisualEditor;

namespace PacketLogViewer.Controls;

public class PacketPartListRow : TextBlock
{
    private double lastWidth = -1;

    public PacketPartListRow()
    {
        FontFamily = new FontFamily("Hack");
        FontSize = 14;
        LineHeight = 16;
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        TextWrapping = TextWrapping.NoWrap;
        Margin = new Thickness(2);
        DataContextChanged += (_, _) => Rebuild();
        SizeChanged += (_, _) =>
        {
            if (Math.Abs(ActualWidth - lastWidth) < 1)
            {
                return;
            }

            lastWidth = ActualWidth;
            Rebuild();
        };
    }

    private void Rebuild()
    {
        Inlines.Clear();
        if (DataContext is not PacketPart part)
        {
            return;
        }

        var comment = PacketPartInlineBuilder.CreateCommentBanner(part, ActualWidth);
        if (comment is not null)
        {
            Inlines.Add(comment);
        }

        PacketPartInlineBuilder.Add(Inlines, part);
    }
}
