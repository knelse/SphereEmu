namespace emu.Helpers;

public static class CommonPackets
{
    public static readonly byte[]
        ReadyToLoadInitialData = { 0x0a, 0x00, 0xc8, 0x00, 0x14, 0x05, 0x00, 0x00, 0x1f, 0x42};

    public static byte[] ServerCredentials(ushort playerIndex)
    {
        var currentSphereTime = TimeHelper.EncodeCurrentSphereDateTime();

        return new byte [] {0x38, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, BitHelper.GetSecondByte(playerIndex), 
            BitHelper.GetFirstByte(playerIndex), 0x08, 0x40, 0x20, 0x10, currentSphereTime[0], currentSphereTime[1], 
            currentSphereTime[2], currentSphereTime[3], currentSphereTime[4], 0x7c, 0x12, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x1a, 0x3b, 0x12, 0x01, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x8d, 0x9d, 0x01, 0x00, 0x00, 0x00};
    }

    public static byte[] CharacterSelectStartData(ushort playerIndex)
    {
        return new byte[] {0x52, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, BitHelper.GetSecondByte(playerIndex), 
            BitHelper.GetFirstByte(playerIndex), 0x08, 0x40, 0x80, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
    }

    public static byte[] CreateNewCharacterData(ushort playerIndex)
    {
        // 6C002C01003AFC41FA084060799101900100000000000000000000000000000000000000000CC800000000000000000000000000C800900190011000100010000000000000000000000000000000000000000000000010000000000000000000000000FCFFFFFF0300000000
        // 6C002C010000044F6F084060 9E 853F0045F402A8068801F004C8070002F40050FF7B188020149001A403AC022C5A98008479040000002C012C0198005C0014031313001CA5C985CD0100000000000000000000000000C0C0C0C0C0C0C0C0C000C0C000FCFFFFFF0300000000

       return new byte[]
        {
            0x6c, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, BitHelper.GetSecondByte(playerIndex),
            BitHelper.GetFirstByte(playerIndex),
            0x08, 0x40, 0x60, 0x79, 0x91, 0x01, 0x90, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0xC8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x00, 0x90, 0x01, 0x90, 0x01, 0x10, 0x00, 0x10, 0x00,
            0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0xFC, 0xFF, 0xFF, 0xFF, 0x03, 0x00, 0x00, 0x00, 0x00
        };
    }

    public static byte[] AccountOutdated(ushort playerIndex)
    {
        return new byte[]
        {
            0x0e, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, BitHelper.GetSecondByte(playerIndex),
            BitHelper.GetFirstByte(playerIndex), 0x08, 0x40, 0xC0, 0x01, 0x00
        };
    }

    public static byte[] NameAlreadyExists(ushort playerIndex)
    {
        // 0E002C01009EAE13A60840000100
        return new byte[]
        {
            0x0e, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, BitHelper.GetSecondByte(playerIndex),
            BitHelper.GetFirstByte(playerIndex), 0x08, 0x40, 0x00, 0x01, 0x00
        };
    }

    public static byte[] NameCheckPassed(ushort playerIndex)
    {
        // 0E002C01007CB413A60840800000
        return new byte[]
        {
            0x0e, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, BitHelper.GetSecondByte(playerIndex),
            BitHelper.GetFirstByte(playerIndex), 0x08, 0x40, 0x80, 0x00, 0x00
        };
    }
}