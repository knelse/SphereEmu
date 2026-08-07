using SphServer.Shared.Logger;

namespace SphServer.Server.GameplayLogic.Combat;

/// <summary>Delivery-layer roll result (miss/crit applied on top of the base damage formula).</summary>
public readonly record struct MeleeHitRoll (int Damage, bool IsMiss, bool IsCrit);

/// <summary>
///     Melee delivery layer on top of <see cref="DamageFormula" />: miss roll, then the damage formula,
///     then crit (re-clamped), then the non-miss floor.
/// </summary>
public static class DamageCalc
{
    /// <summary>Fixed rng draw order keeps seeded tests deterministic.</summary>
    public static MeleeHitRoll RollMeleeHit (int attackerPAtk, bool isBareHanded, double targetPDef, Random rng,
        CombatBalance cfg)
    {
        if (rng is null || cfg is null)
        {
            SphLogger.Error("DamageCalc.RollMeleeHit: rng or combat balance config is null — no damage rolled.");
            return new MeleeHitRoll(0, true, false);
        }

        var missRoll = rng.NextDouble();
        if (missRoll < cfg.MissChance)
        {
            return new MeleeHitRoll(0, true, false);
        }

        // A held item contributes its own attack through PAtk. Fists have no game object to read
        // one from, so the configured flat stands in for it.
        var statSheetDamage = Math.Abs(attackerPAtk) + (isBareHanded ? cfg.FistStatSheetDamage : 0);
        var schoolInput = new DamageSchoolInput(statSheetDamage, cfg.MeleeAmin, cfg.MeleeAmax, targetPDef);
        var damage = DamageFormula.RollSchoolDamage(in schoolInput, rng, cfg);

        var critRoll = rng.NextDouble();
        var isCrit = critRoll < cfg.CritChance;
        if (isCrit)
        {
            damage = (int) Math.Min(Math.Floor(damage * cfg.CritMult), cfg.DamageClampMax);
        }

        // Re-clamp: the floor is applied after the formula's clamp and could otherwise push the
        // result past DamageClampMax, which the wire field cannot encode.
        damage = Math.Clamp(Math.Max(damage, cfg.MinMeleeHit), 0, (int) cfg.DamageClampMax);
        return new MeleeHitRoll(damage, false, isCrit);
    }
}
