using SuffixToLocaleMap = System.Collections.Generic.Dictionary<ItemSuffix, SuffixValueWithLocale>;

public static class GameObjectDataHelper
{
    public static readonly HashSet<GameObjectType> MaterialsPowdersElixirs = new ()
    {
        GameObjectType.Flower,
        GameObjectType.Metal,
        GameObjectType.Mineral,
        GameObjectType.Powder,
        GameObjectType.Powder_Area,
        GameObjectType.Elixir_Castle,
        GameObjectType.Elixir_Trap
    };

    public static readonly HashSet<GameObjectType> RegularWeaponsAndArmor = new ()
    {
        GameObjectType.Crossbow, GameObjectType.Axe, GameObjectType.Sword, GameObjectType.Amulet,
        GameObjectType.Chestplate,
        GameObjectType.Belt, GameObjectType.Bracelet, GameObjectType.Gloves, GameObjectType.Helmet,
        GameObjectType.Pants, GameObjectType.Shield, GameObjectType.Boots, GameObjectType.Robe
    };

    public static readonly HashSet<GameObjectType> WeaponsAndArmor = new ()
    {
        GameObjectType.Amulet,
        GameObjectType.Chestplate,
        GameObjectType.Axe,
        GameObjectType.Belt,
        GameObjectType.Boots,
        GameObjectType.Bracelet,
        GameObjectType.Crossbow,
        GameObjectType.Gloves,
        GameObjectType.Helmet,
        GameObjectType.Pants,
        GameObjectType.Robe,
        GameObjectType.Shield,
        GameObjectType.Sword,
        GameObjectType.Amulet_Unique,
        GameObjectType.Chestplate_Unique,
        GameObjectType.Belt_Unique,
        GameObjectType.Boots_Unique,
        GameObjectType.Bracelet_Unique,
        GameObjectType.Gloves_Unique,
        GameObjectType.Helmet_Premium,
        GameObjectType.Helmet_Unique,
        GameObjectType.Pants_Unique,
        GameObjectType.Robe_Unique,
        GameObjectType.Shield_Unique,
        GameObjectType.Sword_Unique
    };

    public static readonly HashSet<GameObjectType> Mantras = new ()
    {
        GameObjectType.MantraBlack,
        GameObjectType.MantraWhite
    };

    public static readonly HashSet<GameObjectType> AlchemyMaterials = new ()
    {
        GameObjectType.Metal,
        GameObjectType.Flower,
        GameObjectType.Mineral
    };

    public static readonly HashSet<GameObjectType> Powders = new ()
    {
        GameObjectType.Powder,
        GameObjectType.Powder_Area,
        GameObjectType.Powder_Event,
        GameObjectType.Powder_Guild,
        GameObjectType.Elixir_Castle,
        GameObjectType.Elixir_Trap
    };

    // public static bool firstTypeRolled = false;
    public static HashSet<ItemSuffix> RingSuffixes = new ()
    {
        ItemSuffix.Health,
        ItemSuffix.Ether,
        ItemSuffix.Accuracy,
        ItemSuffix.Air,
        ItemSuffix.Durability,
        ItemSuffix.Life,
        ItemSuffix.Precision,
        ItemSuffix.Endurance,
        ItemSuffix.Fire,
        ItemSuffix.Absorption,
        ItemSuffix.Meditation,
        ItemSuffix.Strength,
        ItemSuffix.Earth,
        ItemSuffix.Value,
        ItemSuffix.Safety,
        ItemSuffix.Prana,
        ItemSuffix.Agility,
        ItemSuffix.Water
    };

    public static GameObjectKind GetKindBySphereName (string sphName)
    {
        switch (sphName)
        {
            case "alch": return GameObjectKind.Alchemy;
            case "arbs": return GameObjectKind.Crossbow;
            case "arbs_n": return GameObjectKind.Crossbow_New;
            case "armor": return GameObjectKind.Armor;
            case "armor_n": return GameObjectKind.Armor_New;
            case "armor_o": return GameObjectKind.Armor_Old;
            case "axes": return GameObjectKind.Axe;
            case "axes_n": return GameObjectKind.Axe_New;
            case "fb": return GameObjectKind.Powder;
            case "guilds": return GameObjectKind.Guild;
            case "magdef": return GameObjectKind.Magical;
            case "magdef_n": return GameObjectKind.Magical_New;
            case "mantrab": return GameObjectKind.MantraBlack;
            case "mantraw": return GameObjectKind.MantraWhite;
            case "maps": return GameObjectKind.Map;
            case "monst": return GameObjectKind.Monster;
            case "quest": return GameObjectKind.Quest;
            case "swords": return GameObjectKind.Sword;
            case "swords_n": return GameObjectKind.Sword_New;
            case "unique": return GameObjectKind.Unique;
            case "pref": return GameObjectKind.Pref;
            default:
                Console.WriteLine($"Unknown game object type: {sphName}");
                return GameObjectKind.Unknown;
        }
    }

