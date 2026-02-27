using static GameObjectType;
using static ItemSuffix;

public static class SphObjectDbHelper
{
    public static SphGameObject GetSuffixObject(GameObjectType objectType, ItemSuffix suffix, int tier = 0)
    {
        // TODO: calc proper values
        var prefType = GameObjectToPrefTypeMap.GetValueOrDefault(objectType, Unknown);

        if (prefType == Unknown || !SphObjectDb.SuffixDataDb.TryGetValue(prefType, out Dictionary<ItemSuffix, SphGameObject>? value) ||
            !value.ContainsKey(suffix))
        {
            return new SphGameObject();
        }

        // TODO: if perf is impacted switch to direct field assignment instead of reflection
        var suffixObj = SphGameObject.CreateFromGameObject(value[suffix]);
        var tierScale = BaseItemStatScale[tier];

        suffixObj.Tier = tier;
        suffixObj.StrengthReq *= tierScale;
        suffixObj.AgilityReq *= tierScale;
        suffixObj.AccuracyReq *= tierScale;
        suffixObj.EnduranceReq *= tierScale;
        suffixObj.EarthReq *= tierScale;
        suffixObj.WaterReq *= tierScale;
        suffixObj.AirReq *= tierScale;
        suffixObj.FireReq *= tierScale;
        suffixObj.StrengthUp *= tierScale;
        suffixObj.AgilityUp *= tierScale;
        suffixObj.AccuracyUp *= tierScale;
        suffixObj.EnduranceUp *= tierScale;
        suffixObj.EarthUp *= tierScale;
        suffixObj.WaterUp *= tierScale;
        suffixObj.AirUp *= tierScale;
        suffixObj.FireUp *= tierScale;
        suffixObj.MaxHpUp *= tierScale;
        suffixObj.MaxMpUp *= tierScale;
        suffixObj.PDefUp *= tierScale;
        suffixObj.MDefUp *= tierScale;
        suffixObj.PAtkUpNegative *= tierScale;
        suffixObj.PAtkNegative *= tierScale;
        suffixObj.MAtkUpNegative *= tierScale;
        suffixObj.MAtkNegativeOrHeal *= tierScale;
        suffixObj.VendorCost *= tierScale;

        return suffixObj;
    }

    public static readonly ItemSuffix[] AxeSwordSuffixes =
    {
        Exhaustion, Valor, Damage, Cruelty, Haste, Speed, Disorder, Chaos, Ether, Fatigue, Disease, Instability,
        ItemSuffix.Range, Distance, Decay, Devastation, Weakness, Penetration, Interdict, Value
    };

    public static readonly ItemSuffix[] CrossbowSuffixes =
    {
        Exhaustion, Valor, Damage, Cruelty, Haste, Speed, Disorder, Chaos, Penetration, Instability, Decay,
        ItemSuffix.Range, Distance, Mastery, Fatigue, Ether, Radiance, Disease, Value
    };

    public static readonly ItemSuffix[] ChestplateSuffixes =
    {
        Valor, Durability, Absorption, Deflection, Safety, Invincibility, Health, Life, Meditation, Prana,
        Strength_Old, Agility_Old, Majesty_Old, Concentration_Old, Earth, Water, Air, Fire, Elements, Value, Strength,
        Agility, Majesty, Concentration, Integrity
    };

    public static readonly ItemSuffix[] BeltBootsGlovesHelmetPantsSuffixes =
    {
        Durability, Absorption, Safety, Health, Meditation, Precision, Ether, Value
    };

    public static readonly ItemSuffix[] AmuletBraceletSuffixes =
    {
        Durability, Absorption, Deflection, Safety, Health, Meditation, Precision, Ether, Radiance, Value, Damage
    };

    public static readonly ItemSuffix[] RingSuffixes =
    {
        Durability, Absorption, Safety, Health, Life, Meditation, Prana, Ether, Precision, Strength, Agility, Accuracy,
        Endurance, Earth, Water, Air, Fire, Value
    };

    public static readonly ItemSuffix[] RobeSuffixes =
    {
        Value, Durability, Deflection, Safety, Health, Life, Meditation, Prana, Earth, Water, Air, Fire, Ether,
        Eclipse, Archmage, Durability, Deflection, Safety, Health, Life, Meditation, Prana, Ether, Dragon
    };

    public static readonly ItemSuffix[] CastleSuffixes =
    {
        Eradication_Old, Devastation_Castle_Old, Reliability_Old, Invincibility_Castle_Old, Life_Castle_Old,
        Eradication, Devastation_Castle, Reliability, Invincibility_Castle, Life_Castle, Rule, Blinding, Fright, Halt,
        Deliverance, Purification, Punishment, Shackle, Whirl, Curse
    };

    public static readonly ItemSuffix[] ShieldSuffixes =
    {
        Valor, Durability, Absorption, Deflection, Safety, Invincibility, Health, Life, Meditation, Prana, Strength_Old,
        Agility_Old, Majesty_Old, Concentration_Old, Earth, Water, Air, Fire, Elements_Old, Value, Strength, Agility,
        Majesty, Concentration, Integrity, Elements, Elements_New
    };

    public static readonly ItemSuffix[] QuestSuffixes =
    {
        Adventure, Silence, Prophecy, Secret, Myth, Being, Hike, Existence, Legend, Peace
    };

    public static readonly int[] BaseItemStatScale = { 0, 1, 2, 3, 5, 7, 9, 12, 15, 18, 23, 28, 34, 37, 42, 48 };

