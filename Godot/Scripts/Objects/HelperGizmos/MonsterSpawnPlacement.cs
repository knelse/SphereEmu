using System;
using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Finds randomized spawn positions for a single spawner. Placement is sequential per spawner instance;
///     overlap checks only consider positions produced by that spawner during the current batch.
///     Failed bearings are tracked as a 24-sector bitmask (24 × 15° = 360°).
/// </summary>
public sealed class MonsterSpawnPlacement
{
    public const float DefaultSpawnRadiusMeters = 10f;
    public const float SearchRadiusAfterFailureMeters = 2f;
    public const float SectorWidthDegrees = 15f;
    public const int SectorCount = 24;
    public const float MinMobSeparationMeters = 0.7f;
    private const int MaxRandomAttempts = 48;
    private const int SearchSamplesPerCandidate = 20;
    private const uint AllSectorsMask = (1u << SectorCount) - 1u;

    private readonly List<Vector3> _occupiedWorldPositions = [];
    private readonly Random _random;
    private uint _blockedSectorMask;

    public MonsterSpawnPlacement(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public void Reset(IEnumerable<Vector3>? existingOccupiedWorldPositions = null)
    {
        _blockedSectorMask = 0;
        ResetOccupied(existingOccupiedWorldPositions);
    }

    public void ResetOccupied(IEnumerable<Vector3>? existingOccupiedWorldPositions = null)
    {
        _occupiedWorldPositions.Clear();
        if (existingOccupiedWorldPositions is null)
        {
            return;
        }

        foreach (var position in existingOccupiedWorldPositions)
        {
            _occupiedWorldPositions.Add(position);
        }
    }

    public bool TryFindSpawnPosition(
        MonsterSpawner spawner,
        float spawnRadiusMeters,
        out Vector3 spawnWorldPosition)
    {
        return TryFindSpawnPosition(
            spawner.GlobalPosition,
            spawnRadiusMeters,
            new NodeMonsterSpawnGroundQuery(spawner),
            out spawnWorldPosition);
    }

    public bool TryFindSpawnPosition(
        Vector3 spawnerOrigin,
        float spawnRadiusMeters,
        IMonsterSpawnGroundQuery groundQuery,
        out Vector3 spawnWorldPosition)
    {
        spawnWorldPosition = default;

        if (IsFullyBlacklisted())
        {
            return false;
        }

        for (var attempt = 0; attempt < MaxRandomAttempts; attempt++)
        {
            if (!TryPickRandomAngleDegrees(out var angleDegrees))
            {
                return false;
            }

            var radius = (float)RandRange(0.1, spawnRadiusMeters);
            var candidate = BuildWorldProbeOrigin(spawnerOrigin, angleDegrees, radius);

            if (TryResolveCandidate(groundQuery, candidate, out spawnWorldPosition))
            {
                _occupiedWorldPositions.Add(spawnWorldPosition);
                return true;
            }

            if (TrySearchNearCandidate(groundQuery, candidate, out spawnWorldPosition))
            {
                _occupiedWorldPositions.Add(spawnWorldPosition);
                return true;
            }

            BlacklistAngleDegrees(angleDegrees);
            if (IsFullyBlacklisted())
            {
                return false;
            }
        }

        return false;
    }

    private bool TrySearchNearCandidate(
        IMonsterSpawnGroundQuery groundQuery,
        Vector3 candidate,
        out Vector3 spawnWorldPosition)
    {
        spawnWorldPosition = default;

        for (var sample = 0; sample < SearchSamplesPerCandidate; sample++)
        {
            var angle = RandRange(0d, Math.Tau);
            var radius = RandRange(0d, SearchRadiusAfterFailureMeters);
            var offset = new Vector3(Mathf.Cos((float)angle) * (float)radius, 0f, Mathf.Sin((float)angle) * (float)radius);
            var probe = candidate + offset;

            if (TryResolveCandidate(groundQuery, probe, out spawnWorldPosition))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolveCandidate(
        IMonsterSpawnGroundQuery groundQuery,
        Vector3 probeOrigin,
        out Vector3 spawnWorldPosition)
    {
        return groundQuery.TryFindValidSpawnSurface(
            probeOrigin,
            MinMobSeparationMeters,
            _occupiedWorldPositions,
            out spawnWorldPosition);
    }

    private void BlacklistAngleDegrees(float angleDegrees)
    {
        var sector = AngleToSectorIndex(angleDegrees);
        _blockedSectorMask |= 1u << sector;
    }

    private bool TryPickRandomAngleDegrees(out float angleDegrees)
    {
        var availableMask = AllSectorsMask & ~_blockedSectorMask;
        if (availableMask == 0)
        {
            angleDegrees = 0f;
            return false;
        }

        var availableCount = PopCount(availableMask);
        var pick = _random.Next(availableCount);
        var sector = NthSetBit(availableMask, pick);
        var sectorStart = sector * SectorWidthDegrees;
        angleDegrees = (float)RandRange(sectorStart, sectorStart + SectorWidthDegrees);
        return true;
    }

    private double RandRange(double min, double max)
    {
        return min + _random.NextDouble() * (max - min);
    }

    private bool IsFullyBlacklisted()
    {
        return (_blockedSectorMask & AllSectorsMask) == AllSectorsMask;
    }

    private static int AngleToSectorIndex(float angleDegrees)
    {
        var normalized = NormalizeDegrees(angleDegrees);
        var sector = (int)(normalized / SectorWidthDegrees);
        if (sector >= SectorCount)
        {
            sector = SectorCount - 1;
        }

        return sector;
    }

    private static int NthSetBit(uint mask, int n)
    {
        var seen = 0;
        for (var bit = 0; bit < SectorCount; bit++)
        {
            if ((mask & (1u << bit)) == 0)
            {
                continue;
            }

            if (seen == n)
            {
                return bit;
            }

            seen++;
        }

        return 0;
    }

    private static int PopCount(uint value)
    {
        var count = 0;
        while (value != 0)
        {
            count += (int)(value & 1u);
            value >>= 1;
        }

        return count;
    }

    private static Vector3 BuildWorldProbeOrigin(Vector3 spawnerOrigin, float angleDegrees, float radiusMeters)
    {
        var radians = Mathf.DegToRad(angleDegrees);
        var offset = new Vector3(Mathf.Sin(radians) * radiusMeters, 0f, Mathf.Cos(radians) * radiusMeters);
        return spawnerOrigin + offset;
    }

    private static float NormalizeDegrees(float angleDegrees)
    {
        var normalized = angleDegrees % 360f;
        if (normalized < 0f)
        {
            normalized += 360f;
        }

        return normalized;
    }
}
