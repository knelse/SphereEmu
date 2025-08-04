using System;
using System.Collections.Generic;
using LiteDB;
using static SphServer.Helpers.CharacterDataHelper;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SphServer.Shared.Db.DataModels;

// TODO: skip unnecessary fields for serialization
public class CharacterDbEntry
{
    public int Id { get; set; }
    [BsonIgnore] public int ClientLocalId { get; set; }
    public byte LookType { get; set; }
    public byte IsTurnedOff { get; set; }
    public ushort MaxMP { get; set; }
    public ushort BaseStrength { get; set; }
    public ushort CurrentStrength { get; set; }
    public ushort BaseAgility { get; set; }
    public ushort CurrentAgility { get; set; }
    public ushort BaseAccuracy { get; set; }
    public ushort CurrentAccuracy { get; set; }
    public ushort BaseEndurance { get; set; }
    public ushort CurrentEndurance { get; set; }
    public ushort BaseEarth { get; set; }
    public ushort CurrentEarth { get; set; }
    public ushort BaseAir { get; set; }
    public ushort CurrentAir { get; set; }
    public ushort BaseWater { get; set; }
    public ushort CurrentWater { get; set; }
    public ushort BaseFire { get; set; }
    public ushort CurrentFire { get; set; }
    public ushort MaxSatiety { get; set; }
    public uint TitleXP { get; set; }
    public uint DegreeXP { get; set; }
    public ushort CurrentSatiety { get; set; }
    public ushort CurrentMP { get; set; }
    public ushort AvailableTitleStats { get; set; }
    public ushort AvailableDegreeStats { get; set; }
    public bool IsGenderFemale { get; set; }
    public string Name { get; set; } = "Test";
    [BsonRef("Clans")] public ClanDbEntry? Clan { get; set; } = ClanDbEntry.DefaultClanDbEntry;
    public byte FaceType { get; set; }
    public byte HairStyle { get; set; }
    public byte HairColor { get; set; }
    public byte Tattoo { get; set; }
    public byte BootModelId { get; set; }
    public byte PantsModelId { get; set; }
    public byte ArmorModelId { get; set; }
    public byte HelmetModelId { get; set; }
    public byte GlovesModelId { get; set; }
    public bool IsNotQueuedForDeletion { get; set; } = true;
    public int Money { get; set; }
    public int GuildLevelMinusOne { get; set; }
    public Guild Guild { get; set; } = Guild.None;
    public ClanRank ClanRank { get; set; } = ClanRank.Neophyte;
    public ushort ClientIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; } = 150;
    public double Z { get; set; }
    public double Angle { get; set; }
    public int TitleMinusOne { get; set; }
    public int DegreeMinusOne { get; set; }
    public ushort CurrentHP { get; set; } = 100;
    public ushort MaxHP { get; set; } = 100;
    public ushort PDef { get; set; }
    public ushort MDef { get; set; }
    public KarmaTier Karma { get; set; } = KarmaTier.Neutral;
    public Dictionary<BelongingSlot, int> Items { get; set; } = new ();
    public int PAtk { get; set; }
    public int MAtk { get; set; }
    public int KarmaCount { get; set; }

    public int MaxHPBase => HealthAtTitle[TitleMinusOne % 60] + HealthAtDegree[DegreeMinusOne % 60] - 100;
    public int MaxMPBase => MpAtTitle[TitleMinusOne % 60] + MpAtDegree[DegreeMinusOne % 60] - 100;
    public ulong XpToLevelUp => GetXpToLevelUp();

    public readonly ItemDbEntry Fists = new ()
    {
        ObjectKind = GameObjectKind.Fists,
        GameObjectType = GameObjectType.Fists
    };

    public CharacterDbEntry ()
    {
        LookType = 0x7;
        IsTurnedOff = 0x9;
        CurrentHP = (ushort) MaxHPBase;
        MaxHP = (ushort) MaxHPBase;
        CurrentMP = (ushort) MaxMPBase;
        MaxMP = (ushort) MaxMPBase;
        CurrentSatiety = 100;
        MaxSatiety = 100;
        AvailableDegreeStats = (ushort) AvailableStatsPrimary[0];
        AvailableTitleStats = (ushort) AvailableStatsPrimary[0];
        Fists.Id = DbConnection.Items.Insert(Fists);
    }

    public void LevelUp (int newTitleLevel, int newDegreeLevel)
    {
        if (newTitleLevel > TitleMinusOne)
        {
            var bonusStatsFromReset = TitleMinusOne / 60 * StatBonusForResets[TitleMinusOne];
            AvailableTitleStats =
                (ushort) (AvailableTitleStats + AvailableStatsPrimary[newTitleLevel] + bonusStatsFromReset);
            AvailableDegreeStats = (ushort) (AvailableDegreeStats + AvailableStatsSecondary[newTitleLevel]);
        }
        else if (newDegreeLevel > DegreeMinusOne)
        {
            var bonusStatsFromReset = DegreeMinusOne / 60 * StatBonusForResets[DegreeMinusOne];
            AvailableDegreeStats =
                (ushort) (AvailableDegreeStats + AvailableStatsPrimary[newTitleLevel] + bonusStatsFromReset);
            AvailableTitleStats = (ushort) (AvailableTitleStats + AvailableStatsSecondary[newTitleLevel]);
        }
    }

    public static CharacterDbEntry CreateNewCharacter (ushort clientIndex, string name, bool isFemale, int face,
        int hairStyle, int hairColor, int tattoo)
    {
        return new CharacterDbEntry
        {
            Name = name,
            IsGenderFemale = isFemale,
            FaceType = (byte) face,
            HairStyle = (byte) hairStyle,
            HairColor = (byte) hairColor,
            Tattoo = (byte) tattoo,
            ClientIndex = clientIndex
        };
    }

    public bool HasEmptyInventorySlot (GameObjectType gameObjectType = GameObjectType.Unknown)
    {
        return FindEmptyInventorySlot != null;
    }

    public BelongingSlot? FindEmptyInventorySlot (GameObjectType gameObjectType = GameObjectType.Unknown)
    {
        // TODO: equipped slots, bags, etc
        var lookup = new List<BelongingSlot>
        {
            BelongingSlot.Inventory_1,
            BelongingSlot.Inventory_2,
            BelongingSlot.Inventory_3,
            BelongingSlot.Inventory_4,
            BelongingSlot.Inventory_5,
            BelongingSlot.Inventory_6,
            BelongingSlot.Inventory_7,
            BelongingSlot.Inventory_8,
            BelongingSlot.Inventory_9,
            BelongingSlot.Inventory_10
        };

        foreach (var slot in lookup)
        {
            if (IsItemSlotEmpty(slot))
            {
                return slot;
            }
        }

        return null;
    }

    public bool IsItemSlotEmpty (BelongingSlot belongingSlot)
    {
        return !Items.ContainsKey(belongingSlot);
    }

    private ulong GetXpToLevelUp ()
    {
        if (TitleMinusOne % 60 == 59 && DegreeMinusOne % 60 == 59)
        {
            return 1;
        }

        var minLevel = Math.Min(TitleMinusOne, DegreeMinusOne);
        var maxLevel = Math.Max(TitleMinusOne, DegreeMinusOne);

        return (ulong) (XpPerLevelBase[maxLevel] + XpPerLevelDelta[maxLevel] * minLevel);
    }

    public bool CanUseItem (ItemDbEntry itemDbEntry)
    {
        // TODO: actual check
        return true;
    }

    public bool UpdateCurrentStats ()
    {
        var slotsToUpdate = new HashSet<BelongingSlot>
        {
            BelongingSlot.Amulet, BelongingSlot.Belt, BelongingSlot.Boots, BelongingSlot.Chestplate,
            BelongingSlot.Gloves, BelongingSlot.Guild, BelongingSlot.Helmet, BelongingSlot.Pants,
            BelongingSlot.Ring_1, BelongingSlot.Ring_2, BelongingSlot.Ring_3, BelongingSlot.Ring_4,
            BelongingSlot.Shield, BelongingSlot.BraceletLeft, BelongingSlot.BraceletRight
        };

        var str = (int) BaseStrength;
        var agi = (int) BaseAgility;
        var acc = (int) BaseAccuracy;
        var end = (int) BaseEndurance;
        var ear = (int) BaseEarth;
        var wat = (int) BaseWater;
        var air = (int) BaseAir;
        var fir = (int) BaseFire;
        // TODO: satiety not calculated atm
        var hpMax = MaxHPBase;
        var mpMax = MaxMPBase;
        var pdef = 0;
        var mdef = 0;
        var patk = 0;
        var matk = 0;

        foreach (var slot in slotsToUpdate)
        {
            if (!Items.ContainsKey(slot))
            {
                continue;
            }

            var item = DbConnection.Items.FindById(Items[slot]);
            if (item is null || !CanUseItem(item))
            {
                continue;
            }

            str += item.StrengthUp;
            agi += item.AgilityUp;
            acc += item.AccuracyUp;
            end += item.EnduranceUp;
            ear += item.EarthUp;
            wat += item.WaterUp;
            air += item.AirUp;
            fir += item.FireUp;
            hpMax += item.MaxHpUp;
            mpMax += item.MaxMpUp;
            pdef += item.PDefUp;
            mdef += item.MDefUp;
            patk += item.PAtkUpNegative;
            matk += item.MAtkUpNegative;
        }

        CurrentStrength = (ushort) str;
        CurrentAgility = (ushort) agi;
        CurrentAccuracy = (ushort) acc;
        CurrentEndurance = (ushort) end;
        CurrentEarth = (ushort) ear;
        CurrentWater = (ushort) wat;
        CurrentAir = (ushort) air;
        CurrentFire = (ushort) fir;
        CurrentHP = (ushort) Math.Min(CurrentHP, hpMax);
        CurrentMP = (ushort) Math.Min(CurrentMP, mpMax);
        MaxHP = (ushort) hpMax;
        MaxMP = (ushort) mpMax;
        PDef = (ushort) pdef;
        MDef = (ushort) mdef;
        PAtk = (ushort) patk;
        MAtk = (ushort) matk;

        // TODO: character state shouldn't be updated in starting dungeon
        // MainServer.CharacterCollection.Update(Id, this);

        // Would be cool but Rider console doesn't support emoji until 2023.1 or smth
        // Console.WriteLine($"💪{CurrentStrength} 🦵{CurrentAgility} 👀{CurrentAccuracy} 🏃{CurrentEndurance} 🌍{CurrentEarth} " +
        //                   $"💧{CurrentWater} ⛅{CurrentAir} 🔥{CurrentFire} 💖{CurrentHP}/{MaxHP} 💙{CurrentMP}/{MaxMP} " +
        //                   $"🧿{PDef} 🛐{MDef} 🪓{PAtk} 💥{MAtk}");
        Console.WriteLine(
            $"STR {CurrentStrength} AGI {CurrentAgility} ACC {CurrentAccuracy} END {CurrentEndurance} EAR {CurrentEarth} " +
            $"WAT {CurrentWater} AIR {CurrentAir} FIR {CurrentFire} HP {CurrentHP}/{MaxHP} MP {CurrentMP}/{MaxMP} " +
            $"PD {PDef} MD {MDef} PA {PAtk} MA {MAtk}");

        return true;
    }
}