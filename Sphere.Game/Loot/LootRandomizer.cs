using System;
using System.Collections.Generic;
using System.Linq;
using SphServer.Shared.Db;
using SphServer.System;

namespace SphServer.Sphere.Game.Loot;

public static class LootRandomizer
{
    public static SphGameObject GetRandomLootObject (int titleLevelMinusOne, int gameIdOverride = -1)
    {
        var gameIdsToRemove = new HashSet<int>
        {
            4192, 4193, 4194, 4199, 4186, 4187, 4242, 4243, 4244, 4245, 4246, 4247, 4248, 4249, 4302, 4304, 4306, 4308,
            4310, 4312, 4314, 4316, 4318, 4320, 4450, 4740, 4741, 4742, 4743, 4744, 4745, 4746, 4747, 4748, 4749
        };

        SphGameObject item;
        if (gameIdOverride != -1)
        {
            item = DbConnection.GameObjects.FindById(gameIdOverride);
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

            var lootPool = GameObjectDb.Db
                .Where(x =>
                    !gameIdsToRemove.Contains(x.Key) && kindFilter.Contains(x.Value.ObjectKind) &&
                    typeFilter.Contains(x.Value.GameObjectType)
                    && (x.Value.Tier == tierFilter
                        || tierAgnosticTypes.Contains(x.Value.GameObjectType))).Select(x => x.Value)
                .ToList();
            var random = SphRng.Rng.Next(0, lootPool.Count);
            item = lootPool.ElementAt(random);
            var collectionItem = DbConnection.GameObjects.FindOne(x => x.GameId == item.GameId);
            item.GameObjectDbId = collectionItem.GameObjectDbId;
        }

        var noSuffix = false;
        var suffixFilter = new SortedSet<ItemSuffix> { ItemSuffix.None };

        if (item.GameObjectType is GameObjectType.Ring)
        {
            suffixFilter =
            [
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
            ];
            item.Suffix = suffixFilter.ElementAt(SphRng.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        // Rings should always have a suffix
        if (noSuffix)
        {
            return item;
        }

        if (item.GameObjectType is GameObjectType.Sword or GameObjectType.Axe)
        {
            suffixFilter =
            [
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
            ];

            item.Suffix = suffixFilter.ElementAt(SphRng.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        if (item.GameObjectType is GameObjectType.Crossbow)
        {
            suffixFilter =
            [
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
            ];

            item.Suffix = suffixFilter.ElementAt(SphRng.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        if (item.GameObjectType is GameObjectType.Robe)
        {
            suffixFilter =
            [
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
            ];

            item.Suffix = suffixFilter.ElementAt(SphRng.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        if (item.GameObjectType is GameObjectType.Bracelet or GameObjectType.Amulet)
        {
            suffixFilter =
            [
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
            ];

            item.Suffix = suffixFilter.ElementAt(SphRng.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        if (item.GameObjectType is GameObjectType.Helmet or GameObjectType.Gloves or GameObjectType.Belt
            or GameObjectType.Pants or GameObjectType.Boots)
        {
            suffixFilter =
            [
                ItemSuffix.Health,
                ItemSuffix.Value,
                ItemSuffix.Durability,
                ItemSuffix.Meditation,
                ItemSuffix.Absorption,
                ItemSuffix.Precision,
                ItemSuffix.Safety,
                ItemSuffix.Ether
            ];

            item.Suffix = suffixFilter.ElementAt(SphRng.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        if (item.GameObjectType is GameObjectType.Chestplate or GameObjectType.Shield)
        {
            suffixFilter =
            [
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
                ItemSuffix.Absorption,
                ItemSuffix.Health,
                ItemSuffix.Earth,
                ItemSuffix.Elements,
                ItemSuffix.Majesty
            ];

            item.Suffix = suffixFilter.ElementAt(SphRng.Rng.Next(0, suffixFilter.Count));
            return item;
        }

        if (item.GameObjectType is GameObjectType.Powder or GameObjectType.Powder_Area or GameObjectType.Elixir_Castle
            or GameObjectType.Elixir_Trap)
        {
            item.ItemCount = SphRng.Rng.Next(3, 20);
        }

        item.Suffix = suffixFilter.ElementAt(SphRng.Rng.Next(0, suffixFilter.Count));
        return item;
    }
}