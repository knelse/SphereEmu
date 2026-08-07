using SphServer.Server.Config;

namespace SphServer.Server.GameplayLogic.Combat;

/// <summary>
///     The original game's rounding style is unknown, so it ships as a config choice;
///     Floor matches the client's <c>int(...)</c> truncation.
/// </summary>
public enum DamageRounding
{
    Floor,
    Round
}

/// <summary>
///     Typed view of <c>Config/Balance/combat.json</c> — value provenance is commented there.
/// </summary>
public class CombatBalance : IValidatableBalanceConfig
{
    /// <summary>Subtract-branch mitigation factor. Recovered: exactly 6/7.</summary>
    public double DefenseSubtractFactor { get; init; }

    /// <summary>Quadratic-branch divisor. Recovered: 7.</summary>
    public double DefenseQuadraticDivisor { get; init; }

    /// <summary>
    ///     Melee <c>[Amin, Amax]</c> spread, applied to every swing (invented). Only the ratio matters —
    ///     the roll is scaled so its mean is H — and no weapon in the game data carries a band of its own.
    /// </summary>
    public double[] MeleeAminAmax { get; init; } = [];

    /// <summary>"floor" or "round" (case-insensitive); parsed via <see cref="Rounding" />.</summary>
    public string RoundingMode { get; init; } = "floor";

    /// <summary>Per-hit wire cap: the damage field is a 16-bit biased value — an encoding limit, not a tunable.</summary>
    public int DamageClampMax { get; init; }

    /// <summary>Rolled by the melee handler, not the formula. Default 0 (off).</summary>
    public double CritChance { get; init; }

    public double CritMult { get; init; }

    /// <summary>A miss deals 0 but is still replied. Default 0 (off).</summary>
    public double MissChance { get; init; }

    /// <summary>Stand-in attack for an empty hand (invented): fists have no game object row to read.</summary>
    public double FistStatSheetDamage { get; init; }

    /// <summary>Non-miss melee damage floor, applied after formula + crit (invented).</summary>
    public int MinMeleeHit { get; init; }

    /// <summary>
    ///     Melee range sanity bound in Godot meters, &lt;= 0 disables (invented; the client already
    ///     enforces ~1.5). Out-of-range attacks get the zero-damage swing echo, never a formula roll.
    /// </summary>
    public double MeleeRangeMeters { get; init; }

    public double MeleeAmin => MeleeBandValue(0);

    public double MeleeAmax => MeleeBandValue(1);

    // Validate() rejects anything else at load time, so the packet path never sees an unknown mode.
    public DamageRounding Rounding =>
        (RoundingMode ?? string.Empty).Trim().ToLowerInvariant() == "round"
            ? DamageRounding.Round
            : DamageRounding.Floor;

    public void Validate (string configPath)
    {
        var mode = (RoundingMode ?? string.Empty).Trim().ToLowerInvariant();
        if (mode is not ("floor" or "round"))
        {
            throw new InvalidDataException(
                $"{configPath}: unknown roundingMode '{RoundingMode}' — expected \"floor\" or \"round\".");
        }

        if (MeleeAminAmax is not { Length: 2 } || MeleeAminAmax[0] <= 0 || MeleeAminAmax[1] < MeleeAminAmax[0])
        {
            throw new InvalidDataException(
                $"{configPath}: meleeAminAmax must be [Amin, Amax] with 0 < Amin <= Amax.");
        }

        // Bounds the damage path relies on; without them a bad file reaches the wire encoder.
        if (DamageClampMax is < 0 or > 30000)
        {
            throw new InvalidDataException(
                $"{configPath}: damageClampMax must be 0..30000 — the wire field encodes 30000 - damage.");
        }

        if (MinMeleeHit < 0)
        {
            throw new InvalidDataException($"{configPath}: minMeleeHit must be >= 0.");
        }

        if (CritMult < 0)
        {
            throw new InvalidDataException($"{configPath}: critMult must be >= 0.");
        }

        if (MeleeRangeMeters < 0)
        {
            throw new InvalidDataException(
                $"{configPath}: meleeRangeMeters must be >= 0 (0 disables the range check).");
        }
    }

    private double MeleeBandValue (int index) => MeleeAminAmax[index];
}
