using System.Text;

namespace emu.Helpers;
/// <summary>
/// This is horribly inefficient for the most part, but I opted for faster development first
/// </summary>
public static class BitHelper
{
    public static string ToBinaryString(this byte b)
    {
        return Convert.ToString(b, 2).PadLeft(8, '0');
    }
    public static string ToBinaryString(this ushort us)
    {
        return Convert.ToString(us, 2).PadLeft(16, '0');
    }
    public static string ToBinaryString(this uint ui)
    {
        return Convert.ToString(ui, 2).PadLeft(32, '0');
    }
    public static string ToBinaryString(this long l)
    {
        return Convert.ToString(l, 2).PadLeft(64, '0');
    }
    public static StringBuilder AppendBinaryPadTrim(this StringBuilder sb, byte val, bool reverse = false, 
        int padding = 8)
    {
        var binaryString = Convert.ToString(val, 2).PadLeft(padding, '0');

        if (reverse)
        {
            binaryString = new string(binaryString.Reverse().ToArray());
        }

        return sb.Append(binaryString);
    }
    
    public static StringBuilder AppendBinaryPadTrim(this StringBuilder sb, ushort val, bool reverse = false, 
        int padding = 16)
    {
        var binaryString = Convert.ToString(val, 2).PadLeft(padding, '0');

        if (reverse)
        {
            binaryString = new string(binaryString.Reverse().ToArray());
        }

        return sb.Append(binaryString);
    }
    
    public static StringBuilder AppendBinaryPadTrim(this StringBuilder sb, uint val, bool reverse = false, 
        int padding = 32)
    {
        var binaryString = Convert.ToString(val, 2).PadLeft(padding, '0');

        if (reverse)
        {
            binaryString = new string(binaryString.Reverse().ToArray());
        }

        return sb.Append(binaryString);
    }
    
    public static StringBuilder AppendBinaryPadTrim(this StringBuilder sb, long val, bool reverse = false, 
        int padding = 64)
    {
        var binaryString = Convert.ToString(val, 2).PadLeft(padding, '0');

        if (reverse)
        {
            binaryString = new string(binaryString.Reverse().ToArray());
        }

        return sb.Append(binaryString);
    }
    
    public static StringBuilder AppendBinary(this StringBuilder sb, bool val)
    {
        return sb.Append(val ? '1' : '0');
    }
    
    
    public static byte GetFirstByte(ushort input)
    {
        return (byte)(input & 0xFF);
    }
    public static byte GetSecondByte(ushort input)
    {
        return (byte)(input >> 8);
    }
    
    // public static string GetLittleEndianBinaryString(ushort input)
    // {
    //     if (BitConverter.IsLittleEndian) return Convert.ToString(input, 2).PadLeft(16, '0');
    //
    //     var tempArray = BitConverter.GetBytes(input);
    //     Array.Reverse(tempArray);
    //     return ByteArrayToBinaryString(tempArray);
    // }
    //
    public static string ByteArrayToBinaryString(byte[] ba, bool noPadding = false, bool addSpaces = false)
    {
        var hex = new StringBuilder(ba.Length * 2);
    
        foreach (var val in ba)
        {
            var str = Convert.ToString(val, 2);
            if (!noPadding) str = str.PadLeft(8, '0');
            hex.Append(str);

            if (addSpaces)
            {
                hex.Append(' ');
            }
        }
    
        return hex.ToString();
    }
    
    public static byte[] BinaryStringToByteArray(string s)
    {
        if (s.Length % 8 != 0) return Array.Empty<byte>();
    
        var result = new byte[s.Length / 8];
    
        for (var i = 0; i < s.Length; i += 8)
        {
            result[i / 8] = Convert.ToByte(s[i..(i + 8)], 2);
        }
    
        return result;
    }

