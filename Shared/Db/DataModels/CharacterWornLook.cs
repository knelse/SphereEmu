using System.Text.RegularExpressions;

namespace SphServer.Shared.Db.DataModels;

/// <summary>
///     A wearable's appearance is a two-character code the game data carries as an "@xy@" prefix on
///     the item's ground model name. The letter is the body part and the second character is the
///     look, copied through as it stands: it is not a number, and codes run past 'f'.
/// </summary>
public static class CharacterWornLook
{
    private static readonly Regex WearCode = new (@"^@(.)(.)@", RegexOptions.Compiled);

    /// <summary>Fills the character's model ids from what it is wearing.</summary>
    public static void Apply (CharacterDbEntry character)
    {
        character.BootModelId = CodeFor(character, BelongingSlot.Boots);
        character.PantsModelId = CodeFor(character, BelongingSlot.Pants);
        character.ArmorModelId = CodeFor(character, BelongingSlot.Chestplate);
        character.GlovesModelId = CodeFor(character, BelongingSlot.Gloves);
        character.HelmetModelId = CodeFor(character, BelongingSlot.Helmet);
    }

    private static byte CodeFor (CharacterDbEntry character, BelongingSlot slot)
    {
        if (!character.Items.TryGetValue(slot, out var itemId) ||
            DbConnection.Items.FindById(itemId) is not { } item ||
            !character.CanUseItem(item) ||
            string.IsNullOrEmpty(item.ModelNameGround))
        {
            return 0;
        }

        var code = WearCode.Match(item.ModelNameGround);
        return code.Success ? (byte) code.Groups[2].Value[0] : (byte) 0;
    }
}
