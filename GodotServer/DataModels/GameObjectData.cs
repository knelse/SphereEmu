using System;
using System.Collections.Generic;
using System.Linq;
using static SphServer.Helpers.BitHelper;

namespace SphServer.DataModels;

public enum GameObjectKind
{
    Alchemy,
    Crossbow,
    Crossbow_New,
    Armor,
    Armor_New,
    Armor_Old, // "Old" robes only
    Axe,
    Axe_New,
    Powder,
    Guild,
    Magical,
    Magical_New,
    MantraBlack,
    MantraWhite,
    Map,
    Monster,
    Quest,
    Sword,
    Sword_New,
    Unique,
    Pref,
    Unknown
}

public enum GameObjectType
{
    Pref_A,
    Pref_B,
    Pref_C,
    Pref_D,
    Pref_E,
    Pref_F,
    Pref_G,
    Pref_H,
    Pref_I,
    Pref_Z,
    Flower,
    Metal,
    Mineral,
    Amulet,
    Amulet_Unique, // Castle?
    Armor,
    Robe, // armor2 is always robe
    Robe_Quest,
    Robe_Unique, // Castle?
    Armor_Quest,
    Armor_Unique, // Castle?
    Belt,
    Belt_Quest,
    Belt_Unique, // Castle?
    Bracelet,
    Bracelet_Unique, // Castle?
    Gloves,
    Gloves_Quest,
    Gloves_Unique, // Castle?
    Helmet,
    Helmet_Premium,
    Helmet_Quest,
    Helmet_Unique, // Castle?
    Pants,
    Pants_Quest,
    Pants_Unique, // Castle?
    Ring,
    Ring_Special,
    Ring_Unique, // Castle?
    Shield,
    Shield_Quest,
    Shield_Unique, // Castle?
    Shoes,
    Shoes_Quest,
    Shoes_Unique, // Castle?
    Castle_Crystal,
    Castle_Stone,
    Guild_Bag,
    Flag,
    Guild,
    Letter,
    Lottery,
    MantraBlack,
    MantraWhite,
    Monster,
    Monster_Castle_Stone,
    Monster_Event,
    Monster_Event_Flying,
    Monster_Flying,
    Monster_Tower_Spirit, //invisible, empty model
    Monster_Castle_Spirit,
    Elixir_Castle, 
    Elixir_Trap,
    Powder, // fb02
    Powder_Area, //fb03
    Powder_Event, //fb04
    Powder_Guild, //fb05
    Scroll,
    Special,
    Special_BA,
    Special_CA,
    Special_EA,
    Special_GA,
    Special_GB,
    Special_HA,
    Special_IC,
    Special_MA,
    Special_MB,
    Special_MC,
    Special_NA,
    Special_NB,
    Special_NC,
    Key,
    Map,
    Ear_String,
    Crystal, //unbound to castle
    Crossbow,
    Crossbow_Quest,
    Axe,
    Axe_Quest,
    Sword,
    Sword_Quest,
    Sword_Unique, // Castle?
    X2_Degree,
    X2_Both,
    X2_Title,
    Ear,
    Bead,
    Packet,
    Unknown
}

public enum ItemSuffix
{
    None,
    //---- Rings ----//
    Health,
    Ether,
    Accuracy,
    Air,
    Durability,
    Life,
    Precision,
    Endurance,
    Fire,
    Absorption,
    Meditation,
    Strength,
    Earth,
    Value,
    Safety,
    Prana,
    Agility,
    Water,
    //---- Crossbows ----//
    Cruelty,
    Chaos,
    Range,
    // Ether,
    Exhaustion,
    Haste,
    Penetration,
    Distance,
    Radiance,
    Valor,
    Speed,
    Instability,
    Mastery,
    Disease,
    Damage,
    Disorder,
    Decay,
    Fatigue,
    // Value,
    //---- Other weapons ----//
    // Cruelty,
    // Chaos,
    // Instability,
    Devastation,
    // Value,
    // Exhaustion,
    // Haste,
    // Ether,
    // Range,
    Weakness,
    // Valor,
    // Speed,
    // Fatigue,
    // Distance,
    // Penetration,
    // Damage,
    // Disorder,
    // Disease,
    // Decay,
    Interdict,
    //---- Robes ----//
    // Safety,
    // Prana,
    // Fire,
    // Durability,
    // Life,
    Dragon,
    // Value,
    // Health,
    // Earth,
    // Ether,
    Deflection,
    // Meditation,
    // Durability_Old, // These perks have different stat reqs, we'll leave them at their standard / first occurance values
    // Life_Old,
    // Water,
    Eclipse,
    // Safety_Old,
    // Prana_Old,
    // Deflection_Old,
    // Meditation_Old,
    // Air,
    Archmage,
    // Health_Old,
    // Ether_Old,
    //---- Bracelets, Amulets ----//
    // Safety,
    // Ether,
    // Durability,
    // Health,
    // Radiance,
    // Absorption,
    // Meditation,
    // Value,
    // Deflection,
    // Precision,
    // Damage,
    //---- Helmets, Gloves, Belts, Pants, Shoes ----//
    // Health,
    // Value,
    // Durability,
    // Meditation,
    // Absorption,
    // Precision,
    // Safety,
    // Ether,
    //---- Chestplates ----//
    // Deflection,
    // Life,
    // Agility,
    // Water,
    // Value,
    Concentration,
    // Valor,
    // Safety,
    // Meditation,
    Majesty_Old,
    // Air,
    // Strength,
    Integrity,
    // Durability,
    Invincibility,
    // Prana,
    Concentration_Old,
    // Fire,
    // Agility,
    // Absorption,
    // Health,
    // Strength,
    // Earth,
    Elements,
    Majesty,
    //---- Shields ----//
    // Deflection,
    // Life,
    Agility_Old,
    // Water,
    // Value,
    // Concentration,
    // Valor,
    // Safety,
    // Meditation,
    // Majesty_Old,
    // Air,
    // Strength,
    // Integrity,
    // Durability,
    // Invincibility,
    // Prana,
    // Concentration_Old,
    // Fire,
    // Agility,
    // Elements,
    // Absorption,
    // Health,
    Strength_Old,
    // Earth,
    Elements_Old,
    // Majesty,
    Elements_New,
    //---- Quest ----//
    Secret,
    Existence,
    Adventure,
    Myth,
    Legend,
    Silence,
    Being,
    Peace,
    Prophecy,
    Hike,
    //---- Crystal ----//
    // Strength,
    Energy,
    Persistence,
    // Deflection,
    //---- Castle ----// TODO: later
    // Invincibility,
    // Reliability,
    // Blinding,
    // Purification,
    // Curse,
    // Eradication,
    // Life,
    // Invincibility,
    // Fright,
    // Punishment,
    // Devastation,
    // Eradication,
    // Life,
    // Halt,
    // Shackle,
    // Reliability,
    // Devastation,
    // Rule,
    // Deliverance,
    // Whirl
} 

