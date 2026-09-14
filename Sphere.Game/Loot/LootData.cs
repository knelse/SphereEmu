using System.Collections.Generic;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Sphere.Game.Loot;

public static class LootData
{
    public static readonly HashSet<ObjectType> ObjectTypesWithSuffixes =
    [
        ObjectType.Armor_Amulet,
        ObjectType.Armor_Bracelet,
        ObjectType.Armor_Belt,
        ObjectType.Armor_Boots,
        ObjectType.Armor_Chest,
        ObjectType.Armor_Gloves,
        ObjectType.Armor_Helmet,
        ObjectType.Armor_Pants,
        ObjectType.Armor_Robe,
        ObjectType.Armor_Shield,
        ObjectType.Quest_Armor_Belt,
        ObjectType.Quest_Armor_Boots,
        ObjectType.Quest_Armor_Chest,
        ObjectType.Quest_Armor_Gloves,
        ObjectType.Quest_Armor_Helmet,
        ObjectType.Quest_Armor_Bracelet,
        ObjectType.Quest_Armor_Chest2,
        ObjectType.Quest_Armor_Pants,
        ObjectType.Quest_Armor_Ring,
        ObjectType.Quest_Armor_Robe,
        ObjectType.Quest_Armor_Shield,
        ObjectType.Weapon_Axe,
        ObjectType.Weapon_Crossbow,
        ObjectType.Weapon_Sword,
        ObjectType.Quest_Weapon_Axe,
        ObjectType.Quest_Weapon_Crossbow,
        ObjectType.Quest_Weapon_Sword
    ];

    public static bool IsSlotValidForItem (BelongingSlot slot, ItemDbEntry? item)
    {
        // TODO: actual check
        return true;
    }
}