    public static readonly Dictionary<GameObjectType, ItemSuffix[]> TypeToSuffixIdMap = new()
    {
        [Pref_AxeSword] = AxeSwordSuffixes,
        [Pref_Crossbow] = CrossbowSuffixes,
        [Pref_Chestplate] = ChestplateSuffixes,
        [Pref_BeltBootsGlovesHelmetPants] = BeltBootsGlovesHelmetPantsSuffixes,
        [Pref_AmuletBracelet] = AmuletBraceletSuffixes,
        [Pref_Ring] = RingSuffixes,
        [Pref_Robe] = RobeSuffixes,
        [Pref_Castle] = CastleSuffixes,
        [Pref_Shield] = ShieldSuffixes,
        [Pref_Quest] = QuestSuffixes
    };

    public static readonly Dictionary<GameObjectType, GameObjectType> GameObjectToPrefTypeMap = new()
    {
        // makes no sense for prefs themselves
        [Pref_AxeSword] = Unknown,
        [Pref_Crossbow] = Unknown,
        [Pref_Chestplate] = Unknown,
        [Pref_BeltBootsGlovesHelmetPants] = Unknown,
        [Pref_AmuletBracelet] = Unknown,
        [Pref_Ring] = Unknown,
        [Pref_Robe] = Unknown,
        [Pref_Castle] = Unknown,
        [Pref_Shield] = Unknown,
        [Pref_Quest] = Unknown,
        [Flower] = Unknown,
        [Metal] = Unknown,
        [Mineral] = Unknown,
        [Amulet] = Pref_AmuletBracelet,
        [Amulet_Unique] = Pref_Castle,
        [Chestplate] = Pref_Chestplate,
        [Robe] = Pref_Robe,
        [Robe_Quest] = Pref_Quest,
        [Robe_Unique] = Pref_Castle,
        [Chestplate_Quest] = Pref_Quest,
        [Chestplate_Unique] = Pref_Castle,
        [Belt] = Pref_BeltBootsGlovesHelmetPants,
        [Belt_Quest] = Pref_Quest,
        [Belt_Unique] = Pref_Castle,
        [Bracelet] = Pref_AmuletBracelet,
        [Bracelet_Unique] = Pref_Castle,
        [Gloves] = Pref_BeltBootsGlovesHelmetPants,
        [Gloves_Quest] = Pref_Quest,
        [Gloves_Unique] = Pref_Castle,
        [Helmet] = Pref_BeltBootsGlovesHelmetPants,
        [Helmet_Premium] = Unknown,
        [Helmet_Quest] = Pref_Quest,
        [Helmet_Unique] = Pref_Castle,
        [Pants] = Pref_BeltBootsGlovesHelmetPants,
        [Pants_Quest] = Pref_Quest,
        [Pants_Unique] = Pref_Castle,
        [Ring] = Pref_Ring,
        [Ring_Special] = Unknown,
        [Ring_Unique] = Pref_Castle,
        [Shield] = Pref_Shield,
        [Shield_Quest] = Pref_Quest,
        [Shield_Unique] = Pref_Castle,
        [Boots] = Pref_BeltBootsGlovesHelmetPants,
        [Boots_Quest] = Pref_Quest,
        [Boots_Unique] = Pref_Castle,
        [Castle_Crystal] = Unknown,
        [Castle_Stone] = Unknown,
        [Guild_Bag] = Unknown,
        [Flag] = Unknown,
        [Guild] = Unknown,
        [Letter] = Unknown,
        [Lottery] = Unknown,
        [MantraBlack] = Unknown,
        [MantraWhite] = Unknown,
        [Monster] = Unknown,
        [Monster_Castle_Stone] = Unknown,
        [Monster_Event] = Unknown,
        [Monster_Event_Flying] = Unknown,
        [Monster_Flying] = Unknown,
        [Monster_Tower_Spirit] = Unknown,
        [Monster_Castle_Spirit] = Unknown,
        [Elixir_Castle] = Unknown,
        [Elixir_Trap] = Unknown,
        [Powder] = Unknown,
        [Powder_Area] = Unknown,
        [Powder_Event] = Unknown,
        [Powder_Guild] = Unknown,
        [Scroll] = Unknown,
        [Special] = Unknown,
        [Special_Crusader_Gapclose] = Unknown,
        [Special_Inquisitor_Teleport] = Unknown,
        [Special_Archmage_Teleport] = Unknown,
        [Special_MasterOfSteel_Whirlwind] = Unknown,
        [Special_Druid_Wolf] = Unknown,
        [Special_Thief_Steal] = Unknown,
        [Special_MasterOfSteel_Suicide] = Unknown,
        [Special_Necromancer_Flyer] = Unknown,
        [Special_Necromancer_Resurrection] = Unknown,
        [Special_Necromancer_Zombie] = Unknown,
        [Special_Bandier_Flag] = Unknown,
        [Special_Bandier_DispelControl] = Unknown,
        [Special_Bandier_Fortify] = Unknown,
        [Key] = Unknown,
        [Map] = Unknown,
        [Ear_String] = Unknown,
        [Crystal] = Unknown,
        [Crossbow] = Pref_Crossbow,
        [Crossbow_Quest] = Pref_Quest,
        [Axe] = Pref_AxeSword,
        [Axe_Quest] = Pref_Quest,
        [Sword] = Pref_AxeSword,
        [Sword_Quest] = Pref_Quest,
        [Sword_Unique] = Pref_Castle,
        [X2_Degree] = Unknown,
        [X2_Both] = Unknown,
        [X2_Title] = Unknown,
        [Ear] = Unknown,
        [Bead] = Unknown,
        [Packet] = Unknown,
        [Unknown] = Unknown,
        [Client] = Unknown,
        [LootBag] = Unknown
    };
}