using SphServer.Shared.Logger;

namespace SphServer.Server.GameplayLogic.Combat;

/// <summary>
/// H is stat-sheet damage (PAtk, or normalized MAtk), plus the melee Amin/Amax band and target
/// defense
/// </summary>
public readonly record struct DamageSchoolInput (
    double StatSheetDamage,
    double WeaponAmin,
    double WeaponAmax,
    double TargetDefense);

/// <summary>
/// Roll is uniform(Amin*K, Amax*K) with K = H/Aavg so E[Roll] = H; at or above defense, Roll -
/// Defense*(6/7), else Roll^2 / (Defense*7)
/// </summary>
public static class DamageFormula
{
    public static double RollRaw (double statSheetDamage, double weaponAmin, double weaponAmax, Random rng)
    {
        if (rng is null)
        {
            SphLogger.Error ("DamageFormula.RollRaw: rng is null — rolling zero damage.");
            return 0.0;
        }

        var weaponAvg = (weaponAmin + weaponAmax) / 2.0;
        if (weaponAvg <= 0.0)
        {
            // No weapon band: flat roll of H, and this consumes no RNG draw
            return statSheetDamage;
        }

        var k = statSheetDamage / weaponAvg;
        var rollMin = weaponAmin * k;
        var rollMax = weaponAmax * k;
        return rollMin + rng.NextDouble () * (rollMax - rollMin);
    }

    /// <summary>
    /// Branches meet at roll == defense when subtractFactor is 1 - 1/divisor (recovered 6/7 and 7)
    /// </summary>
    public static double ApplyDefense (double roll, double targetDefense, double defenseSubtractFactor,
        double defenseQuadraticDivisor)
    {
        if (defenseQuadraticDivisor <= 0.0)
        {
            SphLogger.Error (
                $"DamageFormula.ApplyDefense: defenseQuadraticDivisor must be > 0 (combat.json misconfigured?), got {defenseQuadraticDivisor}. Dealing zero damage.");
            return 0.0;
        }

        if (defenseSubtractFactor is <= 0.0 or > 1.0)
        {
            // A missing combat.json key is 0 and would turn mitigation off
            SphLogger.Error (
                $"DamageFormula.ApplyDefense: defenseSubtractFactor must be in (0, 1] (combat.json key missing?), got {defenseSubtractFactor}. Dealing zero damage.");
            return 0.0;
        }

        if (roll <= 0.0)
        {
            // A negative roll squares into positive damage on the quadratic branch
            return 0.0;
        }

        if (targetDefense <= 0.0)
        {
            return roll;
        }

        return roll >= targetDefense
            ? roll - targetDefense * defenseSubtractFactor
            : roll * roll / (targetDefense * defenseQuadraticDivisor);
    }

    /// <summary>
    /// Clamp is on the double before the int cast, at most 30000, the 16-bit biased wire field
    /// </summary>
    public static int FinishDamage (double damage, DamageRounding roundingMode, int clampMax)
    {
        if (clampMax <= 0)
        {
            SphLogger.Error (
                $"DamageFormula.FinishDamage: damageClampMax must be > 0 (combat.json misconfigured?), got {clampMax}. Dealing zero damage.");
            return 0;
        }

        if (double.IsNaN (damage) || damage <= 0.0)
        {
            return 0;
        }

        if (damage >= clampMax)
        {
            return clampMax;
        }

        if (roundingMode is not (DamageRounding.Floor or DamageRounding.Round))
        {
            SphLogger.Error ($"DamageFormula.FinishDamage: unknown rounding mode {roundingMode} — flooring.");
        }

        var rounded = roundingMode == DamageRounding.Round
            ? (int) Math.Round (damage, MidpointRounding.AwayFromZero)
            : (int) Math.Floor (damage);

        return Math.Clamp (rounded, 0, clampMax);
    }

    /// <summary>
    /// Random is not thread-safe; the caller owns which thread draws
    /// </summary>
    public static int RollSchoolDamage (in DamageSchoolInput school, Random rng, CombatBalance cfg)
    {
        if (cfg is null)
        {
            SphLogger.Error ("DamageFormula.RollSchoolDamage: combat balance config is null — dealing zero damage.");
            return 0;
        }

        var roll = RollRaw (school.StatSheetDamage, school.WeaponAmin, school.WeaponAmax, rng);
        var mitigated = ApplyDefense (roll, school.TargetDefense, cfg.DefenseSubtractFactor,
            cfg.DefenseQuadraticDivisor);
        return FinishDamage (mitigated, cfg.Rounding, cfg.DamageClampMax);
    }
}
