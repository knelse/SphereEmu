using Godot;
using LiteDB;
using SphServer.Shared.Logger;
using static SphServer.Helpers.CharacterDataHelper;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SphServer.Shared.Db.DataModels;

// TODO: skip unnecessary fields for serialization
public class CharacterDbEntry
{
    // Deliberately not inserted into the item collection: nothing reads this row, and one per
    // character load would spend world object ids that are never given back.
    public readonly ItemDbEntry Fists = new()
    {
        ObjectKind = GameObjectKind.Fists,
        GameObjectType = GameObjectType.Fists
    };

    public CharacterDbEntry()
    {
        LookType = 0x7;
        IsTurnedOff = 0x9;
        CurrentSatiety = 50;
        MaxSatiety = 100;
        MaxHP = (ushort)WithSatietyMaxHpBonus(MaxHPBase);
        CurrentHP = MaxHP;
        CurrentMP = (ushort)MaxMPBase;
        MaxMP = (ushort)MaxMPBase;
        AvailableDegreeStats = AvailableStatsPrimary[0];
        AvailableTitleStats = AvailableStatsPrimary[0];
    }

    public int Id { get; set; }
    [BsonIgnore] public int ClientLocalId { get; set; }
    public byte LookType { get; set; }
    public byte IsTurnedOff { get; set; }
    public ushort MaxMP { get; set; }
    public int BaseStrength { get; set; }
    public int CurrentStrength { get; set; }
    public int BaseAgility { get; set; }
    public int CurrentAgility { get; set; }
    public int BaseAccuracy { get; set; }
    public int CurrentAccuracy { get; set; }
    public int BaseEndurance { get; set; }
    public int CurrentEndurance { get; set; }
    public int BaseEarth { get; set; }
    public int CurrentEarth { get; set; }
    public int BaseAir { get; set; }
    public int CurrentAir { get; set; }
    public int BaseWater { get; set; }
    public int CurrentWater { get; set; }
    public int BaseFire { get; set; }
    public int CurrentFire { get; set; }
    public ushort MaxSatiety { get; set; }
    public uint TitleXP { get; set; }
    public uint DegreeXP { get; set; }
    public ushort CurrentSatiety { get; set; }
    public ushort CurrentMP { get; set; }
    public int AvailableTitleStats { get; set; }
    public int AvailableDegreeStats { get; set; }
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
    public KarmaTypes Karma { get; set; } = KarmaTypes.Нейтральная;
    public Dictionary<BelongingSlot, int> Items { get; set; } = new();
    public int PAtk { get; set; }
    public int MAtk { get; set; }
    public int KarmaCount { get; set; }

    public int MaxHPBase => HealthAtTitle[TitleMinusOne % 60] + HealthAtDegree[DegreeMinusOne % 60] - 100;
    public int MaxMPBase => MpAtTitle[TitleMinusOne % 60] + MpAtDegree[DegreeMinusOne % 60] - 100;

    /// <summary>
    ///     Satiety tiers: 0–33 → +5% max HP, 34–66 → +10%, 67+ → +15%.
    /// </summary>
    public int SatietyMaxHpBonusPercent => CurrentSatiety switch
    {
        <= 33 => 5,
        <= 66 => 10,
        _ => 15
    };

    public int WithSatietyMaxHpBonus(int hpMax) =>
        hpMax + hpMax * SatietyMaxHpBonusPercent / 100;
    public ulong XpToLevelUp => GetXpToLevelUp();
    public Vector3 Origin => new((float)X, (float)Y, (float)Z);

