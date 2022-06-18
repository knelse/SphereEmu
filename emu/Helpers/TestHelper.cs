using System.Text;
using emu.DataModels;
using emu.Packets;

namespace emu.Helpers;

public static class Extensions
{
    public static string RemoveLineEndings(this string str)
    {
        return str.ReplaceLineEndings("\n").Replace("\n", "");
    }
}

public class TestHelper
{
    public static void DumpLoginData(byte[] rcvBuffer)
    {
        var clientLoginDataFile = File.Open("C:\\source\\client_login_dump", FileMode.Append);
        var loginEnd = 18;

        for (; loginEnd < rcvBuffer.Length; loginEnd++)
        {
            if (rcvBuffer[loginEnd] == 0 || rcvBuffer[loginEnd] == 1)
            {
                break;
            }
        }

        var login = rcvBuffer[18..loginEnd];
        var passwordEnd = loginEnd + 1;

        for (; passwordEnd < rcvBuffer.Length; passwordEnd++)
        {
            if (rcvBuffer[passwordEnd] == 0 || rcvBuffer[passwordEnd] == 1)
            {
                break;
            }
        }

        var password = rcvBuffer[(loginEnd + 1)..passwordEnd];

        //clientLoginDataFile.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(rcvBuffer[..bytesRcvd]) + "\t" + Encoding.GetEncoding("windows-1251").GetString(rcvBuffer[..bytesRcvd]) + "\n"));

        clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + Convert.ToHexString(login) + "\t" + "Password: " +
                                                          Convert.ToHexString(password) + "\n"));

        var loginDecode = new char[login.Length];
        login[0] -= 3;

        for (var i = 0; i < login.Length; i++)
        {
            if (login[i] % 2 == 0)
            {
                loginDecode[i] = (char)(login[i] / 4 - 1 + 'A');
            }
            else
            {
                loginDecode[i] = (char)(login[i] / 4 - 48 + '0');
            }
        }

        var passwordDecode = new char[password.Length];
        password[0] += 1;

        for (var i = 0; i < password.Length; i++)
        {
            if (password[i] % 2 == 0)
            {
                passwordDecode[i] = (char)(password[i] / 4 - 1 + 'A');
            }
            else
            {
                passwordDecode[i] = (char)(password[i] / 4 - 48 + '0');
            }
        }

        clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + new string(loginDecode) + "\t" + new string(passwordDecode) + "\n"));

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

    public static byte[] GetTestEntityData(int index)
    {
        var entityData = File.ReadAllLines("C:\\source\\entityData");
        var entity = new EntitySpawnData
        {
            ID = (ushort) (index * 150),
            Unknown = (ushort) (Convert.ToUInt16(entityData[1])),// + index),
            X = Convert.ToDouble(entityData[2]),
            Y = Convert.ToDouble(entityData[3]),
            Z = Convert.ToDouble(entityData[4]),
            Turn = Convert.ToDouble(entityData[5]),
            HP = Convert.ToUInt16(entityData[6]),
            TypeID = (ushort) (Convert.ToUInt16(entityData[7])),// + index),
            Level = (byte) (Convert.ToByte(entityData[8])),
        };

        return Packet.ToByteArray(entity.ToByteArray(), 1);
    }

    public static byte[] GetNewPlayerDungeonMobData(WorldCoords dungeonEntranceCoords)
    {
        var mobX = dungeonEntranceCoords.x - 50;
        var mobY = dungeonEntranceCoords.y;
        var mobZ = dungeonEntranceCoords.z + 19.5;
        var mobT = -2;
        var entity = new EntitySpawnData
        {
            ID = 54321,
            Unknown = 1260,
            X = mobX,
            Y = mobY,
            Z = mobZ,
            Turn = mobT,
            HP = 1009,
            TypeID = 1069,
            Level = 0
        };
        
        return Packet.ToByteArray(entity.ToByteArray(), 1);
    }
}