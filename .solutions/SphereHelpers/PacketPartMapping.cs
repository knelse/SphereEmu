using System.Text;

namespace SphServer.Helpers;

public enum EntityActionType
{
    SET_POSITION = 0x06,
    FULL_SPAWN = 0x7C,
    FULL_SPAWN_2 = 0x7D,
    ATTACK = 0x2A,
    INTERACT = 0xA,
    UNKNOWN = 0x14,
    UNDEF
}

public enum EntityInteractionType
{
    DEATH = 0x040D,
    // FistAttackTargetEcho / live swing reply: action INTERACT (0x0A) + this 16-bit tag.
    DEAL_DAMAGE = 0x050D,
    OPEN_CONTAINER = 0x0103,
    UNDEF
}

public enum OptionalPacketFields : byte
{
    COUNT = 12,
    PA = 14,
    NAME = 15,
    MADE_BY = 46,
    UNKNOWN = 0xFF
}

public static class PacketPartMapping
{
    public static readonly HashSet<ObjectType> ItemObjectTypes =
    [
        ObjectType.Token,
        ObjectType.Mutator,
        ObjectType.Seed_Castle,
        ObjectType.Xp_X2_Pill_Degree,
        ObjectType.Token_Multiuse,
        ObjectType.Trade_License,
        ObjectType.Scroll_Legend,
        ObjectType.Scroll_Recipe,
        ObjectType.Mission,
        ObjectType.Token_Island,
        ObjectType.Token_Island_Guest,
        ObjectType.Token_Tutorial_Torweal,
        ObjectType.Bead,
        ObjectType.Backpack_Large,
        ObjectType.Backpack_Small,
        ObjectType.Sack,
        ObjectType.Mantra_Book_Small,
        ObjectType.Recipe_Book,
        ObjectType.Mantra_Book_Large,
        ObjectType.Mantra_Book_Great,
        ObjectType.Map_Book,
        ObjectType.Key_Barn,
        ObjectType.Powder_Finale,
        ObjectType.Powder_Single_Target,
        ObjectType.Powder_Amilus,
        ObjectType.Powder_Ao_E,
        ObjectType.Elixir_Castle,
        ObjectType.Elixir_Trap,
        ObjectType.Weapon_Sword,
        ObjectType.Weapon_Starting_Sword,
        ObjectType.Weapon_Axe,
        ObjectType.Weapon_Crossbow,
        ObjectType.Arrow,
        ObjectType.Ring_Diamond,
        ObjectType.Ring_Ruby,
        ObjectType.Ruby,
        ObjectType.Ring_Gold,
        ObjectType.Alchemy_Mineral,
        ObjectType.Alchemy_Plant,
        ObjectType.Alchemy_Metal,
        ObjectType.Food_Apple,
        ObjectType.Food_Pear,
        ObjectType.Food_Meat,
        ObjectType.Food_Bread,
        ObjectType.Food_Fish2,
        ObjectType.Alchemy_Brushwood,
        ObjectType.Key,
        ObjectType.Key_Single_Use,
        ObjectType.Light_Crystal,
        ObjectType.Light_Crystal_Yellow,
        ObjectType.Map,
        ObjectType.Inkpot,
        ObjectType.Firecracker,
        ObjectType.Ear,
        ObjectType.Ear_String,
        ObjectType.Monster_Part,
        ObjectType.Firework_Celebration,
        ObjectType.Item_Expired,
        ObjectType.Armor_Chest,
        ObjectType.Armor_Amulet,
        ObjectType.Armor_Boots,
        ObjectType.Armor_Gloves,
        ObjectType.Armor_Belt,
        ObjectType.Armor_Shield,
        ObjectType.Armor_Helmet,
        ObjectType.Armor_Pants,
        ObjectType.Armor_Bracelet,
        ObjectType.Ring,
        ObjectType.Armor_Robe,
        ObjectType.Ring_Golem,
        ObjectType.Alchemy_Pot,
        ObjectType.Alchemy_Furnace,
        ObjectType.Blueprint,
        ObjectType.Quest_Armor_Chest,
        ObjectType.Quest_Armor_Chest2,
        ObjectType.Quest_Armor_Boots,
        ObjectType.Quest_Armor_Gloves,
        ObjectType.Quest_Armor_Belt,
        ObjectType.Quest_Armor_Shield,
        ObjectType.Quest_Armor_Helmet,
        ObjectType.Quest_Armor_Pants,
        ObjectType.Quest_Armor_Bracelet,
        ObjectType.Quest_Armor_Ring,
        ObjectType.Quest_Armor_Robe,
        ObjectType.Quest_Weapon_Sword,
        ObjectType.Quest_Weapon_Axe,
        ObjectType.Quest_Weapon_Crossbow,
        ObjectType.Special_Guild,
        ObjectType.Special_Ability,
        ObjectType.Special_Ability_Steal,
        ObjectType.Guild_Specialization,
        ObjectType.Armor_Helmet_Premium,
        ObjectType.Mantra_White,
        ObjectType.Mantra_Black
    ];

