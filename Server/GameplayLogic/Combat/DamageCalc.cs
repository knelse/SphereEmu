using SphServer.Shared.Logger;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Server.GameplayLogic.Combat;

/// <summary>
/// Miss and crit sit on top of the base formula
/// </summary>
public readonly record struct MeleeHitRoll (int Damage, bool IsMiss, bool IsCrit);
public readonly record struct MagicHitRoll (int Damage, bool IsMiss, bool IsCrit);

/// <summary>
/// Equal school damage counts as physical, matching the player hit path
/// </summary>
public readonly record struct AttackRoll (
    int PhysicalDamage,
    int MagicDamage,
    bool PhysicalMiss,
    bool MagicMiss,
    bool IsCrit)
{
    public int TotalDamage => PhysicalDamage + MagicDamage;

    public bool IsMiss => PhysicalMiss && MagicMiss;

    public DamageSchool School =>
        MagicDamage > PhysicalDamage ? DamageSchool.Magical : DamageSchool.Physical;
}

/// <summary>
/// Miss, then the formula, then crit re-clamped, then the non-miss floor
/// </summary>
public static class DamageCalc
{
    /// <summary>
    /// Draw order is fixed so a seed stays deterministic
    /// </summary>
    public static MeleeHitRoll RollMeleeHit (int attackerPAtk, bool isBareHanded, double targetPDef, Random rng,
        CombatBalance cfg)
    {
        if (rng is null || cfg is null)
        {
            SphLogger.Error ("DamageCalc.RollMeleeHit: rng or combat balance config is null — no damage rolled.");
            return new MeleeHitRoll (0, true, false);
        }

        // item doesn't contribute PAtk, so this is purely magic damage
        if (attackerPAtk == 0 && !isBareHanded)
        {
            return new MeleeHitRoll (0, true, false);
        }

        var missRoll = rng.NextDouble ();
        if (missRoll < cfg.MissChance)
        {
            return new MeleeHitRoll (0, true, false);
        }

        // Fists have no game object row, so FistStatSheetDamage stands in for held-item PAtk
        var statSheetDamage = Math.Abs (attackerPAtk) + (isBareHanded ? cfg.FistStatSheetDamage : 0);
        var schoolInput = new DamageSchoolInput (statSheetDamage, cfg.MeleeAmin, cfg.MeleeAmax, targetPDef);
        var damage = DamageFormula.RollSchoolDamage (in schoolInput, rng, cfg);

        var critRoll = rng.NextDouble ();
        var isCrit = critRoll < cfg.CritChance;
        if (isCrit)
        {
            damage = (int) Math.Min (Math.Floor (damage * cfg.CritMult), cfg.DamageClampMax);
        }

        // The floor is applied after the formula clamp and can exceed DamageClampMax, which the
        // wire field cannot encode
        damage = Math.Clamp (Math.Max (damage, cfg.MinMeleeHit), 0, cfg.DamageClampMax);
        return new MeleeHitRoll (damage, false, isCrit);
    }


    /// <summary>
    /// Draw order is fixed so a seed stays deterministic
    /// </summary>
    public static MagicHitRoll RollMagicHit (int attackerMAtk, bool isBareHanded, double targetMDef, Random rng,
        CombatBalance cfg)
    {
        if (rng is null || cfg is null)
        {
            SphLogger.Error ("DamageCalc.RollMagicHit: rng or combat balance config is null — no damage rolled.");
            return new MagicHitRoll (0, true, false);
        }

        if (attackerMAtk == 0)
        {
            return new MagicHitRoll (0, true, false);
        }

        if (isBareHanded)
        {
            return new MagicHitRoll (0, true, false);
        }

        var missRoll = rng.NextDouble ();
        if (missRoll < cfg.MissChance)
        {
            return new MagicHitRoll (0, true, false);
        }

        // A held item contributes its own attack through MAtk
        var statSheetDamage = Math.Abs (attackerMAtk);
        var schoolInput = new DamageSchoolInput (statSheetDamage, cfg.MeleeAmin, cfg.MeleeAmax, targetMDef);
        var damage = DamageFormula.RollSchoolDamage (in schoolInput, rng, cfg);

        var critRoll = rng.NextDouble ();
        var isCrit = critRoll < cfg.CritChance;
        if (isCrit)
        {
            damage = (int) Math.Min (Math.Floor (damage * cfg.CritMult), cfg.DamageClampMax);
        }

        // The floor is applied after the formula clamp and can exceed DamageClampMax, which the
        // wire field cannot encode
        damage = Math.Clamp (Math.Max (damage, cfg.MinMeleeHit), 0, cfg.DamageClampMax);
        return new MagicHitRoll (damage, false, isCrit);
    }

    /// <summary>
    /// Physical then magical, same RNG order as the two rolls; bare-handed adds fist attack, and a
    /// 0/0 row stays 0
    /// </summary>
    public static AttackRoll RollAttack (int attackerPAtk, int attackerMAtk, bool isBareHanded, double targetPDef,
        double targetMDef, Random rng, CombatBalance cfg)
    {
        var melee = RollMeleeHit (attackerPAtk, isBareHanded, targetPDef, rng, cfg);
        var magic = RollMagicHit (attackerMAtk, isBareHanded, targetMDef, rng, cfg);
        return new AttackRoll (melee.Damage, magic.Damage, melee.IsMiss, magic.IsMiss, melee.IsCrit || magic.IsCrit);
    }
}
