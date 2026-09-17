using System.Text.Json.Serialization;
using SphServer.Helpers;
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

    /// <summary>
    ///     New-player overlevel kill bonus. Active while
    ///     <c>max(degree-1, title-1) &lt;= 6</c>. Same-or-lower mobs stay at 1.0;
    ///     each level above the displayed player level adds 10%, capped at +55%.
    /// </summary>
    public static double GetNewPlayerKillXpMultiplier(int titleMinusOne, int degreeMinusOne, int mobLevel)
    {
        if (Math.Max(titleMinusOne, degreeMinusOne) > 6)
        {
            return 1.0;
        }

        var playerLevel = Math.Max(titleMinusOne, degreeMinusOne) + 1;
        var levelDiff = mobLevel - playerLevel;
        if (levelDiff <= 0)
        {
            return 1.0;
        }

        return 1.0 + Math.Min(0.55, 0.10 * levelDiff);
    }

    /// <summary>
    ///     Underlevel penalty vs <c>max(title, degree)</c> (rebirths included).
    ///     Full XP while the mob is at most 5 levels below the player; each extra level
    ///     below cuts 5%, reaching 0 after 20 extra levels (mob 25+ below).
    /// </summary>
    public static double GetUnderlevelKillXpMultiplier(int titleMinusOne, int degreeMinusOne, int mobLevel)
    {
        var playerLevel = Math.Max(titleMinusOne, degreeMinusOne) + 1;
        var levelsBelow = playerLevel - mobLevel;
        if (levelsBelow <= 5)
        {
            return 1.0;
        }

        return Math.Max(0.0, 1.0 - 0.05 * (levelsBelow - 5));
    }

    /// <summary>
    ///     Kill XP for <paramref name="isTitle"/> is blocked at display 60 in the current
    ///     rebirth cycle for that track. Either track at 60 plus a guild blocks both.
    /// </summary>
    public static bool CanReceiveKillExperience(int titleMinusOne, int degreeMinusOne, bool hasGuild, bool isTitle)
    {
        var titleAtCap = titleMinusOne % CharacterDataHelper.LevelsPerCycle == CharacterDataHelper.LevelsPerCycle - 1;
        var degreeAtCap = degreeMinusOne % CharacterDataHelper.LevelsPerCycle == CharacterDataHelper.LevelsPerCycle - 1;
        if (hasGuild && (titleAtCap || degreeAtCap))
        {
            return false;
        }

        return isTitle ? !titleAtCap : !degreeAtCap;
    }

    /// <summary>
    ///     Title guilds (Crusader, Hunter, Master of Steel, Armorer, Bandier) take all kill XP
    ///     as title. Degree guilds (Inquisitor, Archmage, Druid, Warlock, Necromancer) take it
    ///     as degree. Everyone else follows physical → title, magic → degree.
    /// </summary>
    public static bool AwardKillExperienceToTitle(Guild guild, bool physicalMajority) =>
        GuildCatalog.LevelTrack(guild) switch
        {
            GuildLevelTrack.Title => true,
            GuildLevelTrack.Degree => false,
            _ => physicalMajority
        };

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