    public static readonly HashSet<ObjectType> EntityObjectTypes =
    [
        ObjectType.Token,
        ObjectType.Mutator,
        ObjectType.Dungeon,
        ObjectType.Seed_Castle,
        ObjectType.Xp_X2_Pill_Degree,
        ObjectType.Door_Entrance,
        ObjectType.Door_Exit,
        ObjectType.Teleport,
        ObjectType.Teleport_Broken,
        ObjectType.Teleport_With_Target,
        ObjectType.Dungeon_Entrance,
        ObjectType.Tutorial_Message,
        ObjectType.Teleport_Wild,
        ObjectType.Token_Multiuse,
        ObjectType.Token_Island,
        ObjectType.Token_Tutorial_Torweal,
        ObjectType.Trade_License,
        ObjectType.Mob_Spawner,
        ObjectType.Tournament_Teleport,
        ObjectType.Castle_Teleport,
        ObjectType.Castle_Tablet,
        ObjectType.Castle_Gate,
        ObjectType.Castle_Chest,
        ObjectType.Castle_Elixir_Pillar,
        ObjectType.Castle_Entrance,
        ObjectType.Teleport_In_Dungeon,
        ObjectType.Door_Entrance_With_Key,
        ObjectType.Teleport_Dungeon_Choice_Island,
        ObjectType.Monster,
        ObjectType.Monster_Flyer,
        ObjectType.Npc_Banker,
        ObjectType.Npc_Trade,
        ObjectType.Npc_Quest_Degree,
        ObjectType.Npc_Quest_Karma,
        ObjectType.Npc_Quest_Title,
        ObjectType.Npc_Guilder,
        ObjectType.Npc_Guide,
        ObjectType.Npc_Tournament,
        ObjectType.Npc_Trade_Random_Name,
        ObjectType.Sack_Mob_Loot,
        ObjectType.Chest5,
        ObjectType.Container_Chest,
        ObjectType.Chest2,
        ObjectType.Scroll_Legend,
        ObjectType.Scroll_Recipe,
        ObjectType.Mission,
        ObjectType.Token_Island_Guest,
        ObjectType.Bead,
        ObjectType.Backpack_Large,
        ObjectType.Backpack_Small,
        ObjectType.Sack,
        ObjectType.Mantra_Book_Small,
        ObjectType.Recipe_Book,
        ObjectType.Mantra_Book_Large,
        ObjectType.Mantra_Book_Great,
        ObjectType.Map_Book,
        ObjectType.Key_Barn,
        ObjectType.Powder_Finale,
        ObjectType.Powder_Single_Target,
        ObjectType.Powder_Amilus,
        ObjectType.Powder_Ao_E,
        ObjectType.Elixir_Castle,
        ObjectType.Elixir_Trap,
        ObjectType.Weapon_Sword,
        ObjectType.Weapon_Starting_Sword,
        ObjectType.Weapon_Axe,
        ObjectType.Weapon_Crossbow,
        ObjectType.Arrow,
        ObjectType.Ring_Diamond,
        ObjectType.Ring_Ruby,
        ObjectType.Ruby,
        ObjectType.Ring_Gold,
        ObjectType.Alchemy_Mineral,
        ObjectType.Alchemy_Plant,
        ObjectType.Alchemy_Metal,
        ObjectType.Food_Apple,
        ObjectType.Food_Pear,
        ObjectType.Food_Meat,
        ObjectType.Food_Bread,
        ObjectType.Food_Fish2,
        ObjectType.Alchemy_Brushwood,
        ObjectType.Key,
        ObjectType.Key_Single_Use,
        ObjectType.Light_Crystal,
        ObjectType.Map,
        ObjectType.Inkpot,
        ObjectType.Light_Crystal_Yellow,
        ObjectType.Firecracker,
        ObjectType.Ear,
        ObjectType.Ear_String,
        ObjectType.Monster_Part,
        ObjectType.Firework_Celebration,
        ObjectType.Item_Expired,
        ObjectType.Armor_Chest,
        ObjectType.Armor_Amulet,
        ObjectType.Armor_Boots,
        ObjectType.Armor_Gloves,
        ObjectType.Armor_Belt,
        ObjectType.Armor_Shield,
        ObjectType.Armor_Helmet,
        ObjectType.Armor_Pants,
        ObjectType.Armor_Bracelet,
        ObjectType.Ring,
        ObjectType.Armor_Robe,
        ObjectType.Ring_Golem,
        ObjectType.Alchemy_Pot,
        ObjectType.Alchemy_Furnace,
        ObjectType.Blueprint,
        ObjectType.Workshop,
        ObjectType.Quest_Armor_Chest,
        ObjectType.Quest_Armor_Chest2,
        ObjectType.Quest_Armor_Boots,
        ObjectType.Quest_Armor_Gloves,
        ObjectType.Quest_Armor_Belt,
        ObjectType.Quest_Armor_Shield,
        ObjectType.Quest_Armor_Helmet,
        ObjectType.Quest_Armor_Pants,
        ObjectType.Quest_Armor_Bracelet,
        ObjectType.Quest_Armor_Ring,
        ObjectType.Quest_Armor_Robe,
        ObjectType.Quest_Weapon_Sword,
        ObjectType.Quest_Weapon_Axe,
        ObjectType.Quest_Weapon_Crossbow,
        ObjectType.Special_Guild,
        ObjectType.Special_Ability,
        ObjectType.Special_Ability_Steal,
        ObjectType.Guild_Specialization,
        ObjectType.Armor_Helmet_Premium,
        ObjectType.Mantra_White,
        ObjectType.Mantra_Black,
        ObjectType.Player
    ];

