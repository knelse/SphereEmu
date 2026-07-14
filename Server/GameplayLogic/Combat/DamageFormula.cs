namespace SphServer.Server.GameplayLogic.Combat;

/// <summary>
///     One damage school's inputs: H = stat-sheet damage (PAtk physical / normalized MAtk magic),
///     the MainHand weapon's [Amin, Amax] band (fist band when empty), the target's PDef/MDef.
/// </summary>
public readonly record struct DamageSchoolInput (
    double StatSheetDamage,
    double WeaponAmin,
    double WeaponAmax,
    double TargetDefense);

/// <summary>
///     Damage formula of the original game, provided by Discord user "akvarel." (pure math; RNG injected, tunables
///     from <see cref="CombatBalance" />): Roll = uniform(Amin*K, Amax*K) with K = H/Aavg, so
///     E[Roll] = H; Damage = Roll - Defense*(6/7) when Roll >= Defense, else Roll^2 / (Defense*7).
/// </summary>
public static class DamageFormula
{
    public static double RollRaw (double statSheetDamage, double weaponAmin, double weaponAmax, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var weaponAvg = (weaponAmin + weaponAmax) / 2.0;
        if (weaponAvg <= 0.0)
        {
            // No weapon band: flat roll of H (consumes no RNG draw).
            return statSheetDamage;
        }

        var k = statSheetDamage / weaponAvg;
        var rollMin = weaponAmin * k;
        var rollMax = weaponAmax * k;
        return rollMin + rng.NextDouble() * (rollMax - rollMin);
    }

    /// <summary>
    ///     Two-branch defense mitigation; branches are continuous at roll == Defense when
    ///     subtractFactor == 1 - 1/divisor (the recovered 6/7 and 7).
    /// </summary>
    public static double ApplyDefense (double roll, double targetDefense, double defenseSubtractFactor,
        double defenseQuadraticDivisor)
    {
        if (defenseQuadraticDivisor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof (defenseQuadraticDivisor), defenseQuadraticDivisor,
                "defenseQuadraticDivisor must be > 0 (combat.json misconfigured?) — the Roll < Defense branch divides by Defense * divisor.");
        }

        if (defenseSubtractFactor is <= 0.0 or > 1.0)
        {
            // A missing combat.json key deserializes to 0.0 and would silently disable mitigation.
            throw new ArgumentOutOfRangeException(nameof (defenseSubtractFactor), defenseSubtractFactor,
                "defenseSubtractFactor must be in (0, 1] (combat.json misconfigured or key missing?).");
        }

        if (roll <= 0.0)
        {
            // A negative roll would square into positive damage in the quadratic branch.
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
    ///     Rounds and clamps to [0, clampMax] (30000 — the wire delta is a 16-bit biased field);
    ///     the clamp happens on the double before the int cast, so oversized rolls cannot overflow.
    /// </summary>
    public static int FinishDamage (double damage, DamageRounding roundingMode, int clampMax)
    {
        if (clampMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof (clampMax), clampMax,
                "damageClampMax must be > 0 (combat.json misconfigured?).");
        }

        if (double.IsNaN(damage) || damage <= 0.0)
        {
            return 0;
        }

        if (damage >= clampMax)
        {
            return clampMax;
        }

        var rounded = roundingMode switch
        {
            DamageRounding.Floor => (int) Math.Floor(damage),
            DamageRounding.Round => (int) Math.Round(damage, MidpointRounding.AwayFromZero),
            _ => throw new ArgumentOutOfRangeException(nameof (roundingMode), roundingMode,
                "Unknown rounding mode.")
        };

        return Math.Clamp(rounded, 0, clampMax);
    }

    /// <summary>
    ///     Full pipeline: raw roll → defense mitigation → round + clamp. <see cref="Random" /> is
    ///     not thread-safe — the caller owns the RNG's thread affinity.
    /// </summary>
    public static int RollSchoolDamage (in DamageSchoolInput school, Random rng, CombatBalance cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);

        var roll = RollRaw(school.StatSheetDamage, school.WeaponAmin, school.WeaponAmax, rng);
        var mitigated = ApplyDefense(roll, school.TargetDefense, cfg.DefenseSubtractFactor,
            cfg.DefenseQuadraticDivisor);
        return FinishDamage(mitigated, cfg.Rounding, cfg.DamageClampMax);
    }
}
