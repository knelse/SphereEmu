using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Sphere.Common.Helpers.Extensions
{
    public static class StringExtensions
    {
        public static string RemoveLineEndings(this string str)
        {
            return Regex.Replace(str, @"\r\n?|\n", "");
        }

        public static string GetHashedString(this string str)
        {
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(str, salt, 100000, HashAlgorithmName.SHA1);
            var hash = pbkdf2.GetBytes(20);
            var saltedHash = new byte[36];
            Array.Copy(salt, 0, saltedHash, 0, 16);
            Array.Copy(hash, 0, saltedHash, 16, 20);

            return Convert.ToBase64String(saltedHash);
        }

        public static bool EqualsHashed(this string left, string right)
        {
            var hashBytes = Convert.FromBase64String(right);
            var salt = new byte[16];
            Array.Copy(hashBytes, 0, salt, 0, 16);

            using var pbkdf2 = new Rfc2898DeriveBytes(left, salt, 100000, HashAlgorithmName.SHA1);
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
