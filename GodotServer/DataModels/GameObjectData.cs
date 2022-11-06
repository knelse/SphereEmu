using System;
using System.Collections.Generic;

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
    Amulet_Unique,
    Armor,
    Robe, // armor2 is always robe
    Robe_Quest,
    Robe_Unique,
    Armor_Quest,
    Armor_Unique,
    Belt,
    Belt_Quest,
    Belt_Unique,
    Bracelet,
    Bracelet_Unique,
    Gloves,
    Gloves_Quest,
    Gloves_Unique,
    Helmet,
    Helmet_Premium,
    Helmet_Quest,
    Helmet_Unique,
    Pants,
    Pants_Quest,
    Pants_Unique,
    Ring,
    Ring_Special,
    Ring_Unique,
    Shield,
    Shield_Quest,
    Shield_Unique,
    Shoes,
    Shoes_Quest,
    Shoes_Unique,
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
    Sword_Unique,
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
    Rush,
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
    // Rush,
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
    Durability_Old,
    Life_Old,
    // Water,
    Eclipse,
    Safety_Old,
    Prana_Old,
    Deflection_Old,
    Meditation_Old,
    // Air,
    Archmage,
    Health_Old,
    Ether_Old,
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
    //---- Chests ----//
    // Deflection,
    // Health,
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
    Wholeness,
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
    // Wholeness,
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
}

public class GameObjectData
{
    public GameObjectKind ObjectKind;
    public int GameId;
    public GameObjectType ObjectType;
    public string ModelNameGround;
    public string ModelNameInventory;
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
    public string t6;
    public string t7;
    public int Tier;
    public int Range;
    public int Radius;
    /// <summary>
    /// Seconds
    /// </summary>
    public int Duration;
    public ItemSuffix Suffix = ItemSuffix.None;

    public string ToDebugString()
    {
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
               $"Suffix: {Enum.GetName(typeof(ItemSuffix), Suffix)}";
    }
}