namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Shared tunables for indoor / dungeon spawn and related gameplay.
/// </summary>
public static class IndoorFieldConfig
{
    /// <summary>
    ///     Default spawn radius for indoor-depth spawners
    ///     (Godot Y &lt; <see cref="IndoorAreaCriteria.MaxIndoorPlacementY" />).
    /// </summary>
    public const float DefaultSpawnRadiusMeters = 4f;

    /// <summary>
    ///     Outdoor vs indoor default spawn radius from Godot/SOURCE_BASIS Y.
    /// </summary>
    public static float ResolveDefaultSpawnRadiusMeters(float godotY) =>
        IndoorAreaCriteria.IsIndoorDepth(godotY)
            ? DefaultSpawnRadiusMeters
            : OutdoorFieldConfig.DefaultSpawnRadiusMeters;
}
