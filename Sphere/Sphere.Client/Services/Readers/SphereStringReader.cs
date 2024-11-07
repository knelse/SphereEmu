using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Readers;
using System.Text;

namespace Sphere.Services.Services.Readers
{
    public class SphereStringReader : ISphereStringReader
    {
        private static readonly byte[] _charsets = [0x00, 0x01, 0x03];

        public static string Read(byte[] bytes, SphereStringType stringType)
        {
            return stringType switch
            {
                SphereStringType.Login => ReadLogin(bytes),
                SphereStringType.Nickname => ReadNickname(bytes),
                _ => throw new NotSupportedException()
            };
        }

        private static string ReadLogin(byte[] bytes)
        {
            if (!(bytes?.Length > 0))
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            var charSet = bytes[^1];

            if (!_charsets.Contains(charSet))
                throw new FormatException("Can't get charset from the byte sequence");

            var charSetAddedValue = charSet switch
            {
                0x03 => 0xC0,
                _ => 0x40
            };

            var chars = bytes[..^1].Select(x =>
            {
                var shift = x >> 2;
                return (byte)(shift switch
                {
                    >= 0x30 and <= 0x39 => shift,
                    < 0x30 => shift + charSetAddedValue,
                    _ => throw new FormatException($"Unknown format of the string bytes [{BitConverter.ToString(bytes)}]")
                });
            }).ToArray();

            return Encoding.GetEncoding(1251).GetString(chars);
        }

        private static string ReadNickname(byte[] bytes)
        {
            var sb = new StringBuilder();
            var firstLetterCharCode = ((bytes[1] & 0b11111) << 3) + (bytes[0] >> 5);
            var firstLetterShouldBeRussian = false;

            for (var i = 1; i < bytes.Length; i++)
            {
                var currentCharCode = ((bytes[i] & 0b11111) << 3) + (bytes[i - 1] >> 5);

                if (currentCharCode % 2 == 0)
                {
                    // English
                    var currentLetter = (char)(currentCharCode / 2);
                    sb.Append(currentLetter);
                }
                else
                {
                    // Russian
                    var currentLetter = currentCharCode >= 193
                        ? (char)((currentCharCode - 192) / 2 + 'а')
                        : (char)((currentCharCode - 129) / 2 + 'А');
                    sb.Append(currentLetter);

                    if (i == 2)
                    {
                        // we assume first letter was russian if second letter is, this is a hack
                        firstLetterShouldBeRussian = true;
                    }
                }
            }

            if (firstLetterShouldBeRussian)
            {
                firstLetterCharCode += 1;
                var firstLetter = firstLetterCharCode >= 193
                    ? (char)((firstLetterCharCode - 192) / 2 + 'а')
                    : (char)((firstLetterCharCode - 129) / 2 + 'А');
                sb[0] = (char)firstLetter;
            }

            return sb.ToString();
        }
    }
}
