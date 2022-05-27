using emu.DataModels;
using emu.Helpers;

namespace emu.Packets;

public class ClientInitialData
{
    public CharacterData? Character1;
    public CharacterData? Character2;
    public CharacterData? Character3;

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

    public ClientInitialData(CharacterData? char1 = null, CharacterData? char2 = null, CharacterData? char3 = null)
    {
        Character1 = char1;
        Character2 = char2;
        Character3 = char3;
    }
}