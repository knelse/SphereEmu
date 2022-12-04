using System;
using System.Linq;
using System.Text.RegularExpressions;
using SphServer.DataModels;
using SphServer.Packets;

namespace SphServer.Helpers
{

    public static class Extensions
    {
        public static string RemoveLineEndings(this string str)
        {
            return Regex.Replace(str, @"\r\n?|\n", "");
        }
    }

    public class TestHelper
    {
        // public static void DumpLoginData(byte[] rcvBuffer)
        // {
        //     var clientLoginDataFile = File.Open("C:\\source\\client_login_dump", File.READWRITE);
        //     var loginEnd = 18;
        //
        //     for (; loginEnd < rcvBuffer.Length; loginEnd++)
        //     {
        //         if (rcvBuffer[loginEnd] == 0 || rcvBuffer[loginEnd] == 1)
        //         {
        //             break;
        //         }
        //     }
        //
        //     var login = rcvBuffer[18..loginEnd];
        //     var passwordEnd = loginEnd + 1;
        //
        //     for (; passwordEnd < rcvBuffer.Length; passwordEnd++)
        //     {
        //         if (rcvBuffer[passwordEnd] == 0 || rcvBuffer[passwordEnd] == 1)
        //         {
        //             break;
        //         }
        //     }
        //
        //     var password = rcvBuffer[(loginEnd + 1)..passwordEnd];
        //
        //     //clientLoginDataFile.Write(Encoding.ASCII.GetBytes(ConvertHelper.ToHexString(rcvBuffer[..bytesRcvd]) + "\t" + Encoding.GetEncoding("windows-1251").GetString(rcvBuffer[..bytesRcvd]) + "\n"));
        //
        //     clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + ConvertHelper.ToHexString(login) + "\t" + "Password: " +
        //                                                       ConvertHelper.ToHexString(password) + "\n"));
        //
        //     var loginDecode = new char[login.Length];
        //     login[0] -= 3;
        //
        //     for (var i = 0; i < login.Length; i++)
        //     {
        //         if (login[i] % 2 == 0)
        //         {
        //             loginDecode[i] = (char)(login[i] / 4 - 1 + 'A');
        //         }
        //         else
        //         {
        //             loginDecode[i] = (char)(login[i] / 4 - 48 + '0');
        //         }
        //     }
        //
        //     var passwordDecode = new char[password.Length];
        //     password[0] += 1;
        //
        //     for (var i = 0; i < password.Length; i++)
        //     {
        //         if (password[i] % 2 == 0)
        //         {
        //             passwordDecode[i] = (char)(password[i] / 4 - 1 + 'A');
        //         }
        //         else
        //         {
        //             passwordDecode[i] = (char)(password[i] / 4 - 48 + '0');
        //         }
        //     }
        //
        //     clientLoginDataFile.Write(Encoding.ASCII.GetBytes("Login: " + new string(loginDecode) + "\t" + new string(passwordDecode) + "\n"));
        //
        //     clientLoginDataFile.Close();
        // }

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
            var entityData = System.IO.File.ReadAllLines("C:\\source\\entityData");
            var entity = new GameEntity
            {
                Unknown = Convert.ToUInt16(entityData[1]),
                X = Convert.ToDouble(entityData[2]),
                Y = -Convert.ToDouble(entityData[3]),
                Z = Convert.ToDouble(entityData[4]),
                Turn = Convert.ToDouble(entityData[5]),
                CurrentHP = Convert.ToUInt16(entityData[6]),
                TypeID = Convert.ToUInt16(entityData[7]),
                TitleLevelMinusOne = Convert.ToByte(entityData[8])
            };
            var id = MainServer.AddToGameObjects(entity);
            entity.Id = id;

            return Packet.ToByteArray(entity.ToByteArray(), 1);
        }

        public static byte[] GetNewPlayerDungeonMobData(WorldCoords dungeonEntranceCoords)
        {
            var mobX = dungeonEntranceCoords.x - 50;
            var mobY = dungeonEntranceCoords.y;
            var mobZ = dungeonEntranceCoords.z + 19.5;
            var mobT = 90;
            var entity = new GameEntity
            {
                Unknown = 1260,
                X = mobX,
                Y = -mobY,
                Z = mobZ,
                Turn = mobT,
                CurrentHP = 1009,
                TypeID = 1241,
                TitleLevelMinusOne = 0
            };
            var id = MainServer.AddToGameObjects(entity);
            entity.Id = id;

            return Packet.ToByteArray(entity.ToByteArray(), 1);
        }

        public static byte[] GetTestMobData()
        {
            var id = (ushort) 0xb19f;
            var entity = new GameEntity
            {
                Id = id,
                Unknown = 1260,
                X = 2310,
                Y = 159.5,
                Z = -2500,
                Turn = 0,
                CurrentHP = 1009,
                TypeID = 1069,
                TitleLevelMinusOne = 0
            };
            MainServer.TryAddToGameObjects(id, entity);

            return Packet.ToByteArray(entity.ToByteArray(), 1);
        }
    }
}