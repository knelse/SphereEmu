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
public class CombatBalance
{
    /// <summary>Subtract-branch mitigation factor. Recovered: exactly 6/7.</summary>
    public double DefenseSubtractFactor { get; init; }

    /// <summary>Quadratic-branch divisor. Recovered: 7.</summary>
    public double DefenseQuadraticDivisor { get; init; }

    /// <summary>Bare-fist <c>[Amin, Amax]</c> damage band (invented — fists have no weapon row).</summary>
    public double[] FistAminAmax { get; init; } = [];

    /// <summary>"floor" or "round" (case-insensitive); parsed via <see cref="Rounding" />.</summary>
    public string RoundingMode { get; init; } = "floor";

    /// <summary>Per-hit wire cap: the damage field is a 16-bit biased value — an encoding limit, not a tunable.</summary>
    public int DamageClampMax { get; init; }

    /// <summary>Rolled by the melee handler, not the formula. Default 0 (off).</summary>
    public double CritChance { get; init; }

    public double CritMult { get; init; }

    /// <summary>A miss deals 0 but is still replied. Default 0 (off).</summary>
    public double MissChance { get; init; }

    /// <summary>
    ///     Flat fist damage forming H (invented): PAtk excludes MainHand, so a naked character
    ///     has PAtk 0 and fists would deal 0 forever without this.
    /// </summary>
    public double FistStatSheetDamage { get; init; }

    /// <summary>Non-miss melee damage floor, applied after formula + crit (invented).</summary>
    public int MinMeleeHit { get; init; }

    /// <summary>
    ///     Melee range sanity bound in Godot meters, &lt;= 0 disables (invented; the client already
    ///     enforces ~1.5). Out-of-range attacks get the zero-damage swing echo, never a formula roll.
    /// </summary>
    public double MeleeRangeMeters { get; init; }

    public double FistAmin => FistBandValue(0);

    public double FistAmax => FistBandValue(1);

    public DamageRounding Rounding =>
        (RoundingMode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "floor" => DamageRounding.Floor,
            "round" => DamageRounding.Round,
            _ => throw new InvalidDataException(
                $"combat.json: unknown roundingMode '{RoundingMode}' — expected \"floor\" or \"round\".")
        };

    private double FistBandValue (int index)
    {
        if (FistAminAmax is not { Length: 2 })
        {
            throw new InvalidDataException(
                "combat.json: fistAminAmax must be a two-element array [Amin, Amax].");
        }

        return FistAminAmax[index];
    }
}
