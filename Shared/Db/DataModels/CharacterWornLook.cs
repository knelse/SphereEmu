using System.Text.RegularExpressions;

namespace SphServer.Shared.Db.DataModels;

/// <summary>
///     A wearable's appearance is a two-character code the game data carries as an "@xy@" prefix on
///     the item's ground model name: the first character is the garment class, the second the look.
///     The look is copied through as it stands — it is not a number, and codes run past 'f'.
///
///     The class does not travel as a value. It chooses which byte of the character's nine-byte
///     look block the look goes in, and physical chest armour ('c') and robes ('v') have a byte
///     each. Written to the wrong one, a robe is drawn as the physical armour of the same tier.
///     The client's own _player.mbc does this in CheckWeapon(), branching on the class character.
/// </summary>
public static class CharacterWornLook
{
    private static readonly Regex WearCode = new (@"^@(.)(.)@", RegexOptions.Compiled);

    /// <summary>Physical chest armour, ar_armor.</summary>
    private const char PhysicalChest = 'c';

    /// <summary>Magical chest armour, ar_armor2 — a robe.</summary>
    private const char MagicalChest = 'v';

    /// <summary>What an empty slot sends. Retail sends '0' for these, not a zero byte.</summary>
    private const byte NothingWorn = (byte) '0';

    /// <summary>Shield and helmet are the two whose classes have a real '0' look, so theirs is 0.</summary>
    private const byte NothingWornWithZeroLook = 0;

    /// <summary>Fills the character's model ids from what it is wearing.</summary>
    public static void Apply (CharacterDbEntry character)
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

    private static byte LookFor (CharacterDbEntry character, BelongingSlot slot, byte whenEmpty)
    {
        return CodeFor(character, slot)?.look ?? whenEmpty;
    }

    private static (char wearClass, byte look)? CodeFor (CharacterDbEntry character, BelongingSlot slot)
    {
        if (!character.Items.TryGetValue(slot, out var itemId) ||
            DbConnection.Items.FindById(itemId) is not { } item ||
            !character.CanUseItem(item) ||
            string.IsNullOrEmpty(item.ModelNameGround))
        {
            return null;
        }

        var code = WearCode.Match(item.ModelNameGround);
        return code.Success ? (code.Groups[1].Value[0], (byte) code.Groups[2].Value[0]) : null;
    }
}