public static class GameObjectDataHelper
{
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
        ItemSuffix.Water,
    };
    public static GameObjectKind GetKindBySphereName(string sphName)
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

    public static GameObjectType GetTypeBySphereName(string sphName)
    {
        switch (sphName)
        {
                case "A": return GameObjectType.Pref_A;
                case "al_flower": return GameObjectType.Flower; 
                case "al_metal": return GameObjectType.Metal;
                case "al_mineral": return GameObjectType.Mineral;
                case "ar_amulet": return GameObjectType.Amulet;
                case "ar_amuletu": return GameObjectType.Amulet_Unique;
                case "ar_armor": return GameObjectType.Armor;
                case "ar_armor2": return GameObjectType.Robe;
                case "ar_armor2f": return GameObjectType.Robe_Quest;
                case "ar_armor2u": return GameObjectType.Robe_Unique;
                case "ar_armorf": return GameObjectType.Armor_Quest;
                case "ar_armoru": return GameObjectType.Armor_Unique;
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
                case "ar_shoes": return GameObjectType.Shoes;
                case "ar_shoesf": return GameObjectType.Shoes_Quest;
                case "ar_shoesu": return GameObjectType.Shoes_Unique;
                case "B": return GameObjectType.Pref_B;
                case "C": return GameObjectType.Pref_C;
                case "crystal": return GameObjectType.Castle_Crystal;
                case "cs_guard": return GameObjectType.Castle_Stone;
                case "ct_bagd": return GameObjectType.Guild_Bag;
                case "D": return GameObjectType.Pref_D;
                case "E": return GameObjectType.Pref_E;
                case "F": return GameObjectType.Pref_F;
                case "flag": return GameObjectType.Flag;
                case "G": return GameObjectType.Pref_G;
                case "guild": return GameObjectType.Guild;
                case "H": return GameObjectType.Pref_H;
                case "I": return GameObjectType.Pref_I;
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
                case "specab_ba": return GameObjectType.Special_BA;
                case "specab_ca": return GameObjectType.Special_CA;
                case "specab_ea": return GameObjectType.Special_EA;
                case "specab_ga": return GameObjectType.Special_GA;
                case "specab_gb": return GameObjectType.Special_GB;
                case "specab_ha": return GameObjectType.Special_HA;
                case "specab_ic": return GameObjectType.Special_IC;
                case "specab_ma": return GameObjectType.Special_MA;
                case "specab_mb": return GameObjectType.Special_MB;
                case "specab_mc": return GameObjectType.Special_MC;
                case "specab_na": return GameObjectType.Special_NA;
                case "specab_nb": return GameObjectType.Special_NB;
                case "specab_nc": return GameObjectType.Special_NC;
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
                case "Z": return GameObjectType.Pref_Z;
                case "st_loot": return GameObjectType.Ear;
                case "item_bead": return GameObjectType.Bead;
                case "packet": return GameObjectType.Packet;
                default:
                    Console.WriteLine($"Unknown GameObjectType: {sphName}");
                    return GameObjectType.Unknown;
        }
    }

    public static GameObjectData GetRandomObjectData(int titleLevelMinusOne, int gameIdOverride = -1)
    {
        GameObjectData item;
        if (gameIdOverride != -1)
        {
            item = MainServer.GameObjectDataDb.First(x => x.Value.GameId == gameIdOverride).Value;
        }
        else
        {
            var tierFilter = Math.Min(titleLevelMinusOne, 74) / 5 + 1;
            var typeFilter = new HashSet<GameObjectType>
            {
                GameObjectType.Flower,
                GameObjectType.Metal,
                GameObjectType.Mineral,
                GameObjectType.Amulet,
                GameObjectType.Armor,
                GameObjectType.Robe,
                GameObjectType.Belt,
                GameObjectType.Bracelet,
                GameObjectType.Gloves,
                GameObjectType.Helmet,
                GameObjectType.Pants,
                GameObjectType.Ring,
                GameObjectType.Shield,
                GameObjectType.Shoes,
                // Flag,
                // Guild,
                GameObjectType.MantraBlack,
                GameObjectType.MantraWhite,
                GameObjectType.Elixir_Castle,
                GameObjectType.Elixir_Trap,
                GameObjectType.Powder,
                GameObjectType.Powder_Area,
                GameObjectType.Crossbow,
                GameObjectType.Axe,
                GameObjectType.Sword,
            };

            var overrideFilter = new HashSet<GameObjectType>
            {
                // GameObjectType.Powder_Area
                // GameObjectType.Ring
            };

            typeFilter = overrideFilter.Count > 0 ? overrideFilter : typeFilter;

            var kindFilter = new HashSet<GameObjectKind>
            {
                GameObjectKind.Alchemy,
                GameObjectKind.Crossbow_New,
                GameObjectKind.Armor_New,
                GameObjectKind.Armor_Old, // "Old" robes only
                GameObjectKind.Axe_New,
                GameObjectKind.Powder,
                GameObjectKind.Magical_New,
                GameObjectKind.MantraBlack,
                GameObjectKind.MantraWhite,
                GameObjectKind.Sword_New,
            };

            var tierAgnosticTypes = new HashSet<GameObjectType>
            {
                GameObjectType.Flower,
                GameObjectType.Metal,
                GameObjectType.Mineral,
            };

            var lootPool = MainServer.GameObjectDataDb
                .Where(x =>
                    kindFilter.Contains(x.Value.ObjectKind) && typeFilter.Contains(x.Value.ObjectType)
                                                            && (x.Value.Tier == tierFilter
                                                                || tierAgnosticTypes.Contains(x.Value.ObjectType)))
                .Select(y => y.Value)
                .ToList();

            var random = MainServer.Rng.RandiRange(0, lootPool.Count - 1);
            item = lootPool.ElementAt(random);
        }

        var noSuffix = false;
        var suffixFilter = new SortedSet<ItemSuffix> { ItemSuffix.None };
            
        if (item.ObjectType is GameObjectType.Ring)
        { 
            suffixFilter = new SortedSet<ItemSuffix>
            {
                // ItemSuffix.Health,
                // ItemSuffix.Accuracy,
                // ItemSuffix.Air,
                // ItemSuffix.Durability,
                // ItemSuffix.Life,
                // ItemSuffix.Endurance,
                // ItemSuffix.Fire,
                // ItemSuffix.Absorption,
                // ItemSuffix.Meditation,
                // ItemSuffix.Strength,
                // ItemSuffix.Earth,
                ItemSuffix.Safety,
                // ItemSuffix.Prana,
                // ItemSuffix.Agility,
                ItemSuffix.Water,
                // ItemSuffix.Value,
                // ItemSuffix.Precision,
                // ItemSuffix.Ether,
            };
            item.Suffix = suffixFilter.ElementAt(MainServer.Rng.RandiRange(0, suffixFilter.Count - 1));
        }

        // Rings should always have a suffix
        if (noSuffix)
        {
            return item;
        }
        if (item.ObjectType is GameObjectType.Sword or GameObjectType.Axe)
        {
            suffixFilter = new SortedSet<ItemSuffix>
            {
                ItemSuffix.Cruelty,
                ItemSuffix.Chaos,
                ItemSuffix.Instability,
                ItemSuffix.Devastation,
                ItemSuffix.Value,
                ItemSuffix.Exhaustion,
                ItemSuffix.Haste,
                ItemSuffix.Ether,
                ItemSuffix.Range,
                ItemSuffix.Weakness,
                ItemSuffix.Valor,
                ItemSuffix.Speed,
                ItemSuffix.Fatigue,
                ItemSuffix.Distance,
                ItemSuffix.Penetration,
                ItemSuffix.Damage,
                ItemSuffix.Disorder,
                ItemSuffix.Disease,
                ItemSuffix.Decay,
                ItemSuffix.Interdict,
            };
        }
        if (item.ObjectType is GameObjectType.Crossbow)
        {
            suffixFilter = new SortedSet<ItemSuffix>
            {
                ItemSuffix.Cruelty,
                ItemSuffix.Chaos,
                ItemSuffix.Instability,
                ItemSuffix.Value,
                ItemSuffix.Exhaustion,
                ItemSuffix.Haste,
                ItemSuffix.Ether,
                ItemSuffix.Range,
                ItemSuffix.Valor,
                ItemSuffix.Speed,
                ItemSuffix.Fatigue,
                ItemSuffix.Distance,
                ItemSuffix.Penetration,
                ItemSuffix.Damage,
                ItemSuffix.Disorder,
                ItemSuffix.Disease,
                ItemSuffix.Decay,
                ItemSuffix.Mastery,
                ItemSuffix.Radiance
            };
        }
        if (item.ObjectType is GameObjectType.Robe)
        {
            suffixFilter = new SortedSet<ItemSuffix>
            {
                ItemSuffix.Safety,
                ItemSuffix.Prana,
                ItemSuffix.Fire,
                ItemSuffix.Durability,
                ItemSuffix.Life,
                ItemSuffix.Dragon,
                ItemSuffix.Value,
                ItemSuffix.Health,
                ItemSuffix.Earth,
                ItemSuffix.Ether,
                ItemSuffix.Deflection,
                ItemSuffix.Meditation,
                ItemSuffix.Water,
                ItemSuffix.Eclipse,
                ItemSuffix.Air,
                ItemSuffix.Archmage,
                // ItemSuffix.Durability_Old,
                // ItemSuffix.Life_Old,
                // ItemSuffix.Safety_Old,
                // ItemSuffix.Prana_Old,
                // ItemSuffix.Deflection_Old,
                // ItemSuffix.Meditation_Old,
                // ItemSuffix.Health_Old,
                // ItemSuffix.Ether_Old,
            };
        }
        if (item.ObjectType is GameObjectType.Bracelet or GameObjectType.Amulet)
        {
            suffixFilter = new SortedSet<ItemSuffix>
            {
                ItemSuffix.Safety,
                ItemSuffix.Ether,
                ItemSuffix.Durability,
                ItemSuffix.Health,
                ItemSuffix.Radiance,
                ItemSuffix.Absorption,
                ItemSuffix.Meditation,
                ItemSuffix.Value,
                ItemSuffix.Deflection,
                ItemSuffix.Precision,
                ItemSuffix.Damage,
            };
        }
        if (item.ObjectType is GameObjectType.Helmet or GameObjectType.Gloves or GameObjectType.Belt 
            or GameObjectType.Pants or GameObjectType.Shoes)
        {
            suffixFilter = new SortedSet<ItemSuffix>
            {
                ItemSuffix.Health,
                ItemSuffix.Value,
                ItemSuffix.Durability,
                ItemSuffix.Meditation,
                ItemSuffix.Absorption,
                ItemSuffix.Precision,
                ItemSuffix.Safety,
                ItemSuffix.Ether,
            };
        }
        if (item.ObjectType is GameObjectType.Armor or GameObjectType.Shield)
        {
            suffixFilter = new SortedSet<ItemSuffix>
            {// for shields Elements is "Old" (e.g. +68 at 12 rank)
                ItemSuffix.Deflection,
                ItemSuffix.Life,
                ItemSuffix.Agility,
                ItemSuffix.Water,
                ItemSuffix.Value,
                ItemSuffix.Concentration,
                ItemSuffix.Valor,
                ItemSuffix.Safety,
                ItemSuffix.Meditation,
                ItemSuffix.Air,
                ItemSuffix.Strength,
                ItemSuffix.Integrity,
                ItemSuffix.Durability,
                ItemSuffix.Invincibility,
                ItemSuffix.Prana,
                ItemSuffix.Fire,
                ItemSuffix.Agility,
                ItemSuffix.Absorption,
                ItemSuffix.Health,
                ItemSuffix.Strength,
                ItemSuffix.Earth,
                ItemSuffix.Elements,
                ItemSuffix.Majesty,
                // ItemSuffix.Concentration_Old,
                // ItemSuffix.Majesty_Old,
                // ItemSuffix.Agility_Old,
                // ItemSuffix.Strength_Old,
                // ItemSuffix.Elements_Old,
                // ItemSuffix.Elements_New,
            };
        }

        if (item.ObjectType is GameObjectType.Powder or GameObjectType.Powder_Area or GameObjectType.Elixir_Castle 
            or GameObjectType.Elixir_Trap)
        {
            item.ItemCount =  MainServer.Rng.RandiRange(3, 19);
        }

        item.Suffix = suffixFilter.ElementAt(MainServer.Rng.RandiRange(0, suffixFilter.Count - 1));
        return item;
    }
}

