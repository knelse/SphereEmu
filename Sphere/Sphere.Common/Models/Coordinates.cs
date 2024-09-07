namespace Sphere.Common.Models
{
    public struct Coordinates
    {
        public Coordinates(double x, double y, double z, double angle)
        {
            X = x;
            Y = y;
            Z = z;
            Angle = angle;
        }

        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }

        public double Angle { get; set; }
    }
}
