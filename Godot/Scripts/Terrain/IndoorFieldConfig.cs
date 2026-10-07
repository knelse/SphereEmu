namespace SphServer.Godot.Scripts.Terrain;

public static class IndoorFieldConfig
{
    /// <summary>
    /// Indoor-depth spawners (Godot Y below IndoorAreaCriteria.MaxIndoorPlacementY)
    /// </summary>
    public const float DefaultSpawnRadiusMeters = 4f;

    public static float ResolveDefaultSpawnRadiusMeters (float godotY) =>
        IndoorAreaCriteria.IsIndoorDepth (godotY)
            ? DefaultSpawnRadiusMeters
            : OutdoorFieldConfig.DefaultSpawnRadiusMeters;
}
