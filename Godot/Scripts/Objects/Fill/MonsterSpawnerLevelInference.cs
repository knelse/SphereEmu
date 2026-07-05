using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;

namespace SphServer.Godot.Scripts.Objects.Fill;

public enum SpawnerLevelInferenceMethod
{
	None,
	DirectMob,
	WideMob,
	InterpolatedSpawner
}

public readonly struct SpawnerLevelInferenceStats
{
	public SpawnerLevelInferenceStats(
		int direct,
		int wideMob,
		int interpolated,
		int none,
		int skippedWeird,
		int flaggedSpread)
	{
		Direct = direct;
		WideMob = wideMob;
		Interpolated = interpolated;
		None = none;
		SkippedWeird = skippedWeird;
		FlaggedSpread = flaggedSpread;
	}

	public int Direct { get; }
	public int WideMob { get; }
	public int Interpolated { get; }
	public int None { get; }
	public int SkippedWeird { get; }
	public int FlaggedSpread { get; }
}

public readonly struct SpawnerLevelAssignment
{
	public SpawnerLevelAssignment(
		int minLevel,
		int maxLevel,
		SpawnerLevelInferenceMethod method,
		int sampleCount,
		float referenceDistanceMeters,
		bool flaggedWideSpread)
	{
		MinLevel = minLevel;
		MaxLevel = maxLevel;
		Method = method;
		SampleCount = sampleCount;
		ReferenceDistanceMeters = referenceDistanceMeters;
		FlaggedWideSpread = flaggedWideSpread;
	}

	public int MinLevel { get; }
	public int MaxLevel { get; }
	public SpawnerLevelInferenceMethod Method { get; }
	public int SampleCount { get; }
	public float ReferenceDistanceMeters { get; }
	public bool FlaggedWideSpread { get; }
	public bool HasLevels => Method != SpawnerLevelInferenceMethod.None;
}

/// <summary>
///     Infers <see cref="MonsterSpawner.RegularMonsterMinLevel" /> /
///     <see cref="MonsterSpawner.RegularMonsterMaxLevel" /> from a live mob dump.
///     Pass 1: mob levels within <see cref="DirectMatchRadiusMeters" />.
///     Pass 2: optional wider mob search, then IDW from spawners that matched in pass 1.
/// </summary>
public static class MonsterSpawnerLevelInference
{
	private const int MaxMobLevel = 120;
	private const int MaxMobHp = 20000;
	private const float WideMobRadiusMeters = 150f;
	private const float WideMobMaxDeltaYMeters = 30f;
	private const float InterpolationMaxDistanceMeters = 500f;
	private const float SpatialCellMeters = 20f;
	private const float MapCellMeters = 500f;
	private const int WideSpreadFlagThreshold = 5;

	private static readonly HashSet<string> ValidMobEntities = new(StringComparer.Ordinal)
	{
		"Monster",
		"MonsterFlyer"
	};

	private readonly struct MobSample
	{
		public MobSample(float x, float y, float z, int level)
		{
			X = x;
			Y = y;
			Z = z;
			Level = level;
		}

		public float X { get; }
		public float Y { get; }
		public float Z { get; }
		public int Level { get; }
	}

	private readonly struct SpawnerTarget
	{
		public SpawnerTarget(MonsterSpawner spawner, float x, float y, float z)
		{
			Spawner = spawner;
			X = x;
			Y = y;
			Z = z;
		}

		public MonsterSpawner Spawner { get; }
		public float X { get; }
		public float Y { get; }
		public float Z { get; }
	}

	private readonly struct DirectSpawnerMatch
	{
		public DirectSpawnerMatch(float x, float y, float z, int minLevel, int maxLevel)
		{
			X = x;
			Y = y;
			Z = z;
			MinLevel = minLevel;
			MaxLevel = maxLevel;
		}

		public float X { get; }
		public float Y { get; }
		public float Z { get; }
		public int MinLevel { get; }
		public int MaxLevel { get; }
	}

