using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Godot;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Editor-only builder: samples outdoor GridMap tile tops and terrain object walk heights into chunked atlases.
/// </summary>
public static class WalkSurfaceAtlasBuilder
{
    public const string DefaultOutputDirectory = "res://Godot/Terrain/WalkData";
    public const float ChunkSizeMeters = 512f;
    public const float SampleSpacingMeters = 0.25f;
    private const int FlushCellInterval = 16;

    private static readonly object BuilderMapLock = new();
    private static readonly object FlushLock = new();

    public sealed class ObjectFootprintSettings
    {
        public string ObjectDataDirectory { get; init; } = "res://Godot/Terrain/ObjectDataJson/";
        public string ModelsDirectory { get; init; } = "res://Godot/Models/";
        public bool Enabled { get; init; } = true;
    }

    public static int BuildFromGridMap(
        GridMap terrain,
        string outputDirectoryResourcePath = DefaultOutputDirectory,
        ObjectFootprintSettings? objectFootprints = null,
        bool resumeFromProgress = true)
    {
        if (terrain.MeshLibrary is null)
        {
            GD.PushError("WalkSurfaceAtlasBuilder: terrain has no MeshLibrary.");
            return 0;
        }

        var usedCells = terrain.GetUsedCells();
        if (usedCells.Count == 0)
        {
            GD.PushWarning("WalkSurfaceAtlasBuilder: terrain has no cells.");
            return 0;
        }

        var stopwatch = Stopwatch.StartNew();
        var absoluteOutputDirectory = ProjectSettings.GlobalizePath(outputDirectoryResourcePath);
        EnsureOutputDirectory(outputDirectoryResourcePath);
        GD.Print(
            $"WalkSurfaceAtlasBuilder: starting bake for {usedCells.Count} cells -> '{absoluteOutputDirectory}'.");

        var terrainSignature = WalkSurfaceBuildProgress.ComputeTerrainSignature(terrain, usedCells);
        var processedCells = new HashSet<(int X, int Z)>();
        var resume = resumeFromProgress
            && WalkSurfaceBuildProgress.TryLoad(
                outputDirectoryResourcePath,
                terrainSignature,
                out processedCells);
        if (!resume)
        {
            ClearChunkFiles(outputDirectoryResourcePath);
            WalkSurfaceBuildProgress.Delete(outputDirectoryResourcePath);
            processedCells = new HashSet<(int X, int Z)>();
        }

        var builders = LoadExistingChunks(outputDirectoryResourcePath);
        TerrainMeshRaycastCache.Clear();
        TerrainTileHeightTemplateCache.Clear();

        var halfTile = terrain.CellSize.X * 0.5f;
        var terrainGlobal = terrain.GlobalTransform;
        var pendingJobs = SnapshotPendingCellJobs(terrain, usedCells, processedCells);

        if (processedCells.Count > 0)
        {
            GD.Print(
                $"WalkSurfaceAtlasBuilder: resuming ({processedCells.Count}/{usedCells.Count} cells done, {pendingJobs.Count} remaining).");
        }

        WriteInitialProgress(outputDirectoryResourcePath, terrainSignature, processedCells);

        var templates = TerrainTileHeightTemplateCache.WarmAll(terrain, usedCells, halfTile, SampleSpacingMeters);
        GD.Print($"WalkSurfaceAtlasBuilder: precomputed {templates.Count} tile template(s) in {stopwatch.ElapsedMilliseconds} ms.");

        var samplesWritten = 0;
        var cellsCompleted = processedCells.Count;

        for (var jobIndex = 0; jobIndex < pendingJobs.Count; jobIndex++)
        {
            var job = pendingJobs[jobIndex];
            if (job.ItemId < 0 || !templates.TryGetValue(job.ItemId, out var template) || template is null)
            {
                processedCells.Add((job.Cell.X, job.Cell.Z));
                cellsCompleted++;
                MaybeFlush(
                    builders,
                    outputDirectoryResourcePath,
                    terrainSignature,
                    processedCells,
                    cellsCompleted,
                    usedCells.Count,
                    stopwatch,
                    jobIndex + 1,
                    pendingJobs.Count);
                continue;
            }

            var localWritten = template.WriteCellSamples(
                terrainGlobal,
                job.CellBasis,
                job.CenterLocal,
                (worldX, worldY, worldZ) =>
                {
                    var builder = GetOrCreateBuilder(builders, worldX, worldZ);
                    builder.SetWorldSample(worldX, worldZ, worldY);
                });
            samplesWritten += localWritten;
            processedCells.Add((job.Cell.X, job.Cell.Z));
            cellsCompleted++;

            MaybeFlush(
                builders,
                outputDirectoryResourcePath,
                terrainSignature,
                processedCells,
                cellsCompleted,
                usedCells.Count,
                stopwatch,
                jobIndex + 1,
                pendingJobs.Count);
        }

        FlushProgress(
            builders,
            outputDirectoryResourcePath,
            terrainSignature,
            processedCells,
            cellsCompleted,
            usedCells.Count,
            stopwatch,
            forceLog: true);

        GD.Print(
            $"WalkSurfaceAtlasBuilder: sampled terrain heights in {stopwatch.ElapsedMilliseconds} ms ({samplesWritten} new samples).");

        foreach (var builder in builders.Values)
        {
            builder.SnapshotTerrainHeights();
        }

        var objectStampCount = StampTerrainObjectWalkData(builders, objectFootprints);
        var savedChunks = SaveBuilders(builders, outputDirectoryResourcePath, samplesWritten, objectStampCount);
        WalkSurfaceBuildProgress.Delete(outputDirectoryResourcePath);
        GD.Print($"WalkSurfaceAtlasBuilder: finished in {stopwatch.ElapsedMilliseconds} ms.");
        return savedChunks;
    }

