using System;
using System.Reflection;
using System.Windows.Documents;

namespace SpherePacketVisualEditor;

public static class TextPointerExtensions
{
    private static readonly PropertyInfo CharOffestProperty =
        typeof(TextPointer).GetProperty("CharOffset", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("TextPointer.CharOffset is missing");

    public static int GetCharOffset(this TextPointer textPointer)
    {
        return CharOffestProperty.GetValue(textPointer) is int offset ? offset : 0;
    }
}
