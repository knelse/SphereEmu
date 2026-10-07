using System.Linq;
using Godot;
using LiteDB;
using SphServer.Shared.Db;
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
    // Not stored: nothing reads it, and one per load spends a world object id that is never
    // returned
    [BsonIgnore]
    public readonly ItemDbEntry Fists = new ()
    {
        ObjectKind = GameObjectKind.Fists,
        GameObjectType = GameObjectType.Fists,
        Radius = 0
    };

    public CharacterDbEntry ()
    {
        LookType = 0x7;
        IsTurnedOff = 0x9;
        CurrentSatiety = 50;
        MaxSatiety = 100;
        MaxHP = (ushort) WithSatietyMaxHpBonus (MaxHPBase);
        CurrentHP = MaxHP;
        CurrentMP = (ushort) MaxMPBase;
        MaxMP = (ushort) MaxMPBase;
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
    [BsonRef ("Clans")] public ClanDbEntry? Clan { get; set; } = ClanDbEntry.DefaultClanDbEntry;
    public byte FaceType { get; set; }
    public byte HairStyle { get; set; }
    public byte HairColor { get; set; }
    public byte Tattoo { get; set; }
    public byte BootModelId { get; set; }
    public byte PantsModelId { get; set; }
    public byte ArmorModelId { get; set; }

    /// <summary>
    /// Magical chest armour has its own byte in the look block; physical armour is ArmorModelId.
    /// </summary>
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
    public Dictionary<BelongingSlot, int> Items { get; set; } = new ();
    public int PAtk { get; set; }
    public int MAtk { get; set; }

    public int MainHandPAtk { get; set; }
    public int MainHandMAtk { get; set; }
    public bool HoldsItemInHand { get; set; }

    /// <summary>
    /// Empty hand is Fists, and radius stays 0
    /// </summary>
    public ItemDbEntry GetHeldItem ()
    {
        if (!Items.TryGetValue (BelongingSlot.MainHand, out var heldItemId))
        {
            return Fists;
        }

        return DbConnection.Items.FindById (heldItemId) ?? Fists;
    }

    /// <summary>
    /// Everything a swing hits with: worn bonuses plus whatever is in the hand.
    /// </summary>
    public int MeleePAtk => HoldsItemInHand && MainHandPAtk == 0 ? 0 : PAtk + MainHandPAtk;
    // Stuff that has no inherent magic attack doesn't roll magic damage
    public int MagicMAtk => MainHandMAtk == 0 ? 0 : MainHandMAtk + MAtk;

    /// <summary>
    /// MainHand is a second claim, so the item stays in a bag cell; a leftover bag claim comes back
    /// blank
    /// </summary>
    public void PlaceItemInSlot (BelongingSlot slot, int itemId)
    {
        var touchedGuild = slot == BelongingSlot.Guild;
        if (slot != BelongingSlot.MainHand)
        {
            foreach (var heldIn in Items.Where (x => x.Value == itemId && x.Key != BelongingSlot.MainHand)
                         .Select (x => x.Key).ToList ())
            {
                touchedGuild |= heldIn == BelongingSlot.Guild;
                Items.Remove (heldIn);
            }
        }

        Items[slot] = itemId;
        if (touchedGuild)
        {
            SyncGuildFromWornEmblem ();
        }

        ClientStateEvents.RaiseCharacterChanged (ClientIndex);
    }

    /// <summary>
    /// Guild and rank come from a type-Guild emblem's game id; anything else, or empty, means none
    /// </summary>
    public bool SyncGuildFromWornEmblem ()
    {
        var guild = Guild.None;
        var rankMinusOne = 0;
        if (Items.TryGetValue (BelongingSlot.Guild, out var itemId)
            && DbConnection.Items.FindById (itemId) is { GameObjectType: GameObjectType.Guild } item)
        {
            GuildCatalog.TryParseMembershipGameId (item.GameId, out guild, out rankMinusOne);
        }

        if (Guild == guild && GuildLevelMinusOne == rankMinusOne)
        {
            return false;
        }

        Guild = guild;
        GuildLevelMinusOne = rankMinusOne;
        return true;
    }

    public int KarmaCount { get; set; }

    public int MaxHPBase => HealthAtTitle[TitleMinusOne % 60] + HealthAtDegree[DegreeMinusOne % 60] - 100;
    public int MaxMPBase => MpAtTitle[TitleMinusOne % 60] + MpAtDegree[DegreeMinusOne % 60] - 100;

    /// <summary>
    /// 0-33 is +5% max HP, 34-66 is +10%, 67+ is +15%
    /// </summary>
    public int SatietyMaxHpBonusPercent => CurrentSatiety switch
    {
        <= 33 => 5,
        <= 66 => 10,
        _ => 15
    };

    public int WithSatietyMaxHpBonus (int hpMax) =>
        hpMax + hpMax * SatietyMaxHpBonusPercent / 100;
    public ulong XpToLevelUp => GetXpToLevelUp ();
    public Vector3 Origin => new ((float) X, (float) Y, (float) Z);

    /// <summary>
    /// Any title or degree delta. Does not persist or push
    /// </summary>
    public bool LevelUp (int newTitleLevel, int newDegreeLevel)
    {
        newTitleLevel = Math.Clamp (newTitleLevel, 0, MaxLevelMinusOne);
        newDegreeLevel = Math.Clamp (newDegreeLevel, 0, MaxLevelMinusOne);
        if (newTitleLevel == TitleMinusOne && newDegreeLevel == DegreeMinusOne)
        {
            return false;
        }

        TitleMinusOne = newTitleLevel;
        DegreeMinusOne = newDegreeLevel;
        RecalcAvailableStats ();
        RecalcCurrentStats ();
        return true;
    }

    /// <summary>
    /// Kill awards stop at display 60; admin overflow goes into rebirth. Does not persist or push
    /// </summary>
    public bool ApplyExperience (bool isTitle, uint newXp, bool allowRebirth = true)
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
            var atCycleCap = level % LevelsPerCycle == LevelsPerCycle - 1;
            var cost = XpToLevelUp;
            var xp = isTitle ? TitleXP : DegreeXP;

            if (!allowRebirth && atCycleCap)
            {
                var clamped = cost >= uint.MaxValue ? xp : Math.Min (xp, (uint) cost);
                if (isTitle)
                {
                    TitleXP = clamped;
                }
                else
                {
                    DegreeXP = clamped;
                }

                break;
            }

            if (level >= MaxLevelMinusOne || cost > xp)
            {
                break;
            }

            if (isTitle)
            {
                TitleXP -= (uint) cost;
                TitleMinusOne++;
            }
            else
            {
                DegreeXP -= (uint) cost;
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
            RecalcAvailableStats ();
            RecalcCurrentStats ();
        }
        else
        {
            ClientStateEvents.RaiseCharacterChanged (ClientIndex);
        }

        return true;
    }

    /// <summary>
    /// Rebirth stays off. Does not persist or push
    /// </summary>
    public bool AwardExperience (uint amount, bool isTitle = true)
    {
        if (amount == 0)
        {
            return false;
        }

        if (isTitle)
        {
            var titleXp = TitleXP > uint.MaxValue - amount ? uint.MaxValue : TitleXP + amount;
            return ApplyExperience (true, titleXp, allowRebirth: false);
        }

        var degreeXp = DegreeXP > uint.MaxValue - amount ? uint.MaxValue : DegreeXP + amount;
        return ApplyExperience (false, degreeXp, allowRebirth: false);
    }

    /// <summary>
    /// Only this cycle's grants (tables start at 0) plus rebirths times StatBonusForResets. Does
    /// not persist or push
    /// </summary>
    public void RecalcAvailableStats ()
    {
        var title = 0;
        var degree = 0;
        AddCurrentCycleGrants (TitleMinusOne, titleIsPrimary: true, ref title, ref degree);
        AddCurrentCycleGrants (DegreeMinusOne, titleIsPrimary: false, ref title, ref degree);
        AvailableTitleStats = title - (BaseStrength + BaseAgility + BaseAccuracy + BaseEndurance);
        AvailableDegreeStats = degree - (BaseEarth + BaseAir + BaseWater + BaseFire);
    }

    /// <summary>
    /// The leveling track is primary; bonus is rebirths times StatBonusForResets per level in this
    /// cycle
    /// </summary>
    private static void AddCurrentCycleGrants (int minusOne, bool titleIsPrimary, ref int title, ref int degree)
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

    public void SetKarmaCount (int value)
    {
        KarmaCount = value;
        SyncKarmaFromCount ();
    }

    /// <summary>
    /// Very bad +1, bad 0, neutral -10, good -20, benign -40, clamped to [-5000, 5000]. Does not
    /// persist or push
    /// </summary>
    public bool ApplyKillKarma (KarmaTypes mobKarma, out bool tierChanged)
    {
        tierChanged = false;
        var delta = mobKarma switch
        {
            KarmaTypes.Очень_Плохая => 1,
            KarmaTypes.Плохая => 0,
            KarmaTypes.Нейтральная => -10,
            KarmaTypes.Хорошая => -20,
            KarmaTypes.Благая => -40,
            _ => 0
        };
        if (delta == 0)
        {
            return false;
        }

        var oldTier = Karma;
        var oldCount = KarmaCount;
        SetKarmaCount (KarmaCount + delta);
        tierChanged = Karma != oldTier;
        return KarmaCount != oldCount;
    }

    /// <summary>
    /// Self-kill penalty is -300, clamped to [-5000, 5000]. Does not persist or push
    /// </summary>
    public bool ApplySelfKillKarma (out bool tierChanged)
    {
        const int selfKillKarmaDelta = -300;
        var oldTier = Karma;
        var oldCount = KarmaCount;
        SetKarmaCount (KarmaCount + selfKillKarmaDelta);
        tierChanged = Karma != oldTier;
        return KarmaCount != oldCount;
    }

    /// <summary>
    /// KarmaCount clamps to [-5000, 5000]; tiers at -1000, -100, 100, 1000
    /// </summary>
    public void SyncKarmaFromCount ()
    {
        KarmaCount = Math.Clamp (KarmaCount, -5000, 5000);
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
    /// The same delta hits Base so gear bonuses stay; no remaining-points check. Does not persist
    /// or push
    /// </summary>
    public bool ApplyCurrentStatEdit (Stat stat, int newCurrentValue)
    {
        var oldCurrent = GetCurrentStat (stat);
        var delta = newCurrentValue - oldCurrent;
        if (delta == 0)
        {
            return false;
        }

        SetBaseStat (stat, GetBaseStat (stat) + delta);
        if (IsTitleStat (stat))
        {
            AvailableTitleStats -= delta;
        }
        else if (IsDegreeStat (stat))
        {
            AvailableDegreeStats -= delta;
        }
        else
        {
            return false;
        }

        RecalcCurrentStats ();
        return true;
    }

    /// <summary>
    /// Negatives become 0; false and no change when either pool cannot cover its total
    /// </summary>
    public bool TrySpendStatPoints (int strength, int agility, int accuracy, int endurance,
        int earth, int air, int water, int fire)
    {
        strength = Math.Max (0, strength);
        agility = Math.Max (0, agility);
        accuracy = Math.Max (0, accuracy);
        endurance = Math.Max (0, endurance);
        earth = Math.Max (0, earth);
        air = Math.Max (0, air);
        water = Math.Max (0, water);
        fire = Math.Max (0, fire);

        var title = strength + agility + accuracy + endurance;
        var degree = earth + air + water + fire;
        if (title > AvailableTitleStats || degree > AvailableDegreeStats)
        {
            return false;
        }

        BaseStrength += strength;
        BaseAgility += agility;
        BaseAccuracy += accuracy;
        BaseEndurance += endurance;
        BaseEarth += earth;
        BaseAir += air;
        BaseWater += water;
        BaseFire += fire;
        AvailableTitleStats -= title;
        AvailableDegreeStats -= degree;
        RecalcCurrentStats ();
        PersistRow ();
        return true;
    }

    private static readonly object PersistGate = new ();

    /// <summary>
    /// Insert or replace this character, then patch vitals and the slot map under one lock.
    /// </summary>
    public void PersistAll ()
    {
        lock (PersistGate)
        {
            if (Id == 0)
            {
                Id = DbConnection.Characters.Insert (this);
            }
            else if (!DbConnection.Characters.Update (this))
            {
                DbConnection.Characters.Upsert (this);
            }

            PatchVitalsUnlocked ();
            PatchSlotMapUnlocked ();
            DbConnection.Checkpoint ();
        }
    }

    /// <summary>
    /// Writes the row even in the starting dungeon
    /// </summary>
    private void PersistRow ()
    {
        PersistAll ();
    }

    /// <summary>
    /// HP, MP, and satiety by field name, because a full entity Update is flaky for regen
    /// </summary>
    public void PersistVitals ()
    {
        if (Id == 0)
        {
            return;
        }

        lock (PersistGate)
        {
            if (!PatchVitalsUnlocked ())
            {
                if (!DbConnection.Characters.Update (this))
                {
                    DbConnection.Characters.Upsert (this);
                }
            }

            DbConnection.Checkpoint ();
        }
    }

    private bool PatchVitalsUnlocked ()
    {
        var col = DbConnection.Db.GetCollection ("Characters");
        var doc = col.FindById (Id);
        if (doc is null)
        {
            return false;
        }

        doc["CurrentHP"] = (int) CurrentHP;
        doc["CurrentMP"] = (int) CurrentMP;
        doc["MaxHP"] = (int) MaxHP;
        doc["MaxMP"] = (int) MaxMP;
        doc["CurrentSatiety"] = (int) CurrentSatiety;
        doc["MaxSatiety"] = (int) MaxSatiety;
        col.Update (doc);
        return true;
    }

    private void PatchSlotMapUnlocked ()
    {
        var col = DbConnection.Db.GetCollection ("Characters");
        var doc = col.FindById (Id);
        if (doc is null)
        {
            return;
        }

        var items = new BsonDocument ();
        foreach (var (slot, itemId) in Items)
        {
            items[slot.ToString ()] = itemId;
        }

        doc["Items"] = items;
        col.Update (doc);
    }

    public static bool IsTitleStat (Stat stat) =>
        stat is Stat.Strength or Stat.Agility or Stat.Accuracy or Stat.Endurance;

    public static bool IsDegreeStat (Stat stat) =>
        stat is Stat.Earth or Stat.Air or Stat.Water or Stat.Fire;

    public int GetCurrentStat (Stat stat) => stat switch
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

    public int GetBaseStat (Stat stat) => stat switch
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

    private void SetBaseStat (Stat stat, int value)
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

    /// <summary>
    /// Keeps id, name, clan, visuals, and position. Does not persist or push
    /// </summary>
    public void ResetToNewCharacterDefaults ()
    {
        foreach (var itemId in Items.Values.Distinct ())
        {
            DbConnection.Items.Delete (itemId);
        }

        Items.Clear ();

        var fresh = CreateNewCharacter (ClientIndex, Name, IsGenderFemale, FaceType, HairStyle, HairColor, Tattoo);
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
        MainHandMAtk = fresh.MainHandMAtk;
        HoldsItemInHand = fresh.HoldsItemInHand;
        BootModelId = fresh.BootModelId;
        PantsModelId = fresh.PantsModelId;
        ArmorModelId = fresh.ArmorModelId;
        RobeModelId = fresh.RobeModelId;
        ShieldModelId = fresh.ShieldModelId;
        HelmetModelId = fresh.HelmetModelId;
        GlovesModelId = fresh.GlovesModelId;

        RecalcAvailableStats ();
        RecalcCurrentStats ();
        CurrentHP = MaxHP;
        CurrentMP = MaxMP;
    }

    public bool HasEmptyInventorySlot (GameObjectType gameObjectType = GameObjectType.Unknown)
    {
        return FindEmptyInventorySlot () != null;
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
            if (IsItemSlotEmpty (slot))
            {
                return slot;
            }
        }

        return null;
    }

    public bool IsItemSlotEmpty (BelongingSlot belongingSlot)
    {
        return !Items.ContainsKey (belongingSlot);
    }

    private ulong GetXpToLevelUp ()
    {
        var title = TitleMinusOne % 60;
        var degree = DegreeMinusOne % 60;
        if (title == 59 && degree == 59)
        {
            return 1;
        }

        var minLevel = Math.Min (title, degree);
        var maxLevel = Math.Max (title, degree);
        return (ulong) (XpPerLevelBase[maxLevel] + XpPerLevelDelta[maxLevel] * minLevel);
    }

    /// <summary>
    /// Checked against base stats, because current stats are rebuilt from worn gear during this
    /// </summary>
    public bool CanUseItem (ItemDbEntry itemDbEntry)
    {
        return UnmetRequirement (itemDbEntry) is null;
    }

    /// <summary>
    /// First failed requirement in the client's refusal wording, or null when the item can be used
    /// </summary>
    public string? UnmetRequirement (ItemDbEntry itemDbEntry)
    {
        itemDbEntry.RecalculateStatReqsFromBase ();

        if (itemDbEntry.IsGuildMembershipEmblem)
        {
            if (TitleMinusOne < itemDbEntry.TitleMinusOne)
            {
                return $"Титул {TitleMinusOne}<{itemDbEntry.TitleMinusOne}";
            }

            if (DegreeMinusOne < itemDbEntry.DegreeMinusOne)
            {
                return $"Степень {DegreeMinusOne}<{itemDbEntry.DegreeMinusOne}";
            }

            return null;
        }

        if (!MeetsItemGuildRequirement (itemDbEntry))
        {
            return Guild != itemDbEntry.RequiredGuild
                ? "Гильдия"
                : $"Ранг гильдии {GuildLevelMinusOne}<{itemDbEntry.RequiredGuildRankMinusOne}";
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
            if (need > 0 && have < need)
            {
                return $"{name} {have}<{need}";
            }
        }

        return null;
    }

    public bool MeetsItemGuildRequirement (ItemDbEntry item)
    {
        if (item.RequiredGuild is Guild.None)
        {
            return true;
        }

        return Guild == item.RequiredGuild && GuildLevelMinusOne >= item.RequiredGuildRankMinusOne;
    }

    public bool RecalcCurrentStats ()
    {
        var slotsToUpdate = new HashSet<BelongingSlot>
        {
            BelongingSlot.Amulet, BelongingSlot.Belt, BelongingSlot.Boots, BelongingSlot.Chestplate,
            BelongingSlot.Gloves, BelongingSlot.Guild, BelongingSlot.Helmet, BelongingSlot.Pants,
            BelongingSlot.Ring_1, BelongingSlot.Ring_2, BelongingSlot.Ring_3, BelongingSlot.Ring_4,
            BelongingSlot.Shield, BelongingSlot.BraceletLeft, BelongingSlot.BraceletRight,
            BelongingSlot.Special_1, BelongingSlot.Special_2, BelongingSlot.Special_3, BelongingSlot.Special_4,
            BelongingSlot.Special_5, BelongingSlot.Special_6, BelongingSlot.Special_7
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
            if (!Items.ContainsKey (slot))
            {
                continue;
            }

            var item = DbConnection.Items.FindById (Items[slot]);
            if (item is null || !CanUseItem (item))
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
            // Positive *UpNegative is attack-up and is stored flipped
            patk += item.PAtkUpNegative > 0 ? -item.PAtkUpNegative : item.PAtkUpNegative;
            matk += item.MAtkUpNegative > 0 ? -item.MAtkUpNegative : item.MAtkUpNegative;
        }

        hpMax = WithSatietyMaxHpBonus (hpMax);

        // Sits after the slot loop so the look matches this move, not the previous one
        CharacterWornLook.Apply (this);

        // The stat packet omits held-item attack: the client reads the item column, not the worn
        // +attack column
        var heldPAtk = 0;
        var heldMAtk = 0;
        var holdsItem = false;

        if (Items.TryGetValue (BelongingSlot.MainHand, out var heldItemId))
        {
            var heldItem = DbConnection.Items.FindById (heldItemId);
            if (heldItem is not null && CanUseItem (heldItem))
            {
                holdsItem = true;
                heldPAtk = heldItem.PAtkNegative;
                heldMAtk = heldItem.MAtkNegativeOrHeal;
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
        CurrentHP = (ushort) Math.Min (CurrentHP, hpMax);
        CurrentMP = (ushort) Math.Min (CurrentMP, mpMax);
        MaxHP = (ushort) hpMax;
        MaxMP = (ushort) mpMax;
        PDef = (ushort) pdef;
        MDef = (ushort) mdef;
        // Signed: damage is stored negative, and a ushort cast wraps added attack into a huge
        // positive
        PAtk = patk;
        MAtk = matk;
        MainHandPAtk = heldPAtk;
        MainHandMAtk = heldMAtk;
        HoldsItemInHand = holdsItem;

        SphLogger.Info ($"Client {ClientLocalId} new stats after recalc: " +
                       $"STR {CurrentStrength} AGI {CurrentAgility} ACC {CurrentAccuracy} END {CurrentEndurance} EAR {CurrentEarth} " +
                       $"WAT {CurrentWater} AIR {CurrentAir} FIR {CurrentFire} HP {CurrentHP}/{MaxHP} MP {CurrentMP}/{MaxMP} " +
                       $"PD {PDef} MD {MDef} PA {PAtk} MA {MAtk} hand PA {MainHandPAtk} hand MA {MainHandMAtk}");

        ClientStateEvents.RaiseCharacterChanged (ClientIndex);
        return true;
    }
}
