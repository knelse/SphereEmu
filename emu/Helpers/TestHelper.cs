using System.Text;
using emu.DataModels;
using emu.Packets;

namespace emu.Helpers;

public class WorldCoords
{
    public uint worldX;
    public uint worldY;
    public uint worldZ;

    public override string ToString()
    {
        return "X: " + worldX + " Y: " + worldY + " Z: " + worldZ;
    }

    public WorldCoords(uint x, uint y, uint z)
    {
        worldX = x;
        worldY = y;
        worldZ = z;
    }

    public static WorldCoords ShipstoneTavern => new (2749818272, 579961153, 2748189146);
    public static WorldCoords Shipstone => new(2730780400, 3266861995, 579147184);

    public static WorldCoords AddOffsetFromShipstoneTavern(int x, int y, int z)
    {
        return new WorldCoords((uint)(ShipstoneTavern.worldX + x), (uint)(ShipstoneTavern.worldY + y), (uint)(ShipstoneTavern.worldZ + z));
    }

    public static WorldCoords AddOffsetFromShipstone(int x, int y, int z)
    {
        return new WorldCoords((uint)(Shipstone.worldX + x), (uint)(Shipstone.worldY + y), (uint)(Shipstone.worldZ + z));
    }
}

public static class Extensions
{
    public static string RemoveLineEndings(this string str)
    {
        return str.ReplaceLineEndings("\n").Replace("\n", "");
    }
}

public class TestHelper
{
    public static ClientInitialData GetTestCharData()
    {
        var testChar1 = new CharacterData
        {
            MaxHP = 4065,
            MaxMP = 4416,
            Strength = 189,
            Agility = 426,
            Accuracy = 98,
            Endurance = 316,
            Earth = 498,
            Air = 128,
            Water = 61,
            Fire = ushort.MaxValue - 44 + 1,
            PDef = 1566,
            MDef = 2080,
            Karma = KarmaTypes.Benign,
            MaxSatiety = 100,
            TitleLevelMinusOne = 233,
            DegreeLevelMinusOne = 171,
            TitleXP = 2496139,
            DegreeXP = 73313,
            CurrentSatiety = 0,
            CurrentHP = 4065,
            CurrentMP = 3620,
            AvailableTitleStats = 38,
            AvailableDegreeStats = 23,
            IsGenderFemale = false,
            Name = "Giras",
            FaceType = 0b00001100,
            HairStyle = 0b00001100,
            HairColor = 0b00001100,
            Tattoo = 0b00001100,
            Boots = 0b00001100,
            Pants = 0b00001100,
            Armor = 0b00001100,
            Helmet = 0b00001100,
            Gloves = 0b00001100,
        };

        var testChar2 = new CharacterData
        {
            MaxHP = 5555,
            MaxMP = 5678,
            Strength = 123,
            Agility = 456,
            Accuracy = 789,
            Endurance = 12345,
            Earth = 44,
            Air = 55,
            Water = 66,
            Fire = 77,
            PDef = 88,
            MDef = 99,
            Karma = KarmaTypes.Benign,
            MaxSatiety = 4444,
            TitleLevelMinusOne = 43,
            DegreeLevelMinusOne = 32,
            TitleXP = 1111,
            DegreeXP = 2222,
            CurrentSatiety = 0,
            CurrentHP = 55,
            CurrentMP = 66,
            AvailableTitleStats = 77,
            AvailableDegreeStats = 88,
            IsGenderFemale = false,
            Name = "OwO",
            FaceType = 0b01001100,
            HairStyle = 0b01001100,
            HairColor = 0b01001100,
            Tattoo = 0b00001100,
            Boots = 0b00001100,
            Pants = 0b00001100,
            Armor = 0b00001100,
            Helmet = 0b00001100,
            Gloves = 0b00001100,
        };

        var testChar3 = new CharacterData
        {
            MaxHP = 4444,
            MaxMP = 5678,
            Strength = 123,
            Agility = 456,
            Accuracy = 789,
            Endurance = 12345,
            Earth = 44,
            Air = 55,
            Water = 66,
            Fire = 77,
            PDef = 88,
            MDef = 99,
            Karma = KarmaTypes.Benign,
            MaxSatiety = 4444,
            TitleLevelMinusOne = 43,
            DegreeLevelMinusOne = 32,
            TitleXP = 1111,
            DegreeXP = 2222,
            CurrentSatiety = 0,
            CurrentHP = 55,
            CurrentMP = 66,
            AvailableTitleStats = 77,
            AvailableDegreeStats = 88,
            IsGenderFemale = true,
            Name = "oNo",
            FaceType = 0b10001100,
            HairStyle = 0b10001100,
            HairColor = 0b10001100,
            Tattoo = 0b00001100,
            Boots = 0b00001100,
            Pants = 0b00001100,
            Armor = 0b00001100,
            Helmet = 0b00001100,
            Gloves = 0b00001100,
        };

        return new ClientInitialData
        {
            Character1 = testChar1,
            Character2 = testChar2,
            Character3 = testChar3
        };
    }

