using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     One outdoor walk-height chunk: walk heights, terrain baseline, blocked flags, and outdoor spawn bits.
/// </summary>
public sealed class WalkSurfaceChunk
{
    public const float NoGround = float.NaN;

    private const ushort LegacyFormatVersion = 2;

    private readonly float[] _heights;
    private readonly float[] _terrainHeights;
    private readonly byte[] _blocked;
    private readonly byte[] _spawnAllowed;

    public WalkSurfaceChunk(
        float originX,
        float originZ,
        float sampleSpacing,
        int width,
        int height,
        float[] heights,
        byte[]? blocked = null,
        float[]? terrainHeights = null,
        byte[]? spawnAllowed = null,
        bool hasOutdoorSpawnChannel = false)
    {
        OriginX = originX;
        OriginZ = originZ;
        SampleSpacing = sampleSpacing;
        Width = width;
        Height = height;
        _heights = heights;
        var count = width * height;
        _blocked = blocked ?? new byte[count];
        _terrainHeights = terrainHeights ?? (float[])heights.Clone();
        _spawnAllowed = spawnAllowed ?? new byte[count];
        HasOutdoorSpawnChannel = hasOutdoorSpawnChannel;
    }

    public float OriginX { get; }
    public float OriginZ { get; }
    public float SampleSpacing { get; }
    public int Width { get; }
    public int Height { get; }
    public bool HasOutdoorSpawnChannel { get; }

    public float MaxWorldX => OriginX + (Width - 1) * SampleSpacing;
    public float MaxWorldZ => OriginZ + (Height - 1) * SampleSpacing;

    public bool Contains(float worldX, float worldZ)
    {
        return worldX >= OriginX && worldX <= MaxWorldX + SampleSpacing * 0.001f
            && worldZ >= OriginZ && worldZ <= MaxWorldZ + SampleSpacing * 0.001f;
    }

    public bool IsBlockedForPlacement(float worldX, float worldZ)
    {
        if (Width <= 0 || Height <= 0)
        {
            return true;
        }

        return IsBlockedNearest(worldX, worldZ);
    }

    public bool IsOutdoorSpawnAllowed(float worldX, float worldZ)
    {
        if (!HasOutdoorSpawnChannel || !TryGetNearestSampleIndex(worldX, worldZ, out var index))
        {
            return false;
        }

        return _spawnAllowed[index] != 0;
    }

    public bool TrySampleOutdoorSpawn(float worldX, float worldZ, out float worldY)
    {
        worldY = NoGround;
        if (!HasOutdoorSpawnChannel || !TryGetNearestSampleIndex(worldX, worldZ, out var index))
        {
            return false;
        }

        if (_spawnAllowed[index] == 0)
        {
            return false;
        }

        var terrainHeight = _terrainHeights[index];
        if (float.IsNaN(terrainHeight))
        {
            return false;
        }

        worldY = terrainHeight;
        return true;
    }

    public bool TrySampleBilinear(float worldX, float worldZ, out float worldY)
    {
        worldY = NoGround;

        if (Width <= 0 || Height <= 0)
        {
            return false;
        }

        if (IsBlockedNearest(worldX, worldZ))
        {
            return false;
        }

        var fx = (worldX - OriginX) / SampleSpacing;
        var fz = (worldZ - OriginZ) / SampleSpacing;

        if (fx < 0f || fz < 0f || fx > Width - 1 || fz > Height - 1)
        {
            return false;
        }

        var x0 = (int)Math.Floor(fx);
        var z0 = (int)Math.Floor(fz);
        var x1 = Math.Min(x0 + 1, Width - 1);
        var z1 = Math.Min(z0 + 1, Height - 1);
        var tx = fx - x0;
        var tz = fz - z0;

        if (IsBlockedSample(x0, z0) || IsBlockedSample(x1, z0) || IsBlockedSample(x0, z1) || IsBlockedSample(x1, z1))
        {
            return TrySampleNearest(worldX, worldZ, out worldY);
        }

        var h00 = _heights[z0 * Width + x0];
        var h10 = _heights[z0 * Width + x1];
        var h01 = _heights[z1 * Width + x0];
        var h11 = _heights[z1 * Width + x1];

        if (float.IsNaN(h00) || float.IsNaN(h10) || float.IsNaN(h01) || float.IsNaN(h11))
        {
            return TrySampleNearest(worldX, worldZ, out worldY);
        }

        var top = Mathf.Lerp(h00, h10, tx);
        var bottom = Mathf.Lerp(h01, h11, tx);
        worldY = Mathf.Lerp(top, bottom, tz);
        return true;
    }

