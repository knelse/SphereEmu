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
        Console.WriteLine("Login fetched");
    
        var playerId = -1;
    
        while (reader.Read())
        {
            Console.WriteLine("Checking DB password hash");
            playerId = reader.GetInt32(0);
            var dbPasswordHash = reader.GetString(1);
    
            if (!LoginHelper.EqualsHashed(password, dbPasswordHash)) {

                Console.WriteLine("Wrong password");
                await sqlConnection.CloseAsync();
                await reader.CloseAsync();
                return null;
            }
        }
        
        await reader.CloseAsync();
        await sqlConnection.CloseAsync();
    
        if (playerId != -1)
        {
            Console.WriteLine($"Existing player {playerId}");
            return await DbCharacters.GetPlayerCharactersFromDbAsync(playerId, playerIndex);
        }

        if (!createOnNewLogin) return null;
        
        Console.WriteLine("Adding player to DB");

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