    public static readonly HashSet<ObjectType> ItemBagObjectTypes =
    [
        ObjectType.Backpack_Large,
        ObjectType.Backpack_Small,
        ObjectType.Mantra_Book_Small,
        ObjectType.Mantra_Book_Large,
        ObjectType.Mantra_Book_Great,
        ObjectType.Map_Book,
        ObjectType.Alchemy_Pot,
        ObjectType.Sack
    ];

    public static readonly HashSet<ObjectType> ItemRecipeBagObjectTypes =
    [
        ObjectType.Recipe_Book
    ];

    public static readonly HashSet<ObjectType> EquippableItemTypes =
    [
        ObjectType.Weapon_Sword,
        ObjectType.Weapon_Starting_Sword,
        ObjectType.Weapon_Axe,
        ObjectType.Weapon_Crossbow,
        ObjectType.Armor_Chest,
        ObjectType.Armor_Amulet,
        ObjectType.Armor_Boots,
        ObjectType.Armor_Gloves,
        ObjectType.Armor_Belt,
        ObjectType.Armor_Shield,
        ObjectType.Armor_Helmet,
        ObjectType.Armor_Pants,
        ObjectType.Armor_Bracelet,
        ObjectType.Ring,
        ObjectType.Armor_Robe,
        ObjectType.Quest_Armor_Chest,
        ObjectType.Quest_Armor_Chest2,
        ObjectType.Quest_Armor_Boots,
        ObjectType.Quest_Armor_Gloves,
        ObjectType.Quest_Armor_Belt,
        ObjectType.Quest_Armor_Shield,
        ObjectType.Quest_Armor_Helmet,
        ObjectType.Quest_Armor_Pants,
        ObjectType.Quest_Armor_Bracelet,
        ObjectType.Quest_Armor_Ring,
        ObjectType.Quest_Armor_Robe,
        ObjectType.Quest_Weapon_Sword,
        ObjectType.Quest_Weapon_Axe,
        ObjectType.Quest_Weapon_Crossbow
    ];

