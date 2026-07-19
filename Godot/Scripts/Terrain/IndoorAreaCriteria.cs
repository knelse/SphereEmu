namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Criteria for indoor / dungeon base geometry (not outdoor terrain tiles).
///     Base tiles are identified by deep placement Y plus name patterns; props may sit inside or
///     around them and are not matched by name alone.
/// </summary>
public static class IndoorAreaCriteria
{
    /// <summary>
    ///     Placements with Godot/SOURCE_BASIS Y strictly below this are indoor-depth candidates.
    ///     ObjectDataJson stores source Y; Godot Y is <c>-sourceY</c> via TerrainObjectsFill SOURCE_BASIS.
    /// </summary>
    public const float MaxIndoorPlacementY = -500f;

    /// <summary>
    ///     True when a placement origin is deep enough to be indoor (Godot Y &lt; <see cref="MaxIndoorPlacementY" />).
    /// </summary>
    public static bool IsIndoorDepth(float godotY) => godotY < MaxIndoorPlacementY;

    /// <summary>
    ///     True when <paramref name="objectName" /> is an indoor base tile (shell / room kit), not a prop.
    ///     Name match is case-insensitive.
    /// </summary>
    public static bool IsIndoorBaseTileName(string? objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        var name = objectName.Trim().ToLowerInvariant();
        if (name is "empty" or "lbridge")
        {
            return false;
        }

        if (name.StartsWith("lbridge", StringComparison.Ordinal))
        {
            return false;
        }

        if (name.EndsWith("_in", StringComparison.Ordinal))
        {
            return true;
        }

        if (name.StartsWith("cci", StringComparison.Ordinal))
        {
            return true;
        }

        // lb* labyrinth / dungeon shells — exclude lbridge*
        if (name.StartsWith("lb", StringComparison.Ordinal))
        {
            return true;
        }

        if (name.StartsWith("rd_island", StringComparison.Ordinal))
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
    public static bool IsIndoorBaseTile(string? objectName, float godotY) =>
        IsIndoorDepth(godotY) && IsIndoorBaseTileName(objectName);
}
