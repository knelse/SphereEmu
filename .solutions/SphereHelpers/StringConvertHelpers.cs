using System.Text;

namespace SphServer.Helpers;

public static class StringConvertHelpers
{
    public static string ByteArrayToBinaryString (byte[] ba, bool noPadding = false, bool addSpaces = false)
    {
        var hex = new StringBuilder(ba.Length * 2);

        foreach (var val in ba)
        {
            var str = Convert.ToString(val, 2);
            if (!noPadding)
            {
                str = str.PadLeft(8, '0');
            }

            hex.Append(str);

            if (addSpaces)
            {
                hex.Append(' ');
            }
        }

        return hex.ToString();
    }
}