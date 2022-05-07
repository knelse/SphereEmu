namespace emu.Helpers;

public static class CommonPackets
{
    public static readonly byte[]
        ReadyToLoadInitialData = { 0x0a, 0x00, 0xc8, 0x00, 0x14, 0x05, 0x00, 0x00, 0x1f, 0x42};

    public static byte[] ServerCredentials(ushort playerIndex)
    {
        var currentSphereTime = TimeHelper.EncodeCurrentSphereDateTime();

        return new byte [] {0x38, 0x00, 0x2c, 0x01, 0x00, 0x00, BitHelper.GetSecondByte(playerIndex), 
            BitHelper.GetFirstByte(playerIndex), 0x6f, 0x08, 0x40, 0x20, 0x10, currentSphereTime[0], currentSphereTime[1], 
            currentSphereTime[2], currentSphereTime[3], currentSphereTime[4], 0x7c, 0x12, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x1a, 0x3b, 0x12, 0x01, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x8d, 0x9d, 0x01, 0x00, 0x00, 0x00};
    }

    public static byte[] CharacterSelectStartData(ushort playerIndex)
    {
        return new byte[] {0x52, 0x00, 0x2c, 0x01, 0x00, 0x00, BitHelper.GetSecondByte(playerIndex), 
            BitHelper.GetFirstByte(playerIndex), 0x6f, 0x08, 0x40, 0x80, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
    }
}