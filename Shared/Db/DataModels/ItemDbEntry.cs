using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using SphServer.Sphere.Game;

namespace SphServer.Shared.Db.DataModels;

public class ItemDbEntry
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
    public Dictionary<Locale, string> Localization { get; set; } = new();
    public int CurrentDurability { get; set; }
    public int? ParentContainerId { get; set; }
    public Dictionary<string, object> ContentsData { get; set; } = new();
    public Guild RequiredGuild { get; set; }
    public int RequiredGuildRankMinusOne { get; set; }

    public bool IsTierVisible()
    {
        return ObjectKind is GameObjectKind.Armor or GameObjectKind.Axe or GameObjectKind.Guild
                   or GameObjectKind.Magical or GameObjectKind.Powder or GameObjectKind.Quest or GameObjectKind.Sword
                   or GameObjectKind.Unique or GameObjectKind.Armor_New or GameObjectKind.Armor_Old
                   or GameObjectKind.Axe_New
                   or GameObjectKind.Crossbow_New or GameObjectKind.Magical_New or GameObjectKind.MantraBlack
                   or GameObjectKind.MantraWhite or GameObjectKind.Sword_New
               && GameObjectType is not GameObjectType.Ear;
    }

    public static ItemDbEntry CreateFromGameObject(SphGameObject go)
    {
        var item = new ItemDbEntry();
        foreach (var prop in go.GetType().GetFields())
        {
            item.GetType().GetField(prop.Name)?.SetValue(item, prop.GetValue(go));
        }

        foreach (var prop in go.GetType().GetProperties())
        {
            item.GetType().GetProperty(prop.Name)?.SetValue(item, prop.GetValue(go));
        }

        item.GameObjectDbId = go.GameObjectDbId;

        // Spelled Localisation on the game object, so the copy above misses it.
        item.Localization = go.Localisation;

        if (item.Suffix != ItemSuffix.None)
        {
            item.UpdateStatsForSuffix();
        }

        item.ObjectType = go.GameObjectType.GetPacketObjectType();

        return item;
    }

    public static ItemDbEntry Clone(ItemDbEntry source, bool insertIntoItemCollection = true)
    {
        var item = new ItemDbEntry();
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
            item.Id = DbConnection.Items.Insert(item);
        }

        return item;
    }

    public static bool IsInventorySlot(BelongingSlot slot)
    {
        return slot is BelongingSlot.Inventory_1 or BelongingSlot.Inventory_2 or BelongingSlot.Inventory_3
            or BelongingSlot.Inventory_4 or BelongingSlot.Inventory_5 or BelongingSlot.Inventory_6
            or BelongingSlot.Inventory_7 or BelongingSlot.Inventory_8 or BelongingSlot.Inventory_9
            or BelongingSlot.Inventory_10;
    }

    private static readonly BelongingSlot[] RingSlots =
        [BelongingSlot.Ring_1, BelongingSlot.Ring_2, BelongingSlot.Ring_3, BelongingSlot.Ring_4];

    private static readonly BelongingSlot[] BraceletSlots =
        [BelongingSlot.BraceletLeft, BelongingSlot.BraceletRight];

    private static readonly BelongingSlot[] SpecialSlots =
    [
        BelongingSlot.Special_5, BelongingSlot.Special_6,
        BelongingSlot.Special_7, BelongingSlot.Special_8, BelongingSlot.Special_9
    ];

    private static readonly BelongingSlot[] CrystalSlots =
    [
        BelongingSlot.Special_1, BelongingSlot.Special_2,
        BelongingSlot.Special_3, BelongingSlot.Special_4
    ];

    private static readonly BelongingSlot[] KeySlots = [BelongingSlot.Key_1, BelongingSlot.Key_2];

    private static readonly BelongingSlot[] WeaponSlots = [BelongingSlot.MainHand];

    private static readonly HashSet<GameObjectType> InventoryOnlyTypes =
    [
        GameObjectType.Flag,
        GameObjectType.Castle_Stone,
        GameObjectType.Letter,
        GameObjectType.Lottery
    ];

    /// <summary>
    ///     Wear / persona slots each item type may occupy. Inventory cells accept everything
    ///     and are not listed here. Guild-kind catalog items also use <see cref="BelongingSlot.Guild"/>.
    /// </summary>
    private static readonly Dictionary<GameObjectType, BelongingSlot[]> TypeToSlots = new()
    {
        [GameObjectType.Helmet] = [BelongingSlot.Helmet],
        [GameObjectType.Helmet_Premium] = [BelongingSlot.Helmet],
        [GameObjectType.Helmet_Quest] = [BelongingSlot.Helmet],
        [GameObjectType.Helmet_Unique] = [BelongingSlot.Helmet],
        [GameObjectType.Amulet] = [BelongingSlot.Amulet],
        [GameObjectType.Amulet_Unique] = [BelongingSlot.Amulet],
        [GameObjectType.Shield] = [BelongingSlot.Shield],
        [GameObjectType.Shield_Quest] = [BelongingSlot.Shield],
        [GameObjectType.Shield_Unique] = [BelongingSlot.Shield],
        [GameObjectType.Chestplate] = [BelongingSlot.Chestplate],
        [GameObjectType.Chestplate_Quest] = [BelongingSlot.Chestplate],
        [GameObjectType.Chestplate_Unique] = [BelongingSlot.Chestplate],
        [GameObjectType.Robe] = [BelongingSlot.Chestplate],
        [GameObjectType.Robe_Quest] = [BelongingSlot.Chestplate],
        [GameObjectType.Robe_Unique] = [BelongingSlot.Chestplate],
        [GameObjectType.Gloves] = [BelongingSlot.Gloves],
        [GameObjectType.Gloves_Quest] = [BelongingSlot.Gloves],
        [GameObjectType.Gloves_Unique] = [BelongingSlot.Gloves],
        [GameObjectType.Belt] = [BelongingSlot.Belt],
        [GameObjectType.Belt_Quest] = [BelongingSlot.Belt],
        [GameObjectType.Belt_Unique] = [BelongingSlot.Belt],
        [GameObjectType.Bracelet] = BraceletSlots,
        [GameObjectType.Bracelet_Unique] = BraceletSlots,
        [GameObjectType.Ring] = RingSlots,
        [GameObjectType.Ring_Special] = RingSlots,
        [GameObjectType.Ring_Unique] = RingSlots,
        [GameObjectType.Pants] = [BelongingSlot.Pants],
        [GameObjectType.Pants_Quest] = [BelongingSlot.Pants],
        [GameObjectType.Pants_Unique] = [BelongingSlot.Pants],
        [GameObjectType.Boots] = [BelongingSlot.Boots],
        [GameObjectType.Boots_Quest] = [BelongingSlot.Boots],
        [GameObjectType.Boots_Unique] = [BelongingSlot.Boots],
        [GameObjectType.Guild] = [BelongingSlot.Guild],
        [GameObjectType.Guild_Bag] = [BelongingSlot.Guild, BelongingSlot.Backpack],
        [GameObjectType.Sword] = WeaponSlots,
        [GameObjectType.Sword_Quest] = WeaponSlots,
        [GameObjectType.Sword_Unique] = WeaponSlots,
        [GameObjectType.Axe] = WeaponSlots,
        [GameObjectType.Axe_Quest] = WeaponSlots,
        [GameObjectType.Crossbow] = WeaponSlots,
        [GameObjectType.Crossbow_Quest] = WeaponSlots,
        [GameObjectType.Fists] = WeaponSlots,
        [GameObjectType.Map] = [BelongingSlot.MapBook],
        [GameObjectType.Scroll] = [BelongingSlot.RecipeBook],
        [GameObjectType.MantraBlack] = [BelongingSlot.MantraBook],
        [GameObjectType.MantraWhite] = [BelongingSlot.MantraBook],
        [GameObjectType.Key] = KeySlots,
        [GameObjectType.Castle_Crystal] = CrystalSlots,
        [GameObjectType.Crystal] = CrystalSlots,
        [GameObjectType.Special] = SpecialSlots,
        [GameObjectType.Special_Crusader_Gapclose] = SpecialSlots,
        [GameObjectType.Special_Inquisitor_Teleport] = SpecialSlots,
        [GameObjectType.Special_Archmage_Teleport] = SpecialSlots,
        [GameObjectType.Special_MasterOfSteel_Whirlwind] = SpecialSlots,
        [GameObjectType.Special_Druid_Wolf] = SpecialSlots,
        [GameObjectType.Special_Thief_Steal] = SpecialSlots,
        [GameObjectType.Special_MasterOfSteel_Suicide] = SpecialSlots,
        [GameObjectType.Special_Necromancer_Flyer] = SpecialSlots,
        [GameObjectType.Special_Necromancer_Resurrection] = SpecialSlots,
        [GameObjectType.Special_Necromancer_Zombie] = SpecialSlots,
        [GameObjectType.Special_Bandier_Flag] = SpecialSlots,
        [GameObjectType.Special_Bandier_DispelControl] = SpecialSlots,
        [GameObjectType.Special_Bandier_Fortify] = SpecialSlots
    };

    /// <summary>
    ///     Whether the admin picker should hide types that cannot go in this slot.
    ///     Inventory cells stay unfiltered; any other slot with no mapped types shows nothing.
    /// </summary>
    public static bool HasSlotTypeFilter(BelongingSlot slot) => !IsInventorySlot(slot);

    public static bool IsTypeValidForSlot(GameObjectType type, BelongingSlot slot)
    {
        if (IsInventorySlot(slot))
        {
            return true;
        }

        if (InventoryOnlyTypes.Contains(type))
        {
            return false;
        }

        return TypeToSlots.TryGetValue(type, out var slots) && slots.Contains(slot);
    }

    public static bool IsAllowedInSlot(GameObjectType type, GameObjectKind kind, BelongingSlot slot)
    {
        if (IsInventorySlot(slot))
        {
            return true;
        }

        if (InventoryOnlyTypes.Contains(type))
        {
            return false;
        }

        if (TypeToSlots.TryGetValue(type, out var slots))
        {
            return slots.Contains(slot);
        }

        return slot is BelongingSlot.Guild && kind is GameObjectKind.Guild;
    }

    public bool IsValidForSlot(BelongingSlot slot) =>
        IsAllowedInSlot(GameObjectType, ObjectKind, slot);

    /// <summary>
    ///     Rebuild title/degree reqs from the base game object + suffix rules.
    ///     Fixes items created before uniform 4-stat suffixes were filtered to existing reqs.
    /// </summary>
    public void RecalculateStatReqsFromBase()
    {
        if (!SphObjectDb.GameObjectDataDb.TryGetValue(GameId, out var go))
        {
            return;
        }

        StrengthReq = go.StrengthReq;
        AgilityReq = go.AgilityReq;
        AccuracyReq = go.AccuracyReq;
        EnduranceReq = go.EnduranceReq;
        EarthReq = go.EarthReq;
        AirReq = go.AirReq;
        WaterReq = go.WaterReq;
        FireReq = go.FireReq;

        if (Suffix == ItemSuffix.None)
        {
            return;
        }

        var suffixObj = SphObjectDbHelper.GetSuffixObject(GameObjectType, Suffix, Tier);
        (StrengthReq, AgilityReq, AccuracyReq, EnduranceReq) = ApplyTitleOrDegreeReqs(
            suffixObj.StrengthReq, suffixObj.AgilityReq, suffixObj.AccuracyReq, suffixObj.EnduranceReq,
            StrengthReq, AgilityReq, AccuracyReq, EnduranceReq);
        (EarthReq, AirReq, WaterReq, FireReq) = ApplyTitleOrDegreeReqs(
            suffixObj.EarthReq, suffixObj.AirReq, suffixObj.WaterReq, suffixObj.FireReq,
            EarthReq, AirReq, WaterReq, FireReq);
    }

    private void UpdateStatsForSuffix()
    {
        var suffixObj = SphObjectDbHelper.GetSuffixObject(GameObjectType, Suffix, Tier);
        Durability *= (100 + suffixObj.Durability) / 100;
        Weight *= (100 + suffixObj.Weight) / 100;
        UseTime = UseTime * (100 + suffixObj.UseTime) / 100;
        VendorCost = VendorCost * (100 + suffixObj.VendorCost) / 100;

        // Integrity / Dragon / Elements / etc. set all 4 title or degree reqs to the same
        // value — those only apply to stats the base item already requires.
        (StrengthReq, AgilityReq, AccuracyReq, EnduranceReq) = ApplyTitleOrDegreeReqs(
            suffixObj.StrengthReq, suffixObj.AgilityReq, suffixObj.AccuracyReq, suffixObj.EnduranceReq,
            StrengthReq, AgilityReq, AccuracyReq, EnduranceReq);
        (EarthReq, AirReq, WaterReq, FireReq) = ApplyTitleOrDegreeReqs(
            suffixObj.EarthReq, suffixObj.AirReq, suffixObj.WaterReq, suffixObj.FireReq,
            EarthReq, AirReq, WaterReq, FireReq);

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
        // *UpNegative DB values are often positive meaning attack-up — normalize to negative.
        // PAtkNegative / MAtkNegativeOrHeal already use negative=up, positive=down in the suffix DB.
        PAtkUpNegative += NegateIfPositive(suffixObj.PAtkUpNegative);
        PAtkNegative += suffixObj.PAtkNegative;
        MAtkUpNegative += NegateIfPositive(suffixObj.MAtkUpNegative);
        MAtkNegativeOrHeal += suffixObj.MAtkNegativeOrHeal;
    }

    /// <summary>
    ///     When suffix sets all four stats to the same positive req, only stack onto
    ///     stats the base item already has; otherwise add each req normally.
    /// </summary>
    private static (int, int, int, int) ApplyTitleOrDegreeReqs(
        int s0, int s1, int s2, int s3,
        int r0, int r1, int r2, int r3)
    {
        if (s0 > 0 && s0 == s1 && s1 == s2 && s2 == s3)
        {
            return (
                r0 > 0 ? r0 + s0 : r0,
                r1 > 0 ? r1 + s1 : r1,
                r2 > 0 ? r2 + s2 : r2,
                r3 > 0 ? r3 + s3 : r3);
        }

        return (r0 + s0, r1 + s1, r2 + s2, r3 + s3);
    }

    private static int NegateIfPositive(int value) => value > 0 ? -value : value;

    public string ToDebugString()
    {
        var itemCountStr = ItemCount > 1 ? $" ({ItemCount})" : "";
        return
            "===============================================================================================================================\n" +
            $"GO: {Enum.GetName(typeof(GameObjectType), GameObjectType)} [{GameId}] T{Tier}" + itemCountStr +
            $" Tit: {TitleMinusOne} Deg: {DegreeMinusOne} $HP: {HpCost} $MP: {MpCost} Of: {Enum.GetName(typeof(ItemSuffix), Suffix)} \n" +
            $"Str: {StrengthReq} Agi: {AgilityReq} Acc: {AccuracyReq} End: {EnduranceReq} Ear: {EarthReq} Air: {AirReq} Wat: {WaterReq} Fir: {FireReq}\n" +
            $"Str+: {StrengthUp} Agi+: {AgilityUp} Acc+: {AccuracyUp} End+: {EnduranceUp} Ear+: {EarthUp} Air+: {AirUp} Wat+: {WaterUp} Fir+: {FireUp}\n" +
            $"MaxHP+: {MaxHpUp} MaxMP+: {MaxMpUp} PD+: {PDefUp} MD+: {MDefUp} PA: {PAtkNegative} PA+: {PAtkUpNegative} MA: {MAtkNegativeOrHeal} MA+: {MAtkUpNegative} MP+: {MPHeal}";
    }
}