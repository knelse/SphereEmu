namespace SphServer.DataModels;

public class ClanDbEntry
{
    public int Id { get; init; }
    public string Name { get; init; }

    public static readonly ClanDbEntry DefaultClanDbEntry = new ()
    {
        Id = -1,
        Name = "___"
    };
}