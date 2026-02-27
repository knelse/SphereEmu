using System.Text;
using BitStreams;
using LiteDB;

namespace SphereHelpers.Extensions;

public static class BitStreamExtensions
{
    public static void SeekBack (this BitStream bitStream, int countBits)
    {
        var newBitOffset = bitStream.BitOffsetFromStart - countBits;
        bitStream.SeekBitOffset(newBitOffset);
    }

    public static void SeekBitOffset (this BitStream bitStream, long bitOffset)
    {
        var newOffset = bitOffset / 8;
        var newBit = (int) bitOffset % 8;
        bitStream.Seek(newOffset, newBit);
    }

    public static void SeekForward (this BitStream bitStream, int countBits)
    {
        var newBitOffset = bitStream.BitOffsetFromStart + countBits;
        bitStream.SeekBitOffset(newBitOffset);
    }

    public static ushort ReadUInt16 (this BitStream bitStream, int countBits)
    {
        return (ushort) BitsToInt(bitStream.ReadBits(countBits));
    }

    public static long ReadInt64 (this BitStream bitStream, long countBits)
    {
        return BitsToInt64(bitStream.ReadBits(countBits));
    }

    public static void WriteUInt16 (this BitStream bitStream, ushort val, int countBits)
    {
        bitStream.WriteBits(IntToBits(val, countBits));
    }

    public static uint ReadUInt32 (this BitStream bitStream, int countBits)
    {
        return (uint) BitsToInt(bitStream.ReadBits(countBits));
    }

    public static void WriteUInt32 (this BitStream bitStream, uint val, int countBits)
    {
        bitStream.WriteBits(IntToBits((int) val, countBits));
    }

    public static string ReadZeroTerminatedString (this BitStream bitStream, Encoding encoding)
    {
        var length = 0;
        while (true)
        {
            if (!bitStream.ValidPositionWhen(8))
            {
                break;
            }

            var currentByte = bitStream.ReadByte();

            length++;

            if (currentByte is 0 or 1)
            {
                break;
            }
        }

        if (length < 2)
        {
            return string.Empty;
        }

        bitStream.SeekBack(length * 8);

        var bytes = bitStream.ReadBytes(length - 1, true);

        return encoding.GetString(bytes);
    }

    public static int BitsToInt (Bit[] bits)
    {
        var result = 0;

        for (var i = bits.Length - 1; i >= 0; i--)
        {
            result <<= 1;
            result += bits[i];
        }

        return result;
    }

    public static int BitsToInt (List<Bit> bits)
    {
        var result = 0;

        for (var i = bits.Count - 1; i >= 0; i--)
        {
            result <<= 1;
            result += bits[i];
        }

        return result;
    }

    public static long BitsToInt64 (Bit[] bits)
    {
        var result = (long) 0;

        for (var i = bits.Length - 1; i >= 0; i--)
        {
            result <<= 1;
            result += bits[i];
        }

        return result;
    }

    public static Bit[] IntToBits (int val, int length)
    {
        var result = new List<Bit>();

        while (val > 0)
        {
            result.Add(val & 0b1);
            val >>= 1;
        }

        while (result.Count < length)
        {
            result.Add(0);
        }

        return result.ToArray();
    }

    public static string ToByteString (this Bit[]? bits)
    {
        return Convert.ToHexString(BitStream.BitArrayToBytes(bits));
    }

    public static void RegisterBsonMapperForBit ()
    {
        BsonMapper.Global.RegisterType
        (
            bit => (int) bit,
            bson => new Bit((int) bson)
        );
        BsonMapper.Global.RegisterType<List<Bit>>
        (
            list => new BsonArray(list.Select(x => new BsonValue(x.AsInt())).ToArray()),
            bson => bson.AsArray.Select(x => new Bit((int) x)).ToList()
        );
    }
}