using System;
using System.Collections.Generic;
using LiteDB;

namespace SphServer.DataModels;

public class Item
{
    [BsonId]
    public int Id { get; set; }
    public int GameObjectDbId { get; set; }
    public GameObjectKind ObjectKind { get; set; }
    public int GameId { get; set; }
    public string SphereType { get; set; } = string.Empty;
    public GameObjectType ObjectType { get; set; }
    public string ModelNameGround { get; set; } = string.Empty;
    public string ModelNameInventory { get; set; } = string.Empty;
    public int HpCost { get; set; }
    public int MpCost { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Angle { get; set; }
    public int TitleMinusOne { get; set; }
    public int DegreeMinusOne { get; set; }
    public KarmaTypes MinKarmaLevel { get; set; }
    public KarmaTypes MaxKarmaLevel { get; set; }
    public int StrengthReq { get; set; }
    public int AgilityReq { get; set; }
    public int AccuracyReq { get; set; }
    public int EnduranceReq { get; set; }
    public int EarthReq { get; set; }
    public int AirReq { get; set; }
    public int WaterReq { get; set; }
    public int FireReq { get; set; }
    public int PAtkNegative { get; set; }
    public int MAtkNegativeOrHeal { get; set; }
    public int MPHeal { get; set; }
    public int t1 { get; set; }
    public int MaxHpUp { get; set; }
    public int MaxMpUp { get; set; }
    public int PAtkUpNegative { get; set; }
    public int PDefUp { get; set; }
    public int MDefUp { get; set; }
    public int StrengthUp { get; set; }
    public int AgilityUp { get; set; }
    public int AccuracyUp { get; set; }
    public int EnduranceUp { get; set; }
    public int EarthUp { get; set; }
    public int AirUp { get; set; }
    public int WaterUp { get; set; }
    public int FireUp { get; set; }
    public int MAtkUpNegative { get; set; }
    public int Weight { get; set; }
    public int Durability { get; set; }
    public int _range { get; set; }
    public int UseTime { get; set; }
    public int VendorCost { get; set; }
    public int MutatorId { get; set; }
    public int _duration { get; set; }
    public int ReuseDelayHours { get; set; }
    public int t2 { get; set; }
    public int t3 { get; set; }
    public int t4 { get; set; }
    public int t5 { get; set; }
    public string t6 { get; set; } = string.Empty;
    public string t7 { get; set; } = string.Empty;
    public int Tier { get; set; }
    public int Range { get; set; }
    public int Radius { get; set; }
    public int Duration { get; set; }
    public ItemSuffix Suffix { get; set; }
    public int ItemCount { get; set; }
    public Dictionary<Locale, string> Localisation { get; set; } = new();
    public int CurrentDurability { get; set; }
    public int? ParentContainerId { get; set; }
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

    public bool IsTierVisible()
    {
        return ObjectKind is GameObjectKind.Armor or GameObjectKind.Axe or GameObjectKind.Guild
                   or GameObjectKind.Magical or GameObjectKind.Powder or GameObjectKind.Quest or GameObjectKind.Sword
                   or GameObjectKind.Unique or GameObjectKind.Armor_New or GameObjectKind.Armor_Old or GameObjectKind.Axe_New
                   or GameObjectKind.Crossbow_New or GameObjectKind.Magical_New or GameObjectKind.MantraBlack
                   or GameObjectKind.MantraWhite or GameObjectKind.Sword_New
               && ObjectType is not GameObjectType.Ear;
    }

    public static Item CreateFromGameObject (SphGameObject go)
    {
        var item = new Item();
        foreach (var prop in go.GetType().GetFields())
        {
            item.GetType().GetField(prop.Name)?.SetValue(item, prop.GetValue(go));
        }

        foreach (var prop in go.GetType().GetProperties())
        {
            item.GetType().GetProperty(prop.Name)?.SetValue(item, prop.GetValue(go));
        }

        item.GameObjectDbId = go.GameObjectDbId;

        return item;
    }
}