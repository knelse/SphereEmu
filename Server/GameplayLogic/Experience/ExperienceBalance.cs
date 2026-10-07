using System.Text.Json.Serialization;
using SphServer.Helpers;
using SphServer.Server.Config;
using SphServer.Sphere.Game.Missions;

namespace SphServer.Server.GameplayLogic.Experience;

/// <summary>
/// experience.json: base XP curve and type/mission multipliers
/// </summary>
public class ExperienceBalance : IValidatableBalanceConfig
{
    /// <summary>
    /// Scales every award after type and mission multipliers; default 1.0
    /// </summary>
    [JsonPropertyName ("global_xp_multiplier")]
    public double GlobalXpMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Mob types missing from MultiplierPerMobType
    /// </summary>
    [JsonPropertyName ("rare_xp_multiplier")]
    public double RareXpMultiplier { get; init; }

    /// <summary>
    /// Full award per mob level, before type, mission, and global multipliers
    /// </summary>
    [JsonPropertyName ("base_xp_per_level")]
    public Dictionary<int, int> BaseXpPerLevel { get; init; } = new ();

    /// <summary>
    /// Missing types use RareXpMultiplier
    /// </summary>
    [JsonPropertyName ("multiplier_per_mob_type")]
    public Dictionary<int, double> MultiplierPerMobType { get; init; } = new ();

    /// <summary>
    /// Multiplier vs base per MissionType
    /// </summary>
    [JsonPropertyName ("multiplier_per_mission_type")]
    public Dictionary<MissionType, double> MultiplierPerMissionType { get; init; } = new ();

    public int GetBaseXpForLevel (int level)
    {
        return BaseXpPerLevel.TryGetValue (level, out var xp) ? xp : 0;
    }

    public double GetMobTypeMultiplier (int mobTypeId)
    {
        return MultiplierPerMobType.TryGetValue (mobTypeId, out var mult) ? mult : RareXpMultiplier;
    }

    public double GetMissionTypeMultiplier (MissionType missionType)
    {
        return MultiplierPerMissionType.TryGetValue (missionType, out var mult) ? mult : 0.0;
    }

    /// <summary>
    /// Active while max(degree-1, title-1) is at most 6; same-or-lower stays 1.0, each level above
    /// adds 10%, cap +55%
    /// </summary>
    public static double GetNewPlayerKillXpMultiplier (int titleMinusOne, int degreeMinusOne, int mobLevel)
    {
        if (Math.Max (titleMinusOne, degreeMinusOne) > 6)
        {
            return 1.0;
        }

        var playerLevel = Math.Max (titleMinusOne, degreeMinusOne) + 1;
        var levelDiff = mobLevel - playerLevel;
        if (levelDiff <= 0)
        {
            return 1.0;
        }

        return 1.0 + Math.Min (0.55, 0.10 * levelDiff);
    }

    /// <summary>
    /// Full XP while the mob is at most 5 below max(title, degree); each extra level cuts 5%, and 0
    /// at 25 below
    /// </summary>
    public static double GetUnderlevelKillXpMultiplier (int titleMinusOne, int degreeMinusOne, int mobLevel)
    {
        var playerLevel = Math.Max (titleMinusOne, degreeMinusOne) + 1;
        var levelsBelow = playerLevel - mobLevel;
        if (levelsBelow <= 5)
        {
            return 1.0;
        }

        return Math.Max (0.0, 1.0 - 0.05 * (levelsBelow - 5));
    }

    /// <summary>
    /// Display 60 in this cycle blocks that track; either track at 60 plus a guild blocks both
    /// </summary>
    public static bool CanReceiveKillExperience (int titleMinusOne, int degreeMinusOne, bool hasGuild, bool isTitle)
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
    /// Title guilds (Crusader, Hunter, Master of Steel, Armorer, Bandier) take title XP; degree
    /// guilds (Inquisitor, Archmage, Druid, Warlock, Necromancer) take degree; otherwise physical
    /// is title
    /// </summary>
    public static bool AwardKillExperienceToTitle (Guild guild, bool physicalMajority) =>
        GuildCatalog.LevelTrack (guild) switch
        {
            GuildLevelTrack.Title => true,
            GuildLevelTrack.Degree => false,
            _ => physicalMajority
        };

    public void Validate (string configPath)
    {
        if (GlobalXpMultiplier < 0)
        {
            throw new InvalidDataException ($"{configPath}: global_xp_multiplier must be >= 0.");
        }

        if (RareXpMultiplier < 0)
        {
            throw new InvalidDataException ($"{configPath}: rare_xp_multiplier must be >= 0.");
        }

        if (BaseXpPerLevel is not { Count: > 0 })
        {
            throw new InvalidDataException ($"{configPath}: base_xp_per_level must be a non-empty object.");
        }

        foreach (var (level, xp) in BaseXpPerLevel)
        {
            if (level <= 0)
            {
                throw new InvalidDataException ($"{configPath}: base_xp_per_level key {level} must be > 0.");
            }

            if (xp < 0)
            {
                throw new InvalidDataException (
                    $"{configPath}: base_xp_per_level[{level}] must be >= 0 (got {xp}).");
            }
        }

        if (MultiplierPerMobType is not { Count: > 0 })
        {
            throw new InvalidDataException ($"{configPath}: multiplier_per_mob_type must be a non-empty object.");
        }

        foreach (var (mobTypeId, mult) in MultiplierPerMobType)
        {
            if (mobTypeId <= 0)
            {
                throw new InvalidDataException ($"{configPath}: multiplier_per_mob_type key {mobTypeId} must be > 0.");
            }

            if (mult < 0)
            {
                throw new InvalidDataException (
                    $"{configPath}: multiplier_per_mob_type[{mobTypeId}] must be >= 0 (got {mult}).");
            }
        }

        if (MultiplierPerMissionType is not { Count: > 0 })
        {
            throw new InvalidDataException (
                $"{configPath}: multiplier_per_mission_type must be a non-empty object.");
        }

        foreach (MissionType missionType in Enum.GetValues<MissionType> ())
        {
            if (!MultiplierPerMissionType.TryGetValue (missionType, out var mult))
            {
                throw new InvalidDataException (
                    $"{configPath}: multiplier_per_mission_type is missing '{missionType}'.");
            }

            if (mult < 0)
            {
                throw new InvalidDataException (
                    $"{configPath}: multiplier_per_mission_type['{missionType}'] must be >= 0 (got {mult}).");
            }
        }
    }
}
