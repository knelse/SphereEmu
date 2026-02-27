using System.Reflection;
using System.Windows.Documents;

namespace SpherePacketVisualEditor;

public static class TextPointerExtensions
{
    private static readonly PropertyInfo CharOffestProperty =
        typeof (TextPointer).GetProperty("CharOffset", BindingFlags.NonPublic | BindingFlags.Instance);

    public static int GetCharOffset (this TextPointer textPointer)
    {
        return (int) CharOffestProperty.GetValue(textPointer);
    }
}