    public bool TrySampleBilinearForSpawn(float worldX, float worldZ, out float worldY)
    {
        worldY = NoGround;

        if (Width <= 0 || Height <= 0)
        {
            return false;
        }

        if (IsBlockedForPlacement(worldX, worldZ))
        {
            return false;
        }

        var fx = (worldX - OriginX) / SampleSpacing;
        var fz = (worldZ - OriginZ) / SampleSpacing;

        if (fx < 0f || fz < 0f || fx > Width - 1 || fz > Height - 1)
        {
            return false;
        }

        var x0 = (int)Math.Floor(fx);
        var z0 = (int)Math.Floor(fz);
        var x1 = Math.Min(x0 + 1, Width - 1);
        var z1 = Math.Min(z0 + 1, Height - 1);

        if (IsBlockedSample(x0, z0) || IsBlockedSample(x1, z0) || IsBlockedSample(x0, z1) || IsBlockedSample(x1, z1))
        {
            return false;
        }

        var h00 = _heights[z0 * Width + x0];
        var h10 = _heights[z0 * Width + x1];
        var h01 = _heights[z1 * Width + x0];
        var h11 = _heights[z1 * Width + x1];

        if (float.IsNaN(h00) || float.IsNaN(h10) || float.IsNaN(h01) || float.IsNaN(h11))
        {
            return false;
        }

        var tx = fx - x0;
        var tz = fz - z0;
        var top = Mathf.Lerp(h00, h10, tx);
        var bottom = Mathf.Lerp(h01, h11, tx);
        worldY = Mathf.Lerp(top, bottom, tz);
        return true;
    }

    internal void CopyToBuilder(
        float[] heights,
        float[] terrainHeights,
        byte[] blocked,
        byte[] spawnAllowed,
        out bool hasOutdoorSpawnChannel)
    {
        Array.Copy(_heights, heights, _heights.Length);
        Array.Copy(_terrainHeights, terrainHeights, _terrainHeights.Length);
        Array.Copy(_blocked, blocked, _blocked.Length);
        Array.Copy(_spawnAllowed, spawnAllowed, _spawnAllowed.Length);
        hasOutdoorSpawnChannel = HasOutdoorSpawnChannel;
    }

    internal void CopyHeightsAndBlockedTo(float[] heights, byte[] blocked)
    {
        Array.Copy(_heights, heights, _heights.Length);
        Array.Copy(_blocked, blocked, _blocked.Length);
    }

    internal void CollectOutdoorSpawnCandidates(
        float centerWorldX,
        float centerWorldZ,
        float radiusMeters,
        List<(float X, float Z, float Y)> candidates)
    {
        if (!HasOutdoorSpawnChannel)
        {
            return;
        }

        var radiusSq = radiusMeters * radiusMeters;
        for (var z = 0; z < Height; z++)
        {
            for (var x = 0; x < Width; x++)
            {
                var index = z * Width + x;
                if (_spawnAllowed[index] == 0)
                {
                    continue;
                }

                var worldX = OriginX + x * SampleSpacing;
                var worldZ = OriginZ + z * SampleSpacing;
                var dx = worldX - centerWorldX;
                var dz = worldZ - centerWorldZ;
                if (dx * dx + dz * dz > radiusSq)
                {
                    continue;
                }

                var terrainHeight = _terrainHeights[index];
                if (float.IsNaN(terrainHeight))
                {
                    continue;
                }

                candidates.Add((worldX, worldZ, terrainHeight));
            }
        }
    }

    public static WalkSurfaceChunk CreateEmpty(float originX, float originZ, float sampleSpacing, int width, int height)
    {
        var heights = new float[width * height];
        Array.Fill(heights, NoGround);
        return new WalkSurfaceChunk(originX, originZ, sampleSpacing, width, height, heights);
    }

    public static string BuildResourcePath(int chunkX, int chunkZ, string directoryResourcePath)
    {
        var trimmed = directoryResourcePath.TrimEnd('/');
        return $"{trimmed}/chunk_{chunkX}_{chunkZ}.bin";
    }

    public static string BuildAbsolutePath(int chunkX, int chunkZ, string directoryResourcePath)
    {
        return ProjectSettings.GlobalizePath(BuildResourcePath(chunkX, chunkZ, directoryResourcePath));
    }

