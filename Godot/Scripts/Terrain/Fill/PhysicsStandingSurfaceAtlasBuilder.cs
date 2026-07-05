using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Godot;
using SphServer.Godot.Scripts.Terrain.OutdoorNav;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
///     Bakes outdoor walk-surface chunks from a physics world (terrain + object colliders).
///     Rooftops and steep hits are stamped blocked; open ground uses the physics standing Y.
/// </summary>
public static class PhysicsStandingSurfaceAtlasBuilder
{
    public static int BuildFromPhysicsWorld(
        Node3D contextNode,
        GridMap terrain,
        string outputDirectoryResourcePath = WalkSurfaceAtlasBuilder.DefaultOutputDirectory,
        bool clearExistingChunks = true)
    {
        var world = contextNode.GetWorld3D();
        if (world is null)
        {
            GD.PushError("PhysicsStandingSurfaceAtlasBuilder: no World3D.");
            return 0;
        }

        if (terrain.MeshLibrary is null)
        {
            GD.PushError("PhysicsStandingSurfaceAtlasBuilder: terrain has no MeshLibrary.");
            return 0;
        }

        var usedCells = terrain.GetUsedCells();
        if (usedCells.Count == 0)
        {
            GD.PushWarning("PhysicsStandingSurfaceAtlasBuilder: terrain has no cells.");
            return 0;
        }

        if (!TerrainPhysicsColliderBuilder.HasColliderRoot(contextNode))
        {
            GD.PushWarning(
                "PhysicsStandingSurfaceAtlasBuilder: no PhysicsBakeColliders node — "
                + "run TerrainPhysicsColliderFill.RebuildPhysicsColliders first.");
        }

        if (clearExistingChunks)
        {
            ClearChunkFiles(outputDirectoryResourcePath);
        }

        var stopwatch = Stopwatch.StartNew();
        var spacing = WalkSurfaceAtlasBuilder.SampleSpacingMeters;
        var halfTile = terrain.CellSize.X * 0.5f;
        var terrainGlobal = terrain.GlobalTransform;

        TerrainMeshRaycastCache.Clear();
        TerrainTileHeightTemplateCache.Clear();
        var templates = TerrainTileHeightTemplateCache.WarmAll(terrain, usedCells, halfTile, spacing);
        GD.Print(
            $"PhysicsStandingSurfaceAtlasBuilder: precomputed {templates.Count} tile template(s) in "
            + $"{stopwatch.ElapsedMilliseconds} ms.");

        var builders = new Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder>();
        var terrainSamplesWritten = 0;
        var cellsCompleted = 0;

        foreach (var cell in usedCells)
        {
            var itemId = terrain.GetCellItem(cell);
            if (itemId < 0 || !templates.TryGetValue(itemId, out var template) || template is null)
            {
                cellsCompleted++;
                continue;
            }

            terrainSamplesWritten += template.WriteCellSamples(
                terrainGlobal,
                terrain.GetCellItemBasis(cell),
                terrain.MapToLocal(cell),
                (worldX, terrainMeshY, worldZ) =>
                {
                    var builder = WalkSurfaceAtlasBuilder.GetOrCreateBuilder(builders, worldX, worldZ);
                    builder.SetWorldSample(worldX, worldZ, terrainMeshY);
                });
            cellsCompleted++;

            if (cellsCompleted == 1 || cellsCompleted % 100 == 0 || cellsCompleted == usedCells.Count)
            {
                GD.Print(
                    $"PhysicsStandingSurfaceAtlasBuilder: terrain baseline {cellsCompleted}/{usedCells.Count} cells "
                    + $"({terrainSamplesWritten} samples, {stopwatch.ElapsedMilliseconds} ms)...");
            }
        }

        foreach (var builder in builders.Values)
        {
            builder.SnapshotTerrainHeights();
        }

        GD.Print(
            $"PhysicsStandingSurfaceAtlasBuilder: sampling physics for {usedCells.Count} terrain cell(s) "
            + $"into {builders.Count} chunk(s)...");

        cellsCompleted = 0;
        var physicsSamples = 0;
        var walkableCells = 0;
        var blockedCells = 0;
        var missedPhysicsCells = 0;

        foreach (var cell in usedCells)
        {
            var itemId = terrain.GetCellItem(cell);
            if (itemId < 0 || !templates.TryGetValue(itemId, out var template) || template is null)
            {
                cellsCompleted++;
                continue;
            }

            template.WriteCellSamples(
                terrainGlobal,
                terrain.GetCellItemBasis(cell),
                terrain.MapToLocal(cell),
                (worldX, terrainMeshY, worldZ) =>
                {
                    physicsSamples++;
                    var sample = PhysicsStandingSurfaceSampler.SampleCellWithTerrainMeshY(
                        world,
                        worldX,
                        worldZ,
                        terrainMeshY);
                    var builder = WalkSurfaceAtlasBuilder.GetOrCreateBuilder(builders, worldX, worldZ);
                    if (sample.Blocked)
                    {
                        builder.TryStampBlockedWorld(worldX, worldZ);
                        blockedCells++;
                        return;
                    }

                    if (!sample.IsWalkable)
                    {
                        missedPhysicsCells++;
                        return;
                    }

                    if (Mathf.IsEqualApprox(sample.StandingY, terrainMeshY, 0.001f))
                    {
                        missedPhysicsCells++;
                    }

                    builder.SetWorldSample(worldX, worldZ, sample.StandingY);
                    walkableCells++;
                });
            cellsCompleted++;

            if (cellsCompleted == 1 || cellsCompleted % 25 == 0 || cellsCompleted == usedCells.Count)
            {
                GD.Print(
                    $"PhysicsStandingSurfaceAtlasBuilder: physics {cellsCompleted}/{usedCells.Count} cells "
                    + $"(samples={physicsSamples}, walkable={walkableCells}, blocked={blockedCells}, "
                    + $"meshFallback~={missedPhysicsCells}, {stopwatch.ElapsedMilliseconds} ms)...");
            }
        }

        WalkSurfaceWalkableBuilder.FinalizeAll(builders.Values);

        var saved = 0;
        foreach (var builder in builders.Values)
        {
            var path = WalkSurfaceChunk.BuildAbsolutePath(builder.ChunkX, builder.ChunkZ, outputDirectoryResourcePath);
            builder.SaveTo(path);
            saved++;
        }

        WalkSurfaceCache.Invalidate();
        GD.Print(
            $"PhysicsStandingSurfaceAtlasBuilder: wrote {saved} chunk(s) to '{outputDirectoryResourcePath}' "
            + $"(physics samples={physicsSamples}, walkable~={walkableCells}, blocked~={blockedCells}) "
            + $"in {stopwatch.ElapsedMilliseconds} ms.");
        return saved;
    }

    public static int BuildAndRebakeNav(
        Node3D contextNode,
        GridMap terrain,
        TerrainGridFill gridFill,
        bool clearExistingChunks = true)
    {
        var saved = BuildFromPhysicsWorld(
            contextNode,
            terrain,
            gridFill.WalkSurfaceDataDirectory,
            clearExistingChunks);
        if (saved <= 0)
        {
            return 0;
        }

        return gridFill.BakeOutdoorNavAtlas();
    }

    private static void ClearChunkFiles(string outputDirectoryResourcePath)
    {
        var absoluteDirectory = ProjectSettings.GlobalizePath(outputDirectoryResourcePath);
        if (!Directory.Exists(absoluteDirectory))
        {
            return;
        }

        foreach (var pattern in new[] { "chunk_*.bin", "chunk_*.bin.tmp" })
        {
            foreach (var file in Directory.GetFiles(absoluteDirectory, pattern))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    GD.PushWarning($"PhysicsStandingSurfaceAtlasBuilder: could not delete '{file}'.");
                }
            }
        }
    }
}
