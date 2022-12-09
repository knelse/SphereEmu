using System;
using System.Security.Cryptography;

namespace SphServer.Helpers
{

    public partial class LoginHelper
    {
        public static Tuple<string, string> GetLoginAndPassword(byte[] rcvBuffer)
        {
            var loginEnd = 18;

            for (; loginEnd < rcvBuffer.Length; loginEnd++)
            {
                if (rcvBuffer[loginEnd] == 0 || rcvBuffer[loginEnd] == 1)
                {
                    break;
                }
            }

            var login = rcvBuffer.AsSpan(18, loginEnd-18).ToArray();
            var passwordEnd = loginEnd + 1;

            for (; passwordEnd < rcvBuffer.Length; passwordEnd++)
            {
                if (rcvBuffer[passwordEnd] == 0)
                {
                    break;
                }
            }

            var password = rcvBuffer.AsSpan((loginEnd + 1), passwordEnd - (loginEnd + 1)).ToArray();

            var loginDecode = new char[login.Length];
            login[0] -= 3;

            for (var i = 0; i < login.Length; i++)
            {
                if (login[i] % 2 == 0)
                {
                    loginDecode[i] = (char)(login[i] / 4 - 1 + 'A');
                }
                else
                {
                    loginDecode[i] = (char)(login[i] / 4 - 48 + '0');
                }
            }

            var passwordDecode = new char[password.Length];
            password[0] += 1;

            for (var i = 0; i < password.Length; i++)
            {
                if (password[i] % 2 == 0)
                {
                    passwordDecode[i] = (char)(password[i] / 4 - 1 + 'A');
                }
                else
                {
                    passwordDecode[i] = (char)(password[i] / 4 - 48 + '0');
                }
            }

            return new Tuple<string, string>(new string(loginDecode), new string(passwordDecode));
        }

        public static string GetHashedString(string str)
        {
            var salt = new byte[16];
            var rng = new RNGCryptoServiceProvider();
            rng.GetBytes(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(str, salt, 100000);
            var hash = pbkdf2.GetBytes(20);
            var saltedHash = new byte[36];
            Array.Copy(salt, 0, saltedHash, 0, 16);
            Array.Copy(hash, 0, saltedHash, 16, 20);

            return Convert.ToBase64String(saltedHash);
        }

        public static bool EqualsHashed(string password, string hashedPassword)
        {
            var hashBytes = Convert.FromBase64String(hashedPassword);
            var salt = new byte[16];
            Array.Copy(hashBytes, 0, salt, 0, 16);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000);
            var hash = pbkdf2.GetBytes(20);

            for (var i = 0; i < 20; i++)
            {
                if (hashBytes[i + 16] != hash[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}