    public static readonly Dictionary<ObjectType, string> WorldObjectsToTrack = new()
    {
        [ObjectType.Teleport] = "teleports",
        [ObjectType.Castle_Teleport] = "castle_teleports",
        [ObjectType.Castle_Tablet] = "castle_tablets",
        [ObjectType.Castle_Gate] = "castle_gates",
        [ObjectType.Castle_Chest] = "castle_chests",
        [ObjectType.Castle_Elixir_Pillar] = "castle_elixir_pillars",
        [ObjectType.Castle_Entrance] = "castle_entrances",
        [ObjectType.Door_Entrance_With_Key] = "door_entrances_with_key",
        [ObjectType.Teleport_In_Dungeon] = "teleport_in_dungeon",
        [ObjectType.Teleport_Dungeon_Choice_Island] = "teleport_dungeon_choice_island",
        [ObjectType.Teleport_With_Target] = "teleports_with_target",
        [ObjectType.Teleport_Wild] = "teleport_wild",
        [ObjectType.Teleport_Broken] = "teleport_broken",
        [ObjectType.Tournament_Teleport] = "teleport_tournament",
        [ObjectType.Alchemy_Mineral] = "alchemy_minerals",
        [ObjectType.Alchemy_Plant] = "alchemy_plants",
        [ObjectType.Alchemy_Metal] = "alchemy_metals",
        [ObjectType.Light_Crystal] = "light_crystals",
        [ObjectType.Light_Crystal_Yellow] = "light_crystals_yellow",
        [ObjectType.Dungeon_Entrance] = "dungeon_entrance",
        [ObjectType.Workshop] = "workshop",
        [ObjectType.Mob_Spawner] = "mob_spawner"
    };