    public static byte[] ReadableBinaryStringToByteArray(string s)
    {
        var tempList = new List<byte>();
        var cleanStr = s.ReplaceLineEndings("\n").Replace("\n", "");
                    
        for (var i = 0; i < cleanStr.Length; i+=8)
        {
            var currByte = cleanStr[i..(i + 8)];
            tempList.Add(Convert.ToByte(currByte, 2));
        }

        return tempList.ToArray();
    }
    // public static StringBuilder AppendBinaryString(this StringBuilder sb, byte input)
    // {
    //     return sb.Append(Convert.ToString(input, 2).PadLeft(8, '0'));
    // }
    //
    // public static StringBuilder AppendBinaryString(this StringBuilder sb, ushort input)
    // {
    //     return sb.Append(Convert.ToString(input, 2).PadLeft(16, '0'));
    // }
    //
    // public static StringBuilder AppendBinaryString(this StringBuilder sb, uint input)
    // {
    //     return sb.Append(Convert.ToString(input, 2).PadLeft(32, '0'));
    // }
    //
    // public static StringBuilder AppendBinaryString(this StringBuilder sb, long input)
    // {
    //     return sb.Append(Convert.ToString(input, 2).PadLeft(64, '0'));
    // }
    //
    // public static StringBuilder AppendReverse(this StringBuilder sb, byte[] input, bool noPadding = false)
    // {
    //     Array.Reverse(input);
    //
    //     return sb.Append(ByteArrayToBinaryString(input, noPadding));
    // }
    //
    // public static readonly byte[] BitReverseTable =
    // {
    //     0x00, 0x80, 0x40, 0xc0, 0x20, 0xa0, 0x60, 0xe0,
    //     0x10, 0x90, 0x50, 0xd0, 0x30, 0xb0, 0x70, 0xf0,
    //     0x08, 0x88, 0x48, 0xc8, 0x28, 0xa8, 0x68, 0xe8,
    //     0x18, 0x98, 0x58, 0xd8, 0x38, 0xb8, 0x78, 0xf8,
    //     0x04, 0x84, 0x44, 0xc4, 0x24, 0xa4, 0x64, 0xe4,
    //     0x14, 0x94, 0x54, 0xd4, 0x34, 0xb4, 0x74, 0xf4,
    //     0x0c, 0x8c, 0x4c, 0xcc, 0x2c, 0xac, 0x6c, 0xec,
    //     0x1c, 0x9c, 0x5c, 0xdc, 0x3c, 0xbc, 0x7c, 0xfc,
    //     0x02, 0x82, 0x42, 0xc2, 0x22, 0xa2, 0x62, 0xe2,
    //     0x12, 0x92, 0x52, 0xd2, 0x32, 0xb2, 0x72, 0xf2,
    //     0x0a, 0x8a, 0x4a, 0xca, 0x2a, 0xaa, 0x6a, 0xea,
    //     0x1a, 0x9a, 0x5a, 0xda, 0x3a, 0xba, 0x7a, 0xfa,
    //     0x06, 0x86, 0x46, 0xc6, 0x26, 0xa6, 0x66, 0xe6,
    //     0x16, 0x96, 0x56, 0xd6, 0x36, 0xb6, 0x76, 0xf6,
    //     0x0e, 0x8e, 0x4e, 0xce, 0x2e, 0xae, 0x6e, 0xee,
    //     0x1e, 0x9e, 0x5e, 0xde, 0x3e, 0xbe, 0x7e, 0xfe,
    //     0x01, 0x81, 0x41, 0xc1, 0x21, 0xa1, 0x61, 0xe1,
    //     0x11, 0x91, 0x51, 0xd1, 0x31, 0xb1, 0x71, 0xf1,
    //     0x09, 0x89, 0x49, 0xc9, 0x29, 0xa9, 0x69, 0xe9,
    //     0x19, 0x99, 0x59, 0xd9, 0x39, 0xb9, 0x79, 0xf9,
    //     0x05, 0x85, 0x45, 0xc5, 0x25, 0xa5, 0x65, 0xe5,
    //     0x15, 0x95, 0x55, 0xd5, 0x35, 0xb5, 0x75, 0xf5,
    //     0x0d, 0x8d, 0x4d, 0xcd, 0x2d, 0xad, 0x6d, 0xed,
    //     0x1d, 0x9d, 0x5d, 0xdd, 0x3d, 0xbd, 0x7d, 0xfd,
    //     0x03, 0x83, 0x43, 0xc3, 0x23, 0xa3, 0x63, 0xe3,
    //     0x13, 0x93, 0x53, 0xd3, 0x33, 0xb3, 0x73, 0xf3,
    //     0x0b, 0x8b, 0x4b, 0xcb, 0x2b, 0xab, 0x6b, 0xeb,
    //     0x1b, 0x9b, 0x5b, 0xdb, 0x3b, 0xbb, 0x7b, 0xfb,
    //     0x07, 0x87, 0x47, 0xc7, 0x27, 0xa7, 0x67, 0xe7,
    //     0x17, 0x97, 0x57, 0xd7, 0x37, 0xb7, 0x77, 0xf7,
    //     0x0f, 0x8f, 0x4f, 0xcf, 0x2f, 0xaf, 0x6f, 0xef,
    //     0x1f, 0x9f, 0x5f, 0xdf, 0x3f, 0xbf, 0x7f, 0xff
    // };
    // public static byte ReverseWithLookupTable(byte toReverse)
    // {
    //     return BitReverseTable[toReverse];
    // }
    //
    // private static readonly byte[] BitMask =
    // {
    //     0b00000001,
    //     0b00000010,
    //     0b00000100,
    //     0b00001000,
    //     0b00010000,
    //     0b00100000,
    //     0b01000000,
    //     0b10000000
    // };
    //
    // public static void AddToBinaryArray(ref bool[] input, byte val, int offset, int numbits = 8, bool reverse = false)
    // {
    //     if (!reverse)
    //     {
    //         for (var i = numbits; i > 0; i++)
    //         {
    //             input[offset + i - 1] = (val & BitMask[i - 1]) > 0;
    //         }
    //     }
    //     else
    //     {
    //         for (var i = numbits; i > 0; i++)
    //         {
    //             input[offset + 8 - i + 1] = (val & BitMask[i - 1]) > 0;
    //         }
    //     }
    // }
    //
    // public static void AddToBinaryArray(ref bool[] input, ushort val, int offset, int numbits = 16,
    //     bool reverse = false)
    // {
    //     if (!reverse)
    //     {
    //         if (numbits > 8)
    //         {
    //             AddToBinaryArray(ref input, (byte) (val % 0xFF), offset + numbits - 8);
    //         }
    //         AddToBinaryArray(ref input, (byte) (val >> 8), offset, numbits - 8);
    //     }
    //     else
    //     {
    //         if (numbits > 8)
    //         {
    //             
    //         }
    //     }
    // }
}