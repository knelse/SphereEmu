using Sphere.Common.Enums;

namespace Sphere.Common.Models
{
    public class OccupationModel
    {
        public OccupationEnum Occupation { get; set; }

        public int Level { get; set; }

        public byte[] ToBytes()
        {
            if (Occupation == OccupationEnum.None)
            {
                return [0x00];
            }
            else
            {
                return [(byte)((1 << 7) + ((byte)Occupation << 1))];
            }
        }
    }
}
