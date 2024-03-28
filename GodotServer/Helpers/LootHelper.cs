using System;
using System.Collections.Generic;
using System.Linq;
using SphServer;
using SphServer.DataModels;

public static class LootHelper
{
    private static bool firstTypeRolled;

    public static ObjectType GetPacketObjectType (this GameObjectType gameObjectType)
    {
        return gameObjectType switch
        {
            GameObjectType.Amulet => ObjectType.ArmorAmulet,
            GameObjectType.Chestplate => ObjectType.ArmorChest,
            GameObjectType.Axe => ObjectType.WeaponAxe,
            GameObjectType.Bead => ObjectType.Bead,
            GameObjectType.Belt => ObjectType.ArmorBelt,
            GameObjectType.Boots => ObjectType.ArmorBoots,
            GameObjectType.Bracelet => ObjectType.ArmorBracelet,
            GameObjectType.Crossbow => ObjectType.WeaponCrossbow,
            GameObjectType.Pref_AxeSword => ObjectType.Unknown,
            GameObjectType.Pref_Crossbow => ObjectType.Unknown,
            GameObjectType.Pref_Chestplate => ObjectType.Unknown,
            GameObjectType.Pref_BeltBootsGlovesHelmetPants => ObjectType.Unknown,
            GameObjectType.Pref_AmuletBracelet => ObjectType.Unknown,
            GameObjectType.Pref_Ring => ObjectType.Unknown,
            GameObjectType.Pref_Robe => ObjectType.Unknown,
            GameObjectType.Pref_Castle => ObjectType.Unknown,
            GameObjectType.Pref_Shield => ObjectType.Unknown,
            GameObjectType.Pref_Quest => ObjectType.Unknown,
            GameObjectType.Flower => ObjectType.AlchemyPlant,
            GameObjectType.Metal => ObjectType.AlchemyMetal,
            GameObjectType.Mineral => ObjectType.AlchemyMineral,
            GameObjectType.Amulet_Unique => ObjectType.ArmorAmulet,
            GameObjectType.Robe => ObjectType.ArmorRobe,
            GameObjectType.Robe_Quest => ObjectType.QuestArmorRobe,
            GameObjectType.Robe_Unique => ObjectType.ArmorRobe,
            GameObjectType.Chestplate_Quest => ObjectType.QuestArmorChest,
            GameObjectType.Chestplate_Unique => ObjectType.ArmorChest,
            GameObjectType.Belt_Quest => ObjectType.QuestArmorBelt,
            GameObjectType.Belt_Unique => ObjectType.ArmorBelt,
            GameObjectType.Bracelet_Unique => ObjectType.ArmorBracelet,
            GameObjectType.Gloves => ObjectType.ArmorGloves,
            GameObjectType.Gloves_Quest => ObjectType.QuestArmorGloves,
            GameObjectType.Gloves_Unique => ObjectType.ArmorGloves,
            GameObjectType.Helmet => ObjectType.ArmorHelmet,
            GameObjectType.Helmet_Premium => ObjectType.ArmorHelmetPremium,
            GameObjectType.Helmet_Quest => ObjectType.QuestArmorHelmet,
            GameObjectType.Helmet_Unique => ObjectType.ArmorHelmet,
            GameObjectType.Pants => ObjectType.ArmorPants,
            GameObjectType.Pants_Quest => ObjectType.QuestArmorPants,
            GameObjectType.Pants_Unique => ObjectType.ArmorPants,
            GameObjectType.Ring => ObjectType.Ring,
            // TODO: change maybe
            GameObjectType.Ring_Special => ObjectType.RingDiamond,
            GameObjectType.Ring_Unique => ObjectType.Ring,
            GameObjectType.Shield => ObjectType.ArmorShield,
            GameObjectType.Shield_Quest => ObjectType.QuestArmorShield,
            GameObjectType.Shield_Unique => ObjectType.ArmorShield,
            GameObjectType.Boots_Quest => ObjectType.QuestArmorBoots,
            GameObjectType.Boots_Unique => ObjectType.ArmorBoots,
            // TODO: change maybe
            GameObjectType.Castle_Crystal => ObjectType.SeedCastle,
            // TODO: change maybe
            GameObjectType.Castle_Stone => ObjectType.SeedCastle,
            GameObjectType.Guild_Bag => ObjectType.Sack,
            GameObjectType.Flag => ObjectType.Unknown,
            GameObjectType.Guild => ObjectType.Unknown,
            GameObjectType.Letter => ObjectType.Unknown,
            GameObjectType.Lottery => ObjectType.Unknown,
            GameObjectType.MantraBlack => ObjectType.MantraBlack,
            GameObjectType.MantraWhite => ObjectType.MantraWhite,
            GameObjectType.Monster => ObjectType.Unknown,
            GameObjectType.Monster_Castle_Stone => ObjectType.Unknown,
            GameObjectType.Monster_Event => ObjectType.Unknown,
            GameObjectType.Monster_Event_Flying => ObjectType.Unknown,
            GameObjectType.Monster_Flying => ObjectType.Unknown,
            GameObjectType.Monster_Tower_Spirit => ObjectType.Unknown,
            GameObjectType.Monster_Castle_Spirit => ObjectType.Unknown,
            GameObjectType.Elixir_Castle => ObjectType.ElixirCastle,
            GameObjectType.Elixir_Trap => ObjectType.ElixirTrap,
            GameObjectType.Powder => ObjectType.PowderSingleTarget,
            GameObjectType.Powder_Area => ObjectType.PowderAoE,
            GameObjectType.Powder_Event => ObjectType.PowderSingleTarget,
            GameObjectType.Powder_Guild => ObjectType.PowderSingleTarget,
            GameObjectType.Scroll => ObjectType.ScrollLegend,
            GameObjectType.Special => ObjectType.Unknown,
            GameObjectType.Special_Crusader_Gapclose => ObjectType.Unknown,
            GameObjectType.Special_Inquisitor_Teleport => ObjectType.Unknown,
            GameObjectType.Special_Archmage_Teleport => ObjectType.Unknown,
            GameObjectType.Special_MasterOfSteel_Whirlwind => ObjectType.Unknown,
            GameObjectType.Special_Druid_Wolf => ObjectType.Unknown,
            GameObjectType.Special_Thief_Steal => ObjectType.Unknown,
            GameObjectType.Special_MasterOfSteel_Suicide => ObjectType.Unknown,
            GameObjectType.Special_Necromancer_Flyer => ObjectType.Unknown,
            GameObjectType.Special_Necromancer_Resurrection => ObjectType.Unknown,
            GameObjectType.Special_Necromancer_Zombie => ObjectType.Unknown,
            GameObjectType.Special_Bandier_Flag => ObjectType.Unknown,
            GameObjectType.Special_Bandier_DispelControl => ObjectType.Unknown,
            GameObjectType.Special_Bandier_Fortify => ObjectType.Unknown,
            GameObjectType.Key => ObjectType.Key,
            GameObjectType.Map => ObjectType.Map,
            GameObjectType.Ear_String => ObjectType.EarString,
            GameObjectType.Crystal => ObjectType.Unknown,
            GameObjectType.Crossbow_Quest => ObjectType.QuestWeaponCrossbow,
            GameObjectType.Axe_Quest => ObjectType.QuestWeaponAxe,
            GameObjectType.Sword => ObjectType.WeaponSword,
            GameObjectType.Sword_Quest => ObjectType.QuestWeaponSword,
            GameObjectType.Sword_Unique => ObjectType.WeaponSword,
            GameObjectType.X2_Degree => ObjectType.XpPillDegree,
            GameObjectType.X2_Both => ObjectType.XpPillDegree,
            GameObjectType.X2_Title => ObjectType.XpPillDegree,
            GameObjectType.Ear => ObjectType.Ear,
            GameObjectType.Packet => ObjectType.Unknown,
            GameObjectType.Unknown => ObjectType.Unknown,
            GameObjectType.FoodApple => ObjectType.FoodApple,
            GameObjectType.FoodBread => ObjectType.FoodBread,
            GameObjectType.FoodFish => ObjectType.FoodFish,
            GameObjectType.FoodMeat => ObjectType.FoodMeat,
            GameObjectType.FoodPear => ObjectType.FoodPear,
            GameObjectType.AlchemyBrushwood => ObjectType.AlchemyBrushwood,
            _ => ObjectType.Unknown
        };
    }

