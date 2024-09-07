namespace Sphere.Common.Models
{
    public class Attributes
    {
        public int Endurance { get; set; }

        public int Strength { get; set; }

        public int Accuracy { get; set; }

        public int Dexterity { get; set; }

        public int Fire { get; set; }

        public int Earth { get; set; }

        public int Water { get; set; }

        public int Air { get; set; }

        public int TotalTitle => Endurance + Strength + Accuracy + Dexterity;

        public int TotalDegree => Fire + Earth + Water + Air;
    }
}
