using System;
using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Headless diagnostics for spawn placement at a world position.
/// </summary>
public static class WalkSurfaceSpawnProbe
{
    public static int Probe(float worldX, float worldY, float worldZ, float spawnRadiusMeters, int targetCount)
    {
        WalkSurfaceCache.Invalidate();
        var chunkX = (int)Mathf.Floor(worldX / WalkSurfaceCache.ChunkSizeMeters);
        var chunkZ = (int)Mathf.Floor(worldZ / WalkSurfaceCache.ChunkSizeMeters);
        GD.Print($"WalkSurfaceSpawnProbe: world=({worldX:0.##}, {worldY:0.##}, {worldZ:0.##}) chunk=({chunkX}, {chunkZ})");
        GD.Print($"  HasAnyChunkFiles={WalkSurfaceCache.HasAnyChunkFiles()}");
        GD.Print($"  HasWalkableField (before preload)={WalkSurfaceCache.HasWalkableField}");
        GD.Print($"  HasChunkCoverage={WalkSurfaceCache.HasChunkCoverageAt(worldX, worldZ)}");

        WalkSurfaceCache.PreloadChunksForRadius(worldX, worldZ, spawnRadiusMeters + 1f);
        var chunkPath = WalkSurfaceChunk.BuildAbsolutePath(chunkX, chunkZ, WalkSurfaceCache.DirectoryResourcePath);
        if (WalkSurfaceChunk.TryPeekFormatVersion(chunkPath, out var formatVersion))
        {
            GD.Print($"  Chunk file format v{formatVersion}");
        }

        if (WalkSurfaceChunk.TryLoad(chunkPath, out var chunk) && chunk is not null)
        {
            chunk.CountFieldStats(out var walkableCells, out var blockedCells, out var terrainCells);
            GD.Print(
                $"  Chunk ({chunkX},{chunkZ}) stats: walkable={walkableCells} blocked={blockedCells}/{chunk.Width * chunk.Height} terrain={terrainCells}");
        }

        GD.Print($"  HasWalkableField (after preload)={WalkSurfaceCache.HasWalkableField}");
        GD.Print($"  Origin walkable={WalkSurfaceCache.IsWalkableAt(worldX, worldZ)}");
        WalkSurfaceCache.TrySampleWalkableGround(worldX, worldZ, out var originY);
        GD.Print($"  Origin terrainY={originY:0.##}");
        GD.Print($"  Origin blocked={WalkSurfaceCache.IsBlocked(worldX, worldZ)}");
        GD.Print($"  Origin footprint OK={WalkSurfaceCache.IsSpawnFootprintAcceptable(worldX, worldZ)}");
        GD.Print($"  Origin openness={WalkSurfaceCache.MeasureLocalOpenness(worldX, worldZ, OutdoorFieldConfig.OpennessRadiusMeters):0.###}");

        ProbeLiveTerrain(worldX, worldZ);

        var candidates = new List<(float X, float Z, float Y)>();
        WalkSurfaceCache.CollectWalkableCandidates(worldX, worldZ, spawnRadiusMeters, candidates);
        GD.Print($"  Walkable candidates in {spawnRadiusMeters:0.##}m radius={candidates.Count}");

        if (WalkSurfaceWalkableQuery.TryPickSpawnSlots(
                new Vector3(worldX, worldY, worldZ),
                spawnRadiusMeters,
                targetCount * 4,
                OutdoorFieldConfig.MinSlotSeparationMeters,
                null,
                out var slots))
        {
            GD.Print($"  PickSpawnSlots returned {slots.Count} slot(s)");
        }
        else
        {
            GD.Print("  PickSpawnSlots returned 0 slot(s)");
        }

        return candidates.Count > 0 ? 0 : 1;
    }

    private static void ProbeLiveTerrain(float worldX, float worldZ)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var fill = tree?.Root?.GetNodeOrNull<TerrainGridFill>("WalkSurfaceHeadlessBake/TerrainGridFill")
                ?? tree?.Root?.FindChild("TerrainGridFill", recursive: true, owned: false) as TerrainGridFill;
            if (fill is null)
            {
                GD.Print("  Live terrain: TerrainGridFill not found");
                return;
            }

            if (!fill.RebuildTerrainGrid())
            {
                GD.Print("  Live terrain: RebuildTerrainGrid failed");
                return;
            }

            var terrain = fill.GetNodeOrNull<GridMap>("Terrain");
            if (terrain is null)
            {
                GD.Print("  Live terrain: GridMap missing");
                return;
            }

            GD.Print($"  Live terrain GridMap position={terrain.GlobalPosition} cellSize={terrain.CellSize.X:0.##}");
            var fromWorld = new Vector3(worldX, 0f, worldZ);
            var cell = TerrainWalkMeshRaycast.ResolveHorizontalTerrainCell(terrain, fromWorld, fromWorld);
            GD.Print($"  Live terrain cell at probe={cell} item={terrain.GetCellItem(cell)}");
            if (TerrainWalkMeshRaycast.TrySampleTerrainTopY(terrain, worldX, worldZ, out var terrainY))
            {
                GD.Print($"  Live terrain top Y={terrainY:0.##}");
            }
            else
            {
                GD.Print("  Live terrain: no mesh hit at probe");
            }
        }
        catch (Exception ex)
        {
            GD.Print($"  Live terrain probe failed: {ex.Message}");
        }
    }
}
