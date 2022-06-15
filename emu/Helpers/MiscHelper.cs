namespace emu.Helpers;

public static class MiscHelper
{
    public static void SetColorAndWriteLine(ConsoleColor foregroundColor, string message)
    {
        Console.ForegroundColor = foregroundColor;
        Console.WriteLine(message);
        Console.ForegroundColor = ConsoleColor.White;
    }
}