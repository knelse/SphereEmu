using System;
using System.Collections.Generic;
using System.Linq;
using SphServer.Shared.Db.DataModels;
using SphServer.Sphere.Game.Loot;
using SphServer.System;

namespace SphServer.Sphere.Game.NpcTrade.ItemsOnSale;

public static class ItemsOnSaleGenerator
{
    private static readonly int[] AlchemyItemsOnSale =
    [
        900, 901, 902, 903, 905, 906, 907, 908, 909, 910, 911, 912, 913, 914, 915, 916, 917, 918, 919, 920, 921, 922,
        923, 924, 925, 926, 927, 928, 929, 930, 931, 940, 941, 942, 943, 944, 945, 946, 947, 948, 949, 950, 951, 952,
        953, 954, 955, 972, 973, 979, 980, 933, 934, 966, 967,
        // metals
        974, 975, 976, 977, 978, 970, 971
    ];

    private static readonly Dictionary<int, int[]> MagicItemsOnSalePerMinTier = new ()
    {
        [1] =
        [
            501, 509, 502, 510, 521, 503, 511, 522, 543, 504, 601, 607, 608, 635, 602, 609, 636, 610, 603, 646, 647,
            648, 701, 702, 703, 722, 704, 713, 705, 714, 801, 812, 807, 802, 813, 822, 836, 808, 841
        ],
        [4] =
        [
            512, 523, 550, 505, 513, 524, 529, 514, 525, 530, 611, 637, 641, 612, 619, 604, 613, 620, 626, 649, 723,
            734, 740, 706, 715, 727, 707, 716, 803, 814, 818, 823, 837, 809, 804, 815, 819
        ]
    };

    public static List<ItemDbEntry> Weapons (int minTier, int maxTier)
    {
        var itemsOnSale = new List<ItemDbEntry>();
        for (var i = minTier; i <= maxTier; i++)
        {
            var weaponsForTier = SphObjectDb.GameObjectDataDb.Where(x =>
                x.Value is
                {
                    GameId: > 1000,
                    GameObjectType: GameObjectType.Sword or GameObjectType.Crossbow or GameObjectType.Axe,
                    SuffixSetName.Length: 1
                } && x.Value.SuffixSetName != "-" &&
                x.Value.Tier == i).GroupBy(x => x.Value.GameObjectType).ToList();
            var output = new List<ItemDbEntry>();
            foreach (var weapons in weaponsForTier.Select(weapons => weapons.ToList()))
            {
                weapons.Sort((a, b) => GameObjectComparator(a.Value, b.Value));
                if (weapons.Count == 0)
                {
                    continue;
                }

                for (var j = 0; j < weapons.Count; j++)
                {
                    if (weaponsForTier.Count > 2 && weapons[j].Value.GameId % 2 == 0)
                    {
                        // skipping every item with even gameid
                        continue;
                    }

                    output.Add(GetItemForGameObject(weapons[j].Value, i));
                }
            }

            itemsOnSale.AddRange(output);
        }

        itemsOnSale.Add(new ItemDbEntry
        {
            ObjectType = ObjectType.Arrows,
            Weight = 75,
            VendorCost = 4,
            ItemCount = 1000
        });

        itemsOnSale.Sort(ItemComparator);

        return itemsOnSale;
    }

    public static List<ItemDbEntry> Armor (int minTier, int maxTier)
    {
        var itemsOnSale = new List<ItemDbEntry>();
        for (var i = minTier; i <= maxTier; i++)
        {
            var armorForTier = SphObjectDb.GameObjectDataDb.Where(x =>
                x.Value is
                {
                    GameId: > 1000, GameObjectType: GameObjectType.Chestplate or GameObjectType.Pants
                    or GameObjectType.Gloves
                    or GameObjectType.Boots or GameObjectType.Belt or GameObjectType.Helmet
                    or GameObjectType.Shield,
                    SuffixSetName.Length: 1
                } && x.Value.SuffixSetName != "-" &&
                x.Value.Tier == i).GroupBy(x => x.Value.GameObjectType).ToList();
            var output = new List<ItemDbEntry>();
            foreach (var armorTypeList in armorForTier.Select(armorType => armorType.ToList()))
            {
                armorTypeList.Sort((a, b) => GameObjectComparator(a.Value, b.Value));
                if (armorTypeList.Count == 0)
                {
                    continue;
                }

                var type = armorTypeList[0].Value.GameObjectType;
                for (var j = 0; j < armorTypeList.Count; j++)
                {
                    if (type is GameObjectType.Chestplate or GameObjectType.Shield && armorTypeList.Count > 2 &&
                        armorTypeList[j].Value.GameId % 2 == 0)
                    {
                        // skipping every 2nd item in tier for armor and shields
                        continue;
                    }

                    output.Add(GetItemForGameObject(armorTypeList[j].Value, i));
                }
            }

            itemsOnSale.AddRange(output);
        }

        itemsOnSale.Sort(ItemComparator);

        return itemsOnSale;
    }