    public void LevelUp(int newTitleLevel, int newDegreeLevel)
    {
        if (newTitleLevel > TitleMinusOne)
        {
            var bonusStatsFromReset = TitleMinusOne / 60 * StatBonusForResets[TitleMinusOne];
            AvailableTitleStats += AvailableStatsPrimary[newTitleLevel] + bonusStatsFromReset;
            AvailableDegreeStats += AvailableStatsSecondary[newTitleLevel];
        }
        else if (newDegreeLevel > DegreeMinusOne)
        {
            var bonusStatsFromReset = DegreeMinusOne / 60 * StatBonusForResets[DegreeMinusOne];
            AvailableDegreeStats += AvailableStatsPrimary[newTitleLevel] + bonusStatsFromReset;
            AvailableTitleStats += AvailableStatsSecondary[newTitleLevel];
        }
    }

    public void SetKarmaCount(int value)
    {
        KarmaCount = value;
        SyncKarmaFromCount();
    }

    /// <summary>
    ///     Clamp <see cref="KarmaCount"/> to [-5000, 5000] and set <see cref="Karma"/> from thresholds.
    /// </summary>
    public void SyncKarmaFromCount()
    {
        KarmaCount = Math.Clamp(KarmaCount, -5000, 5000);
        Karma = KarmaCount switch
        {
            < -1000 => KarmaTypes.Очень_Плохая,
            < -100 => KarmaTypes.Плохая,
            <= 100 => KarmaTypes.Нейтральная,
            <= 1000 => KarmaTypes.Хорошая,
            _ => KarmaTypes.Благая
        };
    }

    /// <summary>
    ///     Admin edit of a displayed <c>Current*</c> stat. Applies the same delta to <c>Base*</c>
    ///     (so gear bonuses are preserved) and adjusts the matching available pool by -delta.
    ///     No remaining-points check; values may go negative. Does not persist or push to client.
    /// </summary>
    public bool ApplyCurrentStatEdit(Stat stat, int newCurrentValue)
    {
        var oldCurrent = GetCurrentStat(stat);
        var delta = newCurrentValue - oldCurrent;
        if (delta == 0)
        {
            return false;
        }

        SetBaseStat(stat, GetBaseStat(stat) + delta);
        if (IsTitleStat(stat))
        {
            AvailableTitleStats -= delta;
        }
        else if (IsDegreeStat(stat))
        {
            AvailableDegreeStats -= delta;
        }
        else
        {
            return false;
        }

        RecalcCurrentStats();
        return true;
    }

    public static bool IsTitleStat(Stat stat) =>
        stat is Stat.Strength or Stat.Agility or Stat.Accuracy or Stat.Endurance;

    public static bool IsDegreeStat(Stat stat) =>
        stat is Stat.Earth or Stat.Air or Stat.Water or Stat.Fire;

    public int GetCurrentStat(Stat stat) => stat switch
    {
        Stat.Strength => CurrentStrength,
        Stat.Agility => CurrentAgility,
        Stat.Accuracy => CurrentAccuracy,
        Stat.Endurance => CurrentEndurance,
        Stat.Earth => CurrentEarth,
        Stat.Air => CurrentAir,
        Stat.Water => CurrentWater,
        Stat.Fire => CurrentFire,
        _ => 0
    };

    public int GetBaseStat(Stat stat) => stat switch
    {
        Stat.Strength => BaseStrength,
        Stat.Agility => BaseAgility,
        Stat.Accuracy => BaseAccuracy,
        Stat.Endurance => BaseEndurance,
        Stat.Earth => BaseEarth,
        Stat.Air => BaseAir,
        Stat.Water => BaseWater,
        Stat.Fire => BaseFire,
        _ => 0
    };

    private void SetBaseStat(Stat stat, int value)
    {
        switch (stat)
        {
            case Stat.Strength: BaseStrength = value; break;
            case Stat.Agility: BaseAgility = value; break;
            case Stat.Accuracy: BaseAccuracy = value; break;
            case Stat.Endurance: BaseEndurance = value; break;
            case Stat.Earth: BaseEarth = value; break;
            case Stat.Air: BaseAir = value; break;
            case Stat.Water: BaseWater = value; break;
            case Stat.Fire: BaseFire = value; break;
        }
    }

