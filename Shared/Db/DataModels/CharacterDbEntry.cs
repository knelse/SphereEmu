using System.Linq;
using Godot;
using LiteDB;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
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

    /// <summary>Magical chest armour has its own byte in the look block; physical armour is ArmorModelId.</summary>
    public byte RobeModelId { get; set; }

    public byte ShieldModelId { get; set; }
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

    public int MainHandPAtk { get; set; }
    public bool HoldsItemInHand { get; set; }

    /// <summary>Everything a swing hits with: worn bonuses plus whatever is in the hand.</summary>
    public int MeleePAtk => PAtk + MainHandPAtk;

    /// <summary>
    ///     Puts an item in a slot and releases whatever slot it was already in. An item is in one
    ///     place: a claim left behind is saved with the character and comes back on relog as a cell
    ///     pointing at an item the client has already bound elsewhere, which it draws as a blank.
    /// </summary>
    public void PlaceItemInSlot(BelongingSlot slot, int itemId)
    {
        foreach (var heldIn in Items.Where(x => x.Value == itemId).Select(x => x.Key).ToList())
        {
            Items.Remove(heldIn);
        }

        Items[slot] = itemId;
        ClientStateEvents.RaiseCharacterChanged(ClientIndex);
    }

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

    /// <summary>
    ///     Move title and/or degree to the given 0-based levels (any delta, up or down).
    ///     Rebuilds available stats from the current rebirth cycle, then recalculates max HP/MP
    ///     from the title/degree tables (plus gear and satiety). Does not persist or push
    ///     to the client.
    /// </summary>
    public bool LevelUp(int newTitleLevel, int newDegreeLevel)
    {
        newTitleLevel = Math.Clamp(newTitleLevel, 0, MaxLevelMinusOne);
        newDegreeLevel = Math.Clamp(newDegreeLevel, 0, MaxLevelMinusOne);
        if (newTitleLevel == TitleMinusOne && newDegreeLevel == DegreeMinusOne)
        {
            return false;
        }

        TitleMinusOne = newTitleLevel;
        DegreeMinusOne = newDegreeLevel;
        RecalcAvailableStats();
        RecalcCurrentStats();
        return true;
    }

    /// <summary>
    ///     Set title or degree XP and consume it into levels while it covers the next cost.
    ///     Recalc runs once after all level-ups. Does not persist or push to the client.
    /// </summary>
    public bool ApplyExperience(bool isTitle, uint newXp)
    {
        var oldXp = isTitle ? TitleXP : DegreeXP;
        var oldTitle = TitleMinusOne;
        var oldDegree = DegreeMinusOne;
        if (isTitle)
        {
            TitleXP = newXp;
        }
        else
        {
            DegreeXP = newXp;
        }

        while (true)
        {
            var level = isTitle ? TitleMinusOne : DegreeMinusOne;
            if (level >= MaxLevelMinusOne)
            {
                break;
            }

            var cost = XpToLevelUp;
            var xp = isTitle ? TitleXP : DegreeXP;
            if (cost > xp)
            {
                break;
            }

            if (isTitle)
            {
                TitleXP -= (uint)cost;
                TitleMinusOne++;
            }
            else
            {
                DegreeXP -= (uint)cost;
                DegreeMinusOne++;
            }
        }

        if (TitleMinusOne == oldTitle && DegreeMinusOne == oldDegree
            && (isTitle ? TitleXP : DegreeXP) == oldXp)
        {
            return false;
        }

        if (TitleMinusOne != oldTitle || DegreeMinusOne != oldDegree)
        {
            RecalcAvailableStats();
            RecalcCurrentStats();
        }
        else
        {
            ClientStateEvents.RaiseCharacterChanged(ClientIndex);
        }

        return true;
    }

    /// <summary>
    ///     Add XP to title and consume it into levels via <see cref="ApplyExperience"/>.
    ///     Does not persist or push to the client.
    /// </summary>
    public bool AwardExperience(uint amount)
    {
        if (amount == 0)
        {
            return false;
        }

        var titleXp = TitleXP > uint.MaxValue - amount ? uint.MaxValue : TitleXP + amount;
        return ApplyExperience(true, titleXp);
    }

    /// <summary>
    ///     Rebuild available pools from the current rebirth cycle only (tables start at 0),
    ///     plus rebirths × StatBonusForResets, minus spent <c>Base*</c> stats.
    ///     Previous cycles' primary grants are not kept. Does not persist or push to the client.
    /// </summary>
    public void RecalcAvailableStats()
    {
        var title = 0;
        var degree = 0;
        AddCurrentCycleGrants(TitleMinusOne, titleIsPrimary: true, ref title, ref degree);
        AddCurrentCycleGrants(DegreeMinusOne, titleIsPrimary: false, ref title, ref degree);
        AvailableTitleStats = title - (BaseStrength + BaseAgility + BaseAccuracy + BaseEndurance);
        AvailableDegreeStats = degree - (BaseEarth + BaseAir + BaseWater + BaseFire);
    }

    /// <summary>
    ///     Title-ups feed the title pool as primary; degree-ups feed the degree pool.
    ///     Bonus is rebirths × StatBonusForResets for each level in this cycle.
    /// </summary>
    private static void AddCurrentCycleGrants(int minusOne, bool titleIsPrimary, ref int title, ref int degree)
    {
        var within = minusOne % 60;
        var rebirths = minusOne / 60;
        for (var i = 0; i <= within; i++)
        {
            var primary = AvailableStatsPrimary[i] + rebirths * StatBonusForResets[i];
            var secondary = AvailableStatsSecondary[i];
            if (titleIsPrimary)
            {
                title += primary;
                degree += secondary;
            }
            else
            {
                degree += primary;
                title += secondary;
            }
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

    /// <summary>
    ///     Restore gameplay to a newly created character: empty slots, money 0, levels/XP 1/1 0/50,
    ///     base stats. Keeps id, name, clan, visuals, and world position. Deletes carried item rows.
    ///     Does not persist or push to the client.
    /// </summary>
    public void ResetToNewCharacterDefaults()
    {
        foreach (var itemId in Items.Values.Distinct())
        {
            DbConnection.Items.Delete(itemId);
        }

        Items.Clear();

        var fresh = CreateNewCharacter(ClientIndex, Name, IsGenderFemale, FaceType, HairStyle, HairColor, Tattoo);
        Money = fresh.Money;
        TitleMinusOne = fresh.TitleMinusOne;
        DegreeMinusOne = fresh.DegreeMinusOne;
        TitleXP = fresh.TitleXP;
        DegreeXP = fresh.DegreeXP;
        BaseStrength = fresh.BaseStrength;
        BaseAgility = fresh.BaseAgility;
        BaseAccuracy = fresh.BaseAccuracy;
        BaseEndurance = fresh.BaseEndurance;
        BaseEarth = fresh.BaseEarth;
        BaseAir = fresh.BaseAir;
        BaseWater = fresh.BaseWater;
        BaseFire = fresh.BaseFire;
        CurrentStrength = fresh.CurrentStrength;
        CurrentAgility = fresh.CurrentAgility;
        CurrentAccuracy = fresh.CurrentAccuracy;
        CurrentEndurance = fresh.CurrentEndurance;
        CurrentEarth = fresh.CurrentEarth;
        CurrentAir = fresh.CurrentAir;
        CurrentWater = fresh.CurrentWater;
        CurrentFire = fresh.CurrentFire;
        AvailableTitleStats = fresh.AvailableTitleStats;
        AvailableDegreeStats = fresh.AvailableDegreeStats;
        CurrentSatiety = fresh.CurrentSatiety;
        MaxSatiety = fresh.MaxSatiety;
        Guild = fresh.Guild;
        GuildLevelMinusOne = fresh.GuildLevelMinusOne;
        Karma = fresh.Karma;
        KarmaCount = fresh.KarmaCount;
        PDef = fresh.PDef;
        MDef = fresh.MDef;
        PAtk = fresh.PAtk;
        MAtk = fresh.MAtk;
        MainHandPAtk = fresh.MainHandPAtk;
        HoldsItemInHand = fresh.HoldsItemInHand;
        BootModelId = fresh.BootModelId;
        PantsModelId = fresh.PantsModelId;
        ArmorModelId = fresh.ArmorModelId;
        RobeModelId = fresh.RobeModelId;
        ShieldModelId = fresh.ShieldModelId;
        HelmetModelId = fresh.HelmetModelId;
        GlovesModelId = fresh.GlovesModelId;

        RecalcAvailableStats();
        RecalcCurrentStats();
        CurrentHP = MaxHP;
        CurrentMP = MaxMP;
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
        var title = TitleMinusOne % 60;
        var degree = DegreeMinusOne % 60;
        if (title == 59 && degree == 59)
        {
            return 1;
        }

        var minLevel = Math.Min(title, degree);
        var maxLevel = Math.Max(title, degree);
        return (ulong)(XpPerLevelBase[maxLevel] + XpPerLevelDelta[maxLevel] * minLevel);
    }

    /// <summary>
    ///     Whether this character meets what the item asks for. Against the base stats, not the
    ///     current ones: the current ones are recalculated from what is worn, and this is called
    ///     during that, so an item could otherwise satisfy its own requirement.
    /// </summary>
    public bool CanUseItem(ItemDbEntry itemDbEntry)
    {
        return UnmetRequirement(itemDbEntry) is null;
    }

    /// <summary>
    ///     The first requirement this character does not meet, worded the way the client words its
    ///     own refusal, or null when the item can be used.
    /// </summary>
    public string? UnmetRequirement(ItemDbEntry itemDbEntry)
    {
        itemDbEntry.RecalculateStatReqsFromBase();

        if (itemDbEntry.RequiredGuild is not Guild.None)
        {
            if (Guild != itemDbEntry.RequiredGuild)
            {
                return "Гильдия";
            }

            if (GuildLevelMinusOne < itemDbEntry.RequiredGuildRankMinusOne)
            {
                return $"Ранг гильдии {GuildLevelMinusOne}<{itemDbEntry.RequiredGuildRankMinusOne}";
            }

            if (!GuildCatalog.MeetsRankRequirements(
                    itemDbEntry.RequiredGuild, itemDbEntry.RequiredGuildRankMinusOne,
                    TitleMinusOne, DegreeMinusOne))
            {
                return "Гильдия";
            }
        }

        (int have, int need, string name)[] checks =
        [
            (CurrentStrength, itemDbEntry.StrengthReq, "Сила"),
            (CurrentAgility, itemDbEntry.AgilityReq, "Ловкость"),
            (CurrentAccuracy, itemDbEntry.AccuracyReq, "Меткость"),
            (CurrentEndurance, itemDbEntry.EnduranceReq, "Выносливость"),
            (CurrentEarth, itemDbEntry.EarthReq, "Земля"),
            (CurrentAir, itemDbEntry.AirReq, "Воздух"),
            (CurrentWater, itemDbEntry.WaterReq, "Вода"),
            (CurrentFire, itemDbEntry.FireReq, "Огонь"),
            (TitleMinusOne, itemDbEntry.TitleMinusOne, "Титул"),
            (DegreeMinusOne, itemDbEntry.DegreeMinusOne, "Степень")
        ];

        foreach (var (have, need, name) in checks)
        {
            if (have < need)
            {
                return $"{name} {have}<{need}";
            }
        }

        return null;
    }

    public bool RecalcCurrentStats()
    {
        var slotsToUpdate = new HashSet<BelongingSlot>
        {
            BelongingSlot.Amulet, BelongingSlot.Belt, BelongingSlot.Boots, BelongingSlot.Chestplate,
            BelongingSlot.Gloves, BelongingSlot.Guild, BelongingSlot.Helmet, BelongingSlot.Pants,
            BelongingSlot.Ring_1, BelongingSlot.Ring_2, BelongingSlot.Ring_3, BelongingSlot.Ring_4,
            BelongingSlot.Shield, BelongingSlot.BraceletLeft, BelongingSlot.BraceletRight,
            BelongingSlot.Special_1, BelongingSlot.Special_2, BelongingSlot.Special_3, BelongingSlot.Special_4
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
            // *UpNegative: negative means attack-up; some items were double-negated by old suffix apply.
            patk += item.PAtkUpNegative > 0 ? -item.PAtkUpNegative : item.PAtkUpNegative;
            matk += item.MAtkUpNegative > 0 ? -item.MAtkUpNegative : item.MAtkUpNegative;
        }

        hpMax = WithSatietyMaxHpBonus(hpMax);

        // After the slot loop, so it sees the move that triggered this rather than the one before.
        CharacterWornLook.Apply(this);

        // The client works out the held item's attack itself, so the stat packet must not carry it.
        // The hand's value sits in the item's own column, not the "+attack" one worn gear uses.
        var heldPAtk = 0;
        var holdsItem = false;

        if (Items.TryGetValue(BelongingSlot.MainHand, out var heldItemId))
        {
            var heldItem = DbConnection.Items.FindById(heldItemId);
            if (heldItem is not null && CanUseItem(heldItem))
            {
                holdsItem = true;
                heldPAtk = heldItem.PAtkNegative;
            }
        }

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
        // Signed, not a ushort cast: the game stores damage as a negative number, so worn gear that
        // adds attack would otherwise wrap into a huge positive one. The stat packet has its own
        // sign bit and the damage formula takes the magnitude.
        PAtk = patk;
        MAtk = matk;
        MainHandPAtk = heldPAtk;
        HoldsItemInHand = holdsItem;

        // TODO: character state shouldn't be updated in starting dungeon
        // MainServer.CharacterCollection.Update(Id, this);

        SphLogger.Info($"Client {ClientLocalId} new stats after recalc: " +
                       $"STR {CurrentStrength} AGI {CurrentAgility} ACC {CurrentAccuracy} END {CurrentEndurance} EAR {CurrentEarth} " +
                       $"WAT {CurrentWater} AIR {CurrentAir} FIR {CurrentFire} HP {CurrentHP}/{MaxHP} MP {CurrentMP}/{MaxMP} " +
                       $"PD {PDef} MD {MDef} PA {PAtk} MA {MAtk} hand PA {MainHandPAtk}");

        ClientStateEvents.RaiseCharacterChanged(ClientIndex);
        return true;
    }
}