using System;

using Godot;



namespace SphServer.Godot.Scripts.Terrain;



/// <summary>

///     Outdoor spawn/placement height queries backed by the prebuilt walk atlas.

/// </summary>

public static class WalkSurfaceQuery

{

    public static bool TrySampleGround(Vector3 worldProbeOrigin, out Vector3 groundPoint)

    {

        groundPoint = default;

        if (!WalkSurfaceCache.TrySampleGround(worldProbeOrigin.X, worldProbeOrigin.Z, out var worldY))

        {

            return false;

        }



        groundPoint = new Vector3(worldProbeOrigin.X, worldY, worldProbeOrigin.Z);

        return true;

    }



    public static bool TryFindValidSpawnSurface(

        Vector3 worldProbeOrigin,

        float minSeparationMeters,

        IReadOnlyList<Vector3> occupiedWorldPositions,

        out Vector3 spawnWorldPosition)

    {

        if (WalkSurfaceOutdoorSpawnQuery.TryFindValidOutdoorSpawnSurface(

                worldProbeOrigin,

                minSeparationMeters,

                occupiedWorldPositions,

                out spawnWorldPosition))

        {

            return true;

        }



        spawnWorldPosition = default;



        if (!WalkSurfaceCache.TrySampleGround(worldProbeOrigin.X, worldProbeOrigin.Z, out var worldY))

        {

            return false;

        }



        var groundPoint = new Vector3(worldProbeOrigin.X, worldY, worldProbeOrigin.Z);



        if (!WalkSurfaceCache.IsSpawnFootprintAcceptable(groundPoint.X, groundPoint.Z))

        {

            return false;

        }



        if (HasOverlap(groundPoint, minSeparationMeters, occupiedWorldPositions))

        {

            return false;

        }



        spawnWorldPosition = groundPoint;

        return true;

    }



    private static bool HasOverlap(

        Vector3 candidate,

        float minSeparationMeters,

        IReadOnlyList<Vector3> occupiedWorldPositions)

    {

        var minSeparationSq = minSeparationMeters * minSeparationMeters;

        foreach (var occupied in occupiedWorldPositions)

        {

            var dx = candidate.X - occupied.X;

            var dz = candidate.Z - occupied.Z;

            if (dx * dx + dz * dz < minSeparationSq)

            {

                return true;

            }

        }



        return false;

    }

}


