using System.Text.Json.Serialization;
using SphServer.Server.Config;
using SphServer.Sphere.Game.Missions;

namespace SphServer.Server.GameplayLogic.Experience;

/// <summary>
///     Typed view of <c>Config/Balance/experience.json</c> — base XP curve and type/mission multipliers.
/// </summary>
public class ExperienceBalance : IValidatableBalanceConfig
{
    /// <summary>Scales all XP awards after type/mission multipliers. Default 1.0.</summary>
    [JsonPropertyName("global_xp_multiplier")]
    public double GlobalXpMultiplier { get; init; } = 1.0;

    /// <summary>
    ///     Default XP multiplier for rare / unlisted mob types
    ///     (entries omitted from <see cref="MultiplierPerMobType" />).
    /// </summary>
    [JsonPropertyName("rare_xp_multiplier")]
    public double RareXpMultiplier { get; init; }

    /// <summary>Discrete base XP per mob level (full award, before type/mission/global multipliers).</summary>
    [JsonPropertyName("base_xp_per_level")]
    public Dictionary<int, int> BaseXpPerLevel { get; init; } = new();

    /// <summary>
    ///     XP multiplier vs base curve per mob type id.
    ///     Types not present use <see cref="RareXpMultiplier" />.
    /// </summary>
    [JsonPropertyName("multiplier_per_mob_type")]
    public Dictionary<int, double> MultiplierPerMobType { get; init; } = new();

    /// <summary>XP multiplier vs base per <see cref="MissionType" />.</summary>
    [JsonPropertyName("multiplier_per_mission_type")]
    public Dictionary<MissionType, double> MultiplierPerMissionType { get; init; } = new();

    public int GetBaseXpForLevel(int level)
    {
        return BaseXpPerLevel.TryGetValue(level, out var xp) ? xp : 0;
    }

    public double GetMobTypeMultiplier(int mobTypeId)
    {
        return MultiplierPerMobType.TryGetValue(mobTypeId, out var mult) ? mult : RareXpMultiplier;
    }

    public double GetMissionTypeMultiplier(MissionType missionType)
    {
        return MultiplierPerMissionType.TryGetValue(missionType, out var mult) ? mult : 0.0;
    }

    public void Validate(string configPath)
    {
        if (GlobalXpMultiplier < 0)
        {
            throw new InvalidDataException($"{configPath}: global_xp_multiplier must be >= 0.");
        }

        if (RareXpMultiplier < 0)
        {
            throw new InvalidDataException($"{configPath}: rare_xp_multiplier must be >= 0.");
        }

        if (BaseXpPerLevel is not { Count: > 0 })
        {
            throw new InvalidDataException($"{configPath}: base_xp_per_level must be a non-empty object.");
        }

        foreach (var (level, xp) in BaseXpPerLevel)
        {
            if (level <= 0)
            {
                throw new InvalidDataException($"{configPath}: base_xp_per_level key {level} must be > 0.");
            }

            if (xp < 0)
            {
                throw new InvalidDataException(
                    $"{configPath}: base_xp_per_level[{level}] must be >= 0 (got {xp}).");
            }
        }

        if (MultiplierPerMobType is not { Count: > 0 })
        {
            throw new InvalidDataException($"{configPath}: multiplier_per_mob_type must be a non-empty object.");
        }

        foreach (var (mobTypeId, mult) in MultiplierPerMobType)
        {
            if (mobTypeId <= 0)
            {
                throw new InvalidDataException($"{configPath}: multiplier_per_mob_type key {mobTypeId} must be > 0.");
            }

            if (mult < 0)
            {
                throw new InvalidDataException(
                    $"{configPath}: multiplier_per_mob_type[{mobTypeId}] must be >= 0 (got {mult}).");
            }
        }

        if (MultiplierPerMissionType is not { Count: > 0 })
        {
            throw new InvalidDataException(
                $"{configPath}: multiplier_per_mission_type must be a non-empty object.");
        }

        foreach (MissionType missionType in Enum.GetValues<MissionType>())
        {
            if (!MultiplierPerMissionType.TryGetValue(missionType, out var mult))
            {
                throw new InvalidDataException(
                    $"{configPath}: multiplier_per_mission_type is missing '{missionType}'.");
            }

            if (mult < 0)
            {
                throw new InvalidDataException(
                    $"{configPath}: multiplier_per_mission_type['{missionType}'] must be >= 0 (got {mult}).");
            }
        }
    }
}
