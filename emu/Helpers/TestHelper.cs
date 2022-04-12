using System.Text;
using emu.DataModels;

namespace emu.Helpers;

public class TestHelper
{
    public static ClientInitialData GetTestCharData()
    {
        var testChar1 = new CharacterData
            {
                MaxHP = 1234,
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
                CurrentSatiety = 3333,
                CurrentHP = 55,
                CurrentMP = 66,
                AvailableTitleStats = 77,
                AvailableDegreeStats = 88,
                IsGenderFemale = false,
                Name = "UwUwHaTsThIs",
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
                CurrentSatiety = 3333,
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
                CurrentSatiety = 3333,
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
                    
        clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + Convert.ToHexString(login) + "\t" + "Password: " + Convert.ToHexString(password) + "\n"));

        var loginDecode = new char[login.Length];
        login[0] -= 2;

        for (var i = 0; i < login.Length; i++)
        {
            loginDecode[i] = (char) (login[i] / 4  - 1 + 'A');
        }
        clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + new string(loginDecode) + "\n"));
                    
        clientLoginDataFile.Close();
    }
}