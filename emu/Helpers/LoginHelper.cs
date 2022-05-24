using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace emu.Helpers;

public class LoginHelper
{
    public static Tuple<string, string> GetLoginAndPasswordHash(byte[] rcvBuffer)
    {
        var loginEnd = 18;

        for (; loginEnd < rcvBuffer.Length; loginEnd++)
        {
            if (rcvBuffer[loginEnd] == 0 || rcvBuffer[loginEnd] == 1)
            {
                break;
            }
        }

        var login = rcvBuffer[18..loginEnd];
        var passwordEnd = loginEnd + 1;

        for (; passwordEnd < rcvBuffer.Length; passwordEnd++)
        {
            if (rcvBuffer[passwordEnd] == 0)
            {
                break;
            }
        }

        var password = rcvBuffer[(loginEnd + 1)..passwordEnd];

        var loginDecode = new char[login.Length];
        login[0] -= 2;

        for (var i = 0; i < login.Length; i++)
        {
            loginDecode[i] = (char)(login[i] / 4 - 1 + 'A');
        }

        var passwordDecode = new char[password.Length];
        password[0] -= 2;

        for (var i = 0; i < password.Length; i++)
        {
            passwordDecode[i] = (char)(password[i] / 4 - 1 + 'A');
        }

        return new Tuple<string, string>(new string(loginDecode), GetHashedString(new string (passwordDecode)));
    }

    public static string GetHashedString(string str)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        var pbkdf2 = new Rfc2898DeriveBytes(str, salt, 100000);
        var hash = pbkdf2.GetBytes(20);
        var saltedHash = new byte[36];
        Array.Copy(salt, 0, saltedHash, 0, 16);
        Array.Copy(hash, 0, saltedHash, 16, 20);
        return Convert.ToBase64String(saltedHash);
    }
}