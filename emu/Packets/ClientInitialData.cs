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
        var firstCharData = Character1?.ToByteArray() ?? CommonPackets.CreateNewCharacterData(playerIndex);
        var secondCharData = Character2?.ToByteArray() ?? CommonPackets.CreateNewCharacterData(playerIndex);
        var thirdCharData = Character3?.ToByteArray() ?? CommonPackets.CreateNewCharacterData(playerIndex);

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

    public void AddNewCharacter(CharacterData newChar)
    {
        if (Character1 is null)
        {
            Character1 = newChar;
        }
        else if (Character2 is null)
        {
            Character2 = newChar;
        }
        else if (Character3 is null)
        {
            Character3 = newChar;
        }
    }
}