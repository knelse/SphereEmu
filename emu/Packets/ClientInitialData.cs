using emu.DataModels;
using emu.Helpers;

namespace emu.Packets;

public class ClientInitialData : Packet
{
    public CharacterData? Character1;
    public CharacterData? Character2;
    public CharacterData? Character3;

    private static readonly byte[] FourBytesOfZeroes = { 0x00, 0x00, 0x00, 0x00 };

    public byte[] ToByteArray(ushort playerIndex)
    {
        var clientValidationCode = new byte[] {BitHelper.GetSecondByte(playerIndex), BitHelper.GetFirstByte(playerIndex), 0x6f, 0x08, 0x40, 0x60 };
        var firstCharData = Character1?.ToByteArray();
        var secondCharData = Character2?.ToByteArray();
        var thirdCharData = Character3?.ToByteArray();

        var clientInitialDataResult = new List<byte>();
        
        var firstCharResult = new List<byte>();
        firstCharResult.AddRange(clientValidationCode);

        if (firstCharData == null) return clientInitialDataResult.ToArray();

        firstCharResult.AddRange(firstCharData);
        firstCharResult.AddRange(FourBytesOfZeroes);
        
        clientInitialDataResult.AddRange(Packet.ToByteArray(firstCharResult.ToArray()));

        if (secondCharData == null) return clientInitialDataResult.ToArray();
        var secondCharResult = new List<byte>();
        secondCharResult.AddRange(clientValidationCode);

        secondCharResult.AddRange(secondCharData);
        secondCharResult.AddRange(FourBytesOfZeroes);
        
        clientInitialDataResult.AddRange(Packet.ToByteArray(secondCharResult.ToArray()));

        if (thirdCharData == null) return clientInitialDataResult.ToArray();
        var thirdCharResult = new List<byte>();
        thirdCharResult.AddRange(clientValidationCode);

        thirdCharResult.AddRange(thirdCharData);
        thirdCharResult.AddRange(FourBytesOfZeroes);
        
        clientInitialDataResult.AddRange(Packet.ToByteArray(thirdCharResult.ToArray()));

        return clientInitialDataResult.ToArray();
    }
}