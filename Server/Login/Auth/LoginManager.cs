using System;
using System.Security.Cryptography;
using SphServer.DataModels;
using SphServer.Providers;

namespace SphServer.Db;

public static class LoginManager
{
    public static PlayerDbEntry? CheckLoginAndGetPlayer (string login, string password,
        ushort playerIndex, bool createOnNewLogin = true)
    {
        var playerCollection = DbConnection.Players;

        var player = playerCollection.Query()
            .Include(["$.Characters[*]", "$.Characters[*].Clan"])
            .Where(x => x.Login == login).FirstOrDefault();

        if (player is not null)
        {
            SphLogger.Info($"SRV: Login [{login}] fetched");
            SphLogger.Info($"SRV: Checking DB password hash for [{login}]");

            if (!EqualsHashed(password, player.PasswordHash))
            {
                SphLogger.Error($"SRV: Wrong password for [{login}]");
                return null;
            }

            SphLogger.Info($"SRV: Existing player [{player.Id}] found for [{login}]");

            return player;
        }

        if (!createOnNewLogin)
        {
            return null;
        }

        SphLogger.Info($"SRV: Adding [{login}] to DB");

        var pwdHash = GetHashedString(password);

        player = new PlayerDbEntry
        {
            Index = playerIndex,
            Login = login,
            PasswordHash = pwdHash,
            Characters = []
        };

        var playerId = playerCollection.Insert(player);

        SphLogger.Info($"SRV: Created player [{playerId}] for [{login}]");

        return player;
    }

    public static bool IsNameValid (string name)
    {
        return !DbConnection.Characters.Exists(x => x.Name == name);
    }

    private static string GetHashedString (string str)
    {
        var salt = new byte[16];
        var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(str, salt, 100000, HashAlgorithmName.SHA3_256, 20);
        var saltedHash = new byte[36];
        Array.Copy(salt, 0, saltedHash, 0, 16);
        Array.Copy(hash, 0, saltedHash, 16, 20);

        return Convert.ToBase64String(saltedHash);
    }

    private static bool EqualsHashed (string password, string hashedPassword)
    {
        var hashBytes = Convert.FromBase64String(hashedPassword);
        var salt = new byte[16];
        Array.Copy(hashBytes, 0, salt, 0, 16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA3_256, 20);

        for (var i = 0; i < 20; i++)
        {
            if (hashBytes[i + 16] != hash[i])
            {
                return false;
            }
        }

        return true;
    }
}