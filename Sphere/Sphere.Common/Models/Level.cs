namespace Sphere.Common.Models
{
    public class Level
    {
        public int TitleLvl { get; set; }

        public int DegreeLvl { get; set; }

        public int RealTitleLvl => TitleLvl % 60;

        public int RealDegreeLvl => DegreeLvl % 60;

        public int TitleRebirthCount => TitleLvl / 60;

        public int DegreeRebirthCount => DegreeLvl / 60;

        public byte[] ToBytes()
        {
            var toEncode = (DegreeLvl) * 100 + (TitleLvl);

            return [
                (byte)(((toEncode & 0b111111) << 2) + 2),
                (byte)((toEncode & 0b11111111000000) >> 6),
            ];
        }
    }
}
