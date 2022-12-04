using LiteDB;

namespace SphServer.DataModels;

public class Clan
{
    [BsonId] 
    public int Id { get; set; }
    public string Name { get; set; }
    
    public static readonly Clan DefaultClan = new ()
    {
        Id = 0,
        Name = "___"
    };
}