using Microsoft.Data.SqlClient;

namespace emu.Db;

public class Startup
{
    public static async Task<SqlConnection> OpenAndGetSqlConnection()
    {
        var connectionString = await File.ReadAllTextAsync("C:\\_sphereStuff\\dbconn.txt");
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        Console.CancelKeyPress += async delegate
        {
            await connection.CloseAsync();
        };

        return connection;
    }
}