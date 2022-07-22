using System.Data.SqlClient;
using System.Threading.Tasks;
using SphServer.DataModels;
using SphServer.Packets;

namespace SphServer.Db
{

    public class DbCharacters
    {
        public static ClientInitialData? GetPlayerCharactersFromDb(int playerId, ushort playerIndex)
        {
            var sqlConnection = Startup.OpenAndGetSqlConnection();
            using var command =
                new SqlCommand("SELECT TOP 3 " +
                               "max_hp," +
                               "max_mp," +
                               "strength," +
                               "agility," +
                               "accuracy," +
                               "endurance," +
                               "earth," +
                               "air," +
                               "water," +
                               "fire," +
                               "pdef," +
                               "mdef," +
                               "karma," +
                               "max_satiety," +
                               "title_level," +
                               "degree_level," +
                               "title_xp," +
                               "degree_xp," +
                               "current_satiety," +
                               "current_hp," +
                               "current_mp," +
                               "available_stats_title," +
                               "available_stats_degree," +
                               "gender_is_female," +
                               "[cha].[name]," +
                               "face_type," +
                               "hair_style," +
                               "hair_color," +
                               "tattoo," +
                               "boots_model," +
                               "pants_model," +
                               "armor_model," +
                               "helmet_model," +
                               "gloves_model," +
                               "deletion_is_not_requested," +
                               "x," +
                               "[y]," +
                               "z," +
                               "turn," +
                               "[cha].[id]," +
                               "player_id," +
                               "[index]," +
                               "helmet_slot," +
                               "amulet_slot," +
                               "spec_slot," +
                               "armor_slot," +
                               "shield_slot," +
                               "belt_slot," +
                               "gloves_slot," +
                               "left_bracelet_slot," +
                               "right_bracelet_slot," +
                               "pants_slot," +
                               "top_left_ring_slot," +
                               "top_right_ring_slot," +
                               "bottom_left_ring_slot," +
                               "bottom_right_ring_slot," +
                               "boots_slot," +
                               "left_special_slot_1," +
                               "left_special_slot_2," +
                               "left_special_slot_3," +
                               "left_special_slot_4," +
                               "left_special_slot_5," +
                               "left_special_slot_6," +
                               "left_special_slot_7," +
                               "left_special_slot_8," +
                               " left_special_slot_9," +
                               "weapon_slot," +
                               "ammo_slot," +
                               "mapbook_slot," +
                               "recipebook_slot," +
                               "mantrabook_slot," +
                               "inkpot_slot," +
                               "islandtoken_slot," +
                               "speedhackmantra_slot," +
                               "money_slot," +
                               "travelbag_slot," +
                               "key_slot_1," +
                               "key_slot_2," +
                               "mission_slot," +
                               "inventory_slot_1," +
                               "inventory_slot_2," +
                               "inventory_slot_3," +
                               "inventory_slot_4," +
                               "inventory_slot_5," +
                               "inventory_slot_6," +
                               "inventory_slot_7," +
                               "inventory_slot_8," +
                               "inventory_slot_9," +
                               "inventory_slot_10," +
                               "[money]," +
                               "spec_level," +
                               "spec_type," +
                               "[cla].[name]," +
                               "clan_rank " +
                               "FROM [sph].[dbo].[characters] cha with (nolock) left join [sph].[dbo].[clans] cla " +
                               $"with (nolock) on cla.id = cha.clan_id where player_id = {playerId} order by [id]",
                    sqlConnection);

            using var reader = command.ExecuteReader();

            var characters = new ClientInitialData(playerId);

            while (reader.Read())
            {
                var maxHp = (ushort)reader.GetInt32(0);
                var maxMp = (ushort)reader.GetInt32(1);
                var str = (ushort)reader.GetInt32(2);
                var agi = (ushort)reader.GetInt32(3);
                var acc = (ushort)reader.GetInt32(4);
                var end = (ushort)reader.GetInt32(5);
                var ert = (ushort)reader.GetInt32(6);
                var air = (ushort)reader.GetInt32(7);
                var wat = (ushort)reader.GetInt32(8);
                var fir = (ushort)reader.GetInt32(9);
                var pd = (ushort)reader.GetInt32(10);
                var md = (ushort)reader.GetInt32(11);
                var krm = (byte)reader.GetInt32(12);
                var maxSat = (ushort)reader.GetInt32(13);
                var ttl = (ushort)reader.GetInt32(14);
                var deg = (ushort)reader.GetInt32(15);
                var txp = (ushort)reader.GetInt32(16);
                var dxp = (ushort)reader.GetInt32(17);
                var curSat = (ushort)reader.GetInt32(18);
                var curHp = (ushort)reader.GetInt32(19);
                var curMp = (ushort)reader.GetInt32(20);
                var titStats = (ushort)reader.GetInt32(21);
                var degStats = (ushort)reader.GetInt32(22);
                var isFemale = reader.GetBoolean(23);
                var name = reader.GetString(24);
                var face = (byte)reader.GetInt32(25);
                var hair = (byte)reader.GetInt32(26);
                var hairCol = (byte)reader.GetInt32(27);
                var tattoo = (byte)reader.GetInt32(28);
                var bootModelId = (byte)reader.GetInt32(29);
                var pantsModelId = (byte)reader.GetInt32(30);
                var armorModelId = (byte)reader.GetInt32(31);
                var helmetModelId = (byte)reader.GetInt32(32);
                var glovesModelId = (byte)reader.GetInt32(33);
                var notDeleting = reader.GetBoolean(34);
                var x = (double)reader.GetDecimal(35);
                var y = (double)reader.GetDecimal(36);
                var z = (double)reader.GetDecimal(37);
                var t = (double)reader.GetDecimal(38);
                var dbid = reader.GetInt32(39);
                var index = reader.GetInt32(41);
                var helmetSlot = reader.GetInt32(42);
                var amuletSlot = reader.GetInt32(43);
                var specSlot = reader.GetInt32(44);
                var armortSlot = reader.GetInt32(45);
                var shieldSlot = reader.GetInt32(46);
                var beltSlot = reader.GetInt32(47);
                var glovesSlot = reader.GetInt32(48);
                var leftBraceletSlot = reader.GetInt32(49);
                var rightBraceletSlot = reader.GetInt32(50);
                var pantsSlot = reader.GetInt32(51);
                var topLeftRingSlot = reader.GetInt32(52);
                var topRightRingSlot = reader.GetInt32(53);
                var bottomLeftRingSlot = reader.GetInt32(54);
                var bottomRightRingSlot = reader.GetInt32(55);
                var bootsSlot = reader.GetInt32(56);
                var leftSpecialSlot1 = reader.GetInt32(57);
                var leftSpecialSlot2 = reader.GetInt32(58);
                var leftSpecialSlot3 = reader.GetInt32(59);
                var leftSpecialSlot4 = reader.GetInt32(60);
                var leftSpecialSlot5 = reader.GetInt32(61);
                var leftSpecialSlot6 = reader.GetInt32(62);
                var leftSpecialSlot7 = reader.GetInt32(63);
                var leftSpecialSlot8 = reader.GetInt32(64);
                var leftSpecialSlot9 = reader.GetInt32(65);
                var weaponSlot = reader.GetInt32(66);
                var ammoSlot = reader.GetInt32(67);
                var mapbookSlot = reader.GetInt32(68);
                var recipeBookSlot = reader.GetInt32(69);
                var mantraBookSlot = reader.GetInt32(70);
                var inkpotSlot = reader.GetInt32(71);
                var islandTokenSlot = reader.GetInt32(72);
                var speedhackMantraSlot = reader.GetInt32(73);
                var moneySlot = reader.GetInt32(74);
                var travelbagSlot = reader.GetInt32(75);
                var keySlot1 = reader.GetInt32(76);
                var keySlot2 = reader.GetInt32(77);
                var missionSlot = reader.GetInt32(78);
                var inventorySlot1 = reader.GetInt32(79);
                var inventorySlot2 = reader.GetInt32(80);
                var inventorySlot3 = reader.GetInt32(81);
                var inventorySlot4 = reader.GetInt32(82);
                var inventorySlot5 = reader.GetInt32(83);
                var inventorySlot6 = reader.GetInt32(84);
                var inventorySlot7 = reader.GetInt32(85);
                var inventorySlot8 = reader.GetInt32(86);
                var inventorySlot9 = reader.GetInt32(87);
                var inventorySlot10 = reader.GetInt32(88);
                var money = reader.GetInt32(89);
                var specLevel = reader.GetInt32(90);
                var specType = reader.GetInt32(91);
                var clanName = reader.GetString(92);
                var clanRank = reader.GetInt32(93);

                var character = new CharacterData
                {
                    Accuracy = acc,
                    Agility = agi,
                    Air = air,
                    ArmorModelId = armorModelId,
                    BootModelId = bootModelId,
                    Earth = ert,
                    Endurance = end,
                    Fire = fir,
                    GlovesModelId = glovesModelId,
                    HelmetModelId = helmetModelId,
                    Karma = (KarmaTypes)krm,
                    Name = name,
                    PantsModelId = pantsModelId,
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
                    X = x,
                    Y = y,
                    Z = z,
                    T = t,
                    DbId = dbid,
                    Money = money,
                    AmmoSlot = NullIfZeroValueOtherwise(ammoSlot),
                    AmuletSlot = NullIfZeroValueOtherwise(amuletSlot),
                    ArmorSlot = NullIfZeroValueOtherwise(armortSlot),
                    BeltSlot = NullIfZeroValueOtherwise(beltSlot),
                    BootsSlot = NullIfZeroValueOtherwise(bootsSlot),
                    GlovesSlot = NullIfZeroValueOtherwise(glovesSlot),
                    HelmetSlot = NullIfZeroValueOtherwise(helmetSlot),
                    InkpotSlot = NullIfZeroValueOtherwise(inkpotSlot),
                    InventorySlot1 = NullIfZeroValueOtherwise(inventorySlot1),
                    InventorySlot2 = NullIfZeroValueOtherwise(inventorySlot2),
                    InventorySlot3 = NullIfZeroValueOtherwise(inventorySlot3),
                    InventorySlot4 = NullIfZeroValueOtherwise(inventorySlot4),
                    InventorySlot5 = NullIfZeroValueOtherwise(inventorySlot5),
                    InventorySlot6 = NullIfZeroValueOtherwise(inventorySlot6),
                    InventorySlot7 = NullIfZeroValueOtherwise(inventorySlot7),
                    InventorySlot8 = NullIfZeroValueOtherwise(inventorySlot8),
                    InventorySlot9 = NullIfZeroValueOtherwise(inventorySlot9),
                    InventorySlot10 = NullIfZeroValueOtherwise(inventorySlot10),
                    KeySlot1 = NullIfZeroValueOtherwise(keySlot1),
                    KeySlot2 = NullIfZeroValueOtherwise(keySlot2),
                    MissionSlot = NullIfZeroValueOtherwise(missionSlot),
                    MoneySlot = NullIfZeroValueOtherwise(moneySlot),
                    PantsSlot = NullIfZeroValueOtherwise(pantsSlot),
                    ShieldSlot = NullIfZeroValueOtherwise(shieldSlot),
                    SpecSlot = NullIfZeroValueOtherwise(specSlot),
                    SpecType = (SpecTypes)specType,
                    TravelbagSlot = NullIfZeroValueOtherwise(travelbagSlot),
                    WeaponSlot = NullIfZeroValueOtherwise(weaponSlot),
                    IslandTokenSlot = NullIfZeroValueOtherwise(islandTokenSlot),
                    LeftBraceletSlot = NullIfZeroValueOtherwise(leftBraceletSlot),
                    LeftSpecialSlot1 = NullIfZeroValueOtherwise(leftSpecialSlot1),
                    LeftSpecialSlot2 = NullIfZeroValueOtherwise(leftSpecialSlot2),
                    LeftSpecialSlot3 = NullIfZeroValueOtherwise(leftSpecialSlot3),
                    LeftSpecialSlot4 = NullIfZeroValueOtherwise(leftSpecialSlot4),
                    LeftSpecialSlot5 = NullIfZeroValueOtherwise(leftSpecialSlot5),
                    LeftSpecialSlot6 = NullIfZeroValueOtherwise(leftSpecialSlot6),
                    LeftSpecialSlot7 = NullIfZeroValueOtherwise(leftSpecialSlot7),
                    LeftSpecialSlot8 = NullIfZeroValueOtherwise(leftSpecialSlot8),
                    LeftSpecialSlot9 = NullIfZeroValueOtherwise(leftSpecialSlot9),
                    MantraBookSlot = NullIfZeroValueOtherwise(mantraBookSlot),
                    MapBookSlot = NullIfZeroValueOtherwise(mapbookSlot),
                    RecipeBookSlot = NullIfZeroValueOtherwise(recipeBookSlot),
                    RightBraceletSlot = NullIfZeroValueOtherwise(rightBraceletSlot),
                    SpeedhackMantraSlot = NullIfZeroValueOtherwise(speedhackMantraSlot),
                    BottomLeftRingSlot = NullIfZeroValueOtherwise(bottomLeftRingSlot),
                    BottomRightRingSlot = NullIfZeroValueOtherwise(bottomRightRingSlot),
                    SpecLevelMinusOne = specLevel,
                    TopLeftRingSlot = NullIfZeroValueOtherwise(topLeftRingSlot),
                    TopRightRingSlot = NullIfZeroValueOtherwise(topRightRingSlot),
                    ClanName = clanName,
                    ClanRank = (ClanRank)clanRank
                };

                characters.AddNewCharacter(character, index);
            }

            sqlConnection.Close();

            return characters;
        }

