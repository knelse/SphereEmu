using System.Collections.Generic;
using Godot;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Builds a level → monster-type lookup from <see cref="SphObjectDb.GameObjectDataDb" /> once,
///     then serves cached subsets for spawner level ranges without re-scanning the database.
/// </summary>
public static class MonsterSpawnerMonsterTypeLookup
{
	private static readonly object BuildLock = new();
	private static Dictionary<int, List<MonsterType>>? monsterTypesByLevel;
	private static bool buildLoggedEmptyLevels;

	public static Dictionary<int, IReadOnlyList<MonsterType>> BuildLevelSubset(int minLevel, int maxLevel)
	{
		EnsureBuilt();

		if (minLevel > maxLevel)
		{
			(minLevel, maxLevel) = (maxLevel, minLevel);
		}

		var subset = new Dictionary<int, IReadOnlyList<MonsterType>>();
		for (var level = minLevel; level <= maxLevel; level++)
		{
			if (monsterTypesByLevel!.TryGetValue(level, out var types) && types.Count > 0)
			{
				subset[level] = types;
			}
			else
			{
				subset[level] = [];
			}
		}

		return subset;
	}

	public static bool TryPickRandomMonsterType(
		IReadOnlyDictionary<int, IReadOnlyList<MonsterType>> levelSubset,
		int level,
		out MonsterType monsterType)
	{
		monsterType = default;
		if (!levelSubset.TryGetValue(level, out var types) || types.Count == 0)
		{
			return false;
		}

		var totalWeight = 0;
		foreach (var type in types)
		{
			totalWeight += GetSpawnWeight(type);
		}

		var roll = Random.Shared.Next(totalWeight);
		foreach (var type in types)
		{
			var weight = GetSpawnWeight(type);
			if (roll < weight)
			{
				monsterType = type;
				return true;
			}

			roll -= weight;
		}

		monsterType = types[^1];
		return true;
	}

	private static int GetSpawnWeight(MonsterType type)
	{
		// Курганник (1330): 0.1× spawn rate vs other types at the same level.
		if (MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue(type, out var dbId) && dbId == 1330)
		{
			return 1;
		}

		return 10;
	}

	private static void EnsureBuilt()
	{
		if (monsterTypesByLevel is not null)
		{
			return;
		}

		lock (BuildLock)
		{
			if (monsterTypesByLevel is not null)
			{
				return;
			}

			monsterTypesByLevel = BuildMonsterTypesByLevel();
		}
	}

	private static Dictionary<int, List<MonsterType>> BuildMonsterTypesByLevel()
	{
		var byLevel = new Dictionary<int, List<MonsterType>>();

		foreach (var (dbId, entry) in SphObjectDb.GameObjectDataDb)
		{
			// 1340 - разбойники, потом эвентовые и духи. Они спавнятся статично.
			// 1024, 1034, 1044, 1066, 1069, 1076, 1106, 1135, 1143, 1164, 1213, 1233, 1253 - только именные или свита
			// 1150 - нифон, только замки и кладб
			// 1170 - кошка, только города
			// 1280, 1281 - замковые камни
			if (dbId is >= 1340 or 1024 or 1034 or 1044 or 1066 or 1069 or 1076 or 1106 or 1135
				or 1143 or 1164 or 1213 or 1233 or 1253 or 1280 or 1281 or 1150 or 1170)
			{
				continue;
			}

			if (entry.ObjectKind != GameObjectKind.Monster)
			{
				continue;
			}

			if (!MonsterTypeMapping.MonsterTypeToMonsterNameMapping.TryGetValue(dbId, out var monsterType))
			{
				continue;
			}

			var minLevel = entry.DegreeMinusOne + 1;
			var maxLevel = (int)entry.MinKarmaLevel + 1;
			if (minLevel > maxLevel)
			{
				(minLevel, maxLevel) = (maxLevel, minLevel);
			}

			if (minLevel <= 0 || maxLevel <= 0)
			{
				continue;
			}

			for (var level = minLevel; level <= maxLevel; level++)
			{
				if (!byLevel.TryGetValue(level, out var types))
				{
					types = [];
					byLevel[level] = types;
				}

				if (!types.Contains(monsterType))
				{
					types.Add(monsterType);
				}
			}
		}

		if (byLevel.Count == 0 && !buildLoggedEmptyLevels)
		{
			buildLoggedEmptyLevels = true;
			GD.PushWarning("MonsterSpawnerMonsterTypeLookup: no monster types indexed from SphObjectDb.");
		}

		return byLevel;
	}
}
