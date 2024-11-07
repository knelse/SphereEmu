namespace Sphere.Common.Models
{
    public abstract class ItemBase
    {
        public int Id { get; set; }
    }

    public class Slot
    {
        public ItemBase Value { get; set; }

        public virtual byte FullOrEmpty => (byte)(Value == null ? 0x00 : 0x04);

        public virtual bool IsEmpty => Value == null;
    }
}
