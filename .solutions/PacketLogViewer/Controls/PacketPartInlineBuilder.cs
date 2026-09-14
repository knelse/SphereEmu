using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using SpherePacketVisualEditor;

namespace PacketLogViewer.Controls;

internal static class PacketPartInlineBuilder
{
    public static void Add(InlineCollection inlines, PacketPart part)
    {
        inlines.Add(new Run(part.Name)
        {
            Background = PacketPartBrushes.Get(part.HighlightColorR, part.HighlightColorG, part.HighlightColorB,
                part.HighlightColorA)
        });
        inlines.Add(new Run(": "));
        if (!string.IsNullOrEmpty(part.ListValuePrimary))
        {
            inlines.Add(new Run(part.ListValuePrimary) { FontWeight = FontWeights.Bold });
        }

        if (!string.IsNullOrEmpty(part.ListValueSecondary))
        {
            inlines.Add(new Run(part.ListValueSecondary) { Foreground = Brushes.Gray, FontSize = 12 });
        }

        if (!string.IsNullOrEmpty(part.ListRangeDisplay))
        {
            inlines.Add(new Run(part.ListRangeDisplay) { Foreground = Brushes.Gray, FontSize = 12 });
        }
    }

    public static Run? CreateCommentBanner(PacketPart part, double actualWidth)
    {
        if (!part.HasCommentBanner)
        {
            return null;
        }

        var lineWidth = actualWidth < 50 ? 120 : (int)(actualWidth / 9);
        var comment = $" {part.Comment} ";
        var paddingLength = Math.Max(0, (lineWidth - comment.Length) / 2);
        var padding = paddingLength == 0 ? string.Empty : new string('=', paddingLength);
        return new Run($"{padding}{comment}{padding}\n\n")
        {
            Background = part.CommentBannerBrush
        };
    }
}
