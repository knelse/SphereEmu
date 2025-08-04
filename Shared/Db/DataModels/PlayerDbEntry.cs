using System.Collections.Generic;
using LiteDB;

namespace SphServer.DataModels;

public class PlayerDbEntry
{
    public int Id { get; set; }
    [BsonIgnore] public ushort Index { get; set; }
    public string Login { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    [BsonRef("Characters")] public List<CharacterDbEntry> Characters { get; init; } = [];
}