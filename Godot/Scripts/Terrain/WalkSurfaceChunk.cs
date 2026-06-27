using System;

using System.IO;

using Godot;



namespace SphServer.Godot.Scripts.Terrain;



/// <summary>

///     One outdoor walk-height chunk: a regular XZ grid of top-surface Y samples in world space,

///     plus optional blocked flags for terrain object footprints.

/// </summary>

public sealed class WalkSurfaceChunk

{

    public const float NoGround = float.NaN;

    private const uint Magic = 0x4B485357; // 'WSHK'

    private const ushort FormatVersion = 2;



    private readonly float[] _heights;

    private readonly byte[] _blocked;



    public WalkSurfaceChunk(

        float originX,

        float originZ,

        float sampleSpacing,

        int width,

        int height,

        float[] heights,

        byte[]? blocked = null)

    {

        OriginX = originX;

        OriginZ = originZ;

        SampleSpacing = sampleSpacing;

        Width = width;

        Height = height;

        _heights = heights;

        _blocked = blocked ?? new byte[width * height];

    }



    public float OriginX { get; }

    public float OriginZ { get; }

    public float SampleSpacing { get; }

    public int Width { get; }

    public int Height { get; }



    public float MaxWorldX => OriginX + (Width - 1) * SampleSpacing;

    public float MaxWorldZ => OriginZ + (Height - 1) * SampleSpacing;



    public bool Contains(float worldX, float worldZ)

    {

        return worldX >= OriginX && worldX <= MaxWorldX + SampleSpacing * 0.001f

            && worldZ >= OriginZ && worldZ <= MaxWorldZ + SampleSpacing * 0.001f;

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

            return false;

        }



        var h00 = _heights[z0 * Width + x0];

        var h10 = _heights[z0 * Width + x1];

        var h01 = _heights[z1 * Width + x0];

        var h11 = _heights[z1 * Width + x1];



        if (float.IsNaN(h00) || float.IsNaN(h10) || float.IsNaN(h01) || float.IsNaN(h11))

        {

            var nearest = _heights[z0 * Width + x0];

            if (float.IsNaN(nearest))

            {

                return false;

            }



            worldY = nearest;

            return true;

        }



        var top = Mathf.Lerp(h00, h10, tx);

        var bottom = Mathf.Lerp(h01, h11, tx);

        worldY = Mathf.Lerp(top, bottom, tz);

        return true;

    }



    internal void CopyHeightsAndBlockedTo(float[] heights, byte[] blocked)

    {

        Array.Copy(_heights, heights, _heights.Length);

        Array.Copy(_blocked, blocked, _blocked.Length);

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



    public void Save(string absolutePath)

    {

        var directory = Path.GetDirectoryName(absolutePath);

        if (!string.IsNullOrEmpty(directory))

        {

            Directory.CreateDirectory(directory);

        }



        using var stream = File.Open(absolutePath, FileMode.Create, global::System.IO.FileAccess.Write, FileShare.None);

        WriteToStream(stream);

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

        using var writer = new BinaryWriter(stream);

        writer.Write(Magic);

        writer.Write(FormatVersion);

        writer.Write(SampleSpacing);

        writer.Write(OriginX);

        writer.Write(OriginZ);

        writer.Write((ushort)Width);

        writer.Write((ushort)Height);

        for (var i = 0; i < _heights.Length; i++)

        {

            writer.Write(_heights[i]);

        }



        for (var i = 0; i < _blocked.Length; i++)

        {

            writer.Write(_blocked[i]);

        }

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

            if (reader.ReadUInt32() != Magic)

            {

                return false;

            }



            var version = reader.ReadUInt16();

            if (version is not 1 and not 2)

            {

                return false;

            }



            var sampleSpacing = reader.ReadSingle();

            var originX = reader.ReadSingle();

            var originZ = reader.ReadSingle();

            var width = reader.ReadUInt16();

            var height = reader.ReadUInt16();

            var count = width * height;

            var heights = new float[count];

            for (var i = 0; i < count; i++)

            {

                heights[i] = reader.ReadSingle();

            }



            var blocked = new byte[count];

            if (version >= 2 && reader.BaseStream.Position < reader.BaseStream.Length)

            {

                for (var i = 0; i < count; i++)

                {

                    blocked[i] = reader.ReadByte();

                }

            }



            chunk = new WalkSurfaceChunk(originX, originZ, sampleSpacing, width, height, heights, blocked);

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

        var x = Mathf.RoundToInt((worldX - OriginX) / SampleSpacing);

        var z = Mathf.RoundToInt((worldZ - OriginZ) / SampleSpacing);

        if (x < 0 || x >= Width || z < 0 || z >= Height)

        {

            return true;

        }



        return IsBlockedSample(x, z);

    }



    private bool IsBlockedSample(int x, int z)

    {

        return _blocked[z * Width + x] != 0;

    }

}