    public static void DumpLoginData(byte[] rcvBuffer)
    {
        var clientLoginDataFile = File.Open("C:\\source\\client_login_dump", FileMode.Append);
        var loginEnd = 18;

        for (; loginEnd < rcvBuffer.Length; loginEnd++)
        {
            if (rcvBuffer[loginEnd] == 0)
            {
                break;
            }
        }

        var login = rcvBuffer[18..loginEnd];
        var passwordEnd = loginEnd + 1;

        for (; passwordEnd < rcvBuffer.Length; passwordEnd++)
        {
            if (rcvBuffer[passwordEnd] == 0)
            {
                break;
            }
        }

        var password = rcvBuffer[(loginEnd + 1)..passwordEnd];

        //clientLoginDataFile.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(rcvBuffer[..bytesRcvd]) + "\t" + Encoding.GetEncoding("windows-1251").GetString(rcvBuffer[..bytesRcvd]) + "\n"));

        clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + Convert.ToHexString(login) + "\t" + "Password: " +
                                                          Convert.ToHexString(password) + "\n"));

        var loginDecode = new char[login.Length];
        login[0] -= 2;

        for (var i = 0; i < login.Length; i++)
        {
            loginDecode[i] = (char)(login[i] / 4 - 1 + 'A');
        }

        clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + new string(loginDecode) + "\n"));

        clientLoginDataFile.Close();
    }

    public static string ShortToBinaryString(short x, bool reverse = false)
    {
        var str = Convert.ToString(x, 2).PadLeft(16, '0');

        if (reverse)
        {
            str = new string(str.Reverse().ToArray());
        }
        return str;
    }

    public static string UIntToBinaryString(uint x, bool reverse = false)
    {
        var str = Convert.ToString(x, 2).PadLeft(32, '0');

        if (reverse)
        {
            str = new string(str.Reverse().ToArray());
        }
        return str;
    }

    public static byte[] GetEnterGameData_1(WorldCoords coords, ushort playerIndex)
    {
        var sb = new StringBuilder();

        sb.Append(@"01001010
00000001
00101100
00000001
00000000
10101100");
        sb.Append(Convert.ToString(BitHelper.GetSecondByte(playerIndex), 2).PadLeft(8, '0'));
        sb.Append(Convert.ToString(BitHelper.GetFirstByte(playerIndex), 2).PadLeft(8, '0'));
        sb.Append(@"
01101111
00001000
00000000
11000010
11100000
00101000
01001101
00101110
01101100
00001110
00000000
01101110
00011010
10011000
00011000
00011001".ReplaceLineEndings("\n").Replace("\n", ""));
        //10100010 11000100 01100110 11110000
        //00010000 11111110 10100101 01001001 00010001
        var result = "00010000 11111110 10100101 01001001 00010001";
        var str = result.Replace(" ", "");
        // sb.Append(Convert.ToString(x_tiny, 2).PadLeft(8, '0'));
        // sb.Append(Convert.ToString(x_big, 2).PadLeft(8, '0'));
        // sb.Append(Convert.ToString(x_med, 2).PadLeft(8, '0'));
        // sb.Append(Convert.ToString(x_small, 2).PadLeft(8, '0'));
        // Console.WriteLine(UIntToBinaryString(coords.worldX, true));
        // Console.WriteLine(UIntToBinaryString(coords.worldY, true));
        // Console.WriteLine(UIntToBinaryString(coords.worldZ, true));
        sb.Append(UIntToBinaryString(coords.worldX, true));
        //00011001 01000101 00100011 01100110 00001111
        //00001111 01100110 00100011 01000101
        //01000101 00100011 01100101 10001011
        //     00011001 10001000 11010001 01010101
        //serv 00001111 01100110 00100011 01000101 11010101     11011010 00011101 01000011
        //ping 11101001 00001101 00000001 01100000 11010000     01100010 11011001 01001000 01010001 00100001 11101010 11000111 00010000 01001110 01011110 00001000 01010001 01111100 01101110 01101001 00010000 
        //     11101001 00001101 00000001 01100000 11010000     01100010 11011001 01001000 01010001 00100001 11101010 11000111 00010000 01001110 01011110 00001000 01010001 01111100 01101110 01101001 00010000 
        //var x = (float) Convert.ToUInt32(x_str [34..40] + x_str [24..32] + x_str [16..24] + x_str [8..16] + x_str[..2], 2) / 100000;

        var x_str = "00001111011001100010001101000101";
        //sb.Append(x_str[24..32] + x_str[16..24] + x_str[8..16] + x_str[..8]);
        //sb.Append("10100010110001000110011011110000");
        sb.Append(UIntToBinaryString(coords.worldY, true));
        // Console.WriteLine(sb.ToString());
        sb.Append(UIntToBinaryString(coords.worldZ, true));
        //X: 11599.47656 Y: 11261.48242 Z: 11430.44336
//01000101001000110110010110001000
//01000011000111111010100010010010
//01000100001000010111100011110000
//
        //ping 11010000 01100010 11011001 01001000 01010001 --- 00100001 11101010 11000111 00010000 01001110 01011110 00001000 01010001 01111100 01101110 01101001 00010000 
        // Console.WriteLine(worldX_str);
        // // var x = "0101001010000011";
        // // Console.WriteLine(Convert.ToInt16(x, 2));
        // var worldX_str = ShortToBinaryString(-20891);
        // var worldY_str = ShortToBinaryString(coords.worldY);
        // var worldZ_str = ShortToBinaryString(coords.worldZ);
        // sb.Append(worldX_str[12..] + "0111");
        // sb.Append(worldX_str[4..12]);
        // sb.Append("1100" + worldX_str[..4]);
        // sb.Append("10000010");
        // sb.Append(worldY_str[12..] + "1010");
        // sb.Append(worldY_str[4..12]);
        // sb.Append("0100" + worldY_str[..4]);
        // sb.Append("01100110");
        // sb.Append(worldZ_str[12..] + "0001");
        // sb.Append(worldZ_str[4..12]);
        // sb.Append("1100" + worldZ_str[..4]);
        sb.Append(@"11110001
10111001
10100101
01000001
00110111
00001101
01111001
00000000
11110000
00000000
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000000
00000000
00000000
00000000
00000100
00000000
00000000
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000000
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000100
00000000
00000100
00000000
00000100
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000100
00000000
00000000
00000000
00000100
00000000
11110000
00000000
00000000
00000000
00100100
00000000
00000000
00110010
00000000
01010010
00000011
11010100
00000111
01000000
01011000
00000000
11101000
00000011
10000000
00111110
00000000
11110100
00000001
10000000
00000100
00000000
11110100
00000001
10000000
00000100
00000000
00001100
00000110
10000000
11110111
00000000
00101000
00000000
10000000
01010111
00000000
00101000
00001000
00000000
10111111
00000000
00000000
00000000
00000000
00000000
00000000
10010000
00000001
00000000
00000000
00000000
01011100
11110001
01010011
00001011
00000000
10110100
00000000
00000000
00000000
00000000
00000000
00100111
10000001
11010100
00001000
10011000
00000001
11000000
00000110
00000000
11000000
00001111
01000000
10101001
00000000
01101000
00001001
00000000
00010011
00000001
01100000
00000000
10000000
01000101
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00101000
00000000
10000000
00000010
00000000
00101000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00110010
00000000
00101000
01000100
00000001
11010000
00010010
00110011
11111100
10100001
01001010
01010011
00010110
01010010
10000000
10010000
01010100
10110101
00010111
00000101
00000000".ReplaceLineEndings("\n").Replace("\n", ""));
         // Console.WriteLine(sb.ToString());
        return BitHelper.ReadableBinaryStringToByteArray(sb.ToString());
    }

    public static byte[] GetTestEntityData(int index)
    {
        var entityData = File.ReadAllLines("C:\\source\\entityData");
        var yOffset = 100000 * (index % 2);
        var zOffset = 7500 * (index / 2);
        var xOffset = 500 * (index % 90);
        var entity = new EntitySpawnData
        {
            ID = (ushort) (index * 2 + 2),
            EntType = (ushort) (Convert.ToUInt16(entityData[1])),// + index),
            X = (uint) (Convert.ToUInt32(entityData[2]) + xOffset * 0),
            Y = (uint) (Convert.ToUInt32(entityData[3]) + yOffset),
            Z = (uint) (Convert.ToUInt32(entityData[4]) - zOffset),
            Angle = Convert.ToByte(entityData[5]),
            HP = Convert.ToUInt16(entityData[6]),
            ModelType = (ushort) (Convert.ToUInt16(entityData[7])),// + index),
            Level = (byte) (Convert.ToByte(entityData[8]) + index),
        };

        Console.WriteLine(entity.ToString());

        return Packet.ToByteArray(entity.ToByteArray(), 1);
    }

}