	public static SpawnerLevelInferenceStats ApplyToSpawnersUnder(
		Node3D fillRoot,
		string mobDataFilePath,
		float directMatchRadiusMeters,
		string? reportFilePath)
	{
		if (!WorldObjectDumpFillCommon.TryReadTextFile(mobDataFilePath, "MonsterSpawnerLevelInference", out var mobText))
		{
			return default;
		}

		var mobs = ParseMobDump(mobText);
		if (mobs.Count == 0)
		{
			GD.PushWarning("MonsterSpawnerLevelInference: no valid mob rows parsed.");
			return default;
		}

		var mobBuckets = BuildMobBuckets(mobs);
		var mobMapCells = BuildMobMapCells(mobs);
		var targets = CollectSpawnerTargets(fillRoot, out var skippedWeird);

		var directReferences = new List<DirectSpawnerMatch>();
		var assignments = new Dictionary<MonsterSpawner, SpawnerLevelAssignment>();
		var reportRows = new List<string>();

		foreach (var target in targets)
		{
			var levels = CollectMobLevelsNear(
				mobBuckets,
				mobs,
				target.X,
				target.Y,
				target.Z,
				directMatchRadiusMeters,
				maxDeltaY: null);

			if (levels.Count > 0)
			{
				var minLevel = MinLevel(levels);
				var maxLevel = MaxLevel(levels);
				var assignment = BuildAssignment(minLevel, maxLevel, SpawnerLevelInferenceMethod.DirectMob, levels.Count, 0f);
				assignments[target.Spawner] = assignment;
				directReferences.Add(new DirectSpawnerMatch(target.X, target.Y, target.Z, minLevel, maxLevel));
				continue;
			}

			if (!IsMobPopulatedMapCell(mobMapCells, target.X, target.Z))
			{
				assignments[target.Spawner] = default;
				continue;
			}

			var wideLevels = CollectMobLevelsNear(
				mobBuckets,
				mobs,
				target.X,
				target.Y,
				target.Z,
				WideMobRadiusMeters,
				WideMobMaxDeltaYMeters);

			if (wideLevels.Count > 0)
			{
				var minLevel = MinLevel(wideLevels);
				var maxLevel = MaxLevel(wideLevels);
				assignments[target.Spawner] = BuildAssignment(
					minLevel,
					maxLevel,
					SpawnerLevelInferenceMethod.WideMob,
					wideLevels.Count,
					0f);
				continue;
			}

			if (TryInterpolateFromDirectSpawners(
					directReferences,
					target.X,
					target.Y,
					target.Z,
					out var interpolatedMin,
					out var interpolatedMax,
					out var nearestDistance,
					out var referenceCount))
			{
				assignments[target.Spawner] = BuildAssignment(
					interpolatedMin,
					interpolatedMax,
					SpawnerLevelInferenceMethod.InterpolatedSpawner,
					referenceCount,
					nearestDistance);
			}
			else
			{
				assignments[target.Spawner] = default;
			}
		}

		var direct = 0;
		var wideMob = 0;
		var interpolated = 0;
		var none = 0;
		var flaggedSpread = 0;

		foreach (var (spawner, assignment) in assignments)
		{
			if (assignment.HasLevels)
			{
				ApplyAssignment(spawner, assignment);
				switch (assignment.Method)
				{
					case SpawnerLevelInferenceMethod.DirectMob:
						direct++;
						break;
					case SpawnerLevelInferenceMethod.WideMob:
						wideMob++;
						break;
					case SpawnerLevelInferenceMethod.InterpolatedSpawner:
						interpolated++;
						break;
				}

				if (assignment.FlaggedWideSpread)
				{
					flaggedSpread++;
				}
			}
			else
			{
				none++;
			}

			reportRows.Add(BuildReportLine(spawner, assignment));
		}

		var stats = new SpawnerLevelInferenceStats(
			direct,
			wideMob,
			interpolated,
			none,
			skippedWeird,
			flaggedSpread);

		if (!string.IsNullOrWhiteSpace(reportFilePath))
		{
			WriteReport(reportFilePath, reportRows);
		}

		return stats;
	}

