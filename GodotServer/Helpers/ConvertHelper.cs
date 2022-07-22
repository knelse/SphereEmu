using System;
using System.Collections.Generic;
using System.Text;

namespace SphServer.Helpers;

public static class ConvertHelper
{
    public static string ToHexString(byte[] arr)
    {
        var sb = new StringBuilder();

        foreach (var b in arr)
        {
            sb.Append(Convert.ToString(b, 16).PadLeft(2, '0'));
        }

        return sb.ToString();
    }

    public static byte[] FromHexString(string str)
    {
        var result = new List<byte>();
        for (var i = 0; i < str.Length; i+=2)
        {
            result.Add(Convert.ToByte(str.Substring(i, 2), 16));
        }

        return result.ToArray();
    }
}