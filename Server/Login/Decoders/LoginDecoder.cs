using System;

namespace SphServer.Helpers;

public static class LoginDecoder
{
    public static Tuple<string, string> DecodeFromBuffer (byte[] rcvBuffer)
    {
        var loginEnd = 18;

        for (; loginEnd < rcvBuffer.Length; loginEnd++)
        {
            if (rcvBuffer[loginEnd] == 0 || rcvBuffer[loginEnd] == 1)
            {
                break;
            }
        }

        var login = rcvBuffer[18..];
        var passwordEnd = loginEnd + 1;

        for (; passwordEnd < rcvBuffer.Length; passwordEnd++)
        {
            if (rcvBuffer[passwordEnd] == 0)
            {
                break;
            }
        }

        var password = rcvBuffer[(loginEnd + 1)..];

        var loginDecode = new char[login.Length];
        login[0] -= 3;

        for (var i = 0; i < login.Length; i++)
        {
            if (login[i] % 2 == 0)
            {
                loginDecode[i] = (char) (login[i] / 4 - 1 + 'A');
            }
            else
            {
                loginDecode[i] = (char) (login[i] / 4 - 48 + '0');
            }
        }

        var passwordDecode = new char[password.Length];
        password[0] += 1;

        for (var i = 0; i < password.Length; i++)
        {
            if (password[i] % 2 == 0)
            {
                passwordDecode[i] = (char) (password[i] / 4 - 1 + 'A');
            }
            else
            {
                passwordDecode[i] = (char) (password[i] / 4 - 48 + '0');
            }
        }

        return new Tuple<string, string>(new string(loginDecode), new string(passwordDecode));
    }
}