using System;
using SphServer.Server.Config;
using SphServer.Server.GameplayLogic.Experience;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
	/// <summary>
	///     Kill XP before pill/server/mission modifiers:
	///     <c>base_xp(level) * type_multiplier</c>, then × <see cref="ExperienceBalance.RareXpMultiplier" />
	///     when <see cref="IsNamed" />, then ±10% variance.
	/// </summary>
	public int GetExperienceForKill()
	{
		var level = MonsterInstance?.Level ?? Level;
		if (!MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue(MonsterType, out var monsterTypeId))
		{
			SphLogger.Error(
				$"Monster.GetExperienceForKill: no type id mapping for {MonsterType} (level={level}).");
			return 0;
		}

		var cfg = BalanceConfig.Get<ExperienceBalance>("experience");
		if (cfg is null)
		{
			SphLogger.Error(
				$"Monster.GetExperienceForKill: experience balance config unavailable (type={monsterTypeId} level={level}).");
			return 0;
		}

		var xp = cfg.GetBaseXpForLevel(level) * cfg.GetMobTypeMultiplier(monsterTypeId);
		if (IsNamed)
		{
			xp *= cfg.RareXpMultiplier;
		}

		// Uniform ±10% on the final award.
		xp *= 0.9 + Random.Shared.NextDouble() * 0.2;
		return (int)Math.Round(xp);
	}
}
