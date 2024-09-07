using Sphere.Common.Models;

namespace Sphere.Common.Helpers
{
    public static class CoordinatesHelper
    {
        public static byte[] EncodeServerCoordinate(double a)
        {
            var scale = 69;

            var a_abs = Math.Abs(a);
            var a_temp = a_abs;

            var steps = 0;

            if ((int)a_abs == 0)
            {
                scale = 58;
            }

            else if (a_temp < 2048)
            {
                while (a_temp < 2048)
                {
                    a_temp *= 2;
                    steps += 1;
                }

                scale -= (steps + 1) / 2;

                if (scale < 0)
                {
                    scale = 58;
                }
            }
            else
            {
                while (a_temp > 4096)
                {
                    a_temp /= 2;
                    steps += 1;
                }

                scale += steps / 2;
            }

            var a_3 = (byte)(((a < 0 ? 1 : 0) << 7) + scale);
            var mul = Math.Pow(2, (int)Math.Log(a_abs, 2));
            var numToEncode = (int)(0b100000000000000000000000 * (a_abs / mul + 1));

            var a_2 = (byte)(((numToEncode & 0b111111110000000000000000) >> 16) + (steps % 2 == 1 ? 0b10000000 : 0));
            var a_1 = (byte)((numToEncode & 0b1111111100000000) >> 8);
            var a_0 = (byte)(numToEncode & 0b11111111);

            return new[] { a_0, a_1, a_2, a_3 };
        }

        public static double DecodeServerCoordinate(byte[] input, int shift = 0)
        {
            var a = GetArrayWithoutBitShift(input, shift);
            var scale = a[3] & 0b1111111;

            if (scale == 58)
            {
                return 0;
            }

            var sign = (a[3] & 0b10000000) > 0 ? -1 : 1;
            var stepsIsOdd = (a[2] & 0b10000000) > 0;

            if (stepsIsOdd)
            {
                scale -= 1;
            }

            var numToEncode = ((a[2] & 0b1111111) << 16) + (a[1] << 8) + a[0];
            var baseCoord = Math.Pow(2, scale - 58);

            return sign * (1 + (double)numToEncode / 0b100000000000000000000000) * baseCoord;
        }

        //public static double DecodeClientCoordinateWithoutShift(byte[] a, bool shouldReverse = true)
        //{
        //    if (a.Length < 4)
        //    {
        //        return 0;
        //    }

        //    if (shouldReverse)
        //    {
        //        a = a.Reverse().ToArray();
        //    }
        //    BitOperations.
        //    var stream = new BitStream(a);
        //    var fraction = stream.ReadInt64(23);
        //    var scale = stream.ReadByte();
        //    var sign = stream.ReadBit().AsBool() ? -1 : 1;

        //    if (scale == 126)
        //    {
        //        return 0.0;
        //    }

        //    var baseCoord = Math.Pow(2, scale - 127);

        //    return (1 + (float)fraction / 0b100000000000000000000000) * baseCoord * sign;
        //}

        public static double DecodeClientCoordinate(byte[] a)
        {
            var x_scale = ((a[4] & 0b11111) << 3) + ((a[3] & 0b11100000) >> 5);

            if (x_scale == 126)
            {
                return 0.0;
            }

            var baseCoord = Math.Pow(2, x_scale - 127);
            var sign = (a[4] & 0b100000) > 0 ? -1 : 1;

            return (1 + (float)(((a[3] & 0b11111) << 18) + (a[2] << 10) + (a[1] << 2) +
                                 ((a[0] & 0b11000000) >> 6)) / 0b100000000000000000000000) * baseCoord * sign;
        }

        public static Coordinates GetCoordsFromPingBytes(byte[] rcvBuffer)
        {
            var x = DecodeClientCoordinate(rcvBuffer.AsSpan(21, 5).ToArray());
            var y = -DecodeClientCoordinate(rcvBuffer.AsSpan(25, 5).ToArray());
            var z = DecodeClientCoordinate(rcvBuffer.AsSpan(29, 5).ToArray());
            var turn = DecodeClientCoordinate(rcvBuffer.AsSpan(33, 5).ToArray());

            return new Coordinates(x, y, z, turn);
        }

        private static byte[] GetArrayWithoutBitShift(byte[] input, int shift = 0)
        {
            if (shift <= 0)
            {
                return input;
            }

            var result = new byte[input.Length - 1];
            // example for 3 bits
            // 11111111 -> 11111000 = 11111111 - 111 = (2^8 - 1) - (2^3 - 1) = 2^8 - 2^3 + 1
            // for specific shift: (2^8 - 1) - 2^shift + 1 = 2^8 - 2^shift
            // then >> by shift
            var twoPowShift = (int)Math.Round(Math.Pow(2, shift));
            var bitRemainder = twoPowShift - 1;
            var bitShift = 256 - twoPowShift;

            for (var i = 0; i < result.Length; i++)
            {
                var t1 = input[i] & bitShift;
                var t2 = input[i + 1] & bitRemainder;
                result[i] = (byte)(((input[i] & bitShift) >> shift) + ((input[i + 1] & bitRemainder) << (8 - shift)));
            }

            return result;
        }
    }
}
