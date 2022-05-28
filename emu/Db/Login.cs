using emu.DataModels;
using emu.Helpers;
using emu.Packets;
using Microsoft.Data.SqlClient;

namespace emu.Db;

public class Login
{
    public static async Task <ClientInitialData?> CheckLoginAndGetPlayerCharacters(string login, string password, ushort playerIndex, bool createOnNewLogin = true)
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
            return await GetPlayerCharacters(playerId, playerIndex);
        }

        if (!createOnNewLogin) return null;
        
        Console.WriteLine("Adding player to DB");

        var sqlConnectionToAdd = await Startup.OpenAndGetSqlConnection();
        var pwdHash = LoginHelper.GetHashedString(password);
        var addCmdStr = "insert into [players] ([login], [pwd_hash]) values (@login_str, @pwd_hash);";
        await using var addNewPlayerCommand =
            new SqlCommand(addCmdStr, sqlConnectionToAdd);
        addNewPlayerCommand.Parameters.AddWithValue("@login_str", login);
        addNewPlayerCommand.Parameters.AddWithValue("@pwd_hash", pwdHash);
        await addNewPlayerCommand.ExecuteNonQueryAsync();
        await sqlConnectionToAdd.CloseAsync();
        return new ClientInitialData();

    }

    public static async Task<ClientInitialData?> GetPlayerCharacters(int playerId, ushort playerIndex)
    {
        var sqlConnection = await Startup.OpenAndGetSqlConnection();
        await using var command =
            new SqlCommand("SELECT TOP 3 [max_hp],[max_mp],[strength],[agility],[accuracy],[endurance],[earth],[air]," +
                           "[water],[fire],[pdef],[mdef],[karma],[max_satiety],[title_level],[degree_level],[title_xp]," +
                           "[degree_xp],[current_satiety],[current_hp],[current_mp],[available_stats_title]," +
                           "[available_stats_degree],[gender_is_female],[name],[face_type],[hair_style],[hair_color]," +
                           "[tattoo],[boots],[pants],[armor],[helmet],[gloves],[deletion_is_not_requested],[x],[y],[z]," +
                           $"[turn],[id],[player_id] from characters with (nolock) where player_id = {playerId} order by [id]", sqlConnection);

        await using var reader = await command.ExecuteReaderAsync();

        var characters = new ClientInitialData();
        var charIndex = 0;

        while (reader.Read())
        {
            var maxHp = (ushort) reader.GetInt32(0);
            var maxMp = (ushort) reader.GetInt32(1);
            var str = (ushort) reader.GetInt32(2);
            var agi = (ushort) reader.GetInt32(3);
            var acc = (ushort) reader.GetInt32(4);
            var end = (ushort) reader.GetInt32(5);
            var ert = (ushort) reader.GetInt32(6);
            var air = (ushort) reader.GetInt32(7);
            var wat = (ushort) reader.GetInt32(8);
            var fir = (ushort) reader.GetInt32(9);
            var pd = (ushort) reader.GetInt32(10);
            var md = (ushort) reader.GetInt32(11);
            var krm = (byte) reader.GetInt32(12);
            var maxSat = (ushort) reader.GetInt32(13);
            var ttl = (ushort) reader.GetInt32(14);
            var deg = (ushort) reader.GetInt32(15);
            var txp = (ushort) reader.GetInt32(16);
            var dxp = (ushort) reader.GetInt32(17);
            var curSat = (ushort) reader.GetInt32(18);
            var curHp = (ushort) reader.GetInt32(19);
            var curMp = (ushort) reader.GetInt32(20);
            var titStats = (ushort) reader.GetInt32(21);
            var degStats = (ushort) reader.GetInt32(22);
            var isFemale = reader.GetBoolean(23);
            var name = reader.GetString(24);
            var face = (byte) reader.GetInt32(25);
            var hair = (byte) reader.GetInt32(26);
            var hairCol = (byte) reader.GetInt32(27);
            var tattoo = (byte) reader.GetInt32(28);
            var boots = (byte) reader.GetInt32(29);
            var pants = (byte) reader.GetInt32(30);
            var armor = (byte) reader.GetInt32(31);
            var helmet = (byte) reader.GetInt32(32);
            var gloves = (byte) reader.GetInt32(33);
            var notDeleting = reader.GetBoolean(34);
            var x = (double) reader.GetDecimal(35);
            var y = (double) reader.GetDecimal(36);
            var z = (double) reader.GetDecimal(37);
            var t = (double) reader.GetDecimal(38);

            var character = new CharacterData
            {
                Accuracy = acc,
                Agility = agi,
                Air = air,
                Armor = armor,
                Boots = boots,
                Earth = ert,
                Endurance = end,
                Fire = fir,
                Gloves = gloves,
                Helmet = helmet,
                Karma = (KarmaTypes) krm,
                Name = name,
                Pants = pants,
                Strength = str,
                Tattoo = tattoo,
                Water = wat,
                CurrentSatiety = curSat,
                FaceType = face,
                HairColor = hairCol,
                HairStyle = hair,
                MaxSatiety = maxSat,
                MDef = md,
                PDef = pd,
                PlayerIndex = playerIndex,
                AvailableDegreeStats = degStats,
                AvailableTitleStats = titStats,
                CurrentHP = curHp,
                CurrentMP = curMp,
                DegreeXP = dxp,
                IsGenderFemale = isFemale,
                MaxHP = maxHp,
                MaxMP = maxMp,
                TitleXP = txp,
                DegreeLevelMinusOne = deg,
                TitleLevelMinusOne = ttl,
                IsNotQueuedForDeletion = notDeleting,
                x = x,
                y = y,
                z = z,
                t = t
            };

            if (charIndex == 0)
            {
                characters.Character1 = character;
            }
            else if (charIndex == 1)
            {
                characters.Character2 = character;
            }
            else
            {
                characters.Character3 = character;
            }

            charIndex++;
        }

        await sqlConnection.CloseAsync();

        return characters;
    }

    public static async Task<bool> IsNameValid(string name)
    {
        var sqlConnection = await Startup.OpenAndGetSqlConnection();
        await using var command =
            new SqlCommand("SELECT count (1) from characters where name = @name", sqlConnection);
        command.Parameters.AddWithValue("@name", name);
        var count = ((await command.ExecuteScalarAsync()) as int?) ?? 0;
        
        return count == 0;
    }
}