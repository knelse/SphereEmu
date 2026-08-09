using System.Text;
using System.Threading.Tasks;
using Godot;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class CharacterSelectHandler(ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private int selectedCharacterIndex = -1;

    public async Task Handle(byte[] frame, double delta)
    {
        if (selectedCharacterIndex == -1)
        {
            if (frame.Length == 0)
            {
                return;
            }

            // select existing
            if (frame.Length == 0x15)
            {
                selectedCharacterIndex = frame[17] / 4 - 1;
                return;
            }

            // delete
            if (frame[0] == 0x2A)
            {
                var index = frame[17] / 4 - 1;
                clientConnection.DeletePlayerCharacter(index);
                return;
            }

            // create
            if (frame[0] < 0x1b ||
                frame[13] != 0x08 ||
                frame[14] != 0x40 ||
                frame[15] != 0x80 ||
                frame[16] != 0x05)
            {
                return;
            }

            selectedCharacterIndex = CreateNewCharacter(frame);
        }

        if (selectedCharacterIndex == -1)
        {
            // something went wrong
            return;
        }

        clientConnection.SetSelectedCharacterIndex(selectedCharacterIndex);

        var character = clientConnection.GetSelectedCharacter();

        if (character is null)
        {
            // should never happen
            SphLogger.Error($"SRV {localId:X4}: Selected character is null");
            return;
        }

        // TODO serializer field on object instead of creating them all the time
        clientConnection.SendPacket(new CharacterDbEntrySerializer(character).ToGameDataByteArray());
        clientConnection.MoveToNextBeforeGameStage();
    }

    private int CreateNewCharacter(byte[] rcvBuffer)
    {
        SphLogger.Info($"SRV {localId:X4}: Creating new character");
        var len = rcvBuffer[0] - 20 - 5;
        var charDataBytesStart = rcvBuffer[0] - 5;
        var nameCheckBytes = rcvBuffer[20..];
        var charDataBytes = rcvBuffer[charDataBytesStart..rcvBuffer[0]];
        var sb = new StringBuilder();
        var firstLetterCharCode = ((nameCheckBytes[1] & 0b11111) << 3) + (nameCheckBytes[0] >> 5);
        var firstLetterShouldBeRussian = false;

        for (var i = 1; i < len; i++)
        {
            var currentCharCode = ((nameCheckBytes[i] & 0b11111) << 3) + (nameCheckBytes[i - 1] >> 5);

            if (currentCharCode % 2 == 0)
            {
                // English
                var currentLetter = (char)(currentCharCode / 2);
                sb.Append(currentLetter);
            }
            else
            {
                // Russian
                var currentLetter = currentCharCode >= 193
                    ? (char)((currentCharCode - 192) / 2 + 'а')
                    : (char)((currentCharCode - 129) / 2 + 'А');
                sb.Append(currentLetter);

                if (i == 2)
                {
                    // we assume first letter was russian if second letter is, this is a hack
                    firstLetterShouldBeRussian = true;
                }
            }
        }

        string name;

        if (firstLetterShouldBeRussian)
        {
            firstLetterCharCode += 1;
            var firstLetter = firstLetterCharCode >= 193
                ? (char)((firstLetterCharCode - 192) / 2 + 'а')
                : (char)((firstLetterCharCode - 129) / 2 + 'А');
            name = firstLetter + sb.ToString()[1..];
        }
        else
        {
            name = sb.ToString();
        }

        var isNameValid = true; // Login.IsNameValid(name);

        if (!isNameValid)
        {
            SphLogger.Error($"SRV {localId:X4}: Name [{name}] already exists!");
            clientConnection.SendPacket(CommonPackets.NameAlreadyExists(localId));
            return -1;
        }

        SphLogger.Info($"SRV {localId:X4}: Name [{name}] OK");

        var isGenderFemale = (charDataBytes[1] >> 4) % 2 == 1;
        var faceType = ((charDataBytes[1] & 0b111111) << 2) + (charDataBytes[0] >> 6);
        var hairStyle = ((charDataBytes[2] & 0b111111) << 2) + (charDataBytes[1] >> 6);
        var hairColor = ((charDataBytes[3] & 0b111111) << 2) + (charDataBytes[2] >> 6);
        var tattoo = ((charDataBytes[4] & 0b111111) << 2) + (charDataBytes[3] >> 6);

        if (isGenderFemale)
        {
            faceType = 256 - faceType;
            hairStyle = 255 - hairStyle;
            hairColor = 255 - hairColor;
            tattoo = 255 - tattoo;
        }

        var charIndex = rcvBuffer[17] / 4 - 1;

        var newCharacterData =
            CharacterDbEntry.CreateNewCharacter(localId, name, isGenderFemale, faceType, hairStyle, hairColor, tattoo);

        clientConnection.CreatePlayerCharacter(newCharacterData, charIndex);

        clientConnection.SendPacket(CommonPackets.NameCheckPassed(localId));

        SphLogger.Info($"SRV {localId:X4}: Successfully created character [{name}]");

        return charIndex;
    }
}