	private static List<MobSample> ParseMobDump(string text)
	{
		var mobs = new List<MobSample>();
		foreach (var (_, parts) in WorldObjectDumpFillCommon.EnumerateDataLines(text))
		{
			if (parts.Length < 11)
			{
				continue;
			}

			if (!ValidMobEntities.Contains(parts[1]))
			{
				continue;
			}

			if (!string.Equals(parts[2], "FULL_SPAWN", StringComparison.Ordinal))
			{
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseDouble(parts[3], out var x)
				|| !WorldObjectDumpFillCommon.TryParseDouble(parts[4], out var y)
				|| !WorldObjectDumpFillCommon.TryParseDouble(parts[5], out var z))
			{
				continue;
			}

			if (WorldObjectDumpFillCommon.ShouldSkipWeirdCoords(parts[3], parts[4], parts[5], x, y, z))
			{
				continue;
			}

			if (!int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxHp)
				|| !int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind)
				|| !int.TryParse(parts[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
			{
				continue;
			}

			_ = kind;
			if (level <= 0 || level > MaxMobLevel || maxHp <= 0 || maxHp > MaxMobHp)
			{
				continue;
			}

			mobs.Add(new MobSample((float)x, (float)y, (float)z, level));
		}

		return mobs;
	}

	private static Dictionary<(int Cx, int Cy, int Cz), List<int>> BuildMobBuckets(IReadOnlyList<MobSample> mobs)
	{
		var buckets = new Dictionary<(int Cx, int Cy, int Cz), List<int>>();
		for (var i = 0; i < mobs.Count; i++)
		{
			var mob = mobs[i];
			var key = SpatialCellKey(mob.X, mob.Y, mob.Z);
			if (!buckets.TryGetValue(key, out var bucket))
			{
				bucket = [];
				buckets[key] = bucket;
			}

			bucket.Add(i);
		}

		return buckets;
	}

	private static HashSet<(int Cx, int Cz)> BuildMobMapCells(IReadOnlyList<MobSample> mobs)
	{
		var cells = new HashSet<(int Cx, int Cz)>();
		foreach (var mob in mobs)
		{
			cells.Add(MapCellKey(mob.X, mob.Z));
		}

		return cells;
	}

	private static List<SpawnerTarget> CollectSpawnerTargets(Node3D fillRoot, out int skippedWeird)
	{
		var targets = new List<SpawnerTarget>();
		skippedWeird = 0;

		foreach (var child in fillRoot.GetChildren())
		{
			if (child is not MonsterSpawner spawner || !GodotObject.IsInstanceValid(spawner))
			{
				continue;
			}

			var source = WorldObjectDumpFillCommon.SourcePositionFromPlacementNode(spawner, fillRoot);
			if (WorldObjectDumpFillCommon.ShouldSkipWeirdCoords(
					source.X.ToString(CultureInfo.InvariantCulture),
					source.Y.ToString(CultureInfo.InvariantCulture),
					source.Z.ToString(CultureInfo.InvariantCulture),
					source.X,
					source.Y,
					source.Z))
			{
				skippedWeird++;
				continue;
			}

			targets.Add(new SpawnerTarget(spawner, source.X, source.Y, source.Z));
		}

		return targets;
	}

	private static List<int> CollectMobLevelsNear(
		IReadOnlyDictionary<(int Cx, int Cy, int Cz), List<int>> mobBuckets,
		IReadOnlyList<MobSample> mobs,
		float sx,
		float sy,
		float sz,
		float radiusMeters,
		float? maxDeltaY)
	{
		var levels = new List<int>();
		var radiusSquared = radiusMeters * radiusMeters;
		var cellRadius = Math.Max(2, (int)Math.Floor(radiusMeters / SpatialCellMeters) + 1);
		var center = SpatialCellKey(sx, sy, sz);

		for (var dx = -cellRadius; dx <= cellRadius; dx++)
		{
			for (var dy = -cellRadius; dy <= cellRadius; dy++)
			{
				for (var dz = -cellRadius; dz <= cellRadius; dz++)
				{
					var key = (center.Cx + dx, center.Cy + dy, center.Cz + dz);
					if (!mobBuckets.TryGetValue(key, out var bucket))
					{
						continue;
					}

					foreach (var index in bucket)
					{
						var mob = mobs[index];
						if (maxDeltaY is float deltaY && Math.Abs(mob.Y - sy) > deltaY)
						{
							continue;
						}

						var distSquared = DistanceSquared(sx, sy, sz, mob.X, mob.Y, mob.Z);
						if (distSquared <= radiusSquared)
						{
							levels.Add(mob.Level);
						}
					}
				}
			}
		}

		return levels;
	}

	private static bool IsMobPopulatedMapCell(IReadOnlySet<(int Cx, int Cz)> mobMapCells, float x, float z) =>
		mobMapCells.Contains(MapCellKey(x, z));

	private static bool TryInterpolateFromDirectSpawners(
		IReadOnlyList<DirectSpawnerMatch> directReferences,
		float sx,
		float sy,
		float sz,
		out int minLevel,
		out int maxLevel,
		out float nearestDistanceMeters,
		out int referenceCount)
	{
		minLevel = 0;
		maxLevel = 0;
		nearestDistanceMeters = float.PositiveInfinity;
		referenceCount = 0;

		double weightedMin = 0;
		double weightedMax = 0;
		double weightSum = 0;

		foreach (var reference in directReferences)
		{
			var distance = MathF.Sqrt(DistanceSquared(sx, sy, sz, reference.X, reference.Y, reference.Z));
			if (distance < 0.01f)
			{
				minLevel = reference.MinLevel;
				maxLevel = reference.MaxLevel;
				nearestDistanceMeters = 0f;
				referenceCount = 1;
				return true;
			}

			if (distance > InterpolationMaxDistanceMeters)
			{
				continue;
			}

			referenceCount++;
			if (distance < nearestDistanceMeters)
			{
				nearestDistanceMeters = distance;
			}

			var weight = 1.0 / (distance * distance);
			weightedMin += weight * reference.MinLevel;
			weightedMax += weight * reference.MaxLevel;
			weightSum += weight;
		}

		if (weightSum <= 0 || referenceCount == 0)
		{
			return false;
		}

		minLevel = (int)Math.Round(weightedMin / weightSum);
		maxLevel = (int)Math.Round(weightedMax / weightSum);
		return true;
	}

	private static SpawnerLevelAssignment BuildAssignment(
		int minLevel,
		int maxLevel,
		SpawnerLevelInferenceMethod method,
		int sampleCount,
		float referenceDistanceMeters)
	{
		minLevel = Math.Clamp(minLevel, 1, MaxMobLevel);
		maxLevel = Math.Clamp(maxLevel, 1, MaxMobLevel);
		if (minLevel > maxLevel)
		{
			(minLevel, maxLevel) = (maxLevel, minLevel);
		}

		var flaggedWideSpread = maxLevel - minLevel > WideSpreadFlagThreshold;
		return new SpawnerLevelAssignment(
			minLevel,
			maxLevel,
			method,
			sampleCount,
			referenceDistanceMeters,
			flaggedWideSpread);
	}

	private static void ApplyAssignment(MonsterSpawner spawner, SpawnerLevelAssignment assignment)
	{
		spawner.RegularMonsterMinLevel = assignment.MinLevel;
		spawner.RegularMonsterMaxLevel = assignment.MaxLevel;
		spawner.NamedMonsterMinLevel = assignment.MinLevel;
		spawner.NamedMonsterMaxLevel = assignment.MaxLevel;
	}

	private static int MinLevel(IReadOnlyList<int> levels)
	{
		var min = levels[0];
		for (var i = 1; i < levels.Count; i++)
		{
			min = Math.Min(min, levels[i]);
		}

		return min;
	}

	private static int MaxLevel(IReadOnlyList<int> levels)
	{
		var max = levels[0];
		for (var i = 1; i < levels.Count; i++)
		{
			max = Math.Max(max, levels[i]);
		}

		return max;
	}

	private static (int Cx, int Cy, int Cz) SpatialCellKey(float x, float y, float z) =>
		((int)Math.Floor(x / SpatialCellMeters),
			(int)Math.Floor(y / SpatialCellMeters),
			(int)Math.Floor(z / SpatialCellMeters));

	private static (int Cx, int Cz) MapCellKey(float x, float z) =>
		((int)Math.Floor(x / MapCellMeters), (int)Math.Floor(z / MapCellMeters));

	private static float DistanceSquared(float ax, float ay, float az, float bx, float by, float bz)
	{
		var dx = ax - bx;
		var dy = ay - by;
		var dz = az - bz;
		return dx * dx + dy * dy + dz * dz;
	}

	private static string BuildReportLine(MonsterSpawner spawner, SpawnerLevelAssignment assignment)
	{
		var source = spawner.GlobalTransform.Origin;
		return string.Join(
			",",
			EscapeCsv(spawner.Name),
			assignment.Method.ToString(),
			assignment.HasLevels ? assignment.MinLevel.ToString(CultureInfo.InvariantCulture) : string.Empty,
			assignment.HasLevels ? assignment.MaxLevel.ToString(CultureInfo.InvariantCulture) : string.Empty,
			assignment.SampleCount.ToString(CultureInfo.InvariantCulture),
			assignment.ReferenceDistanceMeters.ToString("0.###", CultureInfo.InvariantCulture),
			assignment.FlaggedWideSpread ? "1" : "0",
			source.X.ToString("0.###", CultureInfo.InvariantCulture),
			source.Y.ToString("0.###", CultureInfo.InvariantCulture),
			source.Z.ToString("0.###", CultureInfo.InvariantCulture));
	}

	private static void WriteReport(string reportFilePath, IReadOnlyList<string> rows)
	{
		try
		{
			var directory = Path.GetDirectoryName(reportFilePath);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			var sb = new StringBuilder();
			sb.AppendLine("spawner_name,method,min_level,max_level,sample_count,ref_distance_m,flagged_wide_spread,godot_x,godot_y,godot_z");
			foreach (var row in rows)
			{
				sb.AppendLine(row);
			}

			File.WriteAllText(reportFilePath, sb.ToString());
			GD.Print($"MonsterSpawnerLevelInference: wrote report to {reportFilePath}");
		}
		catch (Exception ex)
		{
			GD.PushWarning($"MonsterSpawnerLevelInference: failed to write report ({ex.Message})");
		}
	}

	private static string EscapeCsv(string value)
	{
		if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
		{
			return $"\"{value.Replace("\"", "\"\"")}\"";
		}

		return value;
	}
}