    public static bool TryPeekFormatVersion(string absolutePath, out ushort version)
    {
        version = 0;
        if (!File.Exists(absolutePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(absolutePath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != WalkSurfaceChunkFile.Magic)
            {
                return false;
            }

            version = reader.ReadUInt16();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SaveAtomic(string absolutePath)
    {
        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = absolutePath + ".tmp";
        using (var stream = File.Open(tempPath, FileMode.Create, global::System.IO.FileAccess.Write, FileShare.None))
        {
            WriteToStream(stream);
        }

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        File.Move(tempPath, absolutePath);
    }

    private void WriteToStream(Stream stream)
    {
        if (HasOutdoorSpawnChannel)
        {
            WalkSurfaceChunkCodec.WriteV4(
                stream,
                SampleSpacing,
                OriginX,
                OriginZ,
                Width,
                Height,
                _heights,
                _terrainHeights,
                _blocked,
                _spawnAllowed);
            return;
        }

        WalkSurfaceChunkCodec.WriteV3(stream, SampleSpacing, OriginX, OriginZ, Width, Height, _heights, _blocked);
    }

    public static bool TryLoad(string absolutePath, out WalkSurfaceChunk? chunk)
    {
        chunk = null;
        if (!File.Exists(absolutePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(absolutePath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != WalkSurfaceChunkFile.Magic)
            {
                return false;
            }

            var version = reader.ReadUInt16();
            var sampleSpacing = reader.ReadSingle();
            var originX = reader.ReadSingle();
            var originZ = reader.ReadSingle();
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            var count = width * height;

            float[] heights;
            float[] terrainHeights;
            byte[] blocked;
            byte[] spawnAllowed;
            var hasOutdoorSpawnChannel = false;

            if (version == WalkSurfaceChunkCodec.FormatVersionV4)
            {
                if (!WalkSurfaceChunkCodec.TryReadV4(reader, sampleSpacing, originX, originZ, width, height, out heights, out terrainHeights, out blocked, out spawnAllowed)
                    || heights is null
                    || terrainHeights is null
                    || blocked is null
                    || spawnAllowed is null)
                {
                    return false;
                }

                hasOutdoorSpawnChannel = false;
                foreach (var allowed in spawnAllowed)
                {
                    if (allowed != 0)
                    {
                        hasOutdoorSpawnChannel = true;
                        break;
                    }
                }
            }
            else if (version == WalkSurfaceChunkCodec.FormatVersion)
            {
                if (!WalkSurfaceChunkCodec.TryReadV3(reader, sampleSpacing, originX, originZ, width, height, out heights, out blocked)
                    || heights is null
                    || blocked is null)
                {
                    return false;
                }

                terrainHeights = (float[])heights.Clone();
                spawnAllowed = new byte[count];
            }
            else if (version is 1 or LegacyFormatVersion)
            {
                heights = new float[count];
                for (var i = 0; i < count; i++)
                {
                    heights[i] = reader.ReadSingle();
                }

                blocked = new byte[count];
                if (version >= LegacyFormatVersion && reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    for (var i = 0; i < count; i++)
                    {
                        blocked[i] = reader.ReadByte();
                    }
                }

                terrainHeights = (float[])heights.Clone();
                spawnAllowed = new byte[count];
            }
            else
            {
                return false;
            }

            chunk = new WalkSurfaceChunk(
                originX,
                originZ,
                sampleSpacing,
                width,
                height,
                heights,
                blocked,
                terrainHeights,
                spawnAllowed,
                hasOutdoorSpawnChannel);
            return true;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"WalkSurfaceChunk: failed to load '{absolutePath}': {ex.Message}");
            return false;
        }
    }

    private bool IsBlockedNearest(float worldX, float worldZ)
    {
        if (!TryGetNearestSampleIndex(worldX, worldZ, out var index))
        {
            return true;
        }

        return _blocked[index] != 0;
    }

    private bool TrySampleNearest(float worldX, float worldZ, out float worldY)
    {
        worldY = NoGround;
        if (!TryGetNearestSampleIndex(worldX, worldZ, out var index))
        {
            return false;
        }

        var nearest = _heights[index];
        if (float.IsNaN(nearest))
        {
            return false;
        }

        worldY = nearest;
        return true;
    }

    private bool TryGetNearestSampleIndex(float worldX, float worldZ, out int index)
    {
        index = 0;
        var x = Mathf.RoundToInt((worldX - OriginX) / SampleSpacing);
        var z = Mathf.RoundToInt((worldZ - OriginZ) / SampleSpacing);
        if (x < 0 || x >= Width || z < 0 || z >= Height)
        {
            return false;
        }

        index = z * Width + x;
        return true;
    }

    private bool IsBlockedSample(int x, int z)
    {
        return _blocked[z * Width + x] != 0;
    }
}
