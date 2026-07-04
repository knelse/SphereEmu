using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Tracks which terrain cells have been baked so walk atlas builds can resume after interruption.
/// </summary>
internal static class WalkSurfaceBuildProgress
{
    private const uint Magic = 0x50425357; // 'WSBP'
    private const ushort FormatVersion = 1;
    public const string ProgressFileName = "walk_build_progress.bin";

    public static string BuildAbsolutePath(string outputDirectoryResourcePath)
    {
        var trimmed = outputDirectoryResourcePath.TrimEnd('/');
        return ProjectSettings.GlobalizePath($"{trimmed}/{ProgressFileName}");
    }

    public static uint ComputeTerrainSignature(GridMap terrain, IEnumerable<Vector3I> usedCells)
    {
        var cells = new List<(int X, int Z, int Item)>();
        foreach (var cell in usedCells)
        {
            cells.Add((cell.X, cell.Z, terrain.GetCellItem(cell)));
        }

        cells.Sort(static (a, b) =>
        {
            var cmp = a.X.CompareTo(b.X);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = a.Z.CompareTo(b.Z);
            return cmp != 0 ? cmp : a.Item.CompareTo(b.Item);
        });

        var hash = 2166136261u;
        foreach (var (x, z, item) in cells)
        {
            hash = FnvMix(hash, x);
            hash = FnvMix(hash, z);
            hash = FnvMix(hash, item);
        }

        hash = FnvMix(hash, (int)(WalkSurfaceAtlasBuilder.SampleSpacingMeters * 1000f));
        hash = FnvMix(hash, (int)(WalkSurfaceAtlasBuilder.ChunkSizeMeters * 1000f));
        return hash;
    }

    public static bool TryLoad(
        string outputDirectoryResourcePath,
        uint expectedSignature,
        out HashSet<(int X, int Z)> processedCells)
    {
        processedCells = new HashSet<(int X, int Z)>();
        var path = BuildAbsolutePath(outputDirectoryResourcePath);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != FormatVersion)
            {
                return false;
            }

            if (reader.ReadUInt32() != expectedSignature)
            {
                return false;
            }

            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                processedCells.Add((reader.ReadInt32(), reader.ReadInt32()));
            }

            return true;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"WalkSurfaceBuildProgress: failed to load '{path}': {ex.Message}");
            return false;
        }
    }

    public static void Save(
        string outputDirectoryResourcePath,
        uint signature,
        IEnumerable<(int X, int Z)> processedCells)
    {
        var path = BuildAbsolutePath(outputDirectoryResourcePath);
        var tempPath = path + ".tmp";
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var stream = File.Open(tempPath, FileMode.Create, global::System.IO.FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(signature);

            var list = new List<(int X, int Z)>(processedCells);
            list.Sort(static (a, b) =>
            {
                var cmp = a.X.CompareTo(b.X);
                return cmp != 0 ? cmp : a.Z.CompareTo(b.Z);
            });

            writer.Write(list.Count);
            foreach (var (x, z) in list)
            {
                writer.Write(x);
                writer.Write(z);
            }
        }

        ReplaceFile(tempPath, path);
    }

    public static void Delete(string outputDirectoryResourcePath)
    {
        var path = BuildAbsolutePath(outputDirectoryResourcePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var tempPath = path + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }

    private static uint FnvMix(uint hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 16777619u;
            return hash;
        }
    }

    private static void ReplaceFile(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(sourcePath, destinationPath);
    }
}
