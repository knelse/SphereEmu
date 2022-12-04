using System.Collections.Generic;
using LiteDB;
using SphServer.Helpers;

namespace SphServer.DataModels;

public class Player
{
    [BsonId] 
    public int Id { get; set; }
    [BsonIgnore]
    public ushort Index { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    [BsonRef("Characters")] public List<CharacterData> Characters { get; set; }
    
    public byte[] ToInitialDataByteArray()
    {
        var data = new List<byte>();

        for (var i = 0; i < 3; i++)
        {
            var characterData = Characters.Count > i
                ? Characters[i].ToCharacterListByteArray()
                : CommonPackets.CreateNewCharacterData(Index);
            
            data.AddRange(characterData);
        }

        return data.ToArray();
    }
}