using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SpherePacketVisualEditor;

namespace PacketLogViewer.Controls;

public class PacketPartListRow : StackPanel
{
    private double lastWidth = -1;

    public PacketPartListRow()
    {
        Orientation = Orientation.Vertical;
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
        Children.Clear();
        if (DataContext is not PacketPart part)
        {
            return;
        }

        var comment = PacketPartInlineBuilder.CreateCommentBanner(part, ActualWidth);
        if (comment is not null)
        {
            Children.Add(CreateLine(comment));
        }

        var valueLine = CreateLine();
        PacketPartInlineBuilder.Add(valueLine.Inlines, part);
        Children.Add(valueLine);
    }

    private static TextBlock CreateLine(Inline? leading = null)
    {
        var block = new TextBlock
        {
            FontFamily = new FontFamily("Hack"),
            FontSize = 14,
            LineHeight = 16,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(2)
        };
        if (leading is not null)
        {
            block.Inlines.Add(leading);
        }

        return block;
    }
}
