using emu.DataModels;

namespace emu;

public class ClientInitialData : Packet
{
    private readonly byte[] ClientValidationCode = { 0x04, 0x4f, 0x6f, 0x08, 0x40, 0x60 };
    public CharacterData? Character1;
    public CharacterData? Character2;
    public CharacterData? Character3;

    private static readonly byte[] FourBytesOfZeroes = { 0x00, 0x00, 0x00, 0x00 };

    public byte[] ToByteArray()
    {
        var firstCharData = Character1?.ToByteArray();
        var secondCharData = Character1?.ToByteArray();
        var thirdCharData = Character1?.ToByteArray();

        var clientInitialDataResult = new List<byte>();
        
        var firstCharResult = new List<byte>();
        firstCharResult.AddRange(ClientValidationCode);

        if (firstCharData == null) return clientInitialDataResult.ToArray();

        firstCharResult.AddRange(firstCharData);
        firstCharResult.AddRange(FourBytesOfZeroes);
        
        clientInitialDataResult.AddRange(Packet.ToByteArray(firstCharResult.ToArray()));

        if (secondCharData == null) return clientInitialDataResult.ToArray();
        var secondCharResult = new List<byte>();
        secondCharResult.AddRange(ClientValidationCode);

        secondCharResult.AddRange(secondCharData);
        secondCharResult.AddRange(FourBytesOfZeroes);
        
        clientInitialDataResult.AddRange(Packet.ToByteArray(secondCharResult.ToArray()));

        if (thirdCharData == null) return clientInitialDataResult.ToArray();
        var thirdCharResult = new List<byte>();
        thirdCharResult.AddRange(ClientValidationCode);

        thirdCharResult.AddRange(thirdCharData);
        thirdCharResult.AddRange(FourBytesOfZeroes);
        
        clientInitialDataResult.AddRange(Packet.ToByteArray(thirdCharResult.ToArray()));

        return clientInitialDataResult.ToArray();
    }
}