    public static CharacterDbEntry CreateNewCharacter(ushort clientIndex, string name, bool isFemale, int face,
        int hairStyle, int hairColor, int tattoo)
    {
        return new CharacterDbEntry
        {
            Name = name,
            IsGenderFemale = isFemale,
            FaceType = (byte)face,
            HairStyle = (byte)hairStyle,
            HairColor = (byte)hairColor,
            Tattoo = (byte)tattoo,
            ClientIndex = clientIndex
        };
    }

    public bool HasEmptyInventorySlot(GameObjectType gameObjectType = GameObjectType.Unknown)
    {
        return FindEmptyInventorySlot() != null;
    }

    public BelongingSlot? FindEmptyInventorySlot(GameObjectType gameObjectType = GameObjectType.Unknown)
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

    public bool IsItemSlotEmpty(BelongingSlot belongingSlot)
    {
        return !Items.ContainsKey(belongingSlot);
    }

    private ulong GetXpToLevelUp()
    {
        if (TitleMinusOne % 60 == 59 && DegreeMinusOne % 60 == 59)
        {
            return 1;
        }

        var minLevel = Math.Min(TitleMinusOne, DegreeMinusOne);
        var maxLevel = Math.Max(TitleMinusOne, DegreeMinusOne);

        return (ulong)(XpPerLevelBase[maxLevel] + XpPerLevelDelta[maxLevel] * minLevel);
    }

    public bool CanUseItem(ItemDbEntry itemDbEntry)
    {
        // TODO: actual check
        return true;
    }

    public bool RecalcCurrentStats()
    {
        var slotsToUpdate = new HashSet<BelongingSlot>
        {
            BelongingSlot.Amulet, BelongingSlot.Belt, BelongingSlot.Boots, BelongingSlot.Chestplate,
            BelongingSlot.Gloves, BelongingSlot.Guild, BelongingSlot.Helmet, BelongingSlot.Pants,
            BelongingSlot.Ring_1, BelongingSlot.Ring_2, BelongingSlot.Ring_3, BelongingSlot.Ring_4,
            BelongingSlot.Shield, BelongingSlot.BraceletLeft, BelongingSlot.BraceletRight
        };

        var str = BaseStrength;
        var agi = BaseAgility;
        var acc = BaseAccuracy;
        var end = BaseEndurance;
        var ear = BaseEarth;
        var wat = BaseWater;
        var air = BaseAir;
        var fir = BaseFire;
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

        hpMax = WithSatietyMaxHpBonus(hpMax);

        // PAtk/MAtk: armor/accessories only for now (MainHand omitted) — revisit later
        CurrentStrength = str;
        CurrentAgility = agi;
        CurrentAccuracy = acc;
        CurrentEndurance = end;
        CurrentEarth = ear;
        CurrentWater = wat;
        CurrentAir = air;
        CurrentFire = fir;
        CurrentHP = (ushort)Math.Min(CurrentHP, hpMax);
        CurrentMP = (ushort)Math.Min(CurrentMP, mpMax);
        MaxHP = (ushort)hpMax;
        MaxMP = (ushort)mpMax;
        PDef = (ushort)pdef;
        MDef = (ushort)mdef;
        PAtk = (ushort)patk;
        MAtk = (ushort)matk;

        // TODO: character state shouldn't be updated in starting dungeon
        // MainServer.CharacterCollection.Update(Id, this);

        SphLogger.Info($"Client {ClientLocalId} new stats after recalc: " +
                       $"STR {CurrentStrength} AGI {CurrentAgility} ACC {CurrentAccuracy} END {CurrentEndurance} EAR {CurrentEarth} " +
                       $"WAT {CurrentWater} AIR {CurrentAir} FIR {CurrentFire} HP {CurrentHP}/{MaxHP} MP {CurrentMP}/{MaxMP} " +
                       $"PD {PDef} MD {MDef} PA {PAtk} MA {MAtk}");

        return true;
    }
}