    public static GameObjectType GetTypeBySphereName (string sphName)
    {
        switch (sphName)
        {
            case "A": return GameObjectType.Pref_AxeSword;
            case "al_flower": return GameObjectType.Flower;
            case "al_metal": return GameObjectType.Metal;
            case "al_mineral": return GameObjectType.Mineral;
            case "ar_amulet": return GameObjectType.Amulet;
            case "ar_amuletu": return GameObjectType.Amulet_Unique;
            case "ar_armor": return GameObjectType.Chestplate;
            case "ar_armor2": return GameObjectType.Robe;
            case "ar_armor2f": return GameObjectType.Robe_Quest;
            case "ar_armor2u": return GameObjectType.Robe_Unique;
            case "ar_armorf": return GameObjectType.Chestplate_Quest;
            case "ar_armoru": return GameObjectType.Chestplate_Unique;
            case "ar_belt": return GameObjectType.Belt;
            case "ar_beltf": return GameObjectType.Belt_Quest;
            case "ar_beltu": return GameObjectType.Belt_Unique;
            case "ar_bracelet": return GameObjectType.Bracelet;
            case "ar_braceletu": return GameObjectType.Bracelet_Unique;
            case "ar_gloves": return GameObjectType.Gloves;
            case "ar_glovesf": return GameObjectType.Gloves_Quest;
            case "ar_glovesu": return GameObjectType.Gloves_Unique;
            case "ar_helm": return GameObjectType.Helmet;
            case "ar_helm_pr": return GameObjectType.Helmet_Premium;
            case "ar_helmf": return GameObjectType.Helmet_Quest;
            case "ar_helmu": return GameObjectType.Helmet_Unique;
            case "ar_pants": return GameObjectType.Pants;
            case "ar_pantsf": return GameObjectType.Pants_Quest;
            case "ar_pantsu": return GameObjectType.Pants_Unique;
            case "ar_ring": return GameObjectType.Ring;
            case "ar_ring_s": return GameObjectType.Ring_Special;
            case "ar_ringu": return GameObjectType.Ring_Unique;
            case "ar_shield": return GameObjectType.Shield;
            case "ar_shieldf": return GameObjectType.Shield_Quest;
            case "ar_shieldu": return GameObjectType.Shield_Unique;
            case "ar_shoes": return GameObjectType.Boots;
            case "ar_shoesf": return GameObjectType.Boots_Quest;
            case "ar_shoesu": return GameObjectType.Boots_Unique;
            case "B": return GameObjectType.Pref_Crossbow;
            case "C": return GameObjectType.Pref_Chestplate;
            case "crystal": return GameObjectType.Castle_Crystal;
            case "cs_guard": return GameObjectType.Castle_Stone;
            case "ct_bagd": return GameObjectType.Guild_Bag;
            case "D": return GameObjectType.Pref_BeltBootsGlovesHelmetPants;
            case "E": return GameObjectType.Pref_AmuletBracelet;
            case "F": return GameObjectType.Pref_Ring;
            case "flag": return GameObjectType.Flag;
            case "G": return GameObjectType.Pref_Robe;
            case "guild": return GameObjectType.Guild;
            case "H": return GameObjectType.Pref_Castle;
            case "I": return GameObjectType.Pref_Shield;
            case "item_letter": return GameObjectType.Letter;
            case "lottery": return GameObjectType.Lottery;
            case "mg_mantrab": return GameObjectType.MantraBlack;
            case "mg_mantraw": return GameObjectType.MantraWhite;
            case "monster": return GameObjectType.Monster;
            case "monsterc": return GameObjectType.Monster_Castle_Stone;
            case "monsterd": return GameObjectType.Monster_Event;
            case "monsterdf": return GameObjectType.Monster_Event_Flying;
            case "monsterf": return GameObjectType.Monster_Flying;
            case "monsterh": return GameObjectType.Monster_Tower_Spirit;
            case "monsters": return GameObjectType.Monster_Castle_Spirit;
            case "pw_elixir": return GameObjectType.Elixir_Castle;
            case "pw_elixir1": return GameObjectType.Elixir_Trap;
            case "pw_fb02": return GameObjectType.Powder;
            case "pw_fb03": return GameObjectType.Powder_Area;
            case "pw_fb04": return GameObjectType.Powder_Event;
            case "pw_fb05": return GameObjectType.Powder_Guild;
            case "scroll": return GameObjectType.Scroll;
            case "specab": return GameObjectType.Special;
            case "specab_ba": return GameObjectType.Special_Crusader_Gapclose;
            case "specab_ca": return GameObjectType.Special_Inquisitor_Teleport;
            case "specab_ea": return GameObjectType.Special_Archmage_Teleport;
            case "specab_ga": return GameObjectType.Special_MasterOfSteel_Whirlwind;
            case "specab_gb": return GameObjectType.Special_Druid_Wolf;
            case "specab_ha": return GameObjectType.Special_Thief_Steal;
            case "specab_ic": return GameObjectType.Special_MasterOfSteel_Suicide;
            case "specab_ma": return GameObjectType.Special_Necromancer_Flyer;
            case "specab_mb": return GameObjectType.Special_Necromancer_Resurrection;
            case "specab_mc": return GameObjectType.Special_Necromancer_Zombie;
            case "specab_na": return GameObjectType.Special_Bandier_Flag;
            case "specab_nb": return GameObjectType.Special_Bandier_DispelControl;
            case "specab_nc": return GameObjectType.Special_Bandier_Fortify;
            case "st_key2": return GameObjectType.Key;
            case "st_map": return GameObjectType.Map;
            case "st_string": return GameObjectType.Ear_String;
            case "vn_crystq": return GameObjectType.Crystal;
            case "wp_arbalest1": return GameObjectType.Crossbow;
            case "wp_arbalest1f": return GameObjectType.Crossbow_Quest;
            case "wp_axe1": return GameObjectType.Axe;
            case "wp_axe1f": return GameObjectType.Axe_Quest;
            case "wp_sword1": return GameObjectType.Sword;
            case "wp_sword1f": return GameObjectType.Sword_Quest;
            case "wp_sword1u": return GameObjectType.Sword_Unique;
            case "x2_degree": return GameObjectType.X2_Degree;
            case "x2_tit_degr": return GameObjectType.X2_Both;
            case "x2_titul": return GameObjectType.X2_Title;
            case "Z": return GameObjectType.Pref_Quest;
            case "st_loot": return GameObjectType.Ear;
            case "item_bead": return GameObjectType.Bead;
            case "packet": return GameObjectType.Packet;
            default:
                Console.WriteLine($"Unknown GameObjectType: {sphName}");
                return GameObjectType.Unknown;
        }
    }

