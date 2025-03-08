using BitStreams;
using Sphere.Common.Helpers;
using Sphere.Common.Interfaces.Types;

namespace Sphere.Common.Models
{
    public struct Coordinates : IBitWritable<Coordinates>
    {
        public float X { get; set; }

        public float Y { get; set; }

        public float Z { get; set; }

        public int Angle { get; set; }

        public Coordinates()
        {

        }

        public Coordinates(float x, float y, float z, int angle)
        {
            X = x;
            Y = y;
            Z = z;
            Angle = angle;
        }

        public Coordinates(double x, double y, double z, int angle)
        {
            X = (float)x;
            Y = (float)y;
            Z = (float)z;
            Angle = angle;
        }

        public float Distance(Coordinates to)
        {
            return (to - this).Length();
        }

        public static Coordinates operator -(Coordinates left, Coordinates right)
        {
            left.X -= right.X;
            left.Y -= right.Y;
            left.Z -= right.Z;
            return left;
        }

        public static Coordinates operator +(Coordinates left, Coordinates right)
        {
            left.X += right.X;
            left.Y += right.Y;
            left.Z += right.Z;
            return left;
        }

        public readonly float Length()
        {
            var num = X * X;
            var num2 = Y * Y;
            var num3 = Z * Z;
            return MathF.Sqrt(num + num2 + num3);
        }

        public readonly byte[] GetBytes()
        {
            Span<byte> bytes = [
                ..CoordinatesHelper.EncodeServerCoordinate(X),
                ..CoordinatesHelper.EncodeServerCoordinate(-Y),
                ..CoordinatesHelper.EncodeServerCoordinate(Z),
                BitConverter.GetBytes(Angle).First(),
            ];

            return bytes.ToArray();
        }

        public static BitStream WriteVBytes(BitStream stream, Coordinates value)
        {
            stream.WriteBytes(value.GetBytes());
            return stream;
        }
    }
}
