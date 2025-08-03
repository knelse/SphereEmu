using System.Collections.Generic;
using LiteDB;
using SphServer.Helpers;
using SphServer.Shared.Networking;

namespace SphServer.DataModels;

public class Player
{
    public int Id { get; set; }
    [BsonIgnore] public ushort Index { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    [BsonRef("Characters")] public List<Character> Characters { get; set; } = new ();

    public byte[] ToInitialDataByteArray ()
    {
        var data = new List<byte>();
        foreach (var character in Characters)
        {
            character.ClientIndex = Index;
        }

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