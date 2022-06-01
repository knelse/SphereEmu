using emu.DataModels;
using emu.Helpers;

namespace emu.Packets;

public class ClientInitialData
{
    public CharacterData? Character1;
    public CharacterData? Character2;
    public CharacterData? Character3;
    public readonly int PlayerId;

    public byte[] ToByteArray(ushort playerIndex)
    {
        var firstCharData = Character1?.ToCharacterListByteArray() ?? CommonPackets.CreateNewCharacterData(playerIndex);
        var secondCharData = Character2?.ToCharacterListByteArray() ?? CommonPackets.CreateNewCharacterData(playerIndex);
        var thirdCharData = Character3?.ToCharacterListByteArray() ?? CommonPackets.CreateNewCharacterData(playerIndex);

        var charDataBytes = new byte [324];
        
        Array.Copy(firstCharData, 0, charDataBytes, 0, 108);
        Array.Copy(secondCharData, 0, charDataBytes, 108, 108);
        Array.Copy(thirdCharData, 0, charDataBytes, 216, 108);

        return charDataBytes;
    }

    public ClientInitialData(int playerId, CharacterData? char1 = null, CharacterData? char2 = null, CharacterData? char3 = null)
    {
        PlayerId = playerId;
        Character1 = char1;
        Character2 = char2;
        Character3 = char3;
    }

    public void AddNewCharacter(CharacterData newChar, int index)
    {
        this[index] = newChar;
    }

    public CharacterData? this[int index]
    {
        get => index == 0
                ? Character1
                : index == 1
                    ? Character2
                    : Character3;
        private set
        {
            if (index == 0)
            {
                Character1 = value;
            }
            else if (index == 1)
            {
                Character2 = value;
            }
            else if (index == 2)
            {
                Character3 = value;
            }
        }
    }
}