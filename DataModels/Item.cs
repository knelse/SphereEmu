using System;
using System.Collections.Generic;
using LiteDB;
using SphServer.Providers;

namespace SphServer.DataModels;

public class Item
{
    [BsonId] public int Id { get; set; }
    public int GameObjectDbId { get; set; }
    public GameObjectKind ObjectKind { get; set; }
    public int GameId { get; set; }
    public string SphereType { get; set; } = string.Empty;
    public GameObjectType GameObjectType { get; set; }
    public ObjectType ObjectType { get; set; } = ObjectType.Unknown;
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
    public Dictionary<Locale, string> Localisation { get; set; } = new ();
    public int CurrentDurability { get; set; }
    public int? ParentContainerId { get; set; }
    public Dictionary<string, object> ContentsData { get; set; } = new ();

    public string ToDebugString ()
    {
        var itemCountStr = ItemCount > 1 ? $" ({ItemCount})" : "";
        return
            "===============================================================================================================================\n" +
            $"GO: {Enum.GetName(typeof (GameObjectType), GameObjectType)} [{GameId}] T{Tier}" + itemCountStr +
            $" Tit: {TitleMinusOne} Deg: {DegreeMinusOne} $HP: {HpCost} $MP: {MpCost} Of: {Enum.GetName(typeof (ItemSuffix), Suffix)} \n" +
            $"Str: {StrengthReq} Agi: {AgilityReq} Acc: {AccuracyReq} End: {EnduranceReq} Ear: {EarthReq} Air: {AirReq} Wat: {WaterReq} Fir: {FireReq}\n" +
            $"Str+: {StrengthUp} Agi+: {AgilityUp} Acc+: {AccuracyUp} End+: {EnduranceUp} Ear+: {EarthUp} Air+: {AirUp} Wat+: {WaterUp} Fir+: {FireUp}\n" +
            $"MaxHP+: {MaxHpUp} MaxMP+: {MaxMpUp} PD+: {PDefUp} MD+: {MDefUp} PA: {PAtkNegative} PA+: {PAtkUpNegative} MA: {MAtkNegativeOrHeal} MA+: {MAtkUpNegative} MP+: {MPHeal}";
        // $" T1: {t1} " +
        // $" Weight: {Weight} Durability: {Durability} Range: {Range} Radius: {Radius} " +
        // $"UseTime: {UseTime} VendorCost: {VendorCost} MutatorId: {MutatorId} Duration: {Duration} " +
        // $"ReuseDelayHours: {ReuseDelayHours} T2: {t2} T3: {t3} T4: {t4} T5: {t5} T6: {t6} T7: {t7}" +
        // $"Suffix: {Enum.GetName(typeof(ItemSuffix), Suffix)} {itemCountStr}";
        // Kind: {Enum.GetName(typeof(GameObjectKind), ObjectKind)} 
        //Ground: {ModelNameGround} 
        //Inv: {ModelNameInventory} 
        //KarmaMin: {Enum.GetName(typeof(KarmaTypes), MinKarmaLevel)} 
        //KarmaMax: {Enum.GetName(typeof(KarmaTypes), MaxKarmaLevel)} 
    }

    public bool IsTierVisible ()
    {
        return ObjectKind is GameObjectKind.Armor or GameObjectKind.Axe or GameObjectKind.Guild
                   or GameObjectKind.Magical or GameObjectKind.Powder or GameObjectKind.Quest or GameObjectKind.Sword
                   or GameObjectKind.Unique or GameObjectKind.Armor_New or GameObjectKind.Armor_Old
                   or GameObjectKind.Axe_New
                   or GameObjectKind.Crossbow_New or GameObjectKind.Magical_New or GameObjectKind.MantraBlack
                   or GameObjectKind.MantraWhite or GameObjectKind.Sword_New
               && GameObjectType is not GameObjectType.Ear;
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
        if (item.Suffix != ItemSuffix.None)
        {
            item.UpdateStatsForSuffix();
        }

        item.ObjectType = go.GameObjectType.GetPacketObjectType();

        return item;
    }

    public static Item Clone (Item source, bool insertIntoItemCollection = true)
    {
        var item = new Item();
        foreach (var prop in source.GetType().GetFields())
        {
            item.GetType().GetField(prop.Name)?.SetValue(item, prop.GetValue(source));
        }

        foreach (var prop in source.GetType().GetProperties())
        {
            item.GetType().GetProperty(prop.Name)?.SetValue(item, prop.GetValue(source));
        }

        if (insertIntoItemCollection)
        {
            item.Id = 0;
            item.Id = DbConnectionProvider.ItemCollection.Insert(item);
        }

        return item;
    }

