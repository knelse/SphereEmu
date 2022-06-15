using emu.Helpers;
using emu.Packets;
using Microsoft.Data.SqlClient;

namespace emu.Db;

public class Login
{
    public static async Task <ClientInitialData?> CheckLoginAndGetPlayerCharactersAsync(string login, string password, ushort playerIndex, bool createOnNewLogin = true)
    {
        var sqlConnection = await Startup.OpenAndGetSqlConnectionAsync();
        await using var command = new SqlCommand("SELECT TOP 1 [id], [pwd_hash] from dbo.players with (nolock) where [login] = @login;", sqlConnection);
        command.Parameters.AddWithValue("@login", login);

        var reader = await command.ExecuteReaderAsync();
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
                await sqlConnection.CloseAsync();
                await reader.CloseAsync();
                return null;
            }
        }
        
        await reader.CloseAsync();
        await sqlConnection.CloseAsync();
    
        if (playerId != -1)
        {
            MiscHelper.SetColorAndWriteLine(ConsoleColor.DarkGreen, $"SRV: Existing player [{playerId}] found for [{login}]");
            return await DbCharacters.GetPlayerCharactersFromDbAsync(playerId, playerIndex);
        }

        if (!createOnNewLogin) return null;

        MiscHelper.SetColorAndWriteLine(ConsoleColor.Green, $"SRV: Adding [{login}] to DB");

        var sqlConnectionToAdd = await Startup.OpenAndGetSqlConnectionAsync();
        var pwdHash = LoginHelper.GetHashedString(password);
        var addCmdStr = "insert into [players] ([login], [pwd_hash]) values (@login_str, @pwd_hash);";
        await using var addNewPlayerCommand =
            new SqlCommand(addCmdStr, sqlConnectionToAdd);
        addNewPlayerCommand.Parameters.AddWithValue("@login_str", login);
        addNewPlayerCommand.Parameters.AddWithValue("@pwd_hash", pwdHash);
        await addNewPlayerCommand.ExecuteNonQueryAsync();
        var playerIdCmd = new SqlCommand("select [id] from [players] with (nolock) where [login] = @loginAdded", sqlConnectionToAdd);
        playerIdCmd.Parameters.AddWithValue("@loginAdded", login);
        playerId = (int?) await playerIdCmd.ExecuteScalarAsync() ?? 0;
        await sqlConnectionToAdd.CloseAsync();
        
        MiscHelper.SetColorAndWriteLine(ConsoleColor.Green, $"SRV: Created player [{playerId}] for [{login}]");
        
        return new ClientInitialData(playerId);

    }

    public static async Task<bool> IsNameValidAsync(string name)
    {
        var sqlConnection = await Startup.OpenAndGetSqlConnectionAsync();
        await using var command =
            new SqlCommand("SELECT count (1) from characters where name = @name", sqlConnection);
        command.Parameters.AddWithValue("@name", name);
        var count = ((await command.ExecuteScalarAsync()) as int?) ?? 0;
        await sqlConnection.CloseAsync();
        
        return count == 0;
    }
}