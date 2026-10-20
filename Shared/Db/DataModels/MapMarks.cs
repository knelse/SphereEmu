using System;

namespace SphServer.Shared.Db.DataModels;

/// <summary>
/// st_map marks of one map item, kept in its ContentsData: 24 cells of pic u8, x u16, y u16 and a
/// 16-byte label, as the map process holds them
/// </summary>
public static class MapMarks
{
    public const string ContentsKey = "map_marks";
    public const int CellCount = 24;

    /// <summary>
    /// A label record names its cell as cell + 50
    /// </summary>
    public const int LabelCellOffset = 50;

    public const int MaxLabelLength = 15;
    public const int MaxPic = 9;
    private const int LabelOffset = 5;
    private const int CellSize = LabelOffset + MaxLabelLength + 1;

    /// <summary>
    /// A fresh copy, so a stored array is never changed in place and a cloned item keeps its own
    /// </summary>
    public static byte[] Copy (ItemDbEntry item)
    {
        var marks = new byte[CellCount * CellSize];
        if (item.ContentsData.TryGetValue (ContentsKey, out var value) && value is byte[] stored
            && stored.Length == marks.Length)
        {
            stored.CopyTo (marks, 0);
        }

        return marks;
    }

    public static void Store (ItemDbEntry item, byte[] marks)
    {
        item.ContentsData[ContentsKey] = marks;
    }

    /// <summary>
    /// Pic 0 empties the cell; any pic record also clears the label, as RcvUser does
    /// </summary>
    public static void SetPic (byte[] marks, int cell, byte pic, ushort x, ushort y)
    {
        var at = cell * CellSize;
        Array.Clear (marks, at, CellSize);
        if (pic == 0)
        {
            return;
        }

        marks[at] = pic;
        marks[at + 1] = (byte) x;
        marks[at + 2] = (byte) (x >> 8);
        marks[at + 3] = (byte) y;
        marks[at + 4] = (byte) (y >> 8);
    }

    public static void SetLabel (byte[] marks, int cell, ReadOnlySpan<byte> text)
    {
        var label = marks.AsSpan (cell * CellSize + LabelOffset, MaxLabelLength + 1);
        label.Clear ();
        text[..Math.Min (text.Length, MaxLabelLength)].CopyTo (label);
    }

    public static byte Pic (byte[] marks, int cell) => marks[cell * CellSize];

    public static ushort X (byte[] marks, int cell) =>
        (ushort) (marks[cell * CellSize + 1] | marks[cell * CellSize + 2] << 8);

    public static ushort Y (byte[] marks, int cell) =>
        (ushort) (marks[cell * CellSize + 3] | marks[cell * CellSize + 4] << 8);

    /// <summary>
    /// Label bytes without the terminating zero
    /// </summary>
    public static ReadOnlySpan<byte> Label (byte[] marks, int cell)
    {
        var label = marks.AsSpan (cell * CellSize + LabelOffset, MaxLabelLength);
        var end = label.IndexOf ((byte) 0);
        return end < 0 ? label : label[..end];
    }
}