    public static SphGameObject GetRandomObjectData (int titleLevelMinusOne, int gameIdOverride = -1)
    {
        var gameIdsToRemove = new HashSet<int>
        {
            4192, 4193, 4194, 4199, 4186, 4187, 4242, 4243, 4244, 4245, 4246, 4247, 4248, 4249, 4302, 4304, 4306, 4308,
            4310, 4312, 4314, 4316, 4318, 4320, 4450, 4740, 4741, 4742, 4743, 4744, 4745, 4746, 4747, 4748, 4749
        };

        SphGameObject item;
        if (gameIdOverride != -1)
        {
            item = MainServer.GameObjectCollection.FindById(gameIdOverride);
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
                GameObjectType.Chestplate,
                GameObjectType.Robe,
                GameObjectType.Belt,
                GameObjectType.Bracelet,
                GameObjectType.Gloves,
                GameObjectType.Helmet,
                GameObjectType.Pants,
                GameObjectType.Ring,
                GameObjectType.Shield,
                GameObjectType.Boots,
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
                GameObjectType.Sword
            };
            var overrideFilter = new HashSet<GameObjectType>
            {
                // GameObjectType.Ring,
                // GameObjectType.Powder
            };

            // if (firstTypeRolled)
            // {
            //     overrideFilter = new HashSet<GameObjectType>
            //     {
            //         GameObjectType.Mineral
            //     };
            // }

            firstTypeRolled = !firstTypeRolled;

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
                GameObjectKind.Sword_New
            };

