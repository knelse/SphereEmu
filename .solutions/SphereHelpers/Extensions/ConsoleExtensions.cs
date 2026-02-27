namespace PacketLogViewer.Extensions;

public static class ConsoleExtensions
{
    public static void WriteLineColored (string text, ConsoleColor color)
    {
        var previousColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previousColor;
    }

    public static void WriteException (Exception ex)
    {
        WriteLineColored($"ERROR: {ex.Message}", ConsoleColor.Red);
    }
}