public class GameObjectData
{
    public GameObjectKind ObjectKind;
    public int GameId;
    public GameObjectType ObjectType;
    public string ModelNameGround = null!;
    public string ModelNameInventory = null!;
    public int HpCost;
    public int MpCost;
    public int TitleMinusOne;
    public int DegreeMinusOne;
    public KarmaTypes MinKarmaLevel;
    public KarmaTypes MaxKarmaLevel;
    public int StrengthReq;
    public int AgilityReq;
    public int AccuracyReq;
    public int EnduranceReq;
    public int EarthReq;
    public int AirReq;
    public int WaterReq;
    public int FireReq;
    public int PAtkNegative;
    public int MAtkNegativeOrHeal;
    public int MPHeal;
    public int t1;
    public int MaxHpUp;
    public int MaxMpUp;
    public int PAtkUpNegative;
    public int PDefUp;
    public int MDefUp;
    public int StrengthUp;
    public int AgilityUp;
    public int AccuracyUp;
    public int EnduranceUp;
    public int EarthUp;
    public int AirUp;
    public int WaterUp;
    public int FireUp;
    public int MAtkUpNegative;
    public int Weight;
    public int Durability;
    public int _range;
    public int UseTime;
    public int VendorCost;
    public int MutatorId;
    public int _duration;
    public int ReuseDelayHours;
    public int t2;
    public int t3;
    public int t4;
    public int t5;
    public string t6 = null!;
    public string t7 = null!;
    public int Tier;
    public int Range;
    public int Radius;
    /// <summary>
    /// Seconds
    /// </summary>
    public int Duration;
    public ItemSuffix Suffix = ItemSuffix.None;
    public int ItemCount = 1;

