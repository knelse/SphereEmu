using Sphere.Common.Enums;

namespace Sphere.Common.Models
{
    public struct CharacterInfo
    {
        public CharacterInfo(Gender gender, int face, int hair, int hairColor, int tattoo)
        {
            Gender = gender;
            Face = face;
            Hair = hair;
            HairColor = hairColor;
            Tattoo = tattoo;
        }

        public Gender Gender { get; set; }

        public int Face { get; set; }

        public int Hair { get; set; }

        public int HairColor { get; set; }

        public int Tattoo { get; set; }

    }
}
