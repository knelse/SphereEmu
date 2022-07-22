using System;

namespace SphServer.Helpers
{

    public static class MiscHelper
    {
        public static void SetColorAndWriteLine(ConsoleColor foregroundColor, string message)
        {
            Console.ForegroundColor = foregroundColor;
            Console.WriteLine(message);
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static byte[] Range(this byte[] arr, int start, int end)
        {
            return arr.AsSpan(start, end - start).ToArray();
        }
    }
}