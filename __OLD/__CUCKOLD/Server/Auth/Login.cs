using System;
using System.Collections.Generic;
using LiteDB;
using PacketLogViewer.Extensions;
using SphServer.DataModels;
using SphServer.Helpers;
using SphServer.Providers;

namespace SphServer.Db;

public static class Login
{
    public static Player? CheckLoginAndGetPlayer (string login, string password,
        ushort playerIndex, bool createOnNewLogin = true)
    {
        var playerCollection = DbConnectionProvider.PlayerCollection;

        var player = playerCollection.Query()
            .Include(new List<BsonExpression> { "$.Characters[*]", "$.Characters[*].Clan" })
            .Where(x => x.Login == login).FirstOrDefault();

        if (player is not null)
        {
            Console.WriteLine($"SRV: Login [{login}] fetched");
            Console.WriteLine($"SRV: Checking DB password hash for [{login}]");

            if (!LoginHelper.EqualsHashed(password, player.PasswordHash))
            {
                ConsoleExtensions.WriteLineColored($"SRV: Wrong password for [{login}]", ConsoleColor.Red);
                return null;
            }

            ConsoleExtensions.WriteLineColored($"SRV: Existing player [{player.Id}] found for [{login}]",
                ConsoleColor.DarkGreen);

            return player;
        }

        if (!createOnNewLogin)
        {
            return null;
        }

        ConsoleExtensions.WriteLineColored($"SRV: Adding [{login}] to DB", ConsoleColor.Green);

        var pwdHash = LoginHelper.GetHashedString(password);

        player = new Player
        {
            Index = playerIndex,
            Login = login,
            PasswordHash = pwdHash,
            Characters = new List<Character>()
        };

        var playerId = playerCollection.Insert(player);

        ConsoleExtensions.WriteLineColored($"SRV: Created player [{playerId}] for [{login}]", ConsoleColor.Green);

        return player;
    }

    public static bool IsNameValid (string name)
    {
        return !DbConnectionProvider.CharacterCollection.Exists(x => x.Name == name);
    }
}