    public static Tuple<string, string, bool> GetPacketPartName(ObjectType objectType, EntityActionType actionType,
        EntityInteractionType interactionType, ushort entId, bool hasGameId, List<OptionalPacketFields> optionalFields)
    {
        var entityNameForComment = CamelCaseToUpperWithSpaces(objectType.ToString());
        var packetName = string.Empty;
        var success = true;
        var comment = (string?)null;
        var genericItemPacket = false;
        var shouldHaveOptionalFields = false;
        switch (actionType)
        {
            case EntityActionType.SET_POSITION:
                packetName = "entity_move";
                comment = $"ENTITY MOVES [{entId:X4}])";
                break;
            case EntityActionType.ATTACK:
                packetName = "change_target_health";
                comment = $"ENTITY DEALS DAMAGE [{entId:X4}]";
                break;
            case EntityActionType.INTERACT:
                switch (interactionType)
                {
                    case EntityInteractionType.DEATH:
                        packetName = "entity_killed";
                        comment = $"ENTITY KILLED [{entId:X4}]";
                        break;
                    case EntityInteractionType.DEAL_DAMAGE:
                        packetName = "entity_takes_damage";
                        comment = $"ENTITY TAKES DAMAGE [{entId:X4}]";
                        break;
                    case EntityInteractionType.OPEN_CONTAINER:
                        success = false;
                        packetName = "header_with_action_type";
                        comment = $"CONTAINER OPEN [{entId:X4}]";
                        break;
                    case EntityInteractionType.UNDEF:
                        packetName = "header_with_action_type";
                        success = false;
                        break;
                    default:
                        success = false;
                        break;
                }

                break;
            case EntityActionType.UNKNOWN:
                packetName = "action_0x14";
                comment = $"ENTITY DOING 0x14 [{entId:X4}]";
                break;
            // assuming full
            default:
                {
                    switch (objectType)
                    {
                        case ObjectType.Monster:
                        case ObjectType.Monster_Flyer:
                            packetName = "entity_monster";
                            break;
                        case ObjectType.Mob_Spawner:
                            packetName = "mob_spawner";
                            break;
                        case ObjectType.Npc_Trade:
                            packetName = "npc_trade";
                            break;
                        case ObjectType.Npc_Banker:
                            packetName = "npc_banker";
                            break;
                        case ObjectType.Npc_Quest_Title:
                        case ObjectType.Npc_Quest_Degree:
                        case ObjectType.Npc_Quest_Karma:
                            packetName = "npc_quest_title";
                            break;
                        case ObjectType.Npc_Guilder:
                            packetName = "npc_guilder";
                            break;
                        case ObjectType.Npc_Guide:
                            packetName = "npc_guide";
                            break;
                        case ObjectType.Npc_Tournament:
                            packetName = "npc_tournament";
                            break;
                        case ObjectType.Npc_Trade_Random_Name:
                            packetName = "npc_trade_random_name";
                            break;
                        case ObjectType.Chest5:
                            packetName = "chest_in_dungeon";
                            break;
                        case ObjectType.Sack_Mob_Loot:
                            packetName = "sack_mob_loot";
                            break;
                        case ObjectType.Tutorial_Message:
                            packetName = "tutorial_message";
                            break;
                        case ObjectType.Teleport:
                        case ObjectType.Teleport_Wild:
                        case ObjectType.Teleport_Broken:
                            packetName = "teleport";
                            break;
                        case ObjectType.Castle_Teleport:
                            packetName = "castle_teleport";
                            break;
                        case ObjectType.Castle_Tablet:
                            packetName = "castle_tablet";
                            break;
                        case ObjectType.Castle_Gate:
                            packetName = "castle_gates";
                            break;
                        case ObjectType.Castle_Chest:
                            packetName = "castle_chest";
                            break;
                        case ObjectType.Castle_Elixir_Pillar:
                            packetName = "castle_elixir_pillar";
                            break;
                        case ObjectType.Castle_Entrance:
                            packetName = "castle_entrance";
                            break;
                        case ObjectType.Door_Entrance_With_Key:
                            packetName = "door_entrance_with_key";
                            break;
                        case ObjectType.Teleport_In_Dungeon:
                        case ObjectType.Teleport_Dungeon_Choice_Island:
                            packetName = "teleport_in_dungeon";
                            break;
                        case ObjectType.Key:
                        case ObjectType.Key_Barn:
                            packetName = "item_key";
                            break;
                        case ObjectType.Key_Single_Use:
                            packetName = "item_key_single_use";
                            break;
                        case ObjectType.Light_Crystal:
                        case ObjectType.Light_Crystal_Yellow:
                            packetName = "item_light_crystal";
                            break;
                        case ObjectType.Ring:
                            packetName = "item_ring";
                            shouldHaveOptionalFields = true;
                            break;
                        case ObjectType.Alchemy_Pot:
                            packetName = "item_alchemypot";
                            break;
                        case ObjectType.Firecracker:
                        case ObjectType.Firework_Celebration:
                            packetName = "item_firework";
                            break;
                        case ObjectType.Mantra_Black:
                        case ObjectType.Mantra_White:
                            packetName = "item_mantra_counted";
                            break;
                        case ObjectType.Scroll_Legend:
                        case ObjectType.Scroll_Recipe:
                            packetName = "item_scroll";
                            shouldHaveOptionalFields = true;
                            break;
                        case ObjectType.Sack:
                            packetName = "item_sack";
                            break;
                        case ObjectType.Ear_String:
                            packetName = "item_earstring";
                            break;
                        case ObjectType.Token:
                            packetName = "item_token";
                            break;
                        case ObjectType.Token_Tutorial_Torweal:
                            packetName = "item_token_tutorial";
                            break;
                        case ObjectType.Token_Multiuse:
                            packetName = "item_token_multiuse";
                            break;
                        case ObjectType.Mantra_Book_Great:
                            packetName = "item_mantrabook_great";
                            break;
                        case ObjectType.Token_Island:
                            packetName = "item_token_island";
                            break;
                        case ObjectType.Token_Island_Guest:
                            packetName = "item_token_island_guest";
                            break;
                        case ObjectType.Trade_License:
                            packetName = "item_license_trade";
                            break;
                        case ObjectType.Alchemy_Furnace:
                            packetName = "entity_alchemyfurnace";
                            break;
                        case ObjectType.Door_Entrance:
                            packetName = "door_entrance";
                            break;
                        case ObjectType.Door_Exit:
                            packetName = "door_exit";
                            break;
                        case ObjectType.Dungeon_Entrance:
                            packetName = "dungeon_entrance";
                            break;
                        case ObjectType.Teleport_With_Target:
                            packetName = "teleport_with_target";
                            break;
                        case ObjectType.Tournament_Teleport:
                            packetName = "tournament_teleport";
                            break;
                        case ObjectType.Workshop:
                            packetName = "workshop";
                            break;
                        case ObjectType.Dungeon:
                            packetName = "dungeon";
                            break;
                        case ObjectType.Weapon_Starting_Sword:
                            packetName = "weapon_starting_sword";
                            break;
                        case ObjectType.Container_Chest:
                            packetName = "container_chest";
                            break;
                        case ObjectType.Mutator:
                            packetName = "item_mutator_special";
                            break;
                        case ObjectType.Special_Guild:
                        case ObjectType.Guild_Specialization:
                            packetName = "item_guild";
                            break;
                        case ObjectType.Special_Ability:
                        case ObjectType.Special_Ability_Steal:
                            packetName = "item_guild_ability";
                            break;
                        case ObjectType.Player:
                        case ObjectType.Stats:
                            packetName = "entity_character";
                            comment = $"NEW PLAYER -- [{entId:X4}]";
                            break;
                        default:
                            if (ItemRecipeBagObjectTypes.Contains(objectType))
                            {
                                packetName = "item_recipebook";
                            }
                            else if (ItemBagObjectTypes.Contains(objectType))
                            {
                                packetName = "item_bag";
                            }
                            else if (ItemObjectTypes.Contains(objectType))
                            {
                                packetName = "item";
                                genericItemPacket = true;
                            }
                            else
                            {
                                success = false;
                            }

                            break;
                    }

                    if (genericItemPacket)
                    {
                        if (hasGameId)
                        {
                            packetName += "_with_gameid";
                        }

                        shouldHaveOptionalFields = true;
                    }

                    if (shouldHaveOptionalFields)
                    {
                        foreach (var field in optionalFields)
                        {
                            switch (field)
                            {
                                case OptionalPacketFields.PA:
                                    packetName += "_pa";
                                    break;
                                case OptionalPacketFields.COUNT:
                                    packetName += "_counted";
                                    break;
                                case OptionalPacketFields.NAME:
                                    packetName += "_named";
                                    break;
                                case OptionalPacketFields.MADE_BY:
                                    packetName += "_made";
                                    break;
                            }
                        }
                    }

                    break;
                }
        }

        comment ??= $"NEW ENTITY -- {entityNameForComment} [{entId:X4}]";

        return new Tuple<string, string, bool>(packetName, comment, success);
    }