    public static readonly HashSet<GameObjectType> Mantras = new ()
    {
        GameObjectType.MantraBlack,
        GameObjectType.MantraWhite
    };

    public static readonly HashSet<GameObjectType> MaterialsPowdersElixirs = new()
    {
        GameObjectType.Flower, GameObjectType.Metal, GameObjectType.Mineral, GameObjectType.Powder,
        GameObjectType.Powder_Area, GameObjectType.Elixir_Castle, GameObjectType.Elixir_Trap
    };

    public static readonly HashSet<GameObjectType> WeaponsArmor = new()
    {
        GameObjectType.Crossbow, GameObjectType.Axe, GameObjectType.Sword, GameObjectType.Amulet, GameObjectType.Armor,
        GameObjectType.Belt, GameObjectType.Bracelet, GameObjectType.Gloves, GameObjectType.Helmet,
        GameObjectType.Pants, GameObjectType.Shield, GameObjectType.Shoes, GameObjectType.Robe
    };

    public string ToDebugString()
    {
        var itemCountStr = ItemCount > 1 ? $"Count: {ItemCount}" : "";
        return $"Kind: {Enum.GetName(typeof(GameObjectKind), ObjectKind)} " +
               $"ID: {GameId} Type: {Enum.GetName(typeof(GameObjectType), ObjectType)} Ground: {ModelNameGround} " +
               $"Inv: {ModelNameInventory} HpCost: {HpCost} MpCost: {MpCost} TitleReq: {TitleMinusOne} " +
               $"DegreeReq: {DegreeMinusOne} KarmaMin: {Enum.GetName(typeof(KarmaTypes), MinKarmaLevel)} " +
               $"KarmaMax: {Enum.GetName(typeof(KarmaTypes), MaxKarmaLevel)} StrengthReq: {StrengthReq} " +
               $"AgilityReq: {AgilityReq} AccuracyReq: {AccuracyReq} EnduranceReq: {EnduranceReq} EarthReq: {EarthReq} " +
               $"AirReq: {AirReq} WaterReq: {WaterReq} FireReq: {FireReq} PA: {PAtkNegative} MA: {MAtkNegativeOrHeal} " +
               $"MPHeal: {MPHeal} T1: {t1} MaxHPUp: {MaxHpUp} MaxMpUp: {MaxMpUp} PAUp: {PAtkUpNegative} PDUp: {PDefUp} " +
               $"MDUp: {MDefUp} StrengthUp: {StrengthUp} AgilityUp: {AgilityUp} AccuracyUp: {AccuracyUp} " +
               $"EnduranceUp: {EnduranceUp} EarthUp: {EarthUp} AirUp: {AirUp} WaterUp: {WaterUp} FireUp: {FireUp} " +
               $"MAUp: {MAtkUpNegative} Weight: {Weight} Durability: {Durability} Range: {Range} Radius: {Radius} " +
               $"UseTime: {UseTime} VendorCost: {VendorCost} MutatorId: {MutatorId} Duration: {Duration} " +
               $"ReuseDelayHours: {ReuseDelayHours} T2: {t2} T3: {t3} T4: {t4} T5: {t5} T6: {t6} T7: {t7} Tier: {Tier} " +
               $"Suffix: {Enum.GetName(typeof(ItemSuffix), Suffix)} {itemCountStr}";
    }

