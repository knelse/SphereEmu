using System.Text;

namespace SphServer.System;

public static class SphEncoding
{
    public static readonly Encoding Win1251;

    static SphEncoding ()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Win1251 = Encoding.GetEncoding(1251);
    }
}