using System.Data;
using emu.DataModels;
using emu.Helpers;
using emu.Packets;
using Microsoft.Data.SqlClient;

namespace emu.Db;

public class Login
{
    public static async Task <ClientInitialData?> CheckLoginAndGetPlayerCharacters(string login, string password, bool createOnNewLogin = true)
    {
        var sqlConnection = await Startup.OpenAndGetSqlConnection();
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
            Console.WriteLine(playerId + "\t" + dbPasswordHash);
    
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
            // existing player
            return new ClientInitialData();
        }

        if (createOnNewLogin)
        {
            var sqlConnectionToAdd = await Startup.OpenAndGetSqlConnection();
            Console.WriteLine("Adding player to DB");
            var pwdHash = LoginHelper.GetHashedString(password);
            var addCmdStr = "insert into [players] ([login], [pwd_hash]) values (@login_str, @pwd_hash);";
            await using var addNewPlayerCommand =
                new SqlCommand(addCmdStr, sqlConnectionToAdd);
            addNewPlayerCommand.Parameters.AddWithValue("@login_str", login);
            addNewPlayerCommand.Parameters.AddWithValue("@pwd_hash", pwdHash);
            Console.WriteLine(addNewPlayerCommand.CommandText + " " + login + " " + pwdHash);
            Console.WriteLine(await addNewPlayerCommand.ExecuteNonQueryAsync());
            await sqlConnectionToAdd.CloseAsync();
            return new ClientInitialData();
        }

        return null;
    }

    public static async Task<List<CharacterData>?> GetPlayerCharacters(SqlConnection sqlConnection, int playerId)
    {
        await using var command =
            new SqlCommand("SELECT TOP 3 [max_hp],[max_mp],[strength],[agility],[accuracy],[endurance],[earth],[air]," +
                           "[water],[fire],[pdef],[mdef],[karma],[max_satiety],[title_level],[degree_level],[title_xp]," +
                           "[degree_xp],[current_satiety],[current_hp],[current_mp],[available_stats_title]," +
                           "[available_stats_degree],[gender_is_female],[name],[face_type],[hair_style],[hair_color]," +
                           "[tattoo],[boots],[pants],[armor],[helmet],[gloves],[deletion_is_not_requested],[x],[y],[z]," +
                           $"[turn],[id],[player_id] from characters with (nolock) where player_id = {playerId} order by [id]", sqlConnection);

        await using var reader = await command.ExecuteReaderAsync();

        return new List<CharacterData>();
    }
}