    public static readonly SuffixToLocaleMap SuffixesAmuletBracelet = new ()
    {
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(64, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Radiance] =
            new SuffixValueWithLocale(1090, new Dictionary<Locale, string> { [Locale.Russian] = "сияния" }),
        [ItemSuffix.Absorption] =
            new SuffixValueWithLocale(72, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(1098, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" }),
        [ItemSuffix.Deflection] =
            new SuffixValueWithLocale(80, new Dictionary<Locale, string> { [Locale.Russian] = "отклонения" }),
        [ItemSuffix.Damage] =
            new SuffixValueWithLocale(1106, new Dictionary<Locale, string> { [Locale.Russian] = "урона" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(88, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(96, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(104, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Precision] =
            new SuffixValueWithLocale(112, new Dictionary<Locale, string> { [Locale.Russian] = "точности" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(120, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" })
    };

    public static readonly SuffixToLocaleMap SuffixesSwordAxes = new ()
    {
        [ItemSuffix.Exhaustion] =
            new SuffixValueWithLocale(64, new Dictionary<Locale, string> { [Locale.Russian] = "истощения" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(1090, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Valor] =
            new SuffixValueWithLocale(72, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Fatigue] =
            new SuffixValueWithLocale(1098, new Dictionary<Locale, string> { [Locale.Russian] = "усталости" }),
        [ItemSuffix.Damage] =
            new SuffixValueWithLocale(80, new Dictionary<Locale, string> { [Locale.Russian] = "урона" }),
        [ItemSuffix.Disease] =
            new SuffixValueWithLocale(1106, new Dictionary<Locale, string> { [Locale.Russian] = "болезни" }),
        [ItemSuffix.Cruelty] =
            new SuffixValueWithLocale(88, new Dictionary<Locale, string> { [Locale.Russian] = "жестокости" }),
        [ItemSuffix.Instability] =
            new SuffixValueWithLocale(1114, new Dictionary<Locale, string> { [Locale.Russian] = "неустойчивости" }),
        [ItemSuffix.Haste] =
            new SuffixValueWithLocale(96, new Dictionary<Locale, string> { [Locale.Russian] = "спешки" }),
        [ItemSuffix.Range] =
            new SuffixValueWithLocale(1122, new Dictionary<Locale, string> { [Locale.Russian] = "расстояния" }),
        [ItemSuffix.Speed] =
            new SuffixValueWithLocale(104, new Dictionary<Locale, string> { [Locale.Russian] = "скорости" }),
        [ItemSuffix.Distance] =
            new SuffixValueWithLocale(1130, new Dictionary<Locale, string> { [Locale.Russian] = "дистанции" }),
        [ItemSuffix.Disorder] =
            new SuffixValueWithLocale(112, new Dictionary<Locale, string> { [Locale.Russian] = "беспорядка" }),
        [ItemSuffix.Decay] =
            new SuffixValueWithLocale(1138, new Dictionary<Locale, string> { [Locale.Russian] = "разложения" }),
        [ItemSuffix.Chaos] =
            new SuffixValueWithLocale(120, new Dictionary<Locale, string> { [Locale.Russian] = "хаоса" }),
        [ItemSuffix.Devastation] =
            new SuffixValueWithLocale(1146, new Dictionary<Locale, string> { [Locale.Russian] = "опустошения" }),
        // [ItemSuffix.Exhaustion] =
        //     new SuffixValueWithLocale(128, new Dictionary<Locale, string> { [Locale.Russian] = "истощения" }),
        [ItemSuffix.Weakness] =
            new SuffixValueWithLocale(1154, new Dictionary<Locale, string> { [Locale.Russian] = "слабости" }),
        // [ItemSuffix.Valor] =
        //     new SuffixValueWithLocale(136, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Penetration] =
            new SuffixValueWithLocale(1162, new Dictionary<Locale, string> { [Locale.Russian] = "проникновения" }),
        // [ItemSuffix.Damage] =
        //     new SuffixValueWithLocale(144, new Dictionary<Locale, string> { [Locale.Russian] = "урона" }),
        [ItemSuffix.Interdict] =
            new SuffixValueWithLocale(1170, new Dictionary<Locale, string> { [Locale.Russian] = "эапрета" }),
        // [ItemSuffix.Cruelty] =
        //     new SuffixValueWithLocale(152, new Dictionary<Locale, string> { [Locale.Russian] = "жестокости" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(1178, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" })
    };

    public static readonly SuffixToLocaleMap SuffixesCrossbows = new ()
    {
        [ItemSuffix.Exhaustion] =
            new SuffixValueWithLocale(64, new Dictionary<Locale, string> { [Locale.Russian] = "истощения" }),
        [ItemSuffix.Penetration] =
            new SuffixValueWithLocale(1090, new Dictionary<Locale, string> { [Locale.Russian] = "проникновения" }),
        [ItemSuffix.Valor] =
            new SuffixValueWithLocale(72, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Instability] =
            new SuffixValueWithLocale(1098, new Dictionary<Locale, string> { [Locale.Russian] = "неустойчивости" }),
        [ItemSuffix.Damage] =
            new SuffixValueWithLocale(80, new Dictionary<Locale, string> { [Locale.Russian] = "урона" }),
        [ItemSuffix.Decay] =
            new SuffixValueWithLocale(1106, new Dictionary<Locale, string> { [Locale.Russian] = "разложения" }),
        [ItemSuffix.Cruelty] =
            new SuffixValueWithLocale(88, new Dictionary<Locale, string> { [Locale.Russian] = "жестокости" }),
        [ItemSuffix.Range] =
            new SuffixValueWithLocale(1114, new Dictionary<Locale, string> { [Locale.Russian] = "расстояния" }),
        [ItemSuffix.Haste] =
            new SuffixValueWithLocale(96, new Dictionary<Locale, string> { [Locale.Russian] = "спешки" }),
        [ItemSuffix.Distance] =
            new SuffixValueWithLocale(1122, new Dictionary<Locale, string> { [Locale.Russian] = "дистанции" }),
        [ItemSuffix.Speed] =
            new SuffixValueWithLocale(104, new Dictionary<Locale, string> { [Locale.Russian] = "скорости" }),
        [ItemSuffix.Mastery] =
            new SuffixValueWithLocale(1130, new Dictionary<Locale, string> { [Locale.Russian] = "мастерства" }),
        [ItemSuffix.Disorder] =
            new SuffixValueWithLocale(112, new Dictionary<Locale, string> { [Locale.Russian] = "беспорядка" }),
        [ItemSuffix.Fatigue] =
            new SuffixValueWithLocale(1138, new Dictionary<Locale, string> { [Locale.Russian] = "усталости" }),
        [ItemSuffix.Chaos] =
            new SuffixValueWithLocale(120, new Dictionary<Locale, string> { [Locale.Russian] = "хаоса" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(1146, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        // [ItemSuffix.Exhaustion] =
        //     new SuffixValueWithLocale(128, new Dictionary<Locale, string> { [Locale.Russian] = "истощения" }),
        [ItemSuffix.Radiance] =
            new SuffixValueWithLocale(1154, new Dictionary<Locale, string> { [Locale.Russian] = "сияния" }),
        // [ItemSuffix.Valor] =
        //     new SuffixValueWithLocale(136, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Disease] =
            new SuffixValueWithLocale(1162, new Dictionary<Locale, string> { [Locale.Russian] = "болезни" }),
        // [ItemSuffix.Damage] =
        //     new SuffixValueWithLocale(144, new Dictionary<Locale, string> { [Locale.Russian] = "урона" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(1170, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" })
        // [ItemSuffix.Cruelty] =
        //     new SuffixValueWithLocale(152, new Dictionary<Locale, string> { [Locale.Russian] = "жестокости" }),
    };

    public static readonly SuffixToLocaleMap SuffixesBootsGlovesBeltsHelmetsPants = new ()
    {
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(64, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Absorption] =
            new SuffixValueWithLocale(72, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(80, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(88, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(96, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Precision] =
            new SuffixValueWithLocale(104, new Dictionary<Locale, string> { [Locale.Russian] = "точности" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(112, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(120, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" })
    };

    public static readonly SuffixToLocaleMap SuffixesChestplatesShields = new ()
    {
        [ItemSuffix.Valor] =
            new SuffixValueWithLocale(64, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(1090, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(72, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Prana] =
            new SuffixValueWithLocale(1098, new Dictionary<Locale, string> { [Locale.Russian] = "праны" }),
        [ItemSuffix.Absorption] =
            new SuffixValueWithLocale(80, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Strength] =
            new SuffixValueWithLocale(1106, new Dictionary<Locale, string> { [Locale.Russian] = "силы" }),
        [ItemSuffix.Deflection] =
            new SuffixValueWithLocale(88, new Dictionary<Locale, string> { [Locale.Russian] = "отклонения" }),
        [ItemSuffix.Agility] =
            new SuffixValueWithLocale(1114, new Dictionary<Locale, string> { [Locale.Russian] = "ловкости" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(96, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        // [ItemSuffix.Majesty] =
        //     new SuffixValueWithLocale(1122, new Dictionary<Locale, string> { [Locale.Russian] = "величия" }),
        [ItemSuffix.Invincibility] =
            new SuffixValueWithLocale(104, new Dictionary<Locale, string> { [Locale.Russian] = "неуязвимости" }),
        [ItemSuffix.Concentration] =
            new SuffixValueWithLocale(1130, new Dictionary<Locale, string> { [Locale.Russian] = "концентрации" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(112, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Earth] =
            new SuffixValueWithLocale(1138, new Dictionary<Locale, string> { [Locale.Russian] = "земли" }),
        [ItemSuffix.Life] =
            new SuffixValueWithLocale(120, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        [ItemSuffix.Water] =
            new SuffixValueWithLocale(1146, new Dictionary<Locale, string> { [Locale.Russian] = "воды" }),
        // [ItemSuffix.Valor] =
        //     new SuffixValueWithLocale(128, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Air] =
            new SuffixValueWithLocale(1154, new Dictionary<Locale, string> { [Locale.Russian] = "воздуха" }),
        // [ItemSuffix.Durability] =
        //     new SuffixValueWithLocale(136, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Fire] =
            new SuffixValueWithLocale(1162, new Dictionary<Locale, string> { [Locale.Russian] = "огня" }),
        // [ItemSuffix.Absorption] =
        //     new SuffixValueWithLocale(144, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Elements] =
            new SuffixValueWithLocale(1170, new Dictionary<Locale, string> { [Locale.Russian] = "стихий" }),
        // [ItemSuffix.Deflection] =
        //     new SuffixValueWithLocale(152, new Dictionary<Locale, string> { [Locale.Russian] = "отклонения" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(1178, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" }),
        // [ItemSuffix.Safety] =
        //     new SuffixValueWithLocale(160, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        // [ItemSuffix.Strength] =
        //     new SuffixValueWithLocale(1186, new Dictionary<Locale, string> { [Locale.Russian] = "силы" }),
        // [ItemSuffix.Invincibility] =
        //     new SuffixValueWithLocale(168, new Dictionary<Locale, string> { [Locale.Russian] = "неуязвимости" }),
        // [ItemSuffix.Agility] =
        //     new SuffixValueWithLocale(1194, new Dictionary<Locale, string> { [Locale.Russian] = "ловкости" }),
        // [ItemSuffix.Health] =
        //     new SuffixValueWithLocale(176, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Majesty] =
            new SuffixValueWithLocale(1202, new Dictionary<Locale, string> { [Locale.Russian] = "величия" }),
        // [ItemSuffix.Life] =
        //     new SuffixValueWithLocale(184, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        // [ItemSuffix.Concentration] =
        //     new SuffixValueWithLocale(1210, new Dictionary<Locale, string> { [Locale.Russian] = "концентрации" }),
        // [ItemSuffix.Valor] =
        //     new SuffixValueWithLocale(192, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Integrity] =
            new SuffixValueWithLocale(1218, new Dictionary<Locale, string> { [Locale.Russian] = "цельности" }),
        [ItemSuffix.IntegrityOther] =
            new SuffixValueWithLocale(1213, new Dictionary<Locale, string> { [Locale.Russian] = "цельности" }),
        // [ItemSuffix.Durability] =
        //     new SuffixValueWithLocale(200, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Elements_New] =
            new SuffixValueWithLocale(1226, new Dictionary<Locale, string> { [Locale.Russian] = "цельности" })
    };

    public static readonly SuffixToLocaleMap SuffixesRobes = new ()
    {
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(64, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" }),
        [ItemSuffix.Earth] =
            new SuffixValueWithLocale(1090, new Dictionary<Locale, string> { [Locale.Russian] = "земли" }),
        [ItemSuffix.Absorption] =
            new SuffixValueWithLocale(72, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Water] =
            new SuffixValueWithLocale(1098, new Dictionary<Locale, string> { [Locale.Russian] = "воды" }),
        [ItemSuffix.Deflection] =
            new SuffixValueWithLocale(80, new Dictionary<Locale, string> { [Locale.Russian] = "отклонения" }),
        [ItemSuffix.Air] =
            new SuffixValueWithLocale(1106, new Dictionary<Locale, string> { [Locale.Russian] = "воздуха" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(88, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        [ItemSuffix.Fire] =
            new SuffixValueWithLocale(1114, new Dictionary<Locale, string> { [Locale.Russian] = "огня" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(96, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(1122, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Life] =
            new SuffixValueWithLocale(104, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        [ItemSuffix.Eclipse] =
            new SuffixValueWithLocale(1130, new Dictionary<Locale, string> { [Locale.Russian] = "затмения" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(112, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Archmage] =
            new SuffixValueWithLocale(1138, new Dictionary<Locale, string> { [Locale.Russian] = "архимага" }),
        // [ItemSuffix.Prana] =
        //     new SuffixValueWithLocale(120, new Dictionary<Locale, string> { [Locale.Russian] = "праны" }),
        // [ItemSuffix.Durability] =
        //     new SuffixValueWithLocale(1146, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        // [ItemSuffix.Value] =
        //     new SuffixValueWithLocale(128, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" }),
        // [ItemSuffix.Deflection] =
        //     new SuffixValueWithLocale(1154, new Dictionary<Locale, string> { [Locale.Russian] = "отклонения" }),
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(136, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        // [ItemSuffix.Safety] =
        //     new SuffixValueWithLocale(1162, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        // [ItemSuffix.Deflection] =
        //     new SuffixValueWithLocale(144, new Dictionary<Locale, string> { [Locale.Russian] = "отклонения" }),
        // [ItemSuffix.Health] =
        //     new SuffixValueWithLocale(1170, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        // [ItemSuffix.Safety] =
        //     new SuffixValueWithLocale(152, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        // [ItemSuffix.Life] =
        //     new SuffixValueWithLocale(1178, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        // [ItemSuffix.Health] =
        //     new SuffixValueWithLocale(160, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        // [ItemSuffix.Meditation] =
        //     new SuffixValueWithLocale(1186, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        // [ItemSuffix.Life] =
        //     new SuffixValueWithLocale(168, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        [ItemSuffix.Prana] =
            new SuffixValueWithLocale(1194, new Dictionary<Locale, string> { [Locale.Russian] = "праны" }),
        // [ItemSuffix.Meditation] =
        //     new SuffixValueWithLocale(176, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        // [ItemSuffix.Ether] =
        //     new SuffixValueWithLocale(1202, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        // [ItemSuffix.Prana] =
        //     new SuffixValueWithLocale(184, new Dictionary<Locale, string> { [Locale.Russian] = "праны" }),
        [ItemSuffix.Dragon] =
            new SuffixValueWithLocale(1210, new Dictionary<Locale, string> { [Locale.Russian] = "дракона" }),
        [ItemSuffix.DragonOther] =
            new SuffixValueWithLocale(1214, new Dictionary<Locale, string> { [Locale.Russian] = "дракона" })
    };

    public static readonly SuffixToLocaleMap SuffixesRings = new ()
    {
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(64, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Precision] =
            new SuffixValueWithLocale(1090, new Dictionary<Locale, string> { [Locale.Russian] = "точности" }),
        [ItemSuffix.Absorption] =
            new SuffixValueWithLocale(72, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Strength] =
            new SuffixValueWithLocale(1098, new Dictionary<Locale, string> { [Locale.Russian] = "силы" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(80, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        [ItemSuffix.Agility] =
            new SuffixValueWithLocale(1106, new Dictionary<Locale, string> { [Locale.Russian] = "ловкости" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(88, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Accuracy] =
            new SuffixValueWithLocale(1114, new Dictionary<Locale, string> { [Locale.Russian] = "меткости" }),
        [ItemSuffix.Life] =
            new SuffixValueWithLocale(96, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        [ItemSuffix.Endurance] =
            new SuffixValueWithLocale(1122, new Dictionary<Locale, string> { [Locale.Russian] = "выносливости" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(104, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Earth] =
            new SuffixValueWithLocale(1130, new Dictionary<Locale, string> { [Locale.Russian] = "земли" }),
        [ItemSuffix.Prana] =
            new SuffixValueWithLocale(112, new Dictionary<Locale, string> { [Locale.Russian] = "праны" }),
        [ItemSuffix.Water] =
            new SuffixValueWithLocale(1138, new Dictionary<Locale, string> { [Locale.Russian] = "воды" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(120, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Air] =
            new SuffixValueWithLocale(1146, new Dictionary<Locale, string> { [Locale.Russian] = "воздуха" }),
        // [ItemSuffix.Absorption] =
        //     new SuffixValueWithLocale(128, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Fire] =
            new SuffixValueWithLocale(1154, new Dictionary<Locale, string> { [Locale.Russian] = "огня" }),
        // [ItemSuffix.Durability] =
        //     new SuffixValueWithLocale(136, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(1162, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" })
    };

    // ---------- CORRECT MAPPING (legacy left for compatibility)

    public static readonly SuffixToLocaleMap SuffixesAmuletBraceletActual = new ()
    {
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(0, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Absorption] =
            new SuffixValueWithLocale(1, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Deflection] =
            new SuffixValueWithLocale(2, new Dictionary<Locale, string> { [Locale.Russian] = "отражения" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(3, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(4, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(5, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Precision] =
            new SuffixValueWithLocale(6, new Dictionary<Locale, string> { [Locale.Russian] = "точности" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(7, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Radiance] =
            new SuffixValueWithLocale(8, new Dictionary<Locale, string> { [Locale.Russian] = "сияния" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(9, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" }),
        [ItemSuffix.Damage] =
            new SuffixValueWithLocale(10, new Dictionary<Locale, string> { [Locale.Russian] = "урона" })
    };

    public static readonly SuffixToLocaleMap SuffixesSwordAxesActual = new ()
    {
        [ItemSuffix.Exhaustion] =
            new SuffixValueWithLocale(0, new Dictionary<Locale, string> { [Locale.Russian] = "истощения" }),
        [ItemSuffix.Valor] =
            new SuffixValueWithLocale(1, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Damage] =
            new SuffixValueWithLocale(2, new Dictionary<Locale, string> { [Locale.Russian] = "урона" }),
        [ItemSuffix.Cruelty] =
            new SuffixValueWithLocale(3, new Dictionary<Locale, string> { [Locale.Russian] = "жестокости" }),
        [ItemSuffix.Haste] =
            new SuffixValueWithLocale(4, new Dictionary<Locale, string> { [Locale.Russian] = "спешки" }),
        [ItemSuffix.Speed] =
            new SuffixValueWithLocale(5, new Dictionary<Locale, string> { [Locale.Russian] = "скорости" }),
        [ItemSuffix.Disorder] =
            new SuffixValueWithLocale(6, new Dictionary<Locale, string> { [Locale.Russian] = "беспорядка" }),
        [ItemSuffix.Chaos] =
            new SuffixValueWithLocale(7, new Dictionary<Locale, string> { [Locale.Russian] = "хаоса" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(8, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Fatigue] =
            new SuffixValueWithLocale(9, new Dictionary<Locale, string> { [Locale.Russian] = "усталости" }),
        [ItemSuffix.Disease] =
            new SuffixValueWithLocale(10, new Dictionary<Locale, string> { [Locale.Russian] = "болезни" }),
        [ItemSuffix.Instability] =
            new SuffixValueWithLocale(11, new Dictionary<Locale, string> { [Locale.Russian] = "неустойчивости" }),
        [ItemSuffix.Range] =
            new SuffixValueWithLocale(12, new Dictionary<Locale, string> { [Locale.Russian] = "расстояния" }),
        [ItemSuffix.Distance] =
            new SuffixValueWithLocale(13, new Dictionary<Locale, string> { [Locale.Russian] = "дистанции" }),
        [ItemSuffix.Decay] =
            new SuffixValueWithLocale(14, new Dictionary<Locale, string> { [Locale.Russian] = "разложения" }),
        [ItemSuffix.Devastation] =
            new SuffixValueWithLocale(15, new Dictionary<Locale, string> { [Locale.Russian] = "опустошения" }),
        [ItemSuffix.Weakness] =
            new SuffixValueWithLocale(16, new Dictionary<Locale, string> { [Locale.Russian] = "слабости" }),
        [ItemSuffix.Penetration] =
            new SuffixValueWithLocale(17, new Dictionary<Locale, string> { [Locale.Russian] = "проникновения" }),
        [ItemSuffix.Interdict] =
            new SuffixValueWithLocale(18, new Dictionary<Locale, string> { [Locale.Russian] = "эапрета" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(19, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" })
    };

    public static readonly SuffixToLocaleMap SuffixesCrossbowsActual = new ()
    {
        [ItemSuffix.Exhaustion] =
            new SuffixValueWithLocale(0, new Dictionary<Locale, string> { [Locale.Russian] = "истощения" }),
        [ItemSuffix.Valor] =
            new SuffixValueWithLocale(1, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Damage] =
            new SuffixValueWithLocale(2, new Dictionary<Locale, string> { [Locale.Russian] = "урона" }),
        [ItemSuffix.Cruelty] =
            new SuffixValueWithLocale(3, new Dictionary<Locale, string> { [Locale.Russian] = "жестокости" }),
        [ItemSuffix.Haste] =
            new SuffixValueWithLocale(4, new Dictionary<Locale, string> { [Locale.Russian] = "спешки" }),
        [ItemSuffix.Speed] =
            new SuffixValueWithLocale(5, new Dictionary<Locale, string> { [Locale.Russian] = "скорости" }),
        [ItemSuffix.Disorder] =
            new SuffixValueWithLocale(6, new Dictionary<Locale, string> { [Locale.Russian] = "беспорядка" }),
        [ItemSuffix.Chaos] =
            new SuffixValueWithLocale(7, new Dictionary<Locale, string> { [Locale.Russian] = "хаоса" }),
        [ItemSuffix.Penetration] =
            new SuffixValueWithLocale(8, new Dictionary<Locale, string> { [Locale.Russian] = "проникновения" }),
        [ItemSuffix.Instability] =
            new SuffixValueWithLocale(9, new Dictionary<Locale, string> { [Locale.Russian] = "неустойчивости" }),
        [ItemSuffix.Decay] =
            new SuffixValueWithLocale(10, new Dictionary<Locale, string> { [Locale.Russian] = "разложения" }),
        [ItemSuffix.Range] =
            new SuffixValueWithLocale(11, new Dictionary<Locale, string> { [Locale.Russian] = "расстояния" }),
        [ItemSuffix.Distance] =
            new SuffixValueWithLocale(12, new Dictionary<Locale, string> { [Locale.Russian] = "дистанции" }),
        [ItemSuffix.Mastery] =
            new SuffixValueWithLocale(13, new Dictionary<Locale, string> { [Locale.Russian] = "мастерства" }),
        [ItemSuffix.Fatigue] =
            new SuffixValueWithLocale(14, new Dictionary<Locale, string> { [Locale.Russian] = "усталости" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(15, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Radiance] =
            new SuffixValueWithLocale(16, new Dictionary<Locale, string> { [Locale.Russian] = "сияния" }),
        [ItemSuffix.Disease] =
            new SuffixValueWithLocale(17, new Dictionary<Locale, string> { [Locale.Russian] = "болезни" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(18, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" })
    };

    public static readonly SuffixToLocaleMap SuffixesBootsGlovesBeltsHelmetsPantsActual = new ()
    {
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(0, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Absorption] =
            new SuffixValueWithLocale(1, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(2, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(3, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(4, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Precision] =
            new SuffixValueWithLocale(5, new Dictionary<Locale, string> { [Locale.Russian] = "точности" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(6, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(7, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" })
    };

    public static readonly SuffixToLocaleMap SuffixesChestplatesShieldsActual = new ()
    {
        [ItemSuffix.Valor] =
            new SuffixValueWithLocale(0, new Dictionary<Locale, string> { [Locale.Russian] = "доблести" }),
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(1, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Absorption] =
            new SuffixValueWithLocale(2, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Deflection] =
            new SuffixValueWithLocale(3, new Dictionary<Locale, string> { [Locale.Russian] = "отклонения" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(4, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        [ItemSuffix.Invincibility] =
            new SuffixValueWithLocale(5, new Dictionary<Locale, string> { [Locale.Russian] = "неуязвимости" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(6, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Life] =
            new SuffixValueWithLocale(7, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(8, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Prana] =
            new SuffixValueWithLocale(9, new Dictionary<Locale, string> { [Locale.Russian] = "праны" }),
        [ItemSuffix.Strength_Old] =
            new SuffixValueWithLocale(10, new Dictionary<Locale, string> { [Locale.Russian] = "силы (ст.)" }),
        [ItemSuffix.Agility_Old] =
            new SuffixValueWithLocale(11, new Dictionary<Locale, string> { [Locale.Russian] = "ловкости (ст.)" }),
        [ItemSuffix.Majesty_Old] =
            new SuffixValueWithLocale(12, new Dictionary<Locale, string> { [Locale.Russian] = "величия (ст.)" }),
        [ItemSuffix.Concentration_Old] =
            new SuffixValueWithLocale(13, new Dictionary<Locale, string> { [Locale.Russian] = "концентрации (ст.)" }),
        [ItemSuffix.Earth] =
            new SuffixValueWithLocale(14, new Dictionary<Locale, string> { [Locale.Russian] = "земли" }),
        [ItemSuffix.Water] =
            new SuffixValueWithLocale(15, new Dictionary<Locale, string> { [Locale.Russian] = "воды" }),
        [ItemSuffix.Air] =
            new SuffixValueWithLocale(16, new Dictionary<Locale, string> { [Locale.Russian] = "воздуха" }),
        [ItemSuffix.Fire] =
            new SuffixValueWithLocale(17, new Dictionary<Locale, string> { [Locale.Russian] = "огня" }),
        [ItemSuffix.Elements_Old] =
            new SuffixValueWithLocale(18, new Dictionary<Locale, string> { [Locale.Russian] = "стихий (ст.)" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(19, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" }),
        [ItemSuffix.Strength] =
            new SuffixValueWithLocale(20, new Dictionary<Locale, string> { [Locale.Russian] = "силы" }),
        [ItemSuffix.Agility] =
            new SuffixValueWithLocale(21, new Dictionary<Locale, string> { [Locale.Russian] = "ловкости" }),
        [ItemSuffix.Majesty] =
            new SuffixValueWithLocale(22, new Dictionary<Locale, string> { [Locale.Russian] = "величия" }),
        [ItemSuffix.Concentration] =
            new SuffixValueWithLocale(23, new Dictionary<Locale, string> { [Locale.Russian] = "концентрации" }),
        [ItemSuffix.Integrity] =
            new SuffixValueWithLocale(24, new Dictionary<Locale, string> { [Locale.Russian] = "цельности" }),
        [ItemSuffix.Elements] =
            new SuffixValueWithLocale(25, new Dictionary<Locale, string> { [Locale.Russian] = "стихий" }),
        [ItemSuffix.Elements_New] =
            new SuffixValueWithLocale(26, new Dictionary<Locale, string> { [Locale.Russian] = "стихий (нов.)" })
    };

    public static readonly SuffixToLocaleMap SuffixesRobesActual = new ()
    {
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(0, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" }),
        [ItemSuffix.Durability_Old] =
            new SuffixValueWithLocale(1, new Dictionary<Locale, string> { [Locale.Russian] = "прочности (ст.)" }),
        [ItemSuffix.Deflection_Old] =
            new SuffixValueWithLocale(2, new Dictionary<Locale, string> { [Locale.Russian] = "отклонения (ст.)" }),
        [ItemSuffix.Safety_Old] =
            new SuffixValueWithLocale(3, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности (ст.)" }),
        [ItemSuffix.Health_Old] =
            new SuffixValueWithLocale(4, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья (ст.)" }),
        [ItemSuffix.Life_Old] =
            new SuffixValueWithLocale(5, new Dictionary<Locale, string> { [Locale.Russian] = "жизни (ст.)" }),
        [ItemSuffix.Meditation_Old] =
            new SuffixValueWithLocale(6, new Dictionary<Locale, string> { [Locale.Russian] = "медитации (ст.)" }),
        [ItemSuffix.Prana_Old] =
            new SuffixValueWithLocale(7, new Dictionary<Locale, string> { [Locale.Russian] = "праны (ст.)" }),
        [ItemSuffix.Earth] =
            new SuffixValueWithLocale(8, new Dictionary<Locale, string> { [Locale.Russian] = "земли" }),
        [ItemSuffix.Water] =
            new SuffixValueWithLocale(9, new Dictionary<Locale, string> { [Locale.Russian] = "воды" }),
        [ItemSuffix.Air] =
            new SuffixValueWithLocale(10, new Dictionary<Locale, string> { [Locale.Russian] = "воздуха" }),
        [ItemSuffix.Fire] =
            new SuffixValueWithLocale(11, new Dictionary<Locale, string> { [Locale.Russian] = "огня" }),
        [ItemSuffix.Ether_Old] =
            new SuffixValueWithLocale(12, new Dictionary<Locale, string> { [Locale.Russian] = "эфира (ст.)" }),
        [ItemSuffix.Eclipse] =
            new SuffixValueWithLocale(13, new Dictionary<Locale, string> { [Locale.Russian] = "затмения" }),
        [ItemSuffix.Archmage] =
            new SuffixValueWithLocale(14, new Dictionary<Locale, string> { [Locale.Russian] = "архимага" }),
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(15, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Deflection] =
            new SuffixValueWithLocale(16, new Dictionary<Locale, string> { [Locale.Russian] = "отклонения" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(17, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(18, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Life] =
            new SuffixValueWithLocale(19, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(20, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Prana] =
            new SuffixValueWithLocale(21, new Dictionary<Locale, string> { [Locale.Russian] = "праны" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(22, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Dragon] =
            new SuffixValueWithLocale(23, new Dictionary<Locale, string> { [Locale.Russian] = "дракона" })
    };

    public static readonly SuffixToLocaleMap SuffixesRingsActual = new ()
    {
        [ItemSuffix.Durability] =
            new SuffixValueWithLocale(0, new Dictionary<Locale, string> { [Locale.Russian] = "прочности" }),
        [ItemSuffix.Absorption] =
            new SuffixValueWithLocale(1, new Dictionary<Locale, string> { [Locale.Russian] = "поглощения" }),
        [ItemSuffix.Safety] =
            new SuffixValueWithLocale(2, new Dictionary<Locale, string> { [Locale.Russian] = "безопасности" }),
        [ItemSuffix.Health] =
            new SuffixValueWithLocale(3, new Dictionary<Locale, string> { [Locale.Russian] = "здоровья" }),
        [ItemSuffix.Life] =
            new SuffixValueWithLocale(4, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        [ItemSuffix.Meditation] =
            new SuffixValueWithLocale(5, new Dictionary<Locale, string> { [Locale.Russian] = "медитации" }),
        [ItemSuffix.Prana] =
            new SuffixValueWithLocale(6, new Dictionary<Locale, string> { [Locale.Russian] = "праны" }),
        [ItemSuffix.Ether] =
            new SuffixValueWithLocale(7, new Dictionary<Locale, string> { [Locale.Russian] = "эфира" }),
        [ItemSuffix.Precision] =
            new SuffixValueWithLocale(8, new Dictionary<Locale, string> { [Locale.Russian] = "точности" }),
        [ItemSuffix.Strength] =
            new SuffixValueWithLocale(9, new Dictionary<Locale, string> { [Locale.Russian] = "силы" }),
        [ItemSuffix.Agility] =
            new SuffixValueWithLocale(10, new Dictionary<Locale, string> { [Locale.Russian] = "ловкости" }),
        [ItemSuffix.Accuracy] =
            new SuffixValueWithLocale(11, new Dictionary<Locale, string> { [Locale.Russian] = "меткости" }),
        [ItemSuffix.Endurance] =
            new SuffixValueWithLocale(12, new Dictionary<Locale, string> { [Locale.Russian] = "выносливости" }),
        [ItemSuffix.Earth] =
            new SuffixValueWithLocale(13, new Dictionary<Locale, string> { [Locale.Russian] = "земли" }),
        [ItemSuffix.Water] =
            new SuffixValueWithLocale(14, new Dictionary<Locale, string> { [Locale.Russian] = "воды" }),
        [ItemSuffix.Air] =
            new SuffixValueWithLocale(15, new Dictionary<Locale, string> { [Locale.Russian] = "воздуха" }),
        [ItemSuffix.Fire] =
            new SuffixValueWithLocale(16, new Dictionary<Locale, string> { [Locale.Russian] = "огня" }),
        [ItemSuffix.Value] =
            new SuffixValueWithLocale(17, new Dictionary<Locale, string> { [Locale.Russian] = "ценности" })
    };

    public static readonly SuffixToLocaleMap SuffixesQuestActual = new ()
    {
        [ItemSuffix.Adventure] =
            new SuffixValueWithLocale(0, new Dictionary<Locale, string> { [Locale.Russian] = "Приключения" }),
        [ItemSuffix.Silence] =
            new SuffixValueWithLocale(1, new Dictionary<Locale, string> { [Locale.Russian] = "Безмолвия" }),
        [ItemSuffix.Prophecy] =
            new SuffixValueWithLocale(2, new Dictionary<Locale, string> { [Locale.Russian] = "Пророчества" }),
        [ItemSuffix.Secret] =
            new SuffixValueWithLocale(3, new Dictionary<Locale, string> { [Locale.Russian] = "Тайны" }),
        [ItemSuffix.Myth] =
            new SuffixValueWithLocale(4, new Dictionary<Locale, string> { [Locale.Russian] = "Мифов" }),
        [ItemSuffix.Being] =
            new SuffixValueWithLocale(5, new Dictionary<Locale, string> { [Locale.Russian] = "Бытия" }),
        [ItemSuffix.Hike] =
            new SuffixValueWithLocale(6, new Dictionary<Locale, string> { [Locale.Russian] = "Походов" }),
        [ItemSuffix.Existence] =
            new SuffixValueWithLocale(7, new Dictionary<Locale, string> { [Locale.Russian] = "Существования" }),
        [ItemSuffix.Legend] =
            new SuffixValueWithLocale(8, new Dictionary<Locale, string> { [Locale.Russian] = "Легенды" }),
        [ItemSuffix.Peace] =
            new SuffixValueWithLocale(9, new Dictionary<Locale, string> { [Locale.Russian] = "Мира" })
    };

    public static readonly SuffixToLocaleMap SuffixesCastleActual = new ()
    {
        [ItemSuffix.Eradication_Old] =
            new SuffixValueWithLocale(0, new Dictionary<Locale, string> { [Locale.Russian] = "искоренения (ст.)" }),
        [ItemSuffix.Devastation_Castle_Old] =
            new SuffixValueWithLocale(1, new Dictionary<Locale, string> { [Locale.Russian] = "опустошения (ст.)" }),
        [ItemSuffix.Reliability_Old] =
            new SuffixValueWithLocale(2, new Dictionary<Locale, string> { [Locale.Russian] = "надёжности (ст.)" }),
        [ItemSuffix.Invincibility_Castle_Old] =
            new SuffixValueWithLocale(3, new Dictionary<Locale, string> { [Locale.Russian] = "неуязвимости (ст.)" }),
        [ItemSuffix.Life_Castle_Old] =
            new SuffixValueWithLocale(4, new Dictionary<Locale, string> { [Locale.Russian] = "жизни (ст.)" }),
        [ItemSuffix.Eradication] =
            new SuffixValueWithLocale(5, new Dictionary<Locale, string> { [Locale.Russian] = "искоренения" }),
        [ItemSuffix.Devastation_Castle] =
            new SuffixValueWithLocale(6, new Dictionary<Locale, string> { [Locale.Russian] = "опустошения" }),
        [ItemSuffix.Reliability] =
            new SuffixValueWithLocale(7, new Dictionary<Locale, string> { [Locale.Russian] = "надёжности" }),
        [ItemSuffix.Invincibility_Castle] =
            new SuffixValueWithLocale(8, new Dictionary<Locale, string> { [Locale.Russian] = "неуязвимости" }),
        [ItemSuffix.Life_Castle] =
            new SuffixValueWithLocale(9, new Dictionary<Locale, string> { [Locale.Russian] = "жизни" }),
        [ItemSuffix.Rule] =
            new SuffixValueWithLocale(10, new Dictionary<Locale, string> { [Locale.Russian] = "правления" }),
        [ItemSuffix.Blinding] =
            new SuffixValueWithLocale(11, new Dictionary<Locale, string> { [Locale.Russian] = "ослепления" }),
        [ItemSuffix.Fright] =
            new SuffixValueWithLocale(12, new Dictionary<Locale, string> { [Locale.Russian] = "испуга" }),
        [ItemSuffix.Halt] =
            new SuffixValueWithLocale(13, new Dictionary<Locale, string> { [Locale.Russian] = "остановки" }),
        [ItemSuffix.Deliverance] =
            new SuffixValueWithLocale(14, new Dictionary<Locale, string> { [Locale.Russian] = "избавления" }),
        [ItemSuffix.Purification] =
            new SuffixValueWithLocale(15, new Dictionary<Locale, string> { [Locale.Russian] = "очищения" }),
        [ItemSuffix.Punishment] =
            new SuffixValueWithLocale(16, new Dictionary<Locale, string> { [Locale.Russian] = "наказания" }),
        [ItemSuffix.Shackle] =
            new SuffixValueWithLocale(17, new Dictionary<Locale, string> { [Locale.Russian] = "оков" }),
        [ItemSuffix.Whirl] =
            new SuffixValueWithLocale(18, new Dictionary<Locale, string> { [Locale.Russian] = "вихря" }),
        [ItemSuffix.Curse] =
            new SuffixValueWithLocale(19, new Dictionary<Locale, string> { [Locale.Russian] = "проклятия" })
    };

    public static readonly Dictionary<GameObjectType, SuffixToLocaleMap> ObjectTypeToSuffixLocaleMap = new ()
    {
        [GameObjectType.Amulet] = SuffixesAmuletBracelet,
        [GameObjectType.Bracelet] = SuffixesAmuletBracelet,
        [GameObjectType.Sword] = SuffixesSwordAxes,
        [GameObjectType.Axe] = SuffixesSwordAxes,
        [GameObjectType.Crossbow] = SuffixesCrossbows,
        [GameObjectType.Boots] = SuffixesBootsGlovesBeltsHelmetsPants,
        [GameObjectType.Gloves] = SuffixesBootsGlovesBeltsHelmetsPants,
        [GameObjectType.Belt] = SuffixesBootsGlovesBeltsHelmetsPants,
        [GameObjectType.Helmet] = SuffixesBootsGlovesBeltsHelmetsPants,
        [GameObjectType.Pants] = SuffixesBootsGlovesBeltsHelmetsPants,
        [GameObjectType.Chestplate] = SuffixesChestplatesShields,
        [GameObjectType.Shield] = SuffixesChestplatesShields,
        [GameObjectType.Robe] = SuffixesRobes,
        [GameObjectType.Ring] = SuffixesRings
    };

    public static readonly Dictionary<GameObjectType, SuffixToLocaleMap> ObjectTypeToSuffixLocaleMapActual = new ()
    {
        [GameObjectType.Amulet] = SuffixesAmuletBraceletActual,
        [GameObjectType.Bracelet] = SuffixesAmuletBraceletActual,
        [GameObjectType.Sword] = SuffixesSwordAxesActual,
        [GameObjectType.Axe] = SuffixesSwordAxesActual,
        [GameObjectType.Crossbow] = SuffixesCrossbowsActual,
        [GameObjectType.Boots] = SuffixesBootsGlovesBeltsHelmetsPantsActual,
        [GameObjectType.Gloves] = SuffixesBootsGlovesBeltsHelmetsPantsActual,
        [GameObjectType.Belt] = SuffixesBootsGlovesBeltsHelmetsPantsActual,
        [GameObjectType.Helmet] = SuffixesBootsGlovesBeltsHelmetsPantsActual,
        [GameObjectType.Pants] = SuffixesBootsGlovesBeltsHelmetsPantsActual,
        [GameObjectType.Chestplate] = SuffixesChestplatesShieldsActual,
        [GameObjectType.Shield] = SuffixesChestplatesShieldsActual,
        [GameObjectType.Robe] = SuffixesRobesActual,
        [GameObjectType.Ring] = SuffixesRingsActual,
        [GameObjectType.Axe_Quest] = SuffixesQuestActual,
        [GameObjectType.Belt_Quest] = SuffixesQuestActual,
        [GameObjectType.Boots_Quest] = SuffixesQuestActual,
        [GameObjectType.Chestplate_Quest] = SuffixesQuestActual,
        [GameObjectType.Crossbow_Quest] = SuffixesQuestActual,
        [GameObjectType.Gloves_Quest] = SuffixesQuestActual,
        [GameObjectType.Helmet_Quest] = SuffixesQuestActual,
        [GameObjectType.Pants_Quest] = SuffixesQuestActual,
        [GameObjectType.Robe_Quest] = SuffixesQuestActual,
        [GameObjectType.Shield_Quest] = SuffixesQuestActual,
        [GameObjectType.Sword_Quest] = SuffixesQuestActual,
        [GameObjectType.Amulet_Unique] = SuffixesCastleActual,
        [GameObjectType.Belt_Unique] = SuffixesCastleActual,
        [GameObjectType.Boots_Unique] = SuffixesCastleActual,
        [GameObjectType.Bracelet_Unique] = SuffixesCastleActual,
        [GameObjectType.Chestplate_Unique] = SuffixesCastleActual,
        [GameObjectType.Gloves_Unique] = SuffixesCastleActual,
        [GameObjectType.Helmet_Unique] = SuffixesCastleActual,
        [GameObjectType.Pants_Unique] = SuffixesCastleActual,
        [GameObjectType.Ring_Unique] = SuffixesCastleActual,
        [GameObjectType.Robe_Unique] = SuffixesCastleActual,
        [GameObjectType.Shield_Unique] = SuffixesCastleActual,
        [GameObjectType.Sword_Unique] = SuffixesCastleActual
    };

    public static ItemSuffix GetSuffixById (this SuffixToLocaleMap map, int val)
    {
        return map.All(x => x.Value.value != val)
            ? ItemSuffix.None
            : map.First(x => x.Value.value == val).Key;
    }

    public static string ToRomanTierLiteral (this SphGameObject gameObject)
    {
        if (!gameObject.IsTierVisible())
        {
            return string.Empty;
        }

        return gameObject.Tier switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
            7 => "VII",
            8 => "VIII",
            9 => "IX",
            10 => "X",
            11 => "XI",
            12 => "XII",
            13 => "XIII",
            14 => "XIV",
            15 => "XV",
            _ => string.Empty
        };
    }
}