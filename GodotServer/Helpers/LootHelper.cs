using System;
using System.Collections.Generic;
using System.Linq;
using SphServer;

public static class LootHelper
{
    public static bool firstTypeRolled = false;

    public static SphGameObject GetRandomObjectData(int titleLevelMinusOne, int gameIdOverride = -1)
    {
        SphGameObject item;
        if (gameIdOverride != -1)
        {
            item = MainServer.GameObjectDataDb.First(x => x.Value.GameId == gameIdOverride).Value;
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
                GameObjectType.Armor,
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
                GameObjectType.Sword,
            };
            var overrideFilter = new HashSet<GameObjectType>
            {
                GameObjectType.Ring,
            };

            if (firstTypeRolled)
            {
                overrideFilter = new HashSet<GameObjectType>
                {
                    GameObjectType.Mineral
                };
            }

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
                GameObjectKind.Sword_New,
            };

            var tierAgnosticTypes = new HashSet<GameObjectType>
            {
                GameObjectType.Flower,
                GameObjectType.Metal,
                GameObjectType.Mineral,
            };

            var lootPool = MainServer.GameObjectDataDb
                .Where(x =>
                    kindFilter.Contains(x.Value.ObjectKind) && typeFilter.Contains(x.Value.ObjectType)
                                                            && (x.Value.Tier == tierFilter
                                                                || tierAgnosticTypes.Contains(x.Value.ObjectType)))
                .Select(y => y.Value)
                .ToList();

            var random = MainServer.Rng.RandiRange(0, lootPool.Count - 1);
            item = lootPool.ElementAt(random);
        }

        var noSuffix = false;
        var suffixFilter = new SortedSet<ItemSuffix> { ItemSuffix.None };
            
        if (item.ObjectType is GameObjectType.Ring)
        { 
            suffixFilter = new SortedSet<ItemSuffix>
            {
                // ItemSuffix.Health,
                // ItemSuffix.Accuracy,
                // ItemSuffix.Air,
                // ItemSuffix.Durability,
                // ItemSuffix.Life,
                // ItemSuffix.Endurance,
                ItemSuffix.Fire,
                // ItemSuffix.Absorption,
                // ItemSuffix.Meditation,
                // ItemSuffix.Strength,
                // ItemSuffix.Earth,
                // ItemSuffix.Safety,
                // ItemSuffix.Prana,
                // ItemSuffix.Agility,
                // ItemSuffix.Water,
                // ItemSuffix.Value,
                // ItemSuffix.Precision,
                // ItemSuffix.Ether,
            };
            item.Suffix = suffixFilter.ElementAt(MainServer.Rng.RandiRange(0, suffixFilter.Count - 1));
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
                ItemSuffix.Interdict,
            };
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
                ItemSuffix.Archmage,
                // ItemSuffix.Durability_Old,
                // ItemSuffix.Life_Old,
                // ItemSuffix.Safety_Old,
                // ItemSuffix.Prana_Old,
                // ItemSuffix.Deflection_Old,
                // ItemSuffix.Meditation_Old,
                // ItemSuffix.Health_Old,
                // ItemSuffix.Ether_Old,
            };
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
                ItemSuffix.Damage,
            };
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
                ItemSuffix.Ether,
            };
        }
        if (item.ObjectType is GameObjectType.Armor or GameObjectType.Shield)
        {
            suffixFilter = new SortedSet<ItemSuffix>
            {// for shields Elements is "Old" (e.g. +68 at 12 rank)
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
                ItemSuffix.Majesty,
                // ItemSuffix.Concentration_Old,
                // ItemSuffix.Majesty_Old,
                // ItemSuffix.Agility_Old,
                // ItemSuffix.Strength_Old,
                // ItemSuffix.Elements_Old,
                // ItemSuffix.Elements_New,
            };
        }

        if (item.ObjectType is GameObjectType.Powder or GameObjectType.Powder_Area or GameObjectType.Elixir_Castle 
            or GameObjectType.Elixir_Trap)
        {
            item.ItemCount =  MainServer.Rng.RandiRange(3, 19);
        }

        item.Suffix = suffixFilter.ElementAt(MainServer.Rng.RandiRange(0, suffixFilter.Count - 1));
        return item;
    }
}