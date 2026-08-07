namespace SphServer.Shared.Db.DataModels;

public class ClanDbEntry
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public static readonly ClanDbEntry DefaultClanDbEntry = new()
    {
        Id = -1,
        Name = "___"
    };
}