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
	private const int MaxSpawnableMonsterDbIdExclusive = 1340;
	private static readonly object BuildLock = new();
	private static Dictionary<int, List<MonsterType>>? _monsterTypesByLevel;
	private static bool _buildLoggedEmptyLevels;

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
			if (_monsterTypesByLevel!.TryGetValue(level, out var types) && types.Count > 0)
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

		monsterType = types[Random.Shared.Next(types.Count)];
		return true;
	}

	private static void EnsureBuilt()
	{
		if (_monsterTypesByLevel is not null)
		{
			return;
		}

		lock (BuildLock)
		{
			if (_monsterTypesByLevel is not null)
			{
				return;
			}

			_monsterTypesByLevel = BuildMonsterTypesByLevel();
		}
	}

	private static Dictionary<int, List<MonsterType>> BuildMonsterTypesByLevel()
	{
		var byLevel = new Dictionary<int, List<MonsterType>>();

		foreach (var (dbId, entry) in SphObjectDb.GameObjectDataDb)
		{
			if (dbId >= MaxSpawnableMonsterDbIdExclusive)
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

		if (byLevel.Count == 0 && !_buildLoggedEmptyLevels)
		{
			_buildLoggedEmptyLevels = true;
			GD.PushWarning("MonsterSpawnerMonsterTypeLookup: no monster types indexed from SphObjectDb.");
		}

		return byLevel;
	}
}