    private static string CamelCaseToUpperWithSpaces(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsUpper(c))
            {
                sb.Append(' ');
            }

            sb.Append(char.ToUpper(c));
        }

        return sb.ToString();
    }
}

public static class ObjectTypeToPacketNameMap
{
    public static Dictionary<ObjectType, string> Mapping = new()
    {
        [ObjectType.Despawn] = "despawn",
        [ObjectType.Player] = "",
        [ObjectType.Stats] = "",
        [ObjectType.Token] = "item_token",
        [ObjectType.Mutator] = "item_mutator_special",
        [ObjectType.Seed_Castle] = "",
        [ObjectType.Xp_X2_Pill_Degree] = "",
        [ObjectType.Door_Entrance] = "door_entrance",
        [ObjectType.Door_Exit] = "door_entrance",
        [ObjectType.Dungeon_Entrance] = "dungeon_entrance",
        [ObjectType.Teleport] = "teleport",
        [ObjectType.Teleport_Broken] = "teleport",
        [ObjectType.Teleport_Wild] = "teleport",
        [ObjectType.Castle_Teleport] = "castle_teleport",
        [ObjectType.Castle_Tablet] = "castle_tablet",
        [ObjectType.Castle_Gate] = "castle_gates",
        [ObjectType.Castle_Chest] = "castle_chest",
        [ObjectType.Castle_Elixir_Pillar] = "castle_elixir_pillar",
        [ObjectType.Castle_Entrance] = "castle_entrance",
        [ObjectType.Door_Entrance_With_Key] = "door_entrance_with_key",
        [ObjectType.Teleport_In_Dungeon] = "teleport_in_dungeon",
        [ObjectType.Teleport_Dungeon_Choice_Island] = "teleport_in_dungeon",
        [ObjectType.Teleport_With_Target] = "teleport_with_target",
        [ObjectType.Token_Multiuse] = "item_token_multiuse",
        [ObjectType.Trade_License] = "item_license_trade",
        [ObjectType.Mob_Spawner] = "mob_spawner",
        [ObjectType.Tournament_Teleport] = "tournament_teleport",
        [ObjectType.Tutorial_Message] = "tutorial_message",
        [ObjectType.Scroll_Legend] = "item_scroll_counted", // item_scroll or item_scroll_counted
        [ObjectType.Scroll_Recipe] = "item_scroll_counted", // item_scroll or item_scroll_counted
        [ObjectType.Mission] = "",
        [ObjectType.Token_Island] = "item_token_island",
        [ObjectType.Token_Island_Guest] = "item_token_island_guest",
        [ObjectType.Npc_Quest_Title] = "npc_quest_title",
        [ObjectType.Npc_Quest_Degree] = "",
        [ObjectType.Npc_Quest_Karma] = "npc_quest_karma",
        [ObjectType.Monster] = "monster_full",
        [ObjectType.Monster_Flyer] = "",
        [ObjectType.Npc_Trade] = "npc_trade",
        [ObjectType.Npc_Banker] = "npc_banker",
        [ObjectType.Npc_Guilder] = "npc_guilder",
        [ObjectType.Bead] = "",
        [ObjectType.Npc_Tournament] = "npc_tournament",
        [ObjectType.Backpack_Large] = "item_backpack",
        [ObjectType.Backpack_Small] = "item_backpack",
        [ObjectType.Sack] = "item_sack",
        [ObjectType.Chest2] = "",
        [ObjectType.Sack_Mob_Loot] = "sack_mob_loot",
        [ObjectType.Mantra_Book_Small] = "item_mantrabook",
        [ObjectType.Recipe_Book] = "item_recipebook",
        [ObjectType.Mantra_Book_Large] = "item_mantrabook",
        [ObjectType.Mantra_Book_Great] = "item_mantrabook_great",
        [ObjectType.Map_Book] = "",
        [ObjectType.Chest5] = "chest_in_dungeon",
        [ObjectType.Key_Barn] = "item_key",
        [ObjectType.Powder_Finale] = "item_powder_counted", //item_powder_counted
        [ObjectType.Powder_Single_Target] = "item_powder_counted", //item_powder_counted
        [ObjectType.Powder_Amilus] = "item_powder_counted", //item_powder_counted
        [ObjectType.Powder_Ao_E] = "item_powder_counted", //item_powder_counted
        [ObjectType.Elixir_Castle] = "item_elixir_counted", // item_elixir_counted
        [ObjectType.Elixir_Trap] = "item_elixir_counted", // item_elixir_counted
        [ObjectType.Weapon_Sword] = "item_amulet",
        [ObjectType.Weapon_Axe] = "item_amulet",
        [ObjectType.Weapon_Crossbow] = "item_amulet",
        [ObjectType.Arrow] = "item_arrows_counted",
        [ObjectType.Ring_Diamond] = "item_ring_diamond_counted", //item_ring_diamond_counted
        [ObjectType.Ring_Ruby] = "",
        [ObjectType.Ruby] = "",
        [ObjectType.Ring_Gold] = "", //item_ring_gold_counted
        [ObjectType.Alchemy_Mineral] = "alchemy_resource_ground", // item_alchemy_counted
        [ObjectType.Alchemy_Plant] = "alchemy_resource_ground", // item_alchemy_counted
        [ObjectType.Alchemy_Metal] = "alchemy_resource_ground", // item_alchemy_counted
        [ObjectType.Food_Apple] = "item_food_counted", // item_food_counted
        [ObjectType.Food_Pear] = "item_food_counted", // item_food_counted
        [ObjectType.Food_Meat] = "item_food_counted", // item_food_counted
        [ObjectType.Food_Bread] = "item_food_counted", // item_food_counted
        [ObjectType.Food_Fish2] = "item_food_counted", // item_food_counted
        [ObjectType.Alchemy_Brushwood] = "",
        [ObjectType.Key] = "item_key",
        [ObjectType.Key_Single_Use] = "item_key_single_use",
        [ObjectType.Light_Crystal] = "item_light_crystal",
        [ObjectType.Light_Crystal_Yellow] = "item_light_crystal",
        [ObjectType.Map] = "item_map",
        [ObjectType.Inkpot] = "item_inkpot",
        [ObjectType.Firecracker] = "alchemy_resource_ground",
        [ObjectType.Ear] = "",
        [ObjectType.Ear_String] = "item_earstring",
        [ObjectType.Monster_Part] = "",
        [ObjectType.Firework_Celebration] = "alchemy_resource_ground",
        [ObjectType.Item_Expired] = "",
        [ObjectType.Armor_Chest] = "item_amulet", // generic item packet
        [ObjectType.Armor_Amulet] = "item_amulet", // generic item packet
        [ObjectType.Armor_Boots] = "item_amulet", // generic item packet
        [ObjectType.Armor_Gloves] = "item_amulet", // generic item packet
        [ObjectType.Armor_Belt] = "item_amulet", // generic item packet
        [ObjectType.Armor_Shield] = "item_amulet", // generic item packet
        [ObjectType.Armor_Helmet] = "item_amulet", // generic item packet
        [ObjectType.Armor_Pants] = "item_amulet",
        [ObjectType.Armor_Bracelet] = "item_amulet", // generic item packet
        [ObjectType.Ring] = "item_ring_half",
        [ObjectType.Armor_Robe] = "item_amulet", // item_robe_dragon_pa
        [ObjectType.Ring_Golem] = "",
        [ObjectType.Alchemy_Pot] = "item_alchemypot",
        [ObjectType.Alchemy_Furnace] = "",
        [ObjectType.Blueprint] = "",
        [ObjectType.Workshop] = "workshop",
        [ObjectType.Quest_Armor_Chest] = "", // generic item packet
        [ObjectType.Quest_Armor_Chest2] = "", // generic item packet
        [ObjectType.Quest_Armor_Boots] = "item_quest_boots", // generic item packet
        [ObjectType.Quest_Armor_Gloves] = "", // generic item packet
        [ObjectType.Quest_Armor_Belt] = "", // generic item packet
        [ObjectType.Quest_Armor_Shield] = "item_quest_shield", // generic item packet
        [ObjectType.Quest_Armor_Helmet] = "item_quest_helmet", // generic item packet
        [ObjectType.Quest_Armor_Pants] = "", // generic item packet
        [ObjectType.Quest_Armor_Bracelet] = "", // generic item packet
        [ObjectType.Quest_Armor_Ring] = "", // generic item packet
        [ObjectType.Quest_Armor_Robe] = "item_quest_robe", // generic item packet
        [ObjectType.Quest_Weapon_Sword] = "", // generic item packet
        [ObjectType.Quest_Weapon_Axe] = "", // generic item packet
        [ObjectType.Quest_Weapon_Crossbow] = "item_quest_crossbow", // generic item packet
        [ObjectType.Special_Guild] = "item_guild",
        [ObjectType.Special_Ability] = "item_guild_ability",
        [ObjectType.Special_Ability_Steal] = "item_guild_ability",
        [ObjectType.Guild_Specialization] = "item_guild",
        [ObjectType.Armor_Helmet_Premium] = "", // generic item packet
        [ObjectType.Mantra_White] = "", //item_mantra_counted
        [ObjectType.Mantra_Black] = "" //item_mantra_counted
        //Unknown
    };
}