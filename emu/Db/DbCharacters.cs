using emu.DataModels;
using emu.Packets;
using Microsoft.Data.SqlClient;

namespace emu.Db;

public class DbCharacters
{
    public static async Task<ClientInitialData?> GetPlayerCharactersFromDbAsync(int playerId, ushort playerIndex)
    {
        var sqlConnection = await Startup.OpenAndGetSqlConnectionAsync();
        await using var command =
            new SqlCommand("SELECT TOP 3 [max_hp],[max_mp],[strength],[agility],[accuracy],[endurance],[earth],[air]," +
                           "[water],[fire],[pdef],[mdef],[karma],[max_satiety],[title_level],[degree_level],[title_xp]," +
                           "[degree_xp],[current_satiety],[current_hp],[current_mp],[available_stats_title]," +
                           "[available_stats_degree],[gender_is_female],[name],[face_type],[hair_style],[hair_color]," +
                           "[tattoo],[boots],[pants],[armor],[helmet],[gloves],[deletion_is_not_requested],[x],[y],[z]," +
                           $"[turn],[id],[player_id] from characters with (nolock) where player_id = {playerId} order by [id]", sqlConnection);

        await using var reader = await command.ExecuteReaderAsync();

        var characters = new ClientInitialData(playerId);
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
            var dbid = reader.GetInt32(39);

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
                t = t,
                DbId = dbid
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

    public static async Task AddNewCharacterToDbAsync(int playerId, CharacterData newCharacter)
    {
        var sqlConnection = await Startup.OpenAndGetSqlConnectionAsync();
        var command = new SqlCommand(@"insert into [characters] (max_hp, max_mp, strength, agility, accuracy, endurance, 
                          earth, air, water, fire, pdef, mdef, karma, max_satiety, title_level, degree_level, title_xp, 
                          degree_xp, current_satiety, current_hp, current_mp, available_stats_title, available_stats_degree, 
                          gender_is_female, [name], face_type, hair_style, hair_color, tattoo, boots, pants, armor, helmet, 
                          gloves, deletion_is_not_requested, x, [y], z, turn, player_id) values (@max_hp, @max_mp, @strength, @agility, @accuracy, @endurance, 
                          @earth, @air, @water, @fire, @pdef, @mdef, @karma, @max_satiety, @title_level, @degree_level, @title_xp, 
                          @degree_xp, @current_satiety, @current_hp, @current_mp, @available_stats_title, @available_stats_degree, 
                          @gender_is_female, @name, @face_type, @hair_style, @hair_color, @tattoo, @boots, @pants, @armor, @helmet, 
                          @gloves, @deletion_is_not_requested, @x, @y, @z, @turn, @player_id)", sqlConnection);
        command.Parameters.AddWithValue("@max_hp", (int) newCharacter.MaxHP);
        command.Parameters.AddWithValue("@max_mp", (int) newCharacter.MaxMP);
        command.Parameters.AddWithValue("@strength", (int) newCharacter.Strength);
        command.Parameters.AddWithValue("@agility", (int) newCharacter.Agility);
        command.Parameters.AddWithValue("@accuracy", (int) newCharacter.Accuracy);
        command.Parameters.AddWithValue("@endurance", (int) newCharacter.Endurance);
        command.Parameters.AddWithValue("@earth", (int) newCharacter.Earth);
        command.Parameters.AddWithValue("@air", (int) newCharacter.Air);
        command.Parameters.AddWithValue("@water", (int) newCharacter.Water);
        command.Parameters.AddWithValue("@fire", (int) newCharacter.Fire);
        command.Parameters.AddWithValue("@pdef", (int) newCharacter.PDef);
        command.Parameters.AddWithValue("@mdef", (int) newCharacter.MDef);
        command.Parameters.AddWithValue("@karma", (int) newCharacter.Karma);
        command.Parameters.AddWithValue("@max_satiety", (int) newCharacter.MaxSatiety);
        command.Parameters.AddWithValue("@title_level", (int) newCharacter.TitleLevelMinusOne);
        command.Parameters.AddWithValue("@degree_level", (int) newCharacter.DegreeLevelMinusOne);
        command.Parameters.AddWithValue("@title_xp", (int) newCharacter.TitleXP);
        command.Parameters.AddWithValue("@degree_xp", (int) newCharacter.DegreeXP);
        command.Parameters.AddWithValue("@current_satiety", (int) newCharacter.CurrentSatiety);
        command.Parameters.AddWithValue("@current_hp", (int) newCharacter.CurrentHP);
        command.Parameters.AddWithValue("@current_mp", (int) newCharacter.CurrentMP);
        command.Parameters.AddWithValue("@available_stats_title", (int) newCharacter.AvailableTitleStats);
        command.Parameters.AddWithValue("@available_stats_degree", (int) newCharacter.AvailableDegreeStats);
        command.Parameters.AddWithValue("@gender_is_female", newCharacter.IsGenderFemale);
        command.Parameters.AddWithValue("@name", newCharacter.Name);
        command.Parameters.AddWithValue("@face_type", (int) newCharacter.FaceType);
        command.Parameters.AddWithValue("@hair_style", (int) newCharacter.HairStyle);
        command.Parameters.AddWithValue("@hair_color", (int) newCharacter.HairColor);
        command.Parameters.AddWithValue("@tattoo", (int) newCharacter.Tattoo);
        command.Parameters.AddWithValue("@boots", (int) newCharacter.Boots);
        command.Parameters.AddWithValue("@pants", (int) newCharacter.Pants);
        command.Parameters.AddWithValue("@armor", (int) newCharacter.Armor);
        command.Parameters.AddWithValue("@helmet", (int) newCharacter.Helmet);
        command.Parameters.AddWithValue("@gloves", (int) newCharacter.Gloves);
        command.Parameters.AddWithValue("@deletion_is_not_requested", newCharacter.IsNotQueuedForDeletion);
        command.Parameters.AddWithValue("@x", (decimal) newCharacter.x);
        command.Parameters.AddWithValue("@y", (decimal) newCharacter.y);
        command.Parameters.AddWithValue("@z", (decimal) newCharacter.z);
        command.Parameters.AddWithValue("@turn", (decimal) newCharacter.t);
        command.Parameters.AddWithValue("@player_id", playerId);
        await command.ExecuteNonQueryAsync();

        await sqlConnection.CloseAsync();
    }

    public static async Task DeleteCharacterFromDbAsync(int characterId)
    {
        var sqlConnection = await Startup.OpenAndGetSqlConnectionAsync();
        var sqlCommand = new SqlCommand("delete from characters where [id] = @id", sqlConnection);
        sqlCommand.Parameters.AddWithValue("@id", characterId);
        await sqlCommand.ExecuteNonQueryAsync();
        await sqlConnection.CloseAsync();
    }
}