    public static List<ItemDbEntry> TravelGeneric (int minTier, int maxTier)
    {
        var itemsOnSale = new List<ItemDbEntry>
        {
            new()
            {
                ObjectType = ObjectType.BackpackSmall,
                Weight = 200,
                VendorCost = 120
            },
            new()
            {
                ObjectType = ObjectType.BackpackLarge,
                Weight = 200,
                VendorCost = 240
            }
        };

        return itemsOnSale;
    }

    public static List<ItemDbEntry> Alchemy (int minTier, int maxTier)
    {
        var itemsOnSale = new List<ItemDbEntry>();
        foreach (var alchemyItemId in AlchemyItemsOnSale)
        {
            var go = SphObjectDb.GameObjectDataDb[alchemyItemId];
            var item = GetItemForGameObject(go, 1);
            item.ItemCount = 1000;
            itemsOnSale.Add(item);
        }

        return itemsOnSale;
    }

    public static List<ItemDbEntry> Magic (int minTier, int maxTier)
    {
        var itemsOnSale = new List<ItemDbEntry>();
        if (minTier == 1)
        {
            itemsOnSale.Add(new ItemDbEntry
            {
                ObjectType = ObjectType.AlchemyPot,
                Weight = 500,
                VendorCost = 330
            });
            itemsOnSale.Add(new ItemDbEntry
            {
                ObjectType = ObjectType.RecipeBook,
                Weight = 200,
                VendorCost = 120
            });
            for (var i = 1; i <= 2; i++)
            {
                var itemId = 570 + i - 1;
                var go = SphObjectDb.GameObjectDataDb[itemId];
                var item = GetItemForGameObject(go, i);
                item.ItemCount = 1000;
                itemsOnSale.Add(item);
            }

            itemsOnSale.Add(new ItemDbEntry
            {
                ObjectType = ObjectType.PowderAmilus,
                Weight = 1,
                VendorCost = 5,
                ItemCount = 1000
            });
            itemsOnSale.Add(new ItemDbEntry
            {
                ObjectType = ObjectType.PowderFinale,
                Weight = 1,
                VendorCost = 3,
                ItemCount = 1000
            });
        }

        foreach (var magicId in MagicItemsOnSalePerMinTier[minTier])
        {
            var go = SphObjectDb.GameObjectDataDb[magicId];
            var item = GetItemForGameObject(go, 1);
            item.ItemCount = 1000;
            itemsOnSale.Add(item);
        }

        return itemsOnSale;
    }

    public static List<ItemDbEntry> Jewelry (int minTier, int maxTier)
    {
        var itemsOnSale = new List<ItemDbEntry>
        {
            new()
            {
                ObjectType = ObjectType.MantraBookSmall,
                Weight = 200,
                VendorCost = 350
            }
        };
        for (var i = minTier; i < maxTier; i++)
        {
            itemsOnSale.Add(GetItemForTier(ObjectType.Ring, i, true));
            itemsOnSale.Add(GetItemForTier(ObjectType.Ring, i, true));
        }

        itemsOnSale.Add(GetItemForTier(ObjectType.Ring, maxTier, true));
        if (minTier != 1)
        {
            itemsOnSale.Add(GetItemForTier(ObjectType.Ring, minTier, true));
        }

        for (var i = minTier; i < maxTier; i++)
        {
            itemsOnSale.Add(GetItemForTier([ObjectType.ArmorAmulet, ObjectType.ArmorBracelet], i, true));
            if (i == minTier && i != 1)
            {
                continue;
            }

            itemsOnSale.Add(GetItemForTier([ObjectType.ArmorAmulet, ObjectType.ArmorBracelet], i, true));
        }

        itemsOnSale.Add(GetItemForTier([ObjectType.ArmorAmulet, ObjectType.ArmorBracelet], maxTier,
            true));
        if (minTier == 1)
        {
            for (var i = 0; i <= 2; i++)
            {
                var scroll = new ItemDbEntry
                {
                    ObjectType = ObjectType.ScrollLegend,
                    Weight = 25,
                    VendorCost = 50,
                    ItemCount = 1000,
                    ContentsData =
                    {
                        ["scroll_id"] = i
                    }
                };
                itemsOnSale.Add(scroll);
            }
        }

        for (var i = minTier; i < maxTier; i++)
        {
            itemsOnSale.Add(GetItemForTier(ObjectType.ArmorRobe, i, true));
            itemsOnSale.Add(GetItemForTier(ObjectType.ArmorRobe, i, true));
        }

        itemsOnSale.Add(GetItemForTier(ObjectType.ArmorRobe, maxTier, true));

        if (minTier != 1)
        {
            // trap elixir
            var tierMin = minTier switch
            {
                4 => 3,
                7 => 5,
                10 => 7,
                _ => 0
            };
            for (var i = tierMin; i <= tierMin + 1; i++)
            {
                var itemId = 570 + i - 1;
                var go = SphObjectDb.GameObjectDataDb[itemId];
                var item = GetItemForGameObject(go, i);
                item.ItemCount = 1000;
                itemsOnSale.Add(item);
            }
        }

        return itemsOnSale;
    }

    private static bool ShouldHaveSuffix (ObjectType objectType, int tier)
    {
        if (objectType is ObjectType.Ring)
        {
            return true;
        }

        if (!LootData.ObjectTypesWithSuffixes.Contains(objectType))
        {
            return false;
        }

        // dumb rng to have higher tiers have suffixes less often
        // var rand = SphereServer.Rng.Next(tier * 2 + 1);
        return true; // rand == tier * 2;
    }

    private static ItemDbEntry GetItemForTier (ObjectType objectType, int tier, bool withSuffixMaybe = false)
    {
        return GetItemForTier([objectType], tier, withSuffixMaybe);
    }

    private static ItemDbEntry GetItemForTier (HashSet<ObjectType> objectTypes, int tier, bool withSuffixMaybe = false)
    {
        var candidates = SphObjectDb.GameObjectDataDb.Where(x =>
                objectTypes.Contains(x.Value.GameObjectType.GetPacketObjectType()) && x.Value.Tier == tier
                && (!withSuffixMaybe || (x.Value.SuffixSetName.Length == 1 && x.Value.SuffixSetName != "-")))
            .ToList();
        var randomObjectId = SphRng.Rng.Next(candidates.Count);
        var randomGameObject = candidates.ElementAt(randomObjectId).Value;
        return GetItemForGameObject(randomGameObject, tier, withSuffixMaybe);
    }

    private static ItemDbEntry GetItemForGameObject (SphGameObject gameObject, int tier, bool withSuffixMaybe = false)
    {
        var clone = SphGameObject.CreateFromGameObject(gameObject);
        var withSuffix = withSuffixMaybe && ShouldHaveSuffix(clone.GameObjectType.GetPacketObjectType(), tier);
        if (withSuffix)
        {
            var suffixes =
                GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual.GetValueOrDefault(clone.GameObjectType, []);
            if (suffixes.Any())
            {
                var randSuffixId = SphRng.Rng.Next(suffixes.Count);
                var randSuffix = suffixes.ElementAt(randSuffixId);
                clone.Suffix = randSuffix.Key;
            }
        }

        return ItemDbEntry.CreateFromGameObject(clone);
    }

    private static int GameObjectComparator (SphGameObject a, SphGameObject b)
    {
        var sortOrder = new Dictionary<GameObjectType, int>
        {
            [GameObjectType.Chestplate] = 1,
            [GameObjectType.Chestplate_Unique] = 1,
            [GameObjectType.Chestplate_Quest] = 1,
            [GameObjectType.Shield] = 2,
            [GameObjectType.Shield_Quest] = 2,
            [GameObjectType.Shield_Unique] = 2,
            [GameObjectType.Pants] = 3,
            [GameObjectType.Pants_Quest] = 3,
            [GameObjectType.Pants_Unique] = 3,
            [GameObjectType.Helmet] = 4,
            [GameObjectType.Helmet_Premium] = 4,
            [GameObjectType.Helmet_Quest] = 4,
            [GameObjectType.Helmet_Unique] = 4,
            [GameObjectType.Belt] = 5,
            [GameObjectType.Belt_Quest] = 5,
            [GameObjectType.Belt_Unique] = 5,
            [GameObjectType.Boots] = 6,
            [GameObjectType.Boots_Quest] = 6,
            [GameObjectType.Boots_Unique] = 6,
            [GameObjectType.Gloves] = 7,
            [GameObjectType.Gloves_Quest] = 7,
            [GameObjectType.Gloves_Unique] = 7,
            [GameObjectType.Sword] = 8,
            [GameObjectType.Sword_Quest] = 8,
            [GameObjectType.Sword_Unique] = 8,
            [GameObjectType.Crossbow] = 9,
            [GameObjectType.Crossbow_Quest] = 9,
            [GameObjectType.Axe] = 10,
            [GameObjectType.Axe_Quest] = 10
        };
        var sortOrderA = sortOrder.GetValueOrDefault(a.GameObjectType, int.MaxValue);
        var sortOrderB = sortOrder.GetValueOrDefault(b.GameObjectType, int.MaxValue);
        var typeCompare = sortOrderA.CompareTo(sortOrderB);
        if (typeCompare != 0)
        {
            return typeCompare;
        }

        typeCompare = string.Compare(a.ModelNameInventory, b.ModelNameInventory,
            StringComparison.OrdinalIgnoreCase);
        return typeCompare != 0 ? typeCompare : a.GameId.CompareTo(b.GameId);
    }

    private static int ItemComparator (ItemDbEntry a, ItemDbEntry b)
    {
        var sortOrder = new Dictionary<GameObjectType, int>
        {
            [GameObjectType.Chestplate] = 1,
            [GameObjectType.Chestplate_Unique] = 1,
            [GameObjectType.Chestplate_Quest] = 1,
            [GameObjectType.Shield] = 2,
            [GameObjectType.Shield_Quest] = 2,
            [GameObjectType.Shield_Unique] = 2,
            [GameObjectType.Pants] = 3,
            [GameObjectType.Pants_Quest] = 3,
            [GameObjectType.Pants_Unique] = 3,
            [GameObjectType.Helmet] = 4,
            [GameObjectType.Helmet_Premium] = 4,
            [GameObjectType.Helmet_Quest] = 4,
            [GameObjectType.Helmet_Unique] = 4,
            [GameObjectType.Belt] = 5,
            [GameObjectType.Belt_Quest] = 5,
            [GameObjectType.Belt_Unique] = 5,
            [GameObjectType.Boots] = 6,
            [GameObjectType.Boots_Quest] = 6,
            [GameObjectType.Boots_Unique] = 6,
            [GameObjectType.Gloves] = 7,
            [GameObjectType.Gloves_Quest] = 7,
            [GameObjectType.Gloves_Unique] = 7,
            [GameObjectType.Sword] = 8,
            [GameObjectType.Sword_Quest] = 8,
            [GameObjectType.Sword_Unique] = 8,
            [GameObjectType.Axe] = 9,
            [GameObjectType.Axe_Quest] = 9,
            [GameObjectType.Crossbow] = 10,
            [GameObjectType.Crossbow_Quest] = 10
        };
        var sortOrderA = sortOrder.GetValueOrDefault(a.GameObjectType, int.MaxValue);
        var sortOrderB = sortOrder.GetValueOrDefault(b.GameObjectType, int.MaxValue);
        var typeCompare = sortOrderA.CompareTo(sortOrderB);
        if (typeCompare != 0)
        {
            return typeCompare;
        }

        typeCompare = string.Compare(a.ModelNameInventory, b.ModelNameInventory,
            StringComparison.OrdinalIgnoreCase);
        return typeCompare != 0 ? typeCompare : a.GameId.CompareTo(b.GameId);
    }
}