using System;
using System.Collections.Generic;
using LiteDB;
using SphServer.DataModels;
using SphServer.Helpers;

namespace SphServer.Db
{
    public static class Login
    {
        public static Player? CheckLoginAndGetPlayer(string login, string password, 
            ushort playerIndex, bool createOnNewLogin = true)
        {
            var playerCollection = MainServer.PlayerCollection;

            var player = playerCollection.Query()
                .Include(new List<BsonExpression> { "$.Characters[*]", "$.Characters[*].Clan" })
                .Where(x => x.Login == login).FirstOrDefault();

            if (player is not null)
            {
                Console.WriteLine($"SRV: Login [{login}] fetched");
                Console.WriteLine($"SRV: Checking DB password hash for [{login}]");
                
                if (!LoginHelper.EqualsHashed(password, player.PasswordHash))
                {
                    MiscHelper.SetColorAndWriteLine(ConsoleColor.Red, $"SRV: Wrong password for [{login}]");
                    return null;
                }
                
                MiscHelper.SetColorAndWriteLine(ConsoleColor.DarkGreen,
                    $"SRV: Existing player [{player.Id}] found for [{login}]");

                return player;
            }

            if (!createOnNewLogin) return null;

            MiscHelper.SetColorAndWriteLine(ConsoleColor.Green, $"SRV: Adding [{login}] to DB");

            var pwdHash = LoginHelper.GetHashedString(password);

            player = new Player
            {
                Index = playerIndex,
                Login = login,
                PasswordHash = pwdHash,
                Characters = new List<Character>()
            };

            var playerId = playerCollection.Insert(player);

            MiscHelper.SetColorAndWriteLine(ConsoleColor.Green, $"SRV: Created player [{playerId}] for [{login}]");

            return player;
        }

        public static bool IsNameValid(string name)
        {
            return !MainServer.CharacterCollection.Exists(x => x.Name == name);
        }
    }
}