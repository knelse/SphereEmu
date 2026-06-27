using System;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Mutable height + blocked samples for one walk atlas chunk during editor bakes.
/// </summary>
public sealed class WalkSurfaceChunkBuilder
{
    private readonly float _originX;
    private readonly float _originZ;
    private readonly float _sampleSpacing;
    private readonly int _width;
    private readonly int _height;
    private readonly float[] _heights;
    private readonly byte[] _blocked;
    private readonly object _writeLock = new();
    private bool _dirty;

    public int ChunkX { get; }
    public int ChunkZ { get; }

    public bool IsDirty => _dirty;

    internal float GetSampleSpacingForValidation() => _sampleSpacing;

    public WalkSurfaceChunkBuilder(int chunkX, int chunkZ, float chunkSizeMeters, float sampleSpacing)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        _sampleSpacing = sampleSpacing;
        _originX = chunkX * chunkSizeMeters;
        _originZ = chunkZ * chunkSizeMeters;
        _width = Mathf.CeilToInt(chunkSizeMeters / sampleSpacing) + 1;
        _height = Mathf.CeilToInt(chunkSizeMeters / sampleSpacing) + 1;
        _heights = new float[_width * _height];
        _blocked = new byte[_width * _height];
        for (var i = 0; i < _heights.Length; i++)
        {
            _heights[i] = WalkSurfaceChunk.NoGround;
        }
    }

    public static WalkSurfaceChunkBuilder FromChunk(WalkSurfaceChunk chunk)
    {
        var chunkX = (int)Mathf.Floor(chunk.OriginX / WalkSurfaceAtlasBuilder.ChunkSizeMeters);
        var chunkZ = (int)Mathf.Floor(chunk.OriginZ / WalkSurfaceAtlasBuilder.ChunkSizeMeters);
        var builder = new WalkSurfaceChunkBuilder(chunkX, chunkZ, WalkSurfaceAtlasBuilder.ChunkSizeMeters, chunk.SampleSpacing);
        chunk.CopyHeightsAndBlockedTo(builder._heights, builder._blocked);
        return builder;
    }

    public void ClearBlocked()
    {
        Array.Clear(_blocked, 0, _blocked.Length);
    }

    public void SetWorldSample(float worldX, float worldZ, float worldY)
    {
        if (!TryGetSampleIndex(worldX, worldZ, out var index))
        {
            return;
        }

        lock (_writeLock)
        {
            var existing = _heights[index];
            if (float.IsNaN(existing) || worldY > existing)
            {
                _heights[index] = worldY;
                _dirty = true;
            }
        }
    }

    public bool IsBlockedAtWorld(float worldX, float worldZ)
    {
        if (!TryGetSampleIndex(worldX, worldZ, out var index))
        {
            return false;
        }

        return _blocked[index] != 0;
    }

    public bool TrySetWalkableWorldSample(float worldX, float worldZ, float worldY)
    {
        if (IsBlockedAtWorld(worldX, worldZ))
        {
            return false;
        }

        SetWorldSample(worldX, worldZ, worldY);
        return true;
    }

    public bool StampBlockedDisk(float worldX, float worldZ, float radiusMeters)
    {
        if (radiusMeters <= 0f)
        {
            return false;
        }

        var minWorldX = worldX - radiusMeters;
        var maxWorldX = worldX + radiusMeters;
        var minWorldZ = worldZ - radiusMeters;
        var maxWorldZ = worldZ + radiusMeters;
        var minX = Mathf.Max(0, (int)Mathf.Floor((minWorldX - _originX) / _sampleSpacing));
        var maxX = Mathf.Min(_width - 1, (int)Mathf.Ceil((maxWorldX - _originX) / _sampleSpacing));
        var minZ = Mathf.Max(0, (int)Mathf.Floor((minWorldZ - _originZ) / _sampleSpacing));
        var maxZ = Mathf.Min(_height - 1, (int)Mathf.Ceil((maxWorldZ - _originZ) / _sampleSpacing));
        var radiusSq = radiusMeters * radiusMeters;
        var stamped = false;

        for (var z = minZ; z <= maxZ; z++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var sampleWorldX = _originX + x * _sampleSpacing;
                var sampleWorldZ = _originZ + z * _sampleSpacing;
                var dx = sampleWorldX - worldX;
                var dz = sampleWorldZ - worldZ;
                if (dx * dx + dz * dz > radiusSq)
                {
                    continue;
                }

                _blocked[z * _width + x] = 1;
                _dirty = true;
                stamped = true;
            }
        }

        return stamped;
    }

    public bool StampBlockedOrientedRect(
        float worldX,
        float worldZ,
        float halfExtentXMeters,
        float halfExtentZMeters,
        float yawRadians)
    {
        if (halfExtentXMeters <= 0f || halfExtentZMeters <= 0f)
        {
            return false;
        }

        var cos = Mathf.Cos(yawRadians);
        var sin = Mathf.Sin(yawRadians);
        var boundHalfX = Mathf.Abs(cos * halfExtentXMeters) + Mathf.Abs(sin * halfExtentZMeters);
        var boundHalfZ = Mathf.Abs(sin * halfExtentXMeters) + Mathf.Abs(cos * halfExtentZMeters);
        var minWorldX = worldX - boundHalfX;
        var maxWorldX = worldX + boundHalfX;
        var minWorldZ = worldZ - boundHalfZ;
        var maxWorldZ = worldZ + boundHalfZ;
        var minX = Mathf.Max(0, (int)Mathf.Floor((minWorldX - _originX) / _sampleSpacing));
        var maxX = Mathf.Min(_width - 1, (int)Mathf.Ceil((maxWorldX - _originX) / _sampleSpacing));
        var minZ = Mathf.Max(0, (int)Mathf.Floor((minWorldZ - _originZ) / _sampleSpacing));
        var maxZ = Mathf.Min(_height - 1, (int)Mathf.Ceil((maxWorldZ - _originZ) / _sampleSpacing));
        var stamped = false;

        for (var z = minZ; z <= maxZ; z++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var sampleWorldX = _originX + x * _sampleSpacing;
                var sampleWorldZ = _originZ + z * _sampleSpacing;
                var dx = sampleWorldX - worldX;
                var dz = sampleWorldZ - worldZ;
                var localX = dx * cos + dz * sin;
                var localZ = -dx * sin + dz * cos;
                if (Mathf.Abs(localX) > halfExtentXMeters || Mathf.Abs(localZ) > halfExtentZMeters)
                {
                    continue;
                }

                _blocked[z * _width + x] = 1;
                _dirty = true;
                stamped = true;
            }
        }

        return stamped;
    }

    public WalkSurfaceChunk Build()
    {
        return new WalkSurfaceChunk(_originX, _originZ, _sampleSpacing, _width, _height, _heights, _blocked);
    }

    public void SaveTo(string absolutePath)
    {
        lock (_writeLock)
        {
            Build().SaveAtomic(absolutePath);
            _dirty = false;
        }
    }

    public void ClearDirty()
    {
        _dirty = false;
    }

    private bool TryGetSampleIndex(float worldX, float worldZ, out int index)
    {
        index = 0;
        var x = Mathf.RoundToInt((worldX - _originX) / _sampleSpacing);
        var z = Mathf.RoundToInt((worldZ - _originZ) / _sampleSpacing);
        if (x < 0 || x >= _width || z < 0 || z >= _height)
        {
            return false;
        }

        index = z * _width + x;
        return true;
    }
}