        public static void AddNewCharacterToDb(int playerId, CharacterData newCharacter, int charIndex)
        {
            var sqlConnection = Startup.OpenAndGetSqlConnection();
            var command = new SqlCommand(
                @"insert into [characters] (max_hp, max_mp, strength, agility, accuracy, endurance, 
                          earth, air, water, fire, pdef, mdef, karma, max_satiety, title_level, degree_level, title_xp, 
                          degree_xp, current_satiety, current_hp, current_mp, available_stats_title, available_stats_degree, 
                          gender_is_female, [name], face_type, hair_style, hair_color, tattoo, boots_model, pants_model, armor_model, helmet_model, 
                          gloves_model, deletion_is_not_requested, x, [y], z, turn, player_id, [index], helmet_slot,
                        amulet_slot, spec_slot, armor_slot, shield_slot, belt_slot, gloves_slot, left_bracelet_slot,
                        right_bracelet_slot, pants_slot, top_left_ring_slot, top_right_ring_slot, bottom_left_ring_slot,
                        bottom_right_ring_slot, boots_slot, left_special_slot_1, left_special_slot_2,
                        left_special_slot_3, left_special_slot_4, left_special_slot_5, left_special_slot_6,
                        left_special_slot_7, left_special_slot_8, left_special_slot_9, weapon_slot, ammo_slot,
                        mapbook_slot, recipebook_slot, mantrabook_slot, inkpot_slot, islandtoken_slot,
                        speedhackmantra_slot, money_slot, travelbag_slot, key_slot_1, key_slot_2, mission_slot,
                        inventory_slot_1, inventory_slot_2, inventory_slot_3, inventory_slot_4, inventory_slot_5,
                        inventory_slot_6, inventory_slot_7, inventory_slot_8, inventory_slot_9, inventory_slot_10,
                        [money], spec_level, spec_type, clan_id, clan_rank) values (@max_hp, @max_mp, @strength, @agility, @accuracy, @endurance, 
                          @earth, @air, @water, @fire, @pdef, @mdef, @karma, @max_satiety, @title_level, @degree_level, @title_xp, 
                          @degree_xp, @current_satiety, @current_hp, @current_mp, @available_stats_title, @available_stats_degree, 
                          @gender_is_female, @name, @face_type, @hair_style, @hair_color, @tattoo, @boots_model, @pants_model, @armor_model, @helmet_model, 
                          @gloves_model, @deletion_is_not_requested, @x, @y, @z, @turn, @player_id, @index, 0, 0, 0, 0, 
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)", sqlConnection);
            command.Parameters.AddWithValue("@max_hp", (int)newCharacter.MaxHP);
            command.Parameters.AddWithValue("@max_mp", (int)newCharacter.MaxMP);
            command.Parameters.AddWithValue("@strength", (int)newCharacter.Strength);
            command.Parameters.AddWithValue("@agility", (int)newCharacter.Agility);
            command.Parameters.AddWithValue("@accuracy", (int)newCharacter.Accuracy);
            command.Parameters.AddWithValue("@endurance", (int)newCharacter.Endurance);
            command.Parameters.AddWithValue("@earth", (int)newCharacter.Earth);
            command.Parameters.AddWithValue("@air", (int)newCharacter.Air);
            command.Parameters.AddWithValue("@water", (int)newCharacter.Water);
            command.Parameters.AddWithValue("@fire", (int)newCharacter.Fire);
            command.Parameters.AddWithValue("@pdef", (int)newCharacter.PDef);
            command.Parameters.AddWithValue("@mdef", (int)newCharacter.MDef);
            command.Parameters.AddWithValue("@karma", (int)newCharacter.Karma);
            command.Parameters.AddWithValue("@max_satiety", (int)newCharacter.MaxSatiety);
            command.Parameters.AddWithValue("@title_level", (int)newCharacter.TitleLevelMinusOne);
            command.Parameters.AddWithValue("@degree_level", (int)newCharacter.DegreeLevelMinusOne);
            command.Parameters.AddWithValue("@title_xp", (int)newCharacter.TitleXP);
            command.Parameters.AddWithValue("@degree_xp", (int)newCharacter.DegreeXP);
            command.Parameters.AddWithValue("@current_satiety", (int)newCharacter.CurrentSatiety);
            command.Parameters.AddWithValue("@current_hp", (int)newCharacter.CurrentHP);
            command.Parameters.AddWithValue("@current_mp", (int)newCharacter.CurrentMP);
            command.Parameters.AddWithValue("@available_stats_title", (int)newCharacter.AvailableTitleStats);
            command.Parameters.AddWithValue("@available_stats_degree", (int)newCharacter.AvailableDegreeStats);
            command.Parameters.AddWithValue("@gender_is_female", newCharacter.IsGenderFemale);
            command.Parameters.AddWithValue("@name", newCharacter.Name);
            command.Parameters.AddWithValue("@face_type", (int)newCharacter.FaceType);
            command.Parameters.AddWithValue("@hair_style", (int)newCharacter.HairStyle);
            command.Parameters.AddWithValue("@hair_color", (int)newCharacter.HairColor);
            command.Parameters.AddWithValue("@tattoo", (int)newCharacter.Tattoo);
            command.Parameters.AddWithValue("@boots_model", (int)newCharacter.BootModelId);
            command.Parameters.AddWithValue("@pants_model", (int)newCharacter.PantsModelId);
            command.Parameters.AddWithValue("@armor_model", (int)newCharacter.ArmorModelId);
            command.Parameters.AddWithValue("@helmet_model", (int)newCharacter.HelmetModelId);
            command.Parameters.AddWithValue("@gloves_model", (int)newCharacter.GlovesModelId);
            command.Parameters.AddWithValue("@deletion_is_not_requested", newCharacter.IsNotQueuedForDeletion);
            command.Parameters.AddWithValue("@x", (decimal)newCharacter.X);
            command.Parameters.AddWithValue("@y", (decimal)newCharacter.Y);
            command.Parameters.AddWithValue("@z", (decimal)newCharacter.Z);
            command.Parameters.AddWithValue("@turn", (decimal)newCharacter.T);
            command.Parameters.AddWithValue("@player_id", playerId);
            command.Parameters.AddWithValue("@index", charIndex);
            command.ExecuteNonQuery();

            sqlConnection.Close();
        }

        public static void DeleteCharacterFromDb(int characterId)
        {
            var sqlConnection = Startup.OpenAndGetSqlConnection();
            var sqlCommand = new SqlCommand("delete from characters where [id] = @id", sqlConnection);
            sqlCommand.Parameters.AddWithValue("@id", characterId);
            sqlCommand.ExecuteNonQuery();
            sqlConnection.Close();
        }

        private static int? NullIfZeroValueOtherwise(int x)
        {
            return x == 0 ? null : x;
        }
    }
}