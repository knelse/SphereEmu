using System;
using System.Collections.Generic;

namespace SphServer.Sphere.Game.WorldObject;

public readonly record struct MonsterHitRecord(ushort ClientId, DamageSchool School, DateTime Utc);

/// <summary>
///     Last-minute hit log for a kill: majority school by count (not damage), plus every
///     timestamped record so XP can be split by client later.
/// </summary>
public readonly record struct MonsterKillCredit(
	DamageSchool MajoritySchool,
	int PhysicalHits,
	int MagicalHits,
	int ClientCount,
	IReadOnlyList<MonsterHitRecord> Hits);

/// <summary>
///     Per-mob combat log keyed by client id. Only the last minute is kept or consulted.
/// </summary>
public sealed class MonsterHitHistory
{
	public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

	private readonly List<MonsterHitRecord> hits = [];

	public void Record(ushort clientId, DamageSchool school, DateTime utcNow)
	{
		hits.Add(new MonsterHitRecord(clientId, school, utcNow));
		Prune(utcNow);
	}

	public MonsterKillCredit GetKillCredit(DateTime utcNow)
	{
		Prune(utcNow);

		var physical = 0;
		var magical = 0;
		foreach (var hit in hits)
		{
			if (hit.School == DamageSchool.Physical)
			{
				physical++;
			}
			else
			{
				magical++;
			}
		}

		var school = magical > physical ? DamageSchool.Magical
			: physical > magical ? DamageSchool.Physical
			: hits.Count > 0 ? hits[^1].School
			: DamageSchool.Physical;

		var clients = new HashSet<ushort>();
		foreach (var hit in hits)
		{
			clients.Add(hit.ClientId);
		}

		return new MonsterKillCredit(school, physical, magical, clients.Count, hits.ToArray());
	}

	private void Prune(DateTime utcNow)
	{
		var cutoff = utcNow - Window;
		var stale = 0;
		while (stale < hits.Count && hits[stale].Utc < cutoff)
		{
			stale++;
		}

		if (stale > 0)
		{
			hits.RemoveRange(0, stale);
		}
	}
}

public partial class Monster
{
	private readonly MonsterHitHistory hitHistory = new();

	public MonsterKillCredit GetKillCredit() => hitHistory.GetKillCredit(DateTime.UtcNow);
}