            var tierAgnosticTypes = new HashSet<GameObjectType>
            {
                GameObjectType.Flower,
                GameObjectType.Metal,
                GameObjectType.Mineral
            };

            var time = DateTime.Now;
            var lootPool = MainServer.SphGameObjectDb
                .Where(x =>
                    !gameIdsToRemove.Contains(x.Key) && kindFilter.Contains(x.Value.ObjectKind) &&
                    typeFilter.Contains(x.Value.ObjectType)
                    && (x.Value.Tier == tierFilter
                        || tierAgnosticTypes.Contains(x.Value.ObjectType))).Select(x => x.Value)
                .ToList();
            var random = MainServer.Rng.Next(0, lootPool.Count);
            item = lootPool.ElementAt(random);
            var collectionItem = MainServer.GameObjectCollection.FindOne(x => x.GameId == item.GameId);
            item.GameObjectDbId = collectionItem.GameObjectDbId;
        }

        var noSuffix = false;
        var suffixFilter = new SortedSet<ItemSuffix> { ItemSuffix.None };

        if (item.ObjectType is GameObjectType.Ring)
        {
            suffixFilter = new SortedSet<ItemSuffix>
            {
                ItemSuffix.Health,
                ItemSuffix.Accuracy,
                ItemSuffix.Air,
                ItemSuffix.Durability,
                ItemSuffix.Life,
                ItemSuffix.Endurance,
                ItemSuffix.Fire,
                ItemSuffix.Absorption,
                ItemSuffix.Meditation,
                ItemSuffix.Strength,
                ItemSuffix.Earth,
                ItemSuffix.Safety,
                ItemSuffix.Prana,
                ItemSuffix.Agility,
                ItemSuffix.Water,
                ItemSuffix.Value,
                ItemSuffix.Precision,
                ItemSuffix.Ether
            };
            item.Suffix = suffixFilter.ElementAt(MainServer.Rng.Next(0, suffixFilter.Count));
            return item;
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
                ItemSuffix.Interdict
            };

            item.Suffix = suffixFilter.ElementAt(MainServer.Rng.Next(0, suffixFilter.Count));
            return item;
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

            item.Suffix = suffixFilter.ElementAt(MainServer.Rng.Next(0, suffixFilter.Count));
            return item;
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
                ItemSuffix.Archmage
                // ItemSuffix.Durability_Old,
                // ItemSuffix.Life_Old,
                // ItemSuffix.Safety_Old,
                // ItemSuffix.Prana_Old,
                // ItemSuffix.Deflection_Old,
                // ItemSuffix.Meditation_Old,
                // ItemSuffix.Health_Old,
                // ItemSuffix.Ether_Old,
            };

            item.Suffix = suffixFilter.ElementAt(MainServer.Rng.Next(0, suffixFilter.Count));
            return item;
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
                ItemSuffix.Damage
            };

            item.Suffix = suffixFilter.ElementAt(MainServer.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        if (item.ObjectType is GameObjectType.Helmet or GameObjectType.Gloves or GameObjectType.Belt
            or GameObjectType.Pants or GameObjectType.Boots)
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
                ItemSuffix.Ether
            };

            item.Suffix = suffixFilter.ElementAt(MainServer.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        if (item.ObjectType is GameObjectType.Chestplate or GameObjectType.Shield)
        {
            suffixFilter = new SortedSet<ItemSuffix>
            {
                // for shields Elements is "Old" (e.g. +68 at 12 rank)
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
                ItemSuffix.Majesty
                // ItemSuffix.Concentration_Old,
                // ItemSuffix.Majesty_Old,
                // ItemSuffix.Agility_Old,
                // ItemSuffix.Strength_Old,
                // ItemSuffix.Elements_Old,
                // ItemSuffix.Elements_New,
            };

            item.Suffix = suffixFilter.ElementAt(MainServer.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        if (item.ObjectType is GameObjectType.Powder or GameObjectType.Powder_Area or GameObjectType.Elixir_Castle
            or GameObjectType.Elixir_Trap)
        {
            item.ItemCount = MainServer.Rng.Next(3, 20);
        }

        item.Suffix = suffixFilter.ElementAt(MainServer.Rng.Next(0, suffixFilter.Count));
        return item;
    }

    public static bool IsSlotValidForItem (BelongingSlot slot, Item? item)
    {
        // TODO: actual check
        return true;
    }
}