using static SphServer.Helpers.BitHelper;

namespace SphServer.Helpers
{

    public static class CommonPackets
    {
        public static readonly byte[]
            ReadyToLoadInitialData = { 0x0A, 0x00, 0xC8, 0x00, 0x14, 0x05, 0x00, 0x00, 0x1F, 0x42 };

        public static readonly byte[]
            ReadyToLoadInitialDataReconnect = { 0x0A, 0x00, 0xC8, 0x00, 0x94, 0x05, 0x00, 0x00, 0x2F, 0x64 };

        public static readonly byte[]
            TransmissionEndPacket = { 0x04, 0x00, 0xF4, 0x01 };

        public static byte[] ServerCredentials(ushort playerIndex)
        {
            var currentSphereTime = TimeHelper.EncodeCurrentSphereDateTime();

            return new byte[]
            {
                0x38, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex), MinorByte(playerIndex), 0x08, 0x40, 
                0x20, 0x10, currentSphereTime[0], currentSphereTime[1], currentSphereTime[2], currentSphereTime[3], 
                currentSphereTime[4], 0x7C, 0x12, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1A, 0x3B, 
                0x12, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x00, 0x00, 0x8D, 0x9D, 0x01, 0x00, 0x00, 0x00
            };
        }

        public static byte[] CharacterSelectStartData(ushort playerIndex) =>
            new byte[]
            {
                0x52, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex), MinorByte(playerIndex), 0x08, 0x40, 
                0x80, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x00, 0x00, 0x00
            };

        public static byte[] CreateNewCharacterData(ushort playerIndex) =>
            new byte[]
            {
                0x6C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex), MinorByte(playerIndex), 0x08, 0x40, 
                0x60, 0x79, 0x91, 0x01, 0x90, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0xC8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x00, 0x90, 0x01, 0x90, 0x01, 0x10, 0x00, 0x10, 0x00, 
                0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
                0x00, 0x00, 0x00, 0xFC, 0xFF, 0xFF, 0xFF, 0x03, 0x00, 0x00, 0x00, 0x00
            };

        public static byte[] AccountOutdated(ushort playerIndex) =>
            new byte[]
            {
                0x0E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex),
                MinorByte(playerIndex), 0x08, 0x40, 0xC0, 0x01, 0x00
            };

        public static byte[] NameAlreadyExists(ushort playerIndex) =>
            new byte[]
            {
                0x0E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex),
                MinorByte(playerIndex), 0x08, 0x40, 0x00, 0x01, 0x00
            };

        public static byte[] NameCheckPassed(ushort playerIndex) =>
            new byte[]
            {
                0x0E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex),
                MinorByte(playerIndex), 0x08, 0x40, 0x80, 0x00, 0x00
            };

        public static byte[] AccountAlreadyInUse(ushort playerIndex) =>
            new byte[]
            {
                0x0E, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex),
                MinorByte(playerIndex), 0x08, 0x40, 0xA0, 0x00, 0x00
            };

        public static byte[] ClientInvulnerableEffect(ushort playerIndex) =>
            new byte[]
            {
                0x14, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex),
                MinorByte(playerIndex), 0x08, 0xC0, 0x42, 0x60, 0xFE, 0xD3, 0x90, 0x10, 0xB0, 0x17, 0x00
            };

        public static byte[] SixSecondPing(ushort playerIndex) =>
            new byte[]
            {
                0x13, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex),
                MinorByte(playerIndex), 0x08, 0xC0, 0x42, 0xA0, 0xFF, 0xD3, 0x90, 0x08, 0xB0, 0x07
            };

        public static byte[] FifteenSecondPing(ushort playerIndex) =>
            new byte[]
            {
                0x10, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(playerIndex),
                MinorByte(playerIndex), 0x08, 0x40, 0x81, 0x93, 0xEE, 0xE4, 0x08
            };
        
        public static byte[] LoadNewPlayerDungeon => new byte[]
        {
            0xBF, 0x00, 0x2C, 0x01, 0x00, 0x06, 0x7A, 0x2C, 0x0C, 0x10, 0x80, 0x2F, 0x81, 0x1F, 0x01, 0x0B, 0xE2, 0xE0, 
            0x03, 0x20, 0xA1, 0x4B, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x50, 0x64, 0x91, 0x01, 0xA0, 
            0x00, 0x03, 0x9A, 0xFE, 0x00, 0x85, 0x09, 0x00, 0xF8, 0xF9, 0xAF, 0x00, 0x06, 0x3E, 0x00, 0xC0, 0x44, 0x62, 
            0x00, 0x50, 0xC6, 0x22, 0x00, 0xC0, 0x76, 0x22, 0x00, 0x44, 0x16, 0x19, 0x00, 0x0A, 0x2F, 0x80, 0x9D, 0xF5, 
            0x0B, 0x60, 0xE1, 0x33, 0x7C, 0xA2, 0x83, 0x8D, 0xC4, 0x36, 0xA8, 0x8C, 0x45, 0xF0, 0xFB, 0xF1, 0x44, 0xC1, 
            0x88, 0x2C, 0x32, 0x00, 0x14, 0x5E, 0x40, 0x20, 0xA0, 0xF0, 0x0C, 0xA2, 0x83, 0x8D, 0xC4, 0x36, 0xA8, 0x8C, 
            0x45, 0xF0, 0xFB, 0xF1, 0x44, 0x85, 0x6F, 0xD0, 0xED, 0x2C, 0xFE, 0x55, 0x8F, 0x48, 0x06, 0x56, 0x8F, 0x48, 
            0x06, 0x06, 0xF8, 0x21, 0x2C, 0x40, 0x0D, 0x3E, 0x9F, 0x10, 0x45, 0x62, 0x97, 0x56, 0xC6, 0x22, 0x41, 0xA4, 
            0x76, 0xA2, 0xE5, 0x50, 0x14, 0x00, 0x14, 0x43, 0x3A, 0x29, 0xDE, 0x07, 0x85, 0x17, 0x00, 0x00, 0xF8, 0x25, 
            0x2C, 0xA0, 0x1F, 0x3E, 0x19, 0xA1, 0x46, 0x62, 0xB0, 0x53, 0xC6, 0x22, 0x49, 0xC2, 0x76, 0x22, 0x80, 0x44, 
            0x16, 0x19, 0x89, 0x0A, 0x59, 0x00, 0xF0, 0xFF, 0xFF, 0xFF, 0x0F, 0xC1, 0x00, 0x2C, 0x01, 0x00, 0x06, 0x7A, 
            0x15, 0x0B, 0x24, 0x83, 0xCF, 0x38, 0xA3, 0x91, 0xB8, 0x94, 0x95, 0xB1, 0xC8, 0x13, 0x31, 0x9E, 0x68, 0x0C, 
            0x14, 0x05, 0x00, 0xC5, 0x50, 0xEA, 0x80, 0x00, 0xC0, 0xCF, 0x62, 0x81, 0x3F, 0xF0, 0x01, 0xCD, 0x25, 0x12, 
            0xAB, 0xA2, 0x32, 0x16, 0x09, 0x95, 0xCB, 0x13, 0x01, 0x24, 0xB2, 0xC8, 0x00, 0x50, 0xC8, 0x02, 0x80, 0xFF, 
            0xFF, 0xFF, 0xFF, 0xC2, 0x13, 0x2C, 0x1C, 0x00, 0x04, 0xFC, 0x0C, 0x58, 0x00, 0x16, 0x1F, 0x00, 0x09, 0x5D, 
            0x12, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x22, 0x8B, 0x0C, 0x00, 0x05, 0x18, 0xD0, 0xF4, 
            0x07, 0x28, 0x64, 0x01, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF, 0xEF, 0x7F, 0x85, 0xCF, 0xF0, 0x01, 0x00, 0x26, 0x12, 
            0x03, 0x80, 0x32, 0x16, 0x01, 0x00, 0xB6, 0x13, 0x01, 0x20, 0xB2, 0xC8, 0x00, 0x50, 0x78, 0x81, 0x00, 0x81, 
            0xC2, 0x33, 0x64, 0x6D, 0xCF, 0x15, 0xEB, 0x82, 0x26, 0x12, 0x25, 0x7C, 0xD0, 0x15, 0x17, 0xBE, 0x01, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xA0, 0xF0, 0x0C, 0x19, 0xD9, 0x76, 0xC5,
            0xDB, 0xB4, 0x89, 0x44, 0x0D, 0x7E, 0x72, 0xC5, 0x85, 0x6F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x00, 0x2C, 0x01, 0x00, 0x06, 0x7A, 0xFF, 0x2B, 0x7C, 0x46, 0xE1, 
            0x19, 0x08, 0xC0, 0xE6, 0x8A, 0xF5, 0x41, 0x13, 0x89, 0xFE, 0x18, 0xE2, 0x8A, 0x0B, 0xDF, 0x00, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x03, 0x60, 0xE1, 0x33, 0x7C, 0x00, 0x80, 
            0x89, 0xC4, 0x00, 0xA0, 0x8C, 0x45, 0x00, 0x80, 0xED, 0x44, 0x00, 0x88, 0x2C, 0x32, 0x00, 0x14, 0x5E, 0x40, 
            0x40, 0xA0, 0xF0, 0x0C, 0x73, 0x7F, 0x75, 0xC5, 0x14, 0xA1, 0x89, 0x44, 0x20, 0x82, 0x71, 0xC5, 0x85, 0x6F, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF8, 0xE5, 0x3E, 0xF0, 0x19, 
            0x3E, 0x00, 0xC0, 0x44, 0x62, 0x00, 0x50, 0xC6, 0x22, 0x00, 0xC0, 0x76, 0x22, 0x00, 0x44, 0x16, 0x19, 0x00, 
            0x0A, 0x2F, 0x30, 0x20, 0x50, 0x78, 0x06, 0x99, 0x06, 0xBA, 0xE2, 0x83, 0xDA, 0x44, 0xA2, 0x2B, 0xC1, 0xB9, 
            0xE2, 0xC2, 0x37, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFC, 0x74, 
            0x1F, 0xF8, 0x01, 0x9F, 0xB9, 0x5C, 0x22, 0x31, 0xDE, 0x2A, 0x63, 0x11, 0xFB, 0xDE, 0x3C, 0x11, 0x00, 0x22, 
            0x8B, 0x0C, 0x00, 0x3F, 0x06, 0x16, 0x90, 0xC0, 0x27, 0xC3, 0x96, 0x48, 0x9C, 0xBF, 0xC9, 0x58, 0xE4, 0xDB, 
            0xD7, 0x4E, 0x04, 0x80, 0xC8, 0x22, 0x03, 0x00, 0xC9, 0x00, 0x2C, 0x01, 0x00, 0x06, 0x7A, 0x0C, 0x2C, 0x20, 
            0x41, 0xE1, 0x05, 0x02, 0x00, 0x7E, 0x02, 0x2C, 0x20, 0x81, 0xCF, 0x1B, 0x9A, 0x91, 0xD8, 0xF2, 0x92, 0xB1, 
            0x08, 0x0E, 0xB0, 0x9D, 0x08, 0x00, 0x91, 0x45, 0x06, 0x80, 0xC2, 0x0B, 0x08, 0x00, 0xFC, 0x06, 0x58, 0x40, 
            0x02, 0x9F, 0x58, 0xC7, 0x23, 0x31, 0xAE, 0x28, 0x63, 0x91, 0x1E, 0x86, 0x3B, 0x11, 0x00, 0x22, 0x8B, 0x0C, 
            0x00, 0x85, 0x17, 0x18, 0x00, 0xF8, 0x09, 0x3F, 0x80, 0x04, 0x3E, 0x1F, 0xD3, 0x46, 0x62, 0x15, 0x4F, 0xC6, 
            0x22, 0xEE, 0xA8, 0x78, 0x22, 0x00, 0x44, 0x16, 0x19, 0x00, 0x0A, 0x2F, 0x40, 0x00, 0xF0, 0x2B, 0x53, 0x00, 
            0x09, 0x7C, 0xE8, 0xAF, 0x8B, 0xC4, 0x46, 0x96, 0x8C, 0x45, 0xD2, 0x42, 0xF1, 0x44, 0x00, 0x88, 0x2C, 0x32, 
            0x00, 0x14, 0x5E, 0xA0, 0x00, 0xE0, 0xA7, 0xBF, 0x02, 0x12, 0xF8, 0x38, 0xE5, 0x12, 0x89, 0x99, 0x41, 0x19,
            0x8B, 0x74, 0x4F, 0xE6, 0x89, 0x00, 0x10, 0x59, 0x64, 0x00, 0x28, 0xBC, 0xC0, 0x01, 0xC0, 0x6F, 0x7F, 0x05, 
            0x24, 0xF0, 0x79, 0xE9, 0x25, 0x12, 0xB3, 0x61, 0x32, 0x16, 0x41, 0x82, 0xC6, 0x13, 0x01, 0x20, 0xB2, 0xC8, 
            0x00, 0x50, 0x78, 0x01, 0x03, 0x80, 0x9F, 0xDE, 0x02, 0xAE, 0xE1, 0x03, 0x20, 0xA1, 0x4B, 0x02, 0x00, 0x00, 
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x50, 0x64, 0x91, 0x01, 0x00, 0x1B, 0x00, 0x2C, 0x01, 0x00, 0x06, 0x7A, 
            0x7A, 0x0B, 0xB8, 0x46, 0x01, 0x06, 0x34, 0xFD, 0x01, 0x0A, 0x13, 0x10, 0x50, 0xC8, 0x02, 0x80, 0xFF, 0xFF, 
            0xFF, 0x7F, 0x2D, 0x00, 0x2C, 0x01, 0x00, 0x6D, 0xF7, 0x8A, 0x2C, 0xDB, 0xE1, 0x40, 0x0F, 0x61, 0x01, 0x6A, 
            0x10, 0x98, 0xF9, 0xF4, 0x35, 0xFE, 0xF2, 0x2F, 0x61, 0x01, 0xFD, 0x10, 0x00, 0x6D, 0xFE, 0xD7, 0x1F, 0xC0, 
            0xCF, 0x62, 0x81, 0x3F, 0x10, 0x54, 0x7E, 0xFE, 0xD9, 0x09, 0x00
        };

        public static byte[][] NewCharacterWorldData(ushort ID) => new[]
        {
            new byte[]
            {
                0xA8, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x61, 0x02, 0x00,
                0x0A, 0x82, 0xA0, 0xC3, 0xE1, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x50, 0x10, 0x84, 0x9D, 0x0F, 0x67, 0x00,
                0xA0, 0x25, 0x26, 0x80, 0x10, 0x7D, 0x38, 0x03, 0x00, 0x14, 0x24, 0x60, 0xA7, 0x20, 0x08, 0x14, 0x20,
                0xCE, 0x00, 0x64, 0x00, 0x00, 0x00, 0x0D, 0x1C, 0x02, 0x05, 0x00, 0x00, 0x80, 0xC6, 0x46, 0x40, 0x40,
                0x63, 0x83, 0xAC, 0xCC, 0xCC, 0xAC, 0x6C, 0x8C, 0x6E, 0x8E, 0x8B, 0x0B, 0xC0, 0xA9, 0xEC, 0x0E, 0x64,
                0x0C, 0x2D, 0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x64, 0xC9, 0xAD, 0x8C,
                0x0D, 0x2E, 0x6C, 0x2C, 0x0C, 0x04, 0x65, 0xC9, 0xAD, 0x8C, 0x0D, 0x2E, 0x6C, 0x2C, 0x0C, 0x04, 0x25,
                0xA6, 0x46, 0xC5, 0x65, 0xAE, 0xCC, 0x0C, 0xE0, 0x2B, 0x26, 0x25, 0xC5, 0x45, 0x01, 0xC0, 0x89, 0x08,
                0x64, 0x0C, 0x2D, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07,
                0x24, 0x06, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A,
                0x04, 0x72, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x63, 0x83,
                0x8C, 0x2D, 0xCC, 0x8D, 0x6C, 0x6E, 0x2C, 0x0C, 0xAE, 0x8C, 0x8B, 0x0B, 0xE0, 0x0E, 0x64, 0x0C, 0x2D,
                0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C,
                0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x25, 0xA6, 0x06, 0x86, 0x46,
                0xC5, 0x05, 0x0F, 0x0F, 0x6F, 0x47, 0xC5, 0x85, 0x0E, 0x8F, 0x0E, 0x20, 0xC8, 0x89, 0x08, 0x64, 0x0C,
                0x2D, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06,
                0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72,
                0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0x8C, 0x2D,
                0xCC, 0x8D, 0x6C, 0x6E, 0x2C, 0x0C, 0xAE, 0xEC, 0x0B, 0x4D, 0x8E, 0x8B, 0x0B, 0x00, 0x2D, 0x4C, 0x0E,
                0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C,
                0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x25, 0xA6, 0x06, 0x86, 0x46, 0xC5, 0x05,
                0x0F, 0x0F, 0x0F, 0x40, 0xC5, 0x85, 0x0E, 0x8F, 0x0E, 0x20, 0xC8, 0x89, 0x08, 0x64, 0x0C, 0x2D, 0x4C,
                0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29,
                0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72, 0x00, 0x2c,
                0x01, 0x00, 0x00, 0x04, MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0x8C, 0x2D, 0xCC, 0x8D,
                0x6C, 0x6E, 0x2C, 0x0C, 0xAE, 0xEC, 0x0B, 0x0E, 0x8D, 0x8B, 0x0B, 0x00, 0x2D, 0x4C, 0x0E, 0x24, 0xCD,
                0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x05,
                0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x25, 0xA6, 0x06, 0x86, 0x46, 0xC5, 0x05, 0x0F, 0x0F,
                0x0F, 0x40, 0xC5, 0x85, 0x0E, 0x8F, 0x0E, 0x20, 0xC8, 0x89, 0x08, 0x64, 0x0C, 0x2D, 0x4C, 0xEE, 0x8B,
                0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29, 0xA9, 0x29,
                0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72, 0x00, 0x2c, 0x01, 0x00,
                0x00, 0x04, MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0x8C, 0x2D, 0xCC, 0x8D, 0x6C, 0x6E,
                0x2C, 0x0C, 0xAE, 0xEC, 0x4B, 0x8E, 0x8C, 0x8B, 0x0B, 0x00, 0x2D, 0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4,
                0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D,
                0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x25, 0xA6, 0x06, 0x86, 0x46, 0xC5, 0x05, 0x0F, 0x0F, 0x0F, 0x40,
                0xC5, 0x85, 0x0E, 0x8F, 0x0E, 0x20, 0xC8, 0x89, 0x08, 0x64, 0x0C, 0x2D, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C,
                0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A,
                0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04,
                MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0xAC, 0x4D, 0x6C, 0x8C, 0x8B, 0x0B, 0x20, 0x0C,
                0xAE, 0xEC, 0x4B, 0x8E, 0x8C, 0x8B, 0x0B, 0x00, 0x2D, 0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC,
                0xAD, 0x4C, 0x07, 0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D,
                0x5C, 0x1D, 0x1C, 0x04, 0x25, 0xA6, 0x06, 0x86, 0x46, 0xC5, 0xA5, 0x4D, 0x6C, 0x0C, 0x40, 0xC5, 0x85,
                0x0E, 0x8F, 0x0E, 0x20, 0xC8, 0x89, 0x08, 0x64, 0x0C, 0x2D, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB,
                0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06,
                0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID),
                MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0xAC, 0xED, 0x8D, 0xAC, 0x8C, 0x6D, 0x8E, 0x8B, 0x0B, 0xE0, 0x4B,
                0x8E, 0x8C, 0x8B, 0x0B, 0x00, 0x2D, 0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07,
                0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C,
                0x04, 0x25, 0xA6, 0x06, 0x86, 0x46, 0xC5, 0xA5, 0x8D, 0x8C, 0x6D, 0x47, 0xC5, 0xA5, 0x8D, 0x4E, 0x6E,
                0x47, 0xC5, 0x85, 0x0E, 0x8F, 0x0E, 0x20, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC,
                0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29,
                0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID), MinorByte(ID),
                0x08, 0x40, 0x63, 0x83, 0xAC, 0xED, 0x8D, 0xAC, 0x8C, 0x6D, 0xEE, 0x0B, 0x4D, 0x8E, 0x8B, 0x0B, 0x80,
                0x8B, 0x0B, 0x00, 0x2D, 0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x04, 0x78,
                0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x25,
                0xA6, 0x06, 0x86, 0x46, 0xC5, 0xA5, 0x8D, 0x8C, 0x6D, 0x47, 0xC5, 0xA5, 0x8D, 0x4E, 0x0E, 0x40, 0xC5,
                0x85, 0x0E, 0x8F, 0x0E, 0x20, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84,
                0xC7, 0x07, 0x24, 0x06, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29,
                0x89, 0x0A, 0x04, 0x72, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID), MinorByte(ID), 0x08, 0x40,
                0x63, 0x83, 0xAC, 0xED, 0x8D, 0xAC, 0x8C, 0x6D, 0xEE, 0x0B, 0x0E, 0x8D, 0x8B, 0x0B, 0x80, 0x8B, 0x0B,
                0x00, 0x2D, 0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x04, 0x78, 0x9D, 0xFF,
                0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x25, 0xA6, 0x06,
                0x86, 0x46, 0xC5, 0xA5, 0x8D, 0x8C, 0x6D, 0x47, 0xC5, 0xA5, 0x8D, 0x4E, 0x0E, 0x40, 0xC5, 0x85, 0x0E,
                0x8F, 0x0E, 0x20, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07,
                0x24, 0x06, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A,
                0x04, 0x72, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x63, 0x83,
                0xAC, 0xED, 0x8D, 0xAC, 0x8C, 0x6D, 0xEE, 0x4B, 0x8E, 0x8C, 0x8B, 0x0B, 0x80, 0x8B, 0x0B, 0x00, 0x2D,
                0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C,
                0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x25, 0xA6, 0x06, 0x86, 0x46,
                0xC5, 0xA5, 0x8D, 0x8C, 0x6D, 0x47, 0xC5, 0xA5, 0x8D, 0x4E, 0x0E, 0x40, 0xC5, 0x85, 0x0E, 0x8F, 0x0E,
                0x20, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06,
                0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72,
                0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0x0C, 0x2E,
                0x4C, 0x2E, 0xAC, 0x6D, 0x8E, 0x8B, 0x0B, 0x80, 0x8B, 0x0B, 0x80, 0x8B, 0x0B, 0x00, 0x2D, 0x4C, 0x0E,
                0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C,
                0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x25, 0xA6, 0x06, 0x86, 0x46, 0xC5, 0x65,
                0xCC, 0xEC, 0x6C, 0x47, 0xC5, 0xA5, 0x4D, 0x8C, 0x0C, 0x40, 0xC5, 0x85, 0x0E, 0x8F, 0x0E, 0x20, 0x4C,
                0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29,
                0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72, 0x00, 0x2c,
                0x01, 0x00, 0x00, 0x04, MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0x0C, 0x2E, 0x4C, 0x2E,
                0xAC, 0x6D, 0x8E, 0x8B, 0xAB, 0x2D, 0x4C, 0xAF, 0x6C, 0x8E, 0x8B, 0x0B, 0x20, 0x4C, 0x0E, 0x24, 0xCD,
                0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 0x64, 0xC9, 0xAD, 0x8C, 0x0D, 0x2E, 0x6C, 0x2C, 0x0C, 0x04,
                0x65, 0xC9, 0xAD, 0x8C, 0x0D, 0x2E, 0x6C, 0x2C, 0x0C, 0x04, 0x25, 0xA6, 0x46, 0xC5, 0xA5, 0x4D, 0x8C,
                0x0C, 0x40, 0xC5, 0xA5, 0x4D, 0x8C, 0x0C, 0x40, 0xC5, 0x85, 0x0E, 0x8F, 0x0E, 0x20, 0x4C, 0xEE, 0x8B,
                0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29, 0xA9, 0x29,
                0x89, 0x0A
            },
            new byte[]
            {
                0x24, 0x06, 0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04,
                MajorByte(ID), MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0x0C, 0x2E, 0x4C, 0x2E, 0xAC, 0x6D, 0x8E, 0x8B,
                0x4B, 0xEE, 0xED, 0xAD, 0x6D, 0x8E, 0x8B, 0x0B, 0x20, 0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC,
                0xAD, 0x4C, 0x07, 0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D,
                0x5C, 0x1D, 0x1C, 0x04, 0x25, 0xA6, 0x06, 0x86, 0x46, 0xC5, 0xA5, 0x4D, 0x8C, 0x0C, 0x40, 0xC5, 0xA5,
                0x4D, 0x8C, 0x0C, 0x40, 0xC5, 0x85, 0x0E, 0x8F, 0x0E, 0x20, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB,
                0x8C, 0x2D, 0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06,
                0x00, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x72, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID),
                MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0x0C, 0x2E, 0x4C, 0x2E, 0xAC, 0x6D, 0x8E, 0x8B, 0x6B, 0xAE,
                0xAC, 0x8C, 0x6C, 0x8E, 0x8B, 0x0B, 0x20, 0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C,
                0x07, 0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D,
                0x1C, 0x04, 0x25, 0xA6, 0x06, 0x86, 0x46, 0xC5, 0x65, 0xCC, 0xEC, 0x0C, 0x40, 0xC5, 0xA5, 0x4D, 0x8C,
                0x0C, 0x40, 0xC5, 0x85, 0x0E, 0x8F, 0x0E, 0x20, 0x4C, 0xEE, 0x8B, 0xAC, 0x8C, 0xED, 0xCB, 0x8C, 0x2D,
                0xEC, 0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84,
                0x29, 0xA9, 0x29, 0x89, 0x0A, 0x04, 0x77, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, MajorByte(ID),
                MinorByte(ID), 0x08, 0x40, 0x63, 0x83, 0x0C, 0xAF, 0x0E, 0x8E, 0x2C, 0x8C, 0xAE, 0x8C, 0x8B, 0x0B, 0xA0, 
                0x8C, 0x6C, 0x8E, 0x8B, 0x0B, 0x20, 0x4C, 0x0E, 0x24, 0xCD, 0x0D, 0xE4, 0x2C, 0xAC, 0xAD, 0x4C, 0x07, 
                0x04, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 0x04, 0x05, 0x78, 0x9D, 0xFF, 0x1D, 0x5C, 0x1D, 0x1C, 
                0x04, 0x25, 0xA6, 0x06, 0x86, 0x66, 0x0A, 0x0E, 0xAD, 0x4C, 0xAE, 0xCC, 0xA5, 0x0C, 0xAF, 0x6C, 0x67, 
                0x0A, 0x0E, 0xAD, 0x4C, 0xAE, 0x6C, 0x88, 0x2D, 0xAD, 0xCC, 0x8D, 0xCE, 0xA5, 0x0C, 0xAF, 0x0C, 0xE0, 
                0x0C, 0x84, 0xC7, 0x07, 0x24, 0x06, 0x84, 0x29, 0xA9, 0x29, 0x89, 0x0A, 0x24, 0x06, 0x00, 0x84, 0x29, 
                0xA9, 0x29, 0x89, 0x0A, 0xA4, 0xB1, 0x21, 0x20, 0x00, 0x00
            }
        };
    }
}