    public byte[] GetLootItemBytes(ushort bagId, ushort itemId)
    {
        var objid_1 = (byte) (((GameId & 0b11) << 6) + 0b100110);
        var objid_2 = (byte) ((GameId >> 2) & 0b11111111);
        var objid_3 = (byte) (((GameId >> 10) & 0b1111) + 0b00010000);

        var bagid_1 = (byte) (((bagId) & 0b111) << 5);
        var bagid_2 = (byte) ((bagId >> 3) & 0b11111111);
        var bagid_3 = (byte) ((bagId >> 11) & 0b11111);
        
        Console.WriteLine(ToDebugString());

        if (Mantras.Contains(ObjectType))
        {
            return new byte[] 
            {
                MinorByte(itemId), MajorByte(itemId), (byte) (ObjectType == GameObjectType.MantraBlack ? 0xA4 : 0xA0), 
                0x8F, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, 
                objid_1, objid_2, objid_3, 0x15, 0x60, bagid_1, bagid_2, bagid_3, 0xA0, 0xC0, 0x02, 0x01, 0x00
            };
        }

        if (MaterialsPowdersElixirs.Contains(ObjectType))
        {
            byte typeid_1 = 0;
            byte typeid_2 = 0;

            switch (ObjectType)
            {
                case GameObjectType.Flower:
                    typeid_1 = 0x64;
                    typeid_2 = 0x89;
                    break;
                case GameObjectType.Metal:
                    typeid_1 = 0x68;
                    typeid_2 = 0x89;
                    break;
                case GameObjectType.Mineral:
                    typeid_1 = 0x60;
                    typeid_2 = 0x89;
                    break;
                case GameObjectType.Powder:
                    typeid_1 = 0x14;
                    typeid_2 = 0x87;
                    break;
                case GameObjectType.Powder_Area:
                    typeid_1 = 0x1C;
                    typeid_2 = 0x87;
                    break;
                case GameObjectType.Elixir_Castle:
                case GameObjectType.Elixir_Trap:
                    typeid_1 = 0x60;
                    typeid_2 = 0x87;
                    break;
            }

            return new byte[]
            {
                MinorByte(itemId), MajorByte(itemId), typeid_1, typeid_2, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, objid_1, objid_2, objid_3, 0x15, 0x60, bagid_1,
                bagid_2, bagid_3, 0xA0, 0x90, 0x05, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x05, 0x16,
                (byte) ((ItemCount & 0b11111) << 3), (byte) ((ItemCount >> 5) & 0b11111111), 0x00
            };
        }

        if (WeaponsArmor.Contains(ObjectType))
        {
            
            byte typeid_1 = 0;
            byte typeid_2 = 0;
            byte typeIdMod_1 = 0x15;
            byte typeIdMod_2 = 0x60;
            byte itemSuffixMod = 0x10;
            
            switch (ObjectType)
            {
                case GameObjectType.Crossbow:
                    typeid_1 = 0xD8;
                    typeid_2 = 0x87;
                    break;
                case GameObjectType.Sword:
                    typeid_1 = 0xD0;
                    typeid_2 = 0x87;
                    break;
                case GameObjectType.Axe:
                    typeid_1 = 0xD4;
                    typeid_2 = 0x87;
                    break;
                case GameObjectType.Amulet:
                    typeid_1 = 0xBC;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Armor:
                    typeid_1 = 0xB8;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Bracelet:
                    typeid_1 = 0xDC;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Belt:
                    typeid_1 = 0xCC;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Gloves:
                    typeid_1 = 0xC8;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Helmet:
                    typeid_1 = 0xD4;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Pants:
                    typeid_1 = 0xD8;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Shield:
                    typeid_1 = 0xD0;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Shoes:
                    typeid_1 = 0xC0;
                    typeid_2 = 0x8B;
                    break;
                case GameObjectType.Robe:
                    typeid_1 = 0xE4;
                    typeid_2 = 0x8B;
                    break;
            }

            if (ObjectType is GameObjectType.Sword or GameObjectType.Axe)
            {
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Exhaustion:
                    case ItemSuffix.Ether:
                    case ItemSuffix.Valor:
                    case ItemSuffix.Fatigue:
                        typeIdMod_1 = 0x44;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Damage:
                    case ItemSuffix.Disease:
                    case ItemSuffix.Cruelty:
                    case ItemSuffix.Instability:
                        typeIdMod_1 = 0x45;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Haste:
                    case ItemSuffix.Range:
                    case ItemSuffix.Speed:
                    case ItemSuffix.Distance:
                        typeIdMod_1 = 0x46;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Disorder:
                    case ItemSuffix.Decay:
                    case ItemSuffix.Chaos:
                    case ItemSuffix.Devastation:
                        typeIdMod_1 = 0x47;
                        typeIdMod_2 = 0x01;
                        break;
                    // case ItemSuffix.Exhaustion:
                    case ItemSuffix.Weakness:
                    // case ItemSuffix.Valor:
                    case ItemSuffix.Penetration:
                        typeIdMod_1 = 0x48;
                        typeIdMod_2 = 0x01;
                        break;
                    // case ItemSuffix.Damage:
                    case ItemSuffix.Interdict:
                    // case ItemSuffix.Cruelty:
                    case ItemSuffix.Value:
                        typeIdMod_1 = 0x49;
                        typeIdMod_2 = 0x01;
                        break;
                        
                }
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Exhaustion:
                    case ItemSuffix.Damage:
                    case ItemSuffix.Haste:
                    case ItemSuffix.Disorder:
                        itemSuffixMod = 0x0;
                        break;
                    case ItemSuffix.Ether:
                    case ItemSuffix.Disease:
                    case ItemSuffix.Range:
                    case ItemSuffix.Decay:
                    case ItemSuffix.Weakness:
                    case ItemSuffix.Interdict:
                        itemSuffixMod = 0x20;
                        break;
                    case ItemSuffix.Valor:
                    case ItemSuffix.Cruelty:
                    case ItemSuffix.Speed:
                    case ItemSuffix.Chaos:
                        itemSuffixMod = 0x80;
                        break;
                    case ItemSuffix.Fatigue:
                    case ItemSuffix.Instability:
                    case ItemSuffix.Distance:
                    case ItemSuffix.Devastation:
                    case ItemSuffix.Penetration:
                    case ItemSuffix.Value:
                        itemSuffixMod = 0xA0;
                        break;
                        
                }
            }
            if (ObjectType is GameObjectType.Crossbow)
            {
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Exhaustion:
                    case ItemSuffix.Penetration:
                    case ItemSuffix.Valor:
                    case ItemSuffix.Instability:
                        typeIdMod_1 = 0x44;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Damage:
                    case ItemSuffix.Decay:
                    case ItemSuffix.Cruelty:
                    case ItemSuffix.Range:
                        typeIdMod_1 = 0x45;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Haste:
                    case ItemSuffix.Distance:
                    case ItemSuffix.Speed:
                    case ItemSuffix.Mastery:
                        typeIdMod_1 = 0x46;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Disorder:
                    case ItemSuffix.Fatigue:
                    case ItemSuffix.Chaos:
                    case ItemSuffix.Ether:
                        typeIdMod_1 = 0x47;
                        typeIdMod_2 = 0x01;
                        break;
                    // case ItemSuffix.Exhaustion:
                    case ItemSuffix.Radiance:
                    // case ItemSuffix.Valor:
                    case ItemSuffix.Disease:
                        typeIdMod_1 = 0x48;
                        typeIdMod_2 = 0x01;
                        break;
                    // case ItemSuffix.Damage:
                    case ItemSuffix.Value:
                    // case ItemSuffix.Cruelty:
                        typeIdMod_1 = 0x49;
                        typeIdMod_2 = 0x01;
                        break;
                        
                }
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Exhaustion:
                    case ItemSuffix.Damage:
                    case ItemSuffix.Haste:
                    case ItemSuffix.Disorder:
                        itemSuffixMod = 0x0;
                        break;
                    case ItemSuffix.Penetration:
                    case ItemSuffix.Decay:
                    case ItemSuffix.Distance:
                    case ItemSuffix.Fatigue:
                    case ItemSuffix.Radiance:
                    case ItemSuffix.Value:
                        itemSuffixMod = 0x20;
                        break;
                    case ItemSuffix.Valor:
                    case ItemSuffix.Cruelty:
                    case ItemSuffix.Speed:
                    case ItemSuffix.Chaos:
                        itemSuffixMod = 0x80;
                        break;
                    case ItemSuffix.Instability:
                    case ItemSuffix.Range:
                    case ItemSuffix.Mastery:
                    case ItemSuffix.Ether:
                    case ItemSuffix.Disease:
                        itemSuffixMod = 0xA0;
                        break;
                        
                }
            }
            if (ObjectType is GameObjectType.Robe)
            {
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Value:
                    case ItemSuffix.Earth:
                    case ItemSuffix.Durability:
                    case ItemSuffix.Water:
                        typeIdMod_1 = 0x44;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Deflection:
                    case ItemSuffix.Air:
                    case ItemSuffix.Safety:
                    case ItemSuffix.Fire:
                        typeIdMod_1 = 0x45;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Health:
                    case ItemSuffix.Ether:
                    case ItemSuffix.Life:
                    case ItemSuffix.Eclipse:
                        typeIdMod_1 = 0x46;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Meditation:
                    case ItemSuffix.Archmage:
                    case ItemSuffix.Prana:
                    // case ItemSuffix.Durability:
                        typeIdMod_1 = 0x47;
                        typeIdMod_2 = 0x01;
                        break;
                    // TODO: actually, these are different perks with different stat reqs
                    // case ItemSuffix.Value:
                    // case ItemSuffix.Deflection:
                    // case ItemSuffix.Durability:
                    // case ItemSuffix.Safety:
                    //     typeIdMod_1 = 0x48;
                    //     typeIdMod_2 = 0x01;
                    //     break;
                    // case ItemSuffix.Deflection:
                    // case ItemSuffix.Health:
                    // case ItemSuffix.Safety:
                    // case ItemSuffix.Life:
                    //     typeIdMod_1 = 0x49;
                    //     typeIdMod_2 = 0x01;
                    //     break;
                    // case ItemSuffix.Health:
                    // case ItemSuffix.Meditation:
                    // case ItemSuffix.Life:
                    // case ItemSuffix.Prana:
                    //     typeIdMod_1 = 0x4A;
                    //     typeIdMod_2 = 0x01;
                    //     break;
                    // case ItemSuffix.Meditation:
                    // case ItemSuffix.Ether:
                    // case ItemSuffix.Prana:
                    case ItemSuffix.Dragon:
                        typeIdMod_1 = 0x4B;
                        typeIdMod_2 = 0x01;
                        break;
                }
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Value:
                    case ItemSuffix.Deflection:
                    case ItemSuffix.Health:
                    case ItemSuffix.Meditation:
                    // case ItemSuffix.Value:
                    // case ItemSuffix.Deflection:
                    // case ItemSuffix.Health:
                    // case ItemSuffix.Meditation:
                        itemSuffixMod = 0x0;
                        break;
                    case ItemSuffix.Earth:
                    case ItemSuffix.Air:
                    case ItemSuffix.Ether:
                    case ItemSuffix.Archmage:
                    // case ItemSuffix.Deflection:
                    // case ItemSuffix.Health:
                    // case ItemSuffix.Meditation:
                    // case ItemSuffix.Ether:
                        itemSuffixMod = 0x20;
                        break;
                    case ItemSuffix.Durability:
                    case ItemSuffix.Safety:
                    case ItemSuffix.Life:
                    case ItemSuffix.Prana:
                    // case ItemSuffix.Durability:
                    // case ItemSuffix.Safety:
                    // case ItemSuffix.Life:
                    // case ItemSuffix.Prana:
                        itemSuffixMod = 0x80;
                        break;
                    case ItemSuffix.Water:
                    case ItemSuffix.Fire:
                    case ItemSuffix.Eclipse:
                    // case ItemSuffix.Durability:
                    // case ItemSuffix.Safety:
                    // case ItemSuffix.Life:
                    // case ItemSuffix.Prana:
                    case ItemSuffix.Dragon:
                        itemSuffixMod = 0xA0;
                        break;
                        
                }
            }
            if (ObjectType is GameObjectType.Bracelet or GameObjectType.Amulet)
            {
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Durability:
                    case ItemSuffix.Radiance:
                    case ItemSuffix.Absorption:
                    case ItemSuffix.Value:
                        typeIdMod_1 = 0x44;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Deflection:
                    case ItemSuffix.Damage:
                    case ItemSuffix.Safety:
                        typeIdMod_1 = 0x45;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Health:
                    case ItemSuffix.Meditation:
                        typeIdMod_1 = 0x46;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Precision:
                    case ItemSuffix.Ether:
                        typeIdMod_1 = 0x47;
                        typeIdMod_2 = 0x01;
                        break;
                        
                }
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Durability:
                    case ItemSuffix.Deflection:
                    case ItemSuffix.Health:
                    case ItemSuffix.Precision:
                        itemSuffixMod = 0x0;
                        break;
                    case ItemSuffix.Radiance:
                    case ItemSuffix.Damage:
                        itemSuffixMod = 0x20;
                        break;
                    case ItemSuffix.Absorption:
                    case ItemSuffix.Safety:
                    case ItemSuffix.Meditation:
                    case ItemSuffix.Ether:
                        itemSuffixMod = 0x80;
                        break;
                    case ItemSuffix.Value:
                        itemSuffixMod = 0xA0;
                        break;
                }
            }
            if (ObjectType is GameObjectType.Helmet or GameObjectType.Gloves or GameObjectType.Belt 
                or GameObjectType.Pants or GameObjectType.Shoes)
            {
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Durability:
                    case ItemSuffix.Absorption:
                        typeIdMod_1 = 0x44;
                        typeIdMod_2 = 0x60;
                        break;
                    case ItemSuffix.Safety:
                    case ItemSuffix.Health:
                        typeIdMod_1 = 0x45;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Meditation:
                    case ItemSuffix.Precision:
                        typeIdMod_1 = 0x46;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Ether:
                    case ItemSuffix.Value:
                        typeIdMod_1 = 0x47;
                        typeIdMod_2 = 0x01;
                        break;
                }
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Durability:
                    case ItemSuffix.Safety:
                    case ItemSuffix.Meditation:
                    case ItemSuffix.Ether:
                        itemSuffixMod = 0x0;
                        break;
                    case ItemSuffix.Absorption:
                    case ItemSuffix.Health:
                    case ItemSuffix.Precision:
                    case ItemSuffix.Value:
                        itemSuffixMod = 0x80;
                        break;
                }
            }
            if (ObjectType is GameObjectType.Armor or GameObjectType.Shield)
            {
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Valor:
                    case ItemSuffix.Meditation:
                    case ItemSuffix.Durability:
                    case ItemSuffix.Prana:
                        typeIdMod_1 = 0x44;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Absorption:
                    case ItemSuffix.Strength:
                    case ItemSuffix.Deflection:
                    case ItemSuffix.Agility:
                        typeIdMod_1 = 0x45;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Safety:
                    case ItemSuffix.Majesty:
                    case ItemSuffix.Invincibility:
                    case ItemSuffix.Concentration:
                        typeIdMod_1 = 0x46;
                        typeIdMod_2 = 0x01;
                        break;
                    case ItemSuffix.Health:
                    case ItemSuffix.Earth:
                    case ItemSuffix.Life:
                    case ItemSuffix.Water:
                        typeIdMod_1 = 0x47;
                        typeIdMod_2 = 0x01;
                        break;
                    // case ItemSuffix.Valor:
                    case ItemSuffix.Air:
                    // case ItemSuffix.Durability:
                    case ItemSuffix.Fire:
                        typeIdMod_1 = 0x48;
                        typeIdMod_2 = 0x01;
                        break;
                    // case ItemSuffix.Absorption:
                    case ItemSuffix.Elements:
                    // case ItemSuffix.Deflection:
                    case ItemSuffix.Value:
                        typeIdMod_1 = 0x49;
                        typeIdMod_2 = 0x01;
                        break;
                    // case ItemSuffix.Safety:
                    // case ItemSuffix.Strength:
                    // case ItemSuffix.Invincibility:
                    // case ItemSuffix.Agility:
                    //     typeIdMod_1 = 0x4A;
                    //     typeIdMod_2 = 0x01;
                    //     break;
                    // case ItemSuffix.Health:
                    // case ItemSuffix.Majesty:
                    // case ItemSuffix.Life:
                    // case ItemSuffix.Concentration:
                        // typeIdMod_1 = 0x4B;
                        // typeIdMod_2 = 0x01;
                        // break;
                    // case ItemSuffix.Valor:
                    case ItemSuffix.Integrity:
                    // case ItemSuffix.Durability:
                        typeIdMod_1 = 0x4C;
                        typeIdMod_2 = 0x01;
                        break;
                }
                switch (Suffix)
                {
                    case ItemSuffix.None:
                        break;
                    case ItemSuffix.Valor:
                    case ItemSuffix.Absorption:
                    case ItemSuffix.Safety:
                    case ItemSuffix.Health:
                    // case ItemSuffix.Valor:
                    // case ItemSuffix.Absorption:
                    // case ItemSuffix.Safety:
                    // case ItemSuffix.Health:
                    // case ItemSuffix.Valor:
                        itemSuffixMod = 0x0;
                        break;
                    case ItemSuffix.Meditation:
                    case ItemSuffix.Strength:
                    case ItemSuffix.Majesty:
                    case ItemSuffix.Earth:
                    case ItemSuffix.Air:
                    case ItemSuffix.Elements:
                    // case ItemSuffix.Strength:
                    // case ItemSuffix.Majesty:
                    case ItemSuffix.Integrity:
                        itemSuffixMod = 0x20;
                        break;
                    case ItemSuffix.Durability:
                    case ItemSuffix.Deflection:
                    case ItemSuffix.Invincibility:
                    case ItemSuffix.Life:
                    // case ItemSuffix.Durability:
                    // case ItemSuffix.Deflection:
                    // case ItemSuffix.Invincibility:
                    // case ItemSuffix.Life:
                    // case ItemSuffix.Durability:
                        itemSuffixMod = 0x80;
                        break;
                    case ItemSuffix.Prana:
                    case ItemSuffix.Agility:
                    case ItemSuffix.Concentration:
                    case ItemSuffix.Water:
                    case ItemSuffix.Fire:
                    case ItemSuffix.Value:
                    // case ItemSuffix.Agility:
                    // case ItemSuffix.Concentration:
                        itemSuffixMod = 0xA0;
                        break;
                }
            }
                
            objid_3 = (byte) (((GameId >> 10) & 0b1111) + itemSuffixMod);
                
            return new byte[]
            {
                MinorByte(itemId), MajorByte(itemId), typeid_1, typeid_2, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, objid_1, objid_2, objid_3, typeIdMod_1,
                typeIdMod_2, bagid_1, bagid_2, bagid_3, //0x01, 0x0A, 0x59, 0x00, 0xF0, 0xFF, 0xFF, 0xFF, 0x0F 
                0xA0, 0x90, 0x05, 0x00, 0xFF, 0xFF, 0xFF, 0xFF
            };
        }

        if (ObjectType == GameObjectType.Ring)
        {
            byte ringTypeId_1 = 0;
            byte ringTypeId_2 = 0;
            byte itemSuffixMod = 0;

            switch (Suffix)
            {
                case ItemSuffix.Durability:
                case ItemSuffix.Precision:
                case ItemSuffix.Absorption:
                case ItemSuffix.Strength:
                    ringTypeId_1 = 0x44;
                    ringTypeId_2 = 0x01;

                    break;
                case ItemSuffix.Accuracy:
                case ItemSuffix.Agility:
                case ItemSuffix.Safety:
                case ItemSuffix.Health:
                    ringTypeId_1 = 0x15;
                    ringTypeId_2 = 0x60;

                    break;
                case ItemSuffix.Earth:
                case ItemSuffix.Endurance:
                case ItemSuffix.Life:
                case ItemSuffix.Meditation:
                    ringTypeId_1 = 0x46;
                    ringTypeId_2 = 0x01;

                    break;
                case ItemSuffix.Air:
                case ItemSuffix.Water:
                case ItemSuffix.Prana:
                case ItemSuffix.Ether:
                    ringTypeId_1 = 0x47;
                    ringTypeId_2 = 0x01;

                    break;
                case ItemSuffix.Fire:
                case ItemSuffix.Value:
                    // case ItemSuffix.Absorption:
                    // case ItemSuffix.Durability:
                    ringTypeId_1 = 0x48;
                    ringTypeId_2 = 0x01;

                    break;
                default:
                    Console.WriteLine($"Wrong suffix {Enum.GetName(typeof(ItemSuffix), Suffix)}");

                    break;
            }

            switch (Suffix)
            {
                case ItemSuffix.Durability:
                case ItemSuffix.Safety:
                case ItemSuffix.Life:
                case ItemSuffix.Prana:
                    itemSuffixMod = 0x0;

                    break;
                case ItemSuffix.Precision:
                case ItemSuffix.Agility:
                case ItemSuffix.Endurance:
                case ItemSuffix.Water:
                case ItemSuffix.Fire:
                    itemSuffixMod = 0x20;

                    break;
                case ItemSuffix.Absorption:
                case ItemSuffix.Health:
                case ItemSuffix.Meditation:
                case ItemSuffix.Ether:
                    itemSuffixMod = 0x80;

                    break;
                case ItemSuffix.Strength:
                case ItemSuffix.Accuracy:
                case ItemSuffix.Earth:
                case ItemSuffix.Air:
                case ItemSuffix.Value:
                    itemSuffixMod = 0xA0;

                    break;
            }

            objid_3 = (byte) (((GameId >> 10) & 0b1111) + itemSuffixMod);

            // technically, live server has different values per suffix group for 0x98 0x1A at the end but these
            // seem to be safe to ignore
            bagid_1 = (byte) (bagid_1 + 0b00110);
            var fullStatRingsBytes = new byte[]
            {
                0x00, 0x0A, 0x59, 0x00, 0xF0, 0xFF, 0xFF, 0xFF, 0x5F, 0x78, 0x07, 0xB5, 0xBB, 0x2F, 0xB2, 0xB4, 0xB0,
                0x36, 0xB9, 0x34, 0xB7, 0xB3
            };
            var halfStatRingBytes = new byte[] // shifted by 4 bits
            {
                0xA0, 0x90, 0x05, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x85, 0x77, 0x50, 0xBB, 0xFB, 0x22, 0x4B, 0x0B, 0x6B,
                0x93, 0x4B, 0x73, 0x3B, 0x83
            };
            var result = new List<byte>(new byte[]
            {
                MinorByte(itemId), MajorByte(itemId), 0xE0, 0x8B, 0x0F, 0x80, 0x84, 0x2E, 0x09, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x40, 0x91, 0x45, objid_1, objid_2, objid_3, ringTypeId_1, ringTypeId_2,
                bagid_1, bagid_2, bagid_3
            });

            result.AddRange(Suffix is ItemSuffix.Strength or ItemSuffix.Agility or ItemSuffix.Accuracy
                or ItemSuffix.Endurance or ItemSuffix.Earth or ItemSuffix.Water or ItemSuffix.Air or ItemSuffix.Fire
                ? fullStatRingsBytes
                : halfStatRingBytes);

            var safety = new byte[] { 0x91, 0x01, 0x00 };
            var water = new byte[] { 0x18, 0x1A, 0x00 };

            result.AddRange(Suffix == ItemSuffix.Safety ? safety : water);
            return result.ToArray();
        }

        Console.WriteLine($"Unhandled game object: {ToDebugString()}");

        return new byte[] { };
    }
}