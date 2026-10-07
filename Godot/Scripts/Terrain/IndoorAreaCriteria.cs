namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// A prop inside a dungeon is not a base tile. Name match is not enough without deep Y
/// </summary>
public static class IndoorAreaCriteria
{
    /// <summary>
    /// Godot Y below this is indoor-depth. ObjectDataJson stores source Y; Godot Y is -sourceY
    /// </summary>
    public const float MaxIndoorPlacementY = -500f;

    public static bool IsIndoorDepth (float godotY) => godotY < MaxIndoorPlacementY;

    /// <summary>
    /// Shell or room kit, not a prop. Case-insensitive
    /// </summary>
    public static bool IsIndoorBaseTileName (string? objectName)
    {
        if (string.IsNullOrWhiteSpace (objectName))
        {
            return false;
        }

        var name = objectName.Trim ().ToLowerInvariant ();
        if (name is "empty" or "lbridge")
        {
            return false;
        }

        if (name.StartsWith ("lbridge", StringComparison.Ordinal))
        {
            return false;
        }

        if (name.EndsWith ("_in", StringComparison.Ordinal))
        {
            return true;
        }

        if (name.StartsWith ("cci", StringComparison.Ordinal))
        {
            return true;
        }

        // lb* labyrinth / dungeon shells — exclude lbridge*
        if (name.StartsWith ("lb", StringComparison.Ordinal))
        {
            return true;
        }

        // Only rd_island3 — rd_island1/2 are unused co-located height variants (no nav).
        if (name == "rd_island3")
        {
            return true;
        }

        if (name is "rd_r1" or "rd_r2" or "rd_r3" or "rd_r4" or "rd_r5")
        {
            return true;
        }

        if (name is "rd_rh" or "room1" or "tn4_hotel")
        {
            return true;
        }

        return false;
    }

    /// <summary>
    ///     True when this placement is an indoor base tile: deep Y and matching name.
    /// </summary>
    public static bool IsIndoorBaseTile (string? objectName, float godotY) =>
        IsIndoorDepth (godotY) && IsIndoorBaseTileName (objectName);
}
