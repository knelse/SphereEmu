using System.Collections.Generic;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Sphere.Game.Loot;

public static class LootData
{
    public static readonly HashSet<PacketObjectTypes> ObjectTypesWithSuffixes =
    [
        PacketObjectTypes.ArmorAmulet,
        PacketObjectTypes.ArmorBracelet,
        PacketObjectTypes.ArmorBelt,
        PacketObjectTypes.ArmorBoots,
        PacketObjectTypes.ArmorChest,
        PacketObjectTypes.ArmorGloves,
        PacketObjectTypes.ArmorHelmet,
        PacketObjectTypes.ArmorPants,
        PacketObjectTypes.ArmorRobe,
        PacketObjectTypes.ArmorShield,
        PacketObjectTypes.QuestArmorBelt,
        PacketObjectTypes.QuestArmorBoots,
        PacketObjectTypes.QuestArmorChest,
        PacketObjectTypes.QuestArmorGloves,
        PacketObjectTypes.QuestArmorHelmet,
        PacketObjectTypes.QuestArmorBracelet,
        PacketObjectTypes.QuestArmorChest2,
        PacketObjectTypes.QuestArmorPants,
        PacketObjectTypes.QuestArmorRing,
        PacketObjectTypes.QuestArmorRobe,
        PacketObjectTypes.QuestArmorShield,
        PacketObjectTypes.WeaponAxe,
        PacketObjectTypes.WeaponCrossbow,
        PacketObjectTypes.WeaponSword,
        PacketObjectTypes.QuestWeaponAxe,
        PacketObjectTypes.QuestWeaponCrossbow,
        PacketObjectTypes.QuestWeaponSword
    ];

    public static bool IsSlotValidForItem (BelongingSlot slot, ItemDbEntry? item)
    {
        // TODO: actual check
        return true;
    }
}