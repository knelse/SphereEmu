using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

public interface IMonsterSpawnGroundQuery
{
    bool TryFindValidSpawnSurface(
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition);
}

/// <summary>
///     Walk atlas walkable field — safe to run from background threads during bulk bakes.
/// </summary>
public sealed class AtlasMonsterSpawnGroundQuery : IMonsterSpawnGroundQuery
{
    public bool TryFindValidSpawnSurface(
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition)
    {
        if (WalkSurfaceWalkableQuery.TryFindValidWalkSurface(
                worldProbeOrigin,
                minSeparationMeters,
                occupiedWorldPositions,
                out spawnWorldPosition))
        {
            return true;
        }

        return WalkSurfaceQuery.TryFindValidSpawnSurface(
            worldProbeOrigin,
            minSeparationMeters,
            occupiedWorldPositions,
            out spawnWorldPosition);
    }
}

/// <summary>
///     Atlas with GridMap / physics fallback — main thread only.
/// </summary>
public sealed class NodeMonsterSpawnGroundQuery(Node3D contextNode) : IMonsterSpawnGroundQuery
{
    public bool TryFindValidSpawnSurface(
        Vector3 worldProbeOrigin,
        float minSeparationMeters,
        IReadOnlyList<Vector3> occupiedWorldPositions,
        out Vector3 spawnWorldPosition)
    {
        return MonsterSpawnGroundQuery.TryFindValidSpawnSurface(
            contextNode,
            worldProbeOrigin,
            minSeparationMeters,
            occupiedWorldPositions,
            out spawnWorldPosition);
    }
}
