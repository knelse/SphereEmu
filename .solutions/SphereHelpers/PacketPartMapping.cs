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
    public static HashSet<ObjectType> ItemObjectTypes =
    [
        ObjectType.Token,
        ObjectType.Mutator,
        ObjectType.SeedCastle,
        ObjectType.XpPillDegree,
        ObjectType.TokenMultiuse,
        ObjectType.TradeLicense,
        ObjectType.ScrollLegend,
        ObjectType.ScrollRecipe,
        ObjectType.Mission,
        ObjectType.TokenIsland,
        ObjectType.TokenIslandGuest,
        ObjectType.TokenTutorialTorweal,
        ObjectType.Bead,
        ObjectType.BackpackLarge,
        ObjectType.BackpackSmall,
        ObjectType.Sack,
        ObjectType.MantraBookSmall,
        ObjectType.RecipeBook,
        ObjectType.MantraBookLarge,
        ObjectType.MantraBookGreat,
        ObjectType.MapBook,
        ObjectType.KeyBarn,
        ObjectType.PowderFinale,
        ObjectType.PowderSingleTarget,
        ObjectType.PowderAmilus,
        ObjectType.PowderAoE,
        ObjectType.ElixirCastle,
        ObjectType.ElixirTrap,
        ObjectType.WeaponSword,
        ObjectType.WeaponStartingSword,
        ObjectType.WeaponAxe,
        ObjectType.WeaponCrossbow,
        ObjectType.Arrows,
        ObjectType.RingDiamond,
        ObjectType.RingRuby,
        ObjectType.Ruby,
        ObjectType.RingGold,
        ObjectType.AlchemyMineral,
        ObjectType.AlchemyPlant,
        ObjectType.AlchemyMetal,
        ObjectType.FoodApple,
        ObjectType.FoodPear,
        ObjectType.FoodMeat,
        ObjectType.FoodBread,
        ObjectType.FoodFish,
        ObjectType.AlchemyBrushwood,
        ObjectType.Key,
        ObjectType.Map,
        ObjectType.Inkpot,
        ObjectType.Firecracker,
        ObjectType.Ear,
        ObjectType.EarString,
        ObjectType.MonsterPart,
        ObjectType.Firework,
        ObjectType.InkpotBroken,
        ObjectType.ArmorChest,
        ObjectType.ArmorAmulet,
        ObjectType.ArmorBoots,
        ObjectType.ArmorGloves,
        ObjectType.ArmorBelt,
        ObjectType.ArmorShield,
        ObjectType.ArmorHelmet,
        ObjectType.ArmorPants,
        ObjectType.ArmorBracelet,
        ObjectType.Ring,
        ObjectType.ArmorRobe,
        ObjectType.RingGolem,
        ObjectType.AlchemyPot,
        ObjectType.AlchemyFurnace,
        ObjectType.Blueprint,
        ObjectType.QuestArmorChest,
        ObjectType.QuestArmorChest2,
        ObjectType.QuestArmorBoots,
        ObjectType.QuestArmorGloves,
        ObjectType.QuestArmorBelt,
        ObjectType.QuestArmorShield,
        ObjectType.QuestArmorHelmet,
        ObjectType.QuestArmorPants,
        ObjectType.QuestArmorBracelet,
        ObjectType.QuestArmorRing,
        ObjectType.QuestArmorRobe,
        ObjectType.QuestWeaponSword,
        ObjectType.QuestWeaponAxe,
        ObjectType.QuestWeaponCrossbow,
        ObjectType.SpecialGuild,
        ObjectType.SpecialAbility,
        ObjectType.SpecialAbilitySteal,
        ObjectType.ArmorHelmetPremium,
        ObjectType.MantraWhite,
        ObjectType.MantraBlack
    ];

    public static HashSet<ObjectType> EntityObjectTypes =
    [
        ObjectType.Token,
        ObjectType.Mutator,
        ObjectType.Dungeon,
        ObjectType.SeedCastle,
        ObjectType.XpPillDegree,
        ObjectType.DoorEntrance,
        ObjectType.DoorExit,
        ObjectType.Teleport,
        ObjectType.TeleportWithTarget,
        ObjectType.DungeonEntrance,
        ObjectType.TutorialMessage,
        ObjectType.TeleportWild,
        ObjectType.TokenMultiuse,
        ObjectType.TokenIsland,
        ObjectType.TokenTutorialTorweal,
        ObjectType.TradeLicense,
        ObjectType.MobSpawner,
        ObjectType.TournamentTeleport,
        ObjectType.Monster,
        ObjectType.MonsterFlyer,
        ObjectType.NpcBanker,
        ObjectType.NpcTrade,
        ObjectType.NpcQuestDegree,
        ObjectType.NpcQuestKarma,
        ObjectType.NpcQuestTitle,
        ObjectType.NpcGuilder,
        ObjectType.NpcGuide,
        ObjectType.NpcTradeRandomName,
        ObjectType.SackMobLoot,
        ObjectType.ChestInDungeon,
        ObjectType.NewPlayerDungeonStartPoint,
        ObjectType.Chest,
        ObjectType.ScrollLegend,
        ObjectType.ScrollRecipe,
        ObjectType.Mission,
        ObjectType.TokenIslandGuest,
        ObjectType.Bead,
        ObjectType.BackpackLarge,
        ObjectType.BackpackSmall,
        ObjectType.Sack,
        ObjectType.MantraBookSmall,
        ObjectType.RecipeBook,
        ObjectType.MantraBookLarge,
        ObjectType.MantraBookGreat,
        ObjectType.MapBook,
        ObjectType.KeyBarn,
        ObjectType.PowderFinale,
        ObjectType.PowderSingleTarget,
        ObjectType.PowderAmilus,
        ObjectType.PowderAoE,
        ObjectType.ElixirCastle,
        ObjectType.ElixirTrap,
        ObjectType.WeaponSword,
        ObjectType.WeaponStartingSword,
        ObjectType.WeaponAxe,
        ObjectType.WeaponCrossbow,
        ObjectType.Arrows,
        ObjectType.RingDiamond,
        ObjectType.RingRuby,
        ObjectType.Ruby,
        ObjectType.RingGold,
        ObjectType.AlchemyMineral,
        ObjectType.AlchemyPlant,
        ObjectType.AlchemyMetal,
        ObjectType.FoodApple,
        ObjectType.FoodPear,
        ObjectType.FoodMeat,
        ObjectType.FoodBread,
        ObjectType.FoodFish,
        ObjectType.AlchemyBrushwood,
        ObjectType.Key,
        ObjectType.Map,
        ObjectType.Inkpot,
        ObjectType.Firecracker,
        ObjectType.Ear,
        ObjectType.EarString,
        ObjectType.MonsterPart,
        ObjectType.Firework,
        ObjectType.InkpotBroken,
        ObjectType.ArmorChest,
        ObjectType.ArmorAmulet,
        ObjectType.ArmorBoots,
        ObjectType.ArmorGloves,
        ObjectType.ArmorBelt,
        ObjectType.ArmorShield,
        ObjectType.ArmorHelmet,
        ObjectType.ArmorPants,
        ObjectType.ArmorBracelet,
        ObjectType.Ring,
        ObjectType.ArmorRobe,
        ObjectType.RingGolem,
        ObjectType.AlchemyPot,
        ObjectType.AlchemyFurnace,
        ObjectType.Blueprint,
        ObjectType.Workshop,
        ObjectType.QuestArmorChest,
        ObjectType.QuestArmorChest2,
        ObjectType.QuestArmorBoots,
        ObjectType.QuestArmorGloves,
        ObjectType.QuestArmorBelt,
        ObjectType.QuestArmorShield,
        ObjectType.QuestArmorHelmet,
        ObjectType.QuestArmorPants,
        ObjectType.QuestArmorBracelet,
        ObjectType.QuestArmorRing,
        ObjectType.QuestArmorRobe,
        ObjectType.QuestWeaponSword,
        ObjectType.QuestWeaponAxe,
        ObjectType.QuestWeaponCrossbow,
        ObjectType.SpecialGuild,
        ObjectType.SpecialAbility,
        ObjectType.SpecialAbilitySteal,
        ObjectType.ArmorHelmetPremium,
        ObjectType.MantraWhite,
        ObjectType.MantraBlack
    ];

    public static HashSet<ObjectType> ItemBagObjectTypes =
    [
        ObjectType.BackpackLarge,
        ObjectType.BackpackSmall,
        ObjectType.MantraBookSmall,
        ObjectType.MantraBookLarge,
        ObjectType.MantraBookGreat,
        ObjectType.MapBook,
        ObjectType.AlchemyPot,
        ObjectType.Sack
    ];

    public static HashSet<ObjectType> ItemRecipeBagObjectTypes =
    [
        ObjectType.RecipeBook
    ];

    public static HashSet<ObjectType> EquippableItemTypes =
    [
        ObjectType.WeaponSword,
        ObjectType.WeaponStartingSword,
        ObjectType.WeaponAxe,
        ObjectType.WeaponCrossbow,
        ObjectType.ArmorChest,
        ObjectType.ArmorAmulet,
        ObjectType.ArmorBoots,
        ObjectType.ArmorGloves,
        ObjectType.ArmorBelt,
        ObjectType.ArmorShield,
        ObjectType.ArmorHelmet,
        ObjectType.ArmorPants,
        ObjectType.ArmorBracelet,
        ObjectType.Ring,
        ObjectType.ArmorRobe,
        ObjectType.QuestArmorChest,
        ObjectType.QuestArmorChest2,
        ObjectType.QuestArmorBoots,
        ObjectType.QuestArmorGloves,
        ObjectType.QuestArmorBelt,
        ObjectType.QuestArmorShield,
        ObjectType.QuestArmorHelmet,
        ObjectType.QuestArmorPants,
        ObjectType.QuestArmorBracelet,
        ObjectType.QuestArmorRing,
        ObjectType.QuestArmorRobe,
        ObjectType.QuestWeaponSword,
        ObjectType.QuestWeaponAxe,
        ObjectType.QuestWeaponCrossbow
    ];

    public static Dictionary<ObjectType, string> WorldObjectsToTrack = new ()
    {
        [ObjectType.Teleport] = "teleports",
        [ObjectType.TeleportWild] = "teleport_wild",
        [ObjectType.TournamentTeleport] = "teleport_tournament",
        [ObjectType.AlchemyMineral] = "alchemy_minerals",
        [ObjectType.AlchemyPlant] = "alchemy_plants",
        [ObjectType.AlchemyMetal] = "alchemy_metals",
        [ObjectType.DungeonEntrance] = "dungeon_entrance",
        [ObjectType.Workshop] = "workshop"
    };

    public static Tuple<string, string, bool> GetPacketPartName (ObjectType objectType, EntityActionType actionType,
        EntityInteractionType interactionType, ushort entId, bool hasGameId, List<OptionalPacketFields> optionalFields)
    {
        var entityNameForComment = CamelCaseToUpperWithSpaces(objectType.ToString());
        var packetName = string.Empty;
        var success = true;
        var comment = (string?) null;
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
                    case ObjectType.MonsterFlyer:
                        packetName = "entity_monster";
                        break;
                    case ObjectType.MobSpawner:
                        packetName = "mob_spawner";
                        break;
                    case ObjectType.NpcTrade:
                        packetName = "npc_trade";
                        break;
                    case ObjectType.NpcBanker:
                        packetName = "npc_banker";
                        break;
                    case ObjectType.NpcQuestTitle:
                    case ObjectType.NpcQuestDegree:
                    case ObjectType.NpcQuestKarma:
                        packetName = "npc_quest_title";
                        break;
                    case ObjectType.NpcGuilder:
                        packetName = "npc_guilder";
                        break;
                    case ObjectType.NpcGuide:
                        packetName = "npc_guide";
                        break;
                    case ObjectType.NpcTradeRandomName:
                        packetName = "npc_trade_random_name";
                        break;
                    case ObjectType.ChestInDungeon:
                        packetName = "chest_in_dungeon";
                        break;
                    case ObjectType.SackMobLoot:
                        packetName = "sack_mob_loot";
                        break;
                    case ObjectType.TutorialMessage:
                        packetName = "tutorial_message";
                        break;
                    case ObjectType.Teleport:
                        packetName = "teleport";
                        break;
                    case ObjectType.Key:
                    case ObjectType.KeyBarn:
                        packetName = "item_key";
                        break;
                    case ObjectType.Ring:
                        packetName = "item_ring";
                        shouldHaveOptionalFields = true;
                        break;
                    case ObjectType.AlchemyPot:
                        packetName = "item_alchemypot";
                        break;
                    case ObjectType.Firecracker:
                    case ObjectType.Firework:
                        packetName = "item_firework";
                        break;
                    case ObjectType.MantraBlack:
                    case ObjectType.MantraWhite:
                        packetName = "item_mantra_counted";
                        break;
                    case ObjectType.ScrollLegend:
                    case ObjectType.ScrollRecipe:
                        packetName = "item_scroll";
                        shouldHaveOptionalFields = true;
                        break;
                    case ObjectType.Sack:
                        packetName = "item_sack";
                        break;
                    case ObjectType.EarString:
                        packetName = "item_earstring";
                        break;
                    case ObjectType.Token:
                        packetName = "item_token";
                        break;
                    case ObjectType.TokenTutorialTorweal:
                        packetName = "item_token_tutorial";
                        break;
                    case ObjectType.TokenMultiuse:
                        packetName = "item_token_multiuse";
                        break;
                    case ObjectType.MantraBookGreat:
                        packetName = "item_mantrabook_great";
                        break;
                    case ObjectType.TokenIsland:
                        packetName = "item_token_island";
                        break;
                    case ObjectType.TokenIslandGuest:
                        packetName = "item_token_island_guest";
                        break;
                    case ObjectType.TradeLicense:
                        packetName = "item_license_trade";
                        break;
                    case ObjectType.AlchemyFurnace:
                        packetName = "entity_alchemyfurnace";
                        break;
                    case ObjectType.DoorEntrance:
                    case ObjectType.DoorExit:
                        packetName = "door_entrance";
                        break;
                    case ObjectType.DungeonEntrance:
                        packetName = "dungeon_entrance";
                        break;
                    case ObjectType.TeleportWithTarget:
                        packetName = "teleport_with_target";
                        break;
                    case ObjectType.TournamentTeleport:
                        packetName = "tournament_teleport";
                        break;
                    case ObjectType.Workshop:
                        packetName = "workshop";
                        break;
                    case ObjectType.Dungeon:
                        packetName = "new_player_dungeon";
                        break;
                    case ObjectType.WeaponStartingSword:
                        packetName = "weapon_starting_sword";
                        break;
                    case ObjectType.NewPlayerDungeonStartPoint:
                        packetName = "new_player_dungeon_start";
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

    private static string CamelCaseToUpperWithSpaces (string s)
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
    public static Dictionary<ObjectType, string> Mapping = new ()
    {
        [ObjectType.Despawn] = "despawn",
        [ObjectType.UpdateState] = "",
        [ObjectType.Player] = "",
        [ObjectType.Token] = "item_token",
        [ObjectType.Mutator] = "",
        [ObjectType.SeedCastle] = "",
        [ObjectType.XpPillDegree] = "",
        [ObjectType.DoorEntrance] = "door_entrance",
        [ObjectType.DoorExit] = "door_entrance",
        [ObjectType.DungeonEntrance] = "dungeon_entrance",
        [ObjectType.Teleport] = "teleport",
        [ObjectType.TeleportWithTarget] = "teleport_with_target",
        [ObjectType.TokenMultiuse] = "item_token_multiuse",
        [ObjectType.TradeLicense] = "item_license_trade",
        [ObjectType.MobSpawner] = "mob_spawner",
        [ObjectType.TournamentTeleport] = "tournament_teleport",
        [ObjectType.TutorialMessage] = "tutorial_message",
        [ObjectType.ScrollLegend] = "item_scroll_counted", // item_scroll or item_scroll_counted
        [ObjectType.ScrollRecipe] = "item_scroll_counted", // item_scroll or item_scroll_counted
        [ObjectType.Mission] = "",
        [ObjectType.TokenIsland] = "item_token_island",
        [ObjectType.TokenIslandGuest] = "item_token_island_guest",
        [ObjectType.NpcQuestTitle] = "npc_quest_title",
        [ObjectType.NpcQuestDegree] = "",
        [ObjectType.NpcQuestKarma] = "npc_quest_karma",
        [ObjectType.Monster] = "monster_full",
        [ObjectType.MonsterFlyer] = "",
        [ObjectType.NpcTrade] = "npc_trade",
        [ObjectType.NpcBanker] = "npc_banker",
        [ObjectType.Bead] = "",
        [ObjectType.NpcGuilder] = "npc_guilder",
        [ObjectType.BackpackLarge] = "item_backpack",
        [ObjectType.BackpackSmall] = "item_backpack",
        [ObjectType.Sack] = "item_sack",
        [ObjectType.Chest] = "",
        [ObjectType.SackMobLoot] = "sack_mob_loot",
        [ObjectType.MantraBookSmall] = "item_mantrabook",
        [ObjectType.RecipeBook] = "item_recipebook",
        [ObjectType.MantraBookLarge] = "item_mantrabook",
        [ObjectType.MantraBookGreat] = "item_mantrabook_great",
        [ObjectType.MapBook] = "",
        [ObjectType.ChestInDungeon] = "chest_in_dungeon",
        [ObjectType.KeyBarn] = "item_key",
        [ObjectType.PowderFinale] = "item_powder_counted", //item_powder_counted
        [ObjectType.PowderSingleTarget] = "item_powder_counted", //item_powder_counted
        [ObjectType.PowderAmilus] = "item_powder_counted", //item_powder_counted
        [ObjectType.PowderAoE] = "item_powder_counted", //item_powder_counted
        [ObjectType.ElixirCastle] = "item_elixir_counted", // item_elixir_counted
        [ObjectType.ElixirTrap] = "item_elixir_counted", // item_elixir_counted
        [ObjectType.WeaponSword] = "item_amulet",
        [ObjectType.WeaponAxe] = "item_amulet",
        [ObjectType.WeaponCrossbow] = "item_amulet",
        [ObjectType.Arrows] = "item_arrows_counted",
        [ObjectType.RingDiamond] = "item_ring_diamond_counted", //item_ring_diamond_counted
        [ObjectType.RingRuby] = "",
        [ObjectType.Ruby] = "",
        [ObjectType.RingGold] = "", //item_ring_gold_counted
        [ObjectType.AlchemyMineral] = "alchemy_resource_ground", // item_alchemy_counted
        [ObjectType.AlchemyPlant] = "alchemy_resource_ground", // item_alchemy_counted
        [ObjectType.AlchemyMetal] = "alchemy_resource_ground", // item_alchemy_counted
        [ObjectType.FoodApple] = "item_food_counted", // item_food_counted
        [ObjectType.FoodPear] = "item_food_counted", // item_food_counted
        [ObjectType.FoodMeat] = "item_food_counted", // item_food_counted
        [ObjectType.FoodBread] = "item_food_counted", // item_food_counted
        [ObjectType.FoodFish] = "item_food_counted", // item_food_counted
        [ObjectType.AlchemyBrushwood] = "",
        [ObjectType.Key] = "item_key",
        [ObjectType.Map] = "item_map",
        [ObjectType.Inkpot] = "item_inkpot",
        [ObjectType.Firecracker] = "alchemy_resource_ground",
        [ObjectType.Ear] = "",
        [ObjectType.EarString] = "item_earstring",
        [ObjectType.MonsterPart] = "",
        [ObjectType.Firework] = "alchemy_resource_ground",
        [ObjectType.InkpotBroken] = "",
        [ObjectType.ArmorChest] = "item_amulet", // generic item packet
        [ObjectType.ArmorAmulet] = "item_amulet", // generic item packet
        [ObjectType.ArmorBoots] = "item_amulet", // generic item packet
        [ObjectType.ArmorGloves] = "item_amulet", // generic item packet
        [ObjectType.ArmorBelt] = "item_amulet", // generic item packet
        [ObjectType.ArmorShield] = "item_amulet", // generic item packet
        [ObjectType.ArmorHelmet] = "item_amulet", // generic item packet
        [ObjectType.ArmorPants] = "item_amulet",
        [ObjectType.ArmorBracelet] = "item_amulet", // generic item packet
        [ObjectType.Ring] = "item_ring_half",
        [ObjectType.ArmorRobe] = "item_amulet", // item_robe_dragon_pa
        [ObjectType.RingGolem] = "",
        [ObjectType.AlchemyPot] = "item_alchemypot",
        [ObjectType.AlchemyFurnace] = "",
        [ObjectType.Blueprint] = "",
        [ObjectType.Workshop] = "workshop",
        [ObjectType.QuestArmorChest] = "", // generic item packet
        [ObjectType.QuestArmorChest2] = "", // generic item packet
        [ObjectType.QuestArmorBoots] = "item_quest_boots", // generic item packet
        [ObjectType.QuestArmorGloves] = "", // generic item packet
        [ObjectType.QuestArmorBelt] = "", // generic item packet
        [ObjectType.QuestArmorShield] = "item_quest_shield", // generic item packet
        [ObjectType.QuestArmorHelmet] = "item_quest_helmet", // generic item packet
        [ObjectType.QuestArmorPants] = "", // generic item packet
        [ObjectType.QuestArmorBracelet] = "", // generic item packet
        [ObjectType.QuestArmorRing] = "", // generic item packet
        [ObjectType.QuestArmorRobe] = "item_quest_robe", // generic item packet
        [ObjectType.QuestWeaponSword] = "", // generic item packet
        [ObjectType.QuestWeaponAxe] = "", // generic item packet
        [ObjectType.QuestWeaponCrossbow] = "item_quest_crossbow", // generic item packet
        [ObjectType.SpecialGuild] = "", // item_guild
        [ObjectType.SpecialAbility] = "",
        [ObjectType.SpecialAbilitySteal] = "",
        [ObjectType.ArmorHelmetPremium] = "", // generic item packet
        [ObjectType.MantraWhite] = "", //item_mantra_counted
        [ObjectType.MantraBlack] = "" //item_mantra_counted
        //Unknown
    };
}