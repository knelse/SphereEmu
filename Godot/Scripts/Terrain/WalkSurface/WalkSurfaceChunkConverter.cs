using System.IO;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Converts legacy walk-surface chunk files (v1/v2) to compressed format v3 without rebaking terrain.
/// </summary>
public static class WalkSurfaceChunkConverter
{
    public sealed class ConversionResult
    {
        public int Converted { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public bool Succeeded => Failed == 0;
    }

    public static ConversionResult ConvertDirectory(
        string directoryResourcePath = WalkSurfaceAtlasBuilder.DefaultOutputDirectory,
        bool skipAlreadyCurrent = true)
    {
        var result = new ConversionResult();
        var absoluteDirectory = ProjectSettings.GlobalizePath(directoryResourcePath);
        if (!Directory.Exists(absoluteDirectory))
        {
            GD.PushError($"WalkSurfaceChunkConverter: directory not found: {directoryResourcePath}");
            result.Failed = 1;
            return result;
        }

        var files = Directory.GetFiles(absoluteDirectory, "chunk_*.bin");
        if (files.Length == 0)
        {
            GD.PushWarning($"WalkSurfaceChunkConverter: no chunk files in '{directoryResourcePath}'.");
            return result;
        }

        GD.Print(
            $"WalkSurfaceChunkConverter: scanning {files.Length} chunk file(s) in '{absoluteDirectory}'...");

        foreach (var file in files)
        {
            if (skipAlreadyCurrent
                && WalkSurfaceChunk.TryPeekFormatVersion(file, out var version)
                && version == WalkSurfaceChunkCodec.FormatVersionV4)
            {
                result.Skipped++;
                continue;
            }

            if (!WalkSurfaceChunk.TryLoad(file, out var chunk) || chunk is null)
            {
                result.Failed++;
                GD.PushWarning($"WalkSurfaceChunkConverter: failed to load '{file}'.");
                continue;
            }

            var builder = WalkSurfaceChunkBuilder.FromChunk(chunk);
            WalkSurfaceSpawnChannelBuilder.Finalize(builder);
            builder.SaveTo(file);
            result.Converted++;

            if (result.Converted == 1 || result.Converted % 10 == 0)
            {
                GD.Print($"WalkSurfaceChunkConverter: converted {result.Converted} chunk(s)...");
            }
        }

        WalkSurfaceCache.Invalidate();
        GD.Print(
            $"WalkSurfaceChunkConverter: done — converted={result.Converted}, skipped (already v4)={result.Skipped}, failed={result.Failed}.");
        return result;
    }
}
