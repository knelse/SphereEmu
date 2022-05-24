using emu.DataModels;
using emu.Packets;
using Microsoft.Data.SqlClient;

namespace emu.Db;

public class Login
{
    public static async Task <ClientInitialData> LoginAndGetPlayerCharacters(SqlConnection sqlConnection, Tuple<string, string> loginAndPasswordHash, bool createOnNewLogin = true)
    {
        (var login, var passwordHash) = loginAndPasswordHash;
        await using var command = new SqlCommand($"SELECT TOP 1 [id], [pwd_hash] from dbo.players where [login] = {login}", sqlConnection);

        await using var reader = await command.ExecuteReaderAsync();

        var found = false;
        var playerId = -1;

        while (reader.Read())
        {
            var dbPasswordHash = reader.GetString(1);

            if (!dbPasswordHash.Equals(passwordHash))
            {
                return null;
            }

            playerId = reader.GetInt32(0);
        }

        if (playerId != -1)
        {
            // existing player
            return await GetPlayerCharacters(sqlConnection, playerId);
        }

        return new List<CharacterData>();
    }

    public static async Task<List<CharacterData>?> GetPlayerCharacters(SqlConnection sqlConnection, int playerId)
    {
        await using var command =
            new SqlCommand($"SELECT TOP 3 * from characters where player_id = {playerId}", sqlConnection);

        await using var reader = await command.ExecuteReaderAsync();
        
        return new List<CharacterData>()
    }
}