using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SphServer.Shared.Db.DataModels;

/// <summary>
///     A wearable's appearance is a two-character code the game data carries as an "@xy@" prefix on
///     the item's ground model name: the first character is the garment class, the second the look.
///     The look is copied through as it stands - it is not a number, and codes run past 'f'.
///
///     The class does not travel as a value. It chooses which byte of the character's nine-byte
///     look block the look goes in, and physical chest armour ('c') and robes ('v') have a byte
///     each. Written to the wrong one, a robe is drawn as the physical armour of the same tier.
///     The client's own _player.mbc does this in CheckWeapon(), branching on the class character.
/// </summary>
public static class CharacterWornLook
{
    private static readonly Regex WearCode = new(@"^@(.)(.)@", RegexOptions.Compiled);

    /// <summary>Physical chest armour, ar_armor.</summary>
    private const char PhysicalChest = 'c';

    /// <summary>Magical chest armour, ar_armor2 - a robe.</summary>
    private const char MagicalChest = 'v';

    /// <summary>What an empty slot sends. Retail sends '0' for these, not a zero byte.</summary>
    private const byte NothingWorn = (byte) '0';

    /// <summary>Shield and helmet are the two whose classes have a real '0' look, so theirs is 0.</summary>
    private const byte NothingWornWithZeroLook = 0;

    private static readonly object CatalogLock = new();
    private static Dictionary<string, (char wearClass, byte look)>? wearByInventory;
    private static Dictionary<string, (char wearClass, byte look)>? wearByBareGround;

    /// <summary>
    ///     Nine wear-pattern bytes in CheckWeapon / SetWornGear order (boots...helmet).
    /// </summary>
    public readonly record struct Snapshot(
        byte Boots,
        byte Pants,
        byte Armor,
        byte Robe,
        byte Gloves,
        byte Shield,
        byte Helmet);

    /// <summary>Slots whose contents feed <see cref="Apply" />.</summary>
    public static bool AffectsAppearance(BelongingSlot slot) =>
        slot is BelongingSlot.Helmet or BelongingSlot.Shield or BelongingSlot.Chestplate
            or BelongingSlot.Gloves or BelongingSlot.Pants or BelongingSlot.Boots;

    public static Snapshot Capture(CharacterDbEntry character) =>
        new(
            character.BootModelId,
            character.PantsModelId,
            character.ArmorModelId,
            character.RobeModelId,
            character.GlovesModelId,
            character.ShieldModelId,
            character.HelmetModelId);

    /// <summary>
    ///     <c>g_rec_0898.b00..b08</c> for SetWornGear: boots, pants, armor, robe, gloves, shield,
    ///     two secondary codes, helmet. Empty secondary slots are ASCII <c>'0'</c>.
    /// </summary>
    public static byte[] ToWearPattern(CharacterDbEntry character)
    {
        Apply(character);
        return
        [
            character.BootModelId,
            character.PantsModelId,
            character.ArmorModelId,
            character.RobeModelId,
            character.GlovesModelId,
            character.ShieldModelId,
            NothingWorn,
            NothingWorn,
            character.HelmetModelId
        ];
    }

    /// <summary>Fills the character's model ids from what it is wearing.</summary>
    public static void Apply(CharacterDbEntry character)
    {
        character.BootModelId = LookFor(character, BelongingSlot.Boots, NothingWorn);
        character.PantsModelId = LookFor(character, BelongingSlot.Pants, NothingWorn);
        character.GlovesModelId = LookFor(character, BelongingSlot.Gloves, NothingWorn);
        character.ShieldModelId = LookFor(character, BelongingSlot.Shield, NothingWornWithZeroLook);
        character.HelmetModelId = LookFor(character, BelongingSlot.Helmet, NothingWornWithZeroLook);

        // The chest has a byte per class, and the one not being worn reads 0 rather than '0'.
        var chest = CodeFor(character, BelongingSlot.Chestplate);
        character.ArmorModelId = chest is null ? NothingWorn :
            chest.Value.wearClass == PhysicalChest ? chest.Value.look : (byte) 0;
        character.RobeModelId = chest is null ? NothingWorn :
            chest.Value.wearClass == MagicalChest ? chest.Value.look : (byte) 0;
    }

    private static byte LookFor(CharacterDbEntry character, BelongingSlot slot, byte whenEmpty)
    {
        return CodeFor(character, slot)?.look ?? whenEmpty;
    }

    private static (char wearClass, byte look)? CodeFor(CharacterDbEntry character, BelongingSlot slot)
    {
        if (!character.Items.TryGetValue(slot, out var itemId) ||
            DbConnection.Items.FindById(itemId) is not { } item ||
            !character.CanUseItem(item))
        {
            return null;
        }

        return ResolveWearCode(item);
    }

    /// <summary>
    ///     Prefer the @xy@ prefix on the item row. Legacy catalog/loot rows often store bare
    ///     <c>st_sheet</c> while pref rows carry <c>@s0@st_sheet</c> - resolve from SphObjectDb.
    /// </summary>
    public static (char wearClass, byte look)? ResolveWearCode(ItemDbEntry item)
    {
        if (!string.IsNullOrEmpty(item.ModelNameGround))
        {
            var direct = WearCode.Match(item.ModelNameGround);
            if (direct.Success)
            {
                return (direct.Groups[1].Value[0], (byte) direct.Groups[2].Value[0]);
            }
        }

        EnsureCatalogIndexes();

        if (!string.IsNullOrEmpty(item.ModelNameInventory) &&
            wearByInventory!.TryGetValue(item.ModelNameInventory, out var byInv))
        {
            return byInv;
        }

        if (!string.IsNullOrEmpty(item.ModelNameGround) &&
            wearByBareGround!.TryGetValue(item.ModelNameGround, out var byGround))
        {
            return byGround;
        }

        if (SphObjectDb.GameObjectDataDb.TryGetValue(item.GameId, out var catalog) &&
            !string.IsNullOrEmpty(catalog.ModelNameGround))
        {
            var fromGo = WearCode.Match(catalog.ModelNameGround);
            if (fromGo.Success)
            {
                return (fromGo.Groups[1].Value[0], (byte) fromGo.Groups[2].Value[0]);
            }
        }

        return null;
    }

    private static void EnsureCatalogIndexes()
    {
        if (wearByInventory is not null)
        {
            return;
        }

        lock (CatalogLock)
        {
            if (wearByInventory is not null)
            {
                return;
            }

            var byInv = new Dictionary<string, (char, byte)>(StringComparer.OrdinalIgnoreCase);
            var byGround = new Dictionary<string, (char, byte)>(StringComparer.OrdinalIgnoreCase);

            foreach (var go in SphObjectDb.GameObjectDataDb.Values)
            {
                if (string.IsNullOrEmpty(go.ModelNameGround))
                {
                    continue;
                }

                var match = WearCode.Match(go.ModelNameGround);
                if (!match.Success)
                {
                    continue;
                }

                var code = (match.Groups[1].Value[0], (byte) match.Groups[2].Value[0]);
                if (!string.IsNullOrEmpty(go.ModelNameInventory))
                {
                    byInv.TryAdd(go.ModelNameInventory, code);
                }

                // "@s0@st_sheet" -> bare key "st_sheet"
                var bare = go.ModelNameGround[match.Length..];
                if (!string.IsNullOrEmpty(bare))
                {
                    byGround.TryAdd(bare, code);
                }
            }

            wearByBareGround = byGround;
            wearByInventory = byInv;
        }
    }
}
