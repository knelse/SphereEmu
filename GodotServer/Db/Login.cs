using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using SphServer.Helpers;
using SphServer.Packets;

namespace SphServer.Db
{

    public class Login
    {
        public static ClientInitialData? CheckLoginAndGetPlayerCharacters(string login,
            string password, ushort playerIndex, bool createOnNewLogin = true)
        {
            var sqlConnection = Startup.OpenAndGetSqlConnection();
            using var command =
                new SqlCommand("SELECT TOP 1 [id], [pwd_hash] from dbo.players with (nolock) where [login] = @login;",
                    sqlConnection);
            command.Parameters.AddWithValue("@login", login);

            var reader = command.ExecuteReader();
            Console.WriteLine($"SRV: Login [{login}] fetched");

            var playerId = -1;

            while (reader.Read())
            {
                Console.WriteLine($"SRV: Checking DB password hash for [{login}]");
                playerId = reader.GetInt32(0);
                var dbPasswordHash = reader.GetString(1);

                if (!LoginHelper.EqualsHashed(password, dbPasswordHash))
                {
                    MiscHelper.SetColorAndWriteLine(ConsoleColor.Red, $"SRV: Wrong password for [{login}]");
                    sqlConnection.Close();
                    reader.Close();

                    return null;
                }
            }

            reader.Close();
            sqlConnection.Close();

            if (playerId != -1)
            {
                MiscHelper.SetColorAndWriteLine(ConsoleColor.DarkGreen,
                    $"SRV: Existing player [{playerId}] found for [{login}]");

                return DbCharacters.GetPlayerCharactersFromDb(playerId, playerIndex);
            }

            if (!createOnNewLogin) return null;

            MiscHelper.SetColorAndWriteLine(ConsoleColor.Green, $"SRV: Adding [{login}] to DB");

            var sqlConnectionToAdd = Startup.OpenAndGetSqlConnection();
            var pwdHash = LoginHelper.GetHashedString(password);
            var addCmdStr = "insert into [players] ([login], [pwd_hash]) values (@login_str, @pwd_hash);";
            using var addNewPlayerCommand =
                new SqlCommand(addCmdStr, sqlConnectionToAdd);
            addNewPlayerCommand.Parameters.AddWithValue("@login_str", login);
            addNewPlayerCommand.Parameters.AddWithValue("@pwd_hash", pwdHash);
            addNewPlayerCommand.ExecuteNonQuery();
            var playerIdCmd = new SqlCommand("select [id] from [players] with (nolock) where [login] = @loginAdded",
                sqlConnectionToAdd);
            playerIdCmd.Parameters.AddWithValue("@loginAdded", login);
            playerId = (int?) playerIdCmd.ExecuteScalar() ?? 0;
            sqlConnectionToAdd.Close();

            MiscHelper.SetColorAndWriteLine(ConsoleColor.Green, $"SRV: Created player [{playerId}] for [{login}]");

            return new ClientInitialData(playerId);

        }

        public static bool IsNameValid(string name)
        {
            var sqlConnection = Startup.OpenAndGetSqlConnection();
            using var command =
                new SqlCommand("SELECT count (1) from characters where name = @name", sqlConnection);
            command.Parameters.AddWithValue("@name", name);
            var count = (command.ExecuteScalar() as int?) ?? 0;
            sqlConnection.Close();

            return count == 0;
        }
    }
}