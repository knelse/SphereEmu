using System;

namespace SphServer.Helpers;

public static class ConsoleHelper
{
    public static void WriteLine (object obj)
    {
        if (obj.GetType() == typeof (byte[]))
        {
            Console.WriteLine(Convert.ToHexString((byte[]) obj));
        }
        else
        {
            Console.WriteLine(obj);
        }
    }
}