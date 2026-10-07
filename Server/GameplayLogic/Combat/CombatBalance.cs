using SphServer.Server.Config;

namespace SphServer.Server.GameplayLogic.Combat;

/// <summary>
/// Floor matches the client's int() truncation
/// </summary>
public enum DamageRounding
{
    Floor,
    Round
}

/// <summary>
/// Values are documented in Config/Balance/combat.json
/// </summary>
public class CombatBalance : IValidatableBalanceConfig
{
    /// <summary>
    /// Subtract-branch factor, recovered as exactly 6/7
    /// </summary>
    public double DefenseSubtractFactor { get; init; }

    /// <summary>
    /// Quadratic-branch divisor, recovered as 7
    /// </summary>
    public double DefenseQuadraticDivisor { get; init; }

    /// <summary>
    /// Only the Amin/Amax ratio matters: the roll is scaled so its mean is H, and no weapon row
    /// carries its own band
    /// </summary>
    public double[] MeleeAminAmax { get; init; } = [];

    /// <summary>
    /// "floor" or "round", case-insensitive, parsed via Rounding
    /// </summary>
    public string RoundingMode { get; init; } = "floor";

    /// <summary>
    /// 16-bit biased damage field, an encoding limit
    /// </summary>
    public int DamageClampMax { get; init; }

    /// <summary>
    /// Rolled by the melee handler, not the formula; default 0 (off)
    /// </summary>
    public double CritChance { get; init; }

    public double CritMult { get; init; }

    /// <summary>
    /// A miss deals 0 but is still replied; default 0 (off)
    /// </summary>
    public double MissChance { get; init; }

    /// <summary>
    /// Empty-hand attack: fists have no game object row
    /// </summary>
    public double FistStatSheetDamage { get; init; }

    /// <summary>
    /// Non-miss melee floor, after the formula and crit
    /// </summary>
    public int MinMeleeHit { get; init; }

    /// <summary>
    /// Godot meters; 0 or below disables, and out of range is a zero-damage echo, not a formula
    /// roll
    /// </summary>
    public double MeleeRangeMeters { get; init; }

    /// <summary>
    /// hp/patk/matk/pdef/mdef per NamedBossRank, after level times per-level; 1.0 is no buff
    /// </summary>
    public NamedBossRankStatMultipliers NamedBossRankStatMultiplier { get; init; } = new ();

    public double MeleeAmin => MeleeBandValue (0);

    public double MeleeAmax => MeleeBandValue (1);

    // Validate rejects anything else, so the packet path never sees an unknown mode
    public DamageRounding Rounding =>
        (RoundingMode ?? string.Empty).Trim ().ToLowerInvariant () == "round"
            ? DamageRounding.Round
            : DamageRounding.Floor;

    public void Validate (string configPath)
    {
        var mode = (RoundingMode ?? string.Empty).Trim ().ToLowerInvariant ();
        if (mode is not ("floor" or "round"))
        {
            throw new InvalidDataException (
                $"{configPath}: unknown roundingMode '{RoundingMode}' — expected \"floor\" or \"round\".");
        }

        if (MeleeAminAmax is not { Length: 2 } || MeleeAminAmax[0] <= 0 || MeleeAminAmax[1] < MeleeAminAmax[0])
        {
            throw new InvalidDataException (
                $"{configPath}: meleeAminAmax must be [Amin, Amax] with 0 < Amin <= Amax.");
        }

        // 0-30000: past that the 16-bit biased field cannot encode the value
        if (DamageClampMax is < 0 or > 30000)
        {
            throw new InvalidDataException (
                $"{configPath}: damageClampMax must be 0..30000 — the wire field encodes 30000 - damage.");
        }

        if (MinMeleeHit < 0)
        {
            throw new InvalidDataException ($"{configPath}: minMeleeHit must be >= 0.");
        }

        if (CritMult < 0)
        {
            throw new InvalidDataException ($"{configPath}: critMult must be >= 0.");
        }

        if (MeleeRangeMeters < 0)
        {
            throw new InvalidDataException (
                $"{configPath}: meleeRangeMeters must be >= 0 (0 disables the range check).");
        }

        RequirePositiveMultiplier (configPath, "namedBossRankStatMultiplier.none", NamedBossRankStatMultiplier.None);
        RequirePositiveMultiplier (configPath, "namedBossRankStatMultiplier.randomSpawn", NamedBossRankStatMultiplier.RandomSpawn);
        RequirePositiveMultiplier (configPath, "namedBossRankStatMultiplier.fixedSpawnLootable", NamedBossRankStatMultiplier.FixedSpawnLootable);
        RequirePositiveMultiplier (configPath, "namedBossRankStatMultiplier.fixedSpawnHobo", NamedBossRankStatMultiplier.FixedSpawnHobo);
        RequirePositiveMultiplier (configPath, "namedBossRankStatMultiplier.event", NamedBossRankStatMultiplier.Event);
    }

    public double StatMultiplier (NamedBossRank rank) => rank switch
    {
        NamedBossRank.None => NamedBossRankStatMultiplier.None,
        NamedBossRank.RandomSpawn => NamedBossRankStatMultiplier.RandomSpawn,
        NamedBossRank.FixedSpawnLootable => NamedBossRankStatMultiplier.FixedSpawnLootable,
        NamedBossRank.FixedSpawnHobo => NamedBossRankStatMultiplier.FixedSpawnHobo,
        NamedBossRank.Event => NamedBossRankStatMultiplier.Event,
        _ => throw new ArgumentOutOfRangeException (nameof (rank), rank, null)
    };

    private static void RequirePositiveMultiplier (string configPath, string field, double value)
    {
        if (value <= 0 || double.IsNaN (value) || double.IsInfinity (value))
        {
            throw new InvalidDataException ($"{configPath}: {field} must be a finite number > 0.");
        }
    }

    private double MeleeBandValue (int index) => MeleeAminAmax[index];
}

/// <summary>
/// Coefficients from <c>combat.json</c> <c>namedBossRankStatMultiplier</c>.
/// </summary>
public class NamedBossRankStatMultipliers
{
    public double None { get; init; }

    public double RandomSpawn { get; init; }

    public double FixedSpawnLootable { get; init; }

    public double FixedSpawnHobo { get; init; }

    public double Event { get; init; }
}
