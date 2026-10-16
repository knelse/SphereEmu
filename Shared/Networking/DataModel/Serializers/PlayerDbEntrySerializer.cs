using System.Collections.Generic;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Shared.Networking.DataModel.Serializers;

public class PlayerDbEntrySerializer (PlayerDbEntry playerDbEntry) : SphereDbEntrySerializerBase
{
    public byte[] ToInitialDataByteArray (Guid wireId)
    {
        var data = new List<byte> ();
        foreach (var character in playerDbEntry.Characters)
        {
            character.EntityId = playerDbEntry.Index;
        }

        for (var i = 0; i < 3; i++)
        {
            var characterData = playerDbEntry.Characters.Count > i
                ? new CharacterDbEntrySerializer (playerDbEntry.Characters[i]).ToCharacterListByteArray ()
                : CommonPackets.CreateNewCharacterData (wireId);

            data.AddRange (characterData);
        }

        return data.ToArray ();
    }
}
