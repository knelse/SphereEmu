using System;
using SphServer.Server.Config;
using SphServer.Server.GameplayLogic.Experience;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
    /// <summary>
    /// base_xp(level) * type_multiplier, then rare, overlevel, underlevel, and +/-10%; display 60
    /// in cycle awards 0 and eligible killers floor at 1
    /// </summary>
    public int GetExperienceForKill (CharacterDbEntry? killer = null, bool awardTitle = true)
    {
        var level = MonsterInstance?.Level ?? Level;
        if (!MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue (MonsterType, out var monsterTypeId))
        {
            SphLogger.Error (
                $"Monster.GetExperienceForKill: no type id mapping for {MonsterType} (level={level}).");
            return 0;
        }

        var cfg = BalanceConfig.Get<ExperienceBalance> ("experience");
        if (cfg is null)
        {
            SphLogger.Error (
                $"Monster.GetExperienceForKill: experience balance config unavailable (type={monsterTypeId} level={level}).");
            return 0;
        }

        if (killer is not null
            && !ExperienceBalance.CanReceiveKillExperience (
                killer.TitleMinusOne, killer.DegreeMinusOne, killer.Guild != Guild.None, awardTitle))
        {
            return 0;
        }

        var xp = cfg.GetBaseXpForLevel (level) * cfg.GetMobTypeMultiplier (monsterTypeId);
        if (NamedBossRank != NamedBossRank.None)
        {
            xp *= cfg.RareXpMultiplier;
        }

        if (killer is not null)
        {
            xp *= ExperienceBalance.GetNewPlayerKillXpMultiplier (
                killer.TitleMinusOne, killer.DegreeMinusOne, level);
            xp *= ExperienceBalance.GetUnderlevelKillXpMultiplier (
                killer.TitleMinusOne, killer.DegreeMinusOne, level);
        }

        // Uniform ±10% on the final award.
        xp *= 0.9 + Random.Shared.NextDouble () * 0.2;
        var awarded = (int) Math.Round (xp);
        if (killer is not null)
        {
            awarded = Math.Max (1, awarded);
        }

        return awarded;
    }
}