    private void UpdateStatsForSuffix ()
    {
        var suffixObj = SphObjectDbHelper.GetSuffixObject(GameObjectType, Suffix, Tier);
        Durability *= (100 + suffixObj.Durability) / 100;
        Weight *= (100 + suffixObj.Weight) / 100;
        UseTime = UseTime * (100 + suffixObj.UseTime) / 100;
        VendorCost += suffixObj.VendorCost;
        StrengthReq += suffixObj.StrengthReq;
        AgilityReq += suffixObj.AgilityReq;
        AccuracyReq += suffixObj.AccuracyReq;
        EnduranceReq += suffixObj.EnduranceReq;
        EarthReq += suffixObj.EarthReq;
        WaterReq += suffixObj.WaterReq;
        AirReq += suffixObj.AirReq;
        FireReq += suffixObj.FireReq;
        StrengthUp += suffixObj.StrengthUp;
        AgilityUp += suffixObj.AgilityUp;
        AccuracyUp += suffixObj.AccuracyUp;
        EnduranceUp += suffixObj.EnduranceUp;
        EarthUp += suffixObj.EarthUp;
        WaterUp += suffixObj.WaterUp;
        AirUp += suffixObj.AirUp;
        FireUp += suffixObj.FireUp;
        MaxHpUp += suffixObj.MaxHpUp;
        MaxMpUp += suffixObj.MaxMpUp;
        PDefUp += suffixObj.PDefUp;
        MDefUp += suffixObj.MDefUp;
        PAtkUpNegative -= suffixObj.PAtkUpNegative;
        PAtkNegative -= suffixObj.PAtkNegative;
        MAtkUpNegative -= suffixObj.MAtkUpNegative;
        MAtkNegativeOrHeal -= suffixObj.MAtkNegativeOrHeal;
    }

    public static bool IsInventorySlot (BelongingSlot slot)
    {
        return slot is BelongingSlot.Inventory_1 or BelongingSlot.Inventory_2 or BelongingSlot.Inventory_3
            or BelongingSlot.Inventory_4 or BelongingSlot.Inventory_5 or BelongingSlot.Inventory_6
            or BelongingSlot.Inventory_7 or BelongingSlot.Inventory_8 or BelongingSlot.Inventory_9
            or BelongingSlot.Inventory_10;
    }

    public bool IsValidForSlot (BelongingSlot slot)
    {
        if (slot is BelongingSlot.Inventory_1 or BelongingSlot.Inventory_2 or BelongingSlot.Inventory_3
            or BelongingSlot.Inventory_4 or BelongingSlot.Inventory_5 or BelongingSlot.Inventory_6
            or BelongingSlot.Inventory_7 or BelongingSlot.Inventory_8 or BelongingSlot.Inventory_9
            or BelongingSlot.Inventory_10)
        {
            return true;
        }

        return (GameObjectType is GameObjectType.Amulet or GameObjectType.Amulet_Unique && slot is BelongingSlot.Amulet)
               || (GameObjectType is GameObjectType.Belt or GameObjectType.Belt_Quest or GameObjectType.Belt_Unique &&
                   slot is BelongingSlot.Belt)
               || (GameObjectType is GameObjectType.Boots or GameObjectType.Boots_Quest
                       or GameObjectType.Boots_Unique &&
                   slot is BelongingSlot.Boots)
               || (GameObjectType is GameObjectType.Bracelet or GameObjectType.Bracelet_Unique &&
                   slot is BelongingSlot.BraceletLeft or BelongingSlot.BraceletRight)
               || (GameObjectType is GameObjectType.Chestplate or GameObjectType.Chestplate_Quest
                   or GameObjectType.Chestplate_Unique && slot is BelongingSlot.Chestplate)
               || (GameObjectType is GameObjectType.Helmet or GameObjectType.Helmet_Premium
                   or GameObjectType.Helmet_Quest
                   or GameObjectType.Helmet_Unique && slot is BelongingSlot.Helmet)
               || (GameObjectType is GameObjectType.Gloves or GameObjectType.Gloves_Quest
                   or GameObjectType.Gloves_Unique && slot is BelongingSlot.Gloves)
               || (GameObjectType is GameObjectType.Pants or GameObjectType.Pants_Quest
                       or GameObjectType.Pants_Unique &&
                   slot is BelongingSlot.Pants)
               || (GameObjectType is GameObjectType.Ring or GameObjectType.Ring_Special or GameObjectType.Ring_Unique &&
                   slot is BelongingSlot.Ring_1 or BelongingSlot.Ring_2 or BelongingSlot.Ring_3
                       or BelongingSlot.Ring_4)
               || (GameObjectType is GameObjectType.Robe or GameObjectType.Robe_Quest or GameObjectType.Robe_Unique &&
                   slot is BelongingSlot.Chestplate)
               || (GameObjectType is GameObjectType.Shield or GameObjectType.Shield_Quest
                   or GameObjectType.Shield_Unique && slot is BelongingSlot.Shield);
    }
}