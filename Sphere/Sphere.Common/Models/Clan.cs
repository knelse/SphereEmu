namespace Sphere.Common.Models
{
    public class Clan
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public static readonly Clan DefaultClan = new()
        {
            Id = -1,
            Name = "___"
        };
    }
}
