using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SphServer.Shared.Db.DataModels;

/// <summary>
/// @xy@ on the ground model: class picks the look-block byte ('c' and 'v' are separate), look is
/// copied as-is past 'f'
/// </summary>
public static class CharacterWornLook
{
    private static readonly Regex WearCode = new (@"^@(.)(.)@", RegexOptions.Compiled);

    /// <summary>
    /// Physical chest, ar_armor
    /// </summary>
    private const char PhysicalChest = 'c';

    /// <summary>
    /// Magical chest, ar_armor2, a robe
    /// </summary>
    private const char MagicalChest = 'v';

    /// <summary>
    /// Empty slot is ASCII '0', not a zero byte
    /// </summary>
    private const byte NothingWorn = (byte) '0';

    /// <summary>
    /// Shield and helmet have a real '0' look, so empty is a zero byte
    /// </summary>
    private const byte NothingWornWithZeroLook = 0;

    private static readonly object CatalogLock = new ();
    private static Dictionary<string, (char wearClass, byte look)>? wearByInventory;
    private static Dictionary<string, (char wearClass, byte look)>? wearByBareGround;

    /// <summary>
    /// CheckWeapon / SetWornGear order, boots through helmet
    /// </summary>
    public readonly record struct Snapshot (
        byte Boots,
        byte Pants,
        byte Armor,
        byte Robe,
        byte Gloves,
        byte Shield,
        byte Helmet);

    /// <summary>
    /// Slots whose contents feed Apply
    /// </summary>
    public static bool AffectsAppearance (BelongingSlot slot) =>
        slot is BelongingSlot.Helmet or BelongingSlot.Shield or BelongingSlot.Chestplate
            or BelongingSlot.Gloves or BelongingSlot.Pants or BelongingSlot.Boots;

    public static Snapshot Capture (CharacterDbEntry character) =>
        new (
            character.BootModelId,
            character.PantsModelId,
            character.ArmorModelId,
            character.RobeModelId,
            character.GlovesModelId,
            character.ShieldModelId,
            character.HelmetModelId);

    /// <summary>
    /// g_rec_0898.b00-b08: boots, pants, armor, robe, gloves, shield, two secondary ASCII '0'
    /// codes, helmet
    /// </summary>
    public static byte[] ToWearPattern (CharacterDbEntry character)
    {
        Apply (character);
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

    public static void Apply (CharacterDbEntry character)
    {
        character.BootModelId = LookFor (character, BelongingSlot.Boots, NothingWorn);
        character.PantsModelId = LookFor (character, BelongingSlot.Pants, NothingWorn);
        character.GlovesModelId = LookFor (character, BelongingSlot.Gloves, NothingWorn);
        character.ShieldModelId = LookFor (character, BelongingSlot.Shield, NothingWornWithZeroLook);
        character.HelmetModelId = LookFor (character, BelongingSlot.Helmet, NothingWornWithZeroLook);

        // The unworn chest class reads 0, not ASCII '0'
        var chest = CodeFor (character, BelongingSlot.Chestplate);
        character.ArmorModelId = chest is null ? NothingWorn :
            chest.Value.wearClass == PhysicalChest ? chest.Value.look : (byte) 0;
        character.RobeModelId = chest is null ? NothingWorn :
            chest.Value.wearClass == MagicalChest ? chest.Value.look : (byte) 0;
    }

    private static byte LookFor (CharacterDbEntry character, BelongingSlot slot, byte whenEmpty)
    {
        return CodeFor (character, slot)?.look ?? whenEmpty;
    }

    private static (char wearClass, byte look)? CodeFor (CharacterDbEntry character, BelongingSlot slot)
    {
        if (!character.Items.TryGetValue (slot, out var itemId) ||
            DbConnection.Items.FindById (itemId) is not { } item ||
            !character.CanUseItem (item))
        {
            return null;
        }

        return ResolveWearCode (item);
    }

    /// <summary>
    /// @xy@ on the row wins; a bare st_sheet resolves through SphObjectDb
    /// </summary>
    public static (char wearClass, byte look)? ResolveWearCode (ItemDbEntry item)
    {
        if (!string.IsNullOrEmpty (item.ModelNameGround))
        {
            var direct = WearCode.Match (item.ModelNameGround);
            if (direct.Success)
            {
                return (direct.Groups[1].Value[0], (byte) direct.Groups[2].Value[0]);
            }
        }

        EnsureCatalogIndexes ();

        if (!string.IsNullOrEmpty (item.ModelNameInventory) &&
            wearByInventory!.TryGetValue (item.ModelNameInventory, out var byInv))
        {
            return byInv;
        }

        if (!string.IsNullOrEmpty (item.ModelNameGround) &&
            wearByBareGround!.TryGetValue (item.ModelNameGround, out var byGround))
        {
            return byGround;
        }

        if (SphObjectDb.GameObjectDataDb.TryGetValue (item.GameId, out var catalog) &&
            !string.IsNullOrEmpty (catalog.ModelNameGround))
        {
            var fromGo = WearCode.Match (catalog.ModelNameGround);
            if (fromGo.Success)
            {
                return (fromGo.Groups[1].Value[0], (byte) fromGo.Groups[2].Value[0]);
            }
        }

        return null;
    }

    private static void EnsureCatalogIndexes ()
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

            var byInv = new Dictionary<string, (char, byte)> (StringComparer.OrdinalIgnoreCase);
            var byGround = new Dictionary<string, (char, byte)> (StringComparer.OrdinalIgnoreCase);

            foreach (var go in SphObjectDb.GameObjectDataDb.Values)
            {
                if (string.IsNullOrEmpty (go.ModelNameGround))
                {
                    continue;
                }

                var match = WearCode.Match (go.ModelNameGround);
                if (!match.Success)
                {
                    continue;
                }

                var code = (match.Groups[1].Value[0], (byte) match.Groups[2].Value[0]);
                if (!string.IsNullOrEmpty (go.ModelNameInventory))
                {
                    byInv.TryAdd (go.ModelNameInventory, code);
                }

                // "@s0@st_sheet" -> bare key "st_sheet"
                var bare = go.ModelNameGround[match.Length..];
                if (!string.IsNullOrEmpty (bare))
                {
                    byGround.TryAdd (bare, code);
                }
            }

            wearByBareGround = byGround;
            wearByInventory = byInv;
        }
    }
}
