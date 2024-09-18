using BitStreams;
using Godot;
using System.Text;

namespace Sphere.Common.Helpers.Extensions;

/// <summary>
///     This is horribly inefficient for the most part, but I opted for faster development first
/// </summary>
public static class BitHelper
{
    public static BitStream GetWriteBitStream()
    {
        return new BitStream(new MemoryStream())
        {
            AutoIncreaseStream = true
        };
    }

    public static BitStream GetReadBitStream(byte[] buffer)
    {
        return new BitStream(buffer);
    }

    public static int GetBytes(this StreamPeerTcp streamPeerTcp, byte[] bytes, int length) => GetBytes(streamPeerTcp, bytes, length);

    public static int GetBytes(this StreamPeerTcp streamPeerTcp, byte[] bytes, int? length = null)
    {
        var temp = streamPeerTcp.GetPartialData(length ?? bytes.Length);
        var arr = (byte[]?)temp[1];

        var i = 0;

        if (arr is not null)
        {
            for (; i < arr.Length; i++)
            {
                bytes[i] = arr[i];
            }
        }

        return i;
    }

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


    public static byte MinorByte(ushort input)
    {
        return (byte)(input & 0xFF);
    }

    public static byte MajorByte(ushort input)
    {
        return (byte)(input >> 8);
    }

    public static byte[] BinaryStringToByteArray(string s)
    {
        if (s.Length % 8 != 0)
        {
            return Array.Empty<byte>();
        }

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
        var cleanStr = s.RemoveLineEndings();

        for (var i = 0; i < cleanStr.Length; i += 8)
        {
            var currByte = cleanStr[i..(i + 8)];
            tempList.Add(Convert.ToByte(currByte, 2));
        }

        return tempList.ToArray();
    }

    public static ushort ByteSwap(ushort u)
    {
        return (ushort)(((u & 0b11111111) << 8) + ((u & 0b1111111100000000) >> 8));
    }
}