    /// <summary>
    ///     Re-applies terrain object blocked footprints and height samples onto existing saved chunks.
    /// </summary>
    public static int ApplyObjectFootprintsToSavedChunks(
        string outputDirectoryResourcePath = DefaultOutputDirectory,
        ObjectFootprintSettings? objectFootprints = null)
    {
        var settings = objectFootprints ?? new ObjectFootprintSettings();
        if (!settings.Enabled)
        {
            return 0;
        }

        var absoluteDirectory = ProjectSettings.GlobalizePath(outputDirectoryResourcePath);
        if (!Directory.Exists(absoluteDirectory))
        {
            GD.PushWarning($"WalkSurfaceAtlasBuilder: walk data directory not found: {outputDirectoryResourcePath}");
            return 0;
        }

        var builders = new Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder>();
        foreach (var file in Directory.GetFiles(absoluteDirectory, "chunk_*.bin"))
        {
            if (!WalkSurfaceChunk.TryLoad(file, out var chunk) || chunk is null)
            {
                continue;
            }

            var builder = WalkSurfaceChunkBuilder.FromChunk(chunk);
            builder.ClearBlocked();
            var chunkX = (int)Mathf.Floor(chunk.OriginX / ChunkSizeMeters);
            var chunkZ = (int)Mathf.Floor(chunk.OriginZ / ChunkSizeMeters);
            builders[(chunkX, chunkZ)] = builder;
        }

        if (builders.Count == 0)
        {
            GD.PushWarning("WalkSurfaceAtlasBuilder: no existing walk chunks to stamp object walk data onto.");
            return 0;
        }

        foreach (var builder in builders.Values)
        {
            if (Mathf.Abs(builder.GetSampleSpacingForValidation() - SampleSpacingMeters) > 0.001f)
            {
                GD.PushError(
                    $"WalkSurfaceAtlasBuilder: existing chunk ({builder.ChunkX},{builder.ChunkZ}) uses "
                    + $"{builder.GetSampleSpacingForValidation():0.##}m spacing; expected {SampleSpacingMeters:0.##}m. "
                    + "Run a full walk-surface rebake with --force.");
                return 0;
            }
        }

        var objectStampCount = StampTerrainObjectWalkData(builders, settings);
        return SaveBuilders(builders, outputDirectoryResourcePath, heightSamplesWritten: 0, objectStampCount);
    }

    internal static WalkSurfaceChunkBuilder GetOrCreateBuilder(
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders,
        float worldX,
        float worldZ)
    {
        var chunkX = FloorDiv(worldX, ChunkSizeMeters);
        var chunkZ = FloorDiv(worldZ, ChunkSizeMeters);
        var key = (chunkX, chunkZ);
        lock (BuilderMapLock)
        {
            if (!builders.TryGetValue(key, out var builder))
            {
                builder = new WalkSurfaceChunkBuilder(chunkX, chunkZ, ChunkSizeMeters, SampleSpacingMeters);
                builders[key] = builder;
            }

            return builder;
        }
    }

    private static List<WalkSurfaceCellJob> SnapshotPendingCellJobs(
        GridMap terrain,
        IEnumerable<Vector3I> usedCells,
        HashSet<(int X, int Z)> processedCells)
    {
        var pending = new List<WalkSurfaceCellJob>();
        foreach (var cell in usedCells)
        {
            if (processedCells.Contains((cell.X, cell.Z)))
            {
                continue;
            }

            pending.Add(new WalkSurfaceCellJob(
                cell,
                terrain.GetCellItem(cell),
                terrain.GetCellItemBasis(cell),
                terrain.MapToLocal(cell)));
        }

        return pending;
    }

    private static void WriteInitialProgress(
        string outputDirectoryResourcePath,
        uint terrainSignature,
        HashSet<(int X, int Z)> processedCells)
    {
        WalkSurfaceBuildProgress.Save(outputDirectoryResourcePath, terrainSignature, processedCells);
        GD.Print($"WalkSurfaceAtlasBuilder: progress file written ({processedCells.Count} cells tracked).");
    }

    private static void MaybeFlush(
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders,
        string outputDirectoryResourcePath,
        uint terrainSignature,
        HashSet<(int X, int Z)> processedCells,
        int cellsCompleted,
        int totalCells,
        Stopwatch stopwatch,
        int pendingCompleted,
        int pendingTotal)
    {
        if (cellsCompleted % FlushCellInterval != 0 && pendingCompleted != pendingTotal)
        {
            return;
        }

        FlushProgress(
            builders,
            outputDirectoryResourcePath,
            terrainSignature,
            processedCells,
            cellsCompleted,
            totalCells,
            stopwatch,
            forceLog: pendingCompleted == pendingTotal || cellsCompleted <= FlushCellInterval);
    }

    private static void FlushProgress(
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders,
        string outputDirectoryResourcePath,
        uint terrainSignature,
        HashSet<(int X, int Z)> processedCells,
        int cellsCompleted,
        int totalCells,
        Stopwatch stopwatch,
        bool forceLog = false)
    {
        lock (FlushLock)
        {
            var flushedChunks = 0;
            foreach (var builder in builders.Values)
            {
                if (!builder.IsDirty)
                {
                    continue;
                }

                var path = WalkSurfaceChunk.BuildAbsolutePath(builder.ChunkX, builder.ChunkZ, outputDirectoryResourcePath);
                builder.SaveTo(path);
                flushedChunks++;
            }

            WalkSurfaceBuildProgress.Save(outputDirectoryResourcePath, terrainSignature, processedCells);

            if (forceLog || flushedChunks > 0)
            {
                GD.Print(
                    $"WalkSurfaceAtlasBuilder: flushed {flushedChunks} chunk(s), progress {cellsCompleted}/{totalCells} cells ({stopwatch.ElapsedMilliseconds} ms).");
            }
        }
    }

    private static Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> LoadExistingChunks(
        string outputDirectoryResourcePath)
    {
        var builders = new Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder>();
        var absoluteDirectory = ProjectSettings.GlobalizePath(outputDirectoryResourcePath);
        if (!Directory.Exists(absoluteDirectory))
        {
            return builders;
        }

        foreach (var file in Directory.GetFiles(absoluteDirectory, "chunk_*.bin"))
        {
            if (!WalkSurfaceChunk.TryLoad(file, out var chunk) || chunk is null)
            {
                continue;
            }

            var builder = WalkSurfaceChunkBuilder.FromChunk(chunk);
            builder.ClearDirty();
            var chunkX = (int)Mathf.Floor(chunk.OriginX / ChunkSizeMeters);
            var chunkZ = (int)Mathf.Floor(chunk.OriginZ / ChunkSizeMeters);
            builders[(chunkX, chunkZ)] = builder;
        }

        return builders;
    }

    private static int StampTerrainObjectWalkData(
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders,
        ObjectFootprintSettings? objectFootprints)
    {
        var settings = objectFootprints ?? new ObjectFootprintSettings();
        if (!settings.Enabled)
        {
            GD.Print("WalkSurfaceAtlasBuilder: terrain object walk data disabled.");
            return 0;
        }

        var placements = TerrainObjectPlacementSource.LoadAll(settings.ObjectDataDirectory);
        GD.Print($"WalkSurfaceAtlasBuilder: stamping {placements.Count} terrain object footprint(s)...");
        foreach (var builder in builders.Values)
        {
            builder.EnsureTerrainBaseline();
            builder.ClearBlocked();
        }

        var footprintCount = WalkSurfaceObjectFootprintStamper.StampPlacements(builders, placements, settings.ModelsDirectory);
        var blockedRasterCount = WalkSurfaceObjectBlockedRasterStamper.StampPlacements(
            builders,
            placements,
            settings.ModelsDirectory,
            SampleSpacingMeters);
        GD.Print(
            $"WalkSurfaceAtlasBuilder: stamping object height templates at {SampleSpacingMeters:0.##}m spacing "
            + $"(plants/rocks blocked only, {placements.Count} total placement(s))...");
        var heightCount = WalkSurfaceObjectHeightBaker.BakePlacements(
            builders,
            placements,
            settings.ModelsDirectory,
            SampleSpacingMeters);
        GD.Print(
            $"WalkSurfaceAtlasBuilder: object footprints={footprintCount}, blocked raster cells={blockedRasterCount}, object height samples={heightCount}.");
        WalkSurfaceWalkableBuilder.FinalizeAll(builders.Values);
        return footprintCount + blockedRasterCount + heightCount;
    }

    private static int SaveBuilders(
        Dictionary<(int ChunkX, int ChunkZ), WalkSurfaceChunkBuilder> builders,
        string outputDirectoryResourcePath,
        int heightSamplesWritten,
        int objectStampCount)
    {
        var savedChunks = 0;
        foreach (var builder in builders.Values)
        {
            var path = WalkSurfaceChunk.BuildAbsolutePath(builder.ChunkX, builder.ChunkZ, outputDirectoryResourcePath);
            builder.SaveTo(path);
            savedChunks++;
        }

        GD.Print(
            $"WalkSurfaceAtlasBuilder: wrote {savedChunks} chunk(s), {heightSamplesWritten} height sample(s), {objectStampCount} object footprint(s) to '{outputDirectoryResourcePath}'.");
        WalkSurfaceCache.Invalidate();
        return savedChunks;
    }

    private static int FloorDiv(float value, float divisor)
    {
        return (int)Mathf.Floor(value / divisor);
    }

    private static void EnsureOutputDirectory(string outputDirectoryResourcePath)
    {
        var absoluteDirectory = ProjectSettings.GlobalizePath(outputDirectoryResourcePath);
        Directory.CreateDirectory(absoluteDirectory);
    }

    private static void ClearChunkFiles(string outputDirectoryResourcePath)
    {
        var absoluteDirectory = ProjectSettings.GlobalizePath(outputDirectoryResourcePath);
        if (!Directory.Exists(absoluteDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(absoluteDirectory, "chunk_*.bin"))
        {
            File.Delete(file);
        }

        foreach (var file in Directory.GetFiles(absoluteDirectory, "chunk_*.bin.tmp"))
        {
            File.Delete(file);
        }
    }
}
