using System;
using System.Data.SqlClient;
using System.IO;

namespace SphServer.Db
{
    public class Startup
    {
        public static SqlConnection OpenAndGetSqlConnection()
        {
            var connectionString = File.ReadAllText("C:\\_sphereStuff\\dbconn.txt");
            var connection = new SqlConnection(connectionString);
            connection.Open();
            Console.CancelKeyPress += delegate { connection.Close(); };

            return connection;
        }
    }
}