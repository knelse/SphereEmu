using System.Collections.Generic;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Sphere.Game.Loot;

public static class LootData
{
    public static readonly HashSet<ObjectType> ObjectTypesWithSuffixes =
    [
        ObjectType.ArmorAmulet,
        ObjectType.ArmorBracelet,
        ObjectType.ArmorBelt,
        ObjectType.ArmorBoots,
        ObjectType.ArmorChest,
        ObjectType.ArmorGloves,
        ObjectType.ArmorHelmet,
        ObjectType.ArmorPants,
        ObjectType.ArmorRobe,
        ObjectType.ArmorShield,
        ObjectType.QuestArmorBelt,
        ObjectType.QuestArmorBoots,
        ObjectType.QuestArmorChest,
        ObjectType.QuestArmorGloves,
        ObjectType.QuestArmorHelmet,
        ObjectType.QuestArmorBracelet,
        ObjectType.QuestArmorChest2,
        ObjectType.QuestArmorPants,
        ObjectType.QuestArmorRing,
        ObjectType.QuestArmorRobe,
        ObjectType.QuestArmorShield,
        ObjectType.WeaponAxe,
        ObjectType.WeaponCrossbow,
        ObjectType.WeaponSword,
        ObjectType.QuestWeaponAxe,
        ObjectType.QuestWeaponCrossbow,
        ObjectType.QuestWeaponSword
    ];

    public static bool IsSlotValidForItem (BelongingSlot slot, ItemDbEntry? item)
    {
        // TODO: actual check
        return true;
    }
}