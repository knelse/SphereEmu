using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Client .wtr planes, 12 by 12 cells of height then material. Underwater is g_ground code 5
/// </summary>
public static class TerrainWater
{
    private const int CellCount = 144;
    private const int CellsPerAxis = 12;
    private const float PatchSize = 100f;

    // char*.mdl top is the origin (max Y 0, min Y -1.8). g_ground feet are min Y - 0.25
    private const double PlayerFeetBelowOrigin = 1.8 + 0.25;
    private const double UnderwaterBand = 30.0;

    // ControlMove skips g_ground while Y >= 1000
    private const double SurfaceHeightLimit = 1000.0;

    private static readonly Dictionary<string, string> WtrPaths = new (StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, WaterCell[]?> CellsByMaster = new (StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Mesh?> MeshCache = new (StringComparer.OrdinalIgnoreCase);
    private static bool filesReady;

    // initializeWater primary_animation, then secondary_animation. wwN_00 is drawWater frame 0
    private static readonly int[] PrimaryAnim = { 0, 1, 2, 1, 2, 3, 4, 3 };
    private static readonly int[] SecondaryAnim = { 0, 1, 2, 2, 2, 3, 4, 3 };
    private static readonly float[] PrimaryOpacity = { 0f, 0.7f, 0.6f, 0.6f, 0.7f, 0.8f, 1f, 0.6f };
    private static readonly float[] SecondaryOpacity = { 0f, 0.4f, 0.2f, 0.33f, 0.35f, 0.5f, 0.45f, 0.4f };

    private static readonly StandardMaterial3D?[] PrimaryMaterials = new StandardMaterial3D?[8];
    private static readonly StandardMaterial3D?[] SecondaryMaterials = new StandardMaterial3D?[8];
    private static readonly Texture2D?[] FrameTextures = new Texture2D?[5];
    private static bool textureWarned;

    private readonly struct WaterCell (float height, uint material)
    {
        public float Height { get; } = height;
        public uint Material { get; } = material;
    }

    public static bool IsUnderwater (double clientX, double clientHeight, double clientZ)
    {
        if (clientHeight >= SurfaceHeightLimit)
        {
            return false;
        }

        if (!TrySample (clientX, clientZ, out var height))
        {
            return false;
        }

        var feet = clientHeight - PlayerFeetBelowOrigin;
        return height <= feet && height + UnderwaterBand > feet;
    }

    public static Mesh? GetOrBuildMesh (string masterName)
    {
        if (MeshCache.TryGetValue (masterName, out var cached))
        {
            return cached;
        }

        var cells = CellsFor (masterName);
        var mesh = cells is null ? null : BuildMesh (cells);
        MeshCache[masterName] = mesh;
        return mesh;
    }

    private static bool TrySample (double clientX, double clientZ, out double height)
    {
        height = 0;
        var patchX = PatchIndex (clientX);
        var patchZ = PatchIndex (clientZ);
        var row = patchX + 40;
        var col = 39 - patchZ;
        if ((uint) row >= 80 || (uint) col >= 80)
        {
            return false;
        }

        var gx = 79 - col;
        var gz = row;
        if (!TerrainGroundIndex.GetOrLoad ().TryGetMasterName (gx, gz, out var masterName))
        {
            return false;
        }

        var cells = CellsFor (masterName);
        if (cells is null)
        {
            return false;
        }

        var cell = cells[LocalCell (clientX) + CellsPerAxis * LocalCell (clientZ)];
        if (!IsLive (cell))
        {
            return false;
        }

        height = cell.Height;
        return true;
    }

    private static WaterCell[]? CellsFor (string masterName)
    {
        EnsureFiles ();
        if (CellsByMaster.TryGetValue (masterName, out var cached))
        {
            return cached;
        }

        if (!WtrPaths.TryGetValue (masterName, out var path))
        {
            CellsByMaster[masterName] = null;
            return null;
        }

        var cells = ReadCells (path);
        CellsByMaster[masterName] = cells;
        return cells;
    }

    private static Mesh? BuildMesh (WaterCell[] cells)
    {
        // Same frame as the ground mesh: scn (x, -y, -z), then TileMeshBasisAfterImport
        var basis = TerrainTileMeshFactory.TileMeshBasisAfterImport ();
        var up = basis * new Vector3 (0f, -1f, 0f);
        if (up.Y < 0f)
        {
            up = -up;
        }

        up = up.Normalized ();
        var mesh = new ArrayMesh ();
        var surface = AddPass (mesh, 0, cells, basis, up, secondary: false);
        surface = AddPass (mesh, surface, cells, basis, up, secondary: true);
        return surface == 0 ? null : mesh;
    }

    private static int AddPass (
        ArrayMesh mesh,
        int surface,
        WaterCell[] cells,
        Basis basis,
        Vector3 up,
        bool secondary)
    {
        var cellSize = PatchSize / CellsPerAxis;
        for (var material = 1u; material <= 7u; material++)
        {
            var vertices = new List<Vector3> ();
            var normals = new List<Vector3> ();
            var uvs = new List<Vector2> ();
            var indices = new List<int> ();
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (!IsLive (cell) || cell.Material != material)
                {
                    continue;
                }

                var cellX = i % CellsPerAxis;
                var cellZ = i / CellsPerAxis;
                var x0 = cellX * cellSize;
                var z0 = cellZ * cellSize;
                var x1 = x0 + cellSize;
                var z1 = z0 + cellSize;
                var height = cell.Height;
                var start = vertices.Count;
                vertices.Add (basis * new Vector3 (x0, -height, -z0));
                vertices.Add (basis * new Vector3 (x1, -height, -z0));
                vertices.Add (basis * new Vector3 (x1, -height, -z1));
                vertices.Add (basis * new Vector3 (x0, -height, -z1));
                normals.Add (up);
                normals.Add (up);
                normals.Add (up);
                normals.Add (up);
                if (secondary)
                {
                    // drawWater pass 2: one quarter of the frame, tiled across 4 cells
                    var u = (cellX % 4) * 0.25f;
                    var v = (cellZ % 4) * 0.25f;
                    uvs.Add (new Vector2 (u, v));
                    uvs.Add (new Vector2 (u + 0.25f, v));
                    uvs.Add (new Vector2 (u + 0.25f, v + 0.25f));
                    uvs.Add (new Vector2 (u, v + 0.25f));
                }
                else
                {
                    uvs.Add (new Vector2 (0f, 0f));
                    uvs.Add (new Vector2 (1f, 0f));
                    uvs.Add (new Vector2 (1f, 1f));
                    uvs.Add (new Vector2 (0f, 1f));
                }

                indices.Add (start);
                indices.Add (start + 1);
                indices.Add (start + 2);
                indices.Add (start);
                indices.Add (start + 2);
                indices.Add (start + 3);
            }

            if (vertices.Count == 0)
            {
                continue;
            }

            var arrays = new global::Godot.Collections.Array ();
            arrays.Resize ((int) Mesh.ArrayType.Max);
            arrays[(int) Mesh.ArrayType.Vertex] = vertices.ToArray ();
            arrays[(int) Mesh.ArrayType.Normal] = normals.ToArray ();
            arrays[(int) Mesh.ArrayType.TexUV] = uvs.ToArray ();
            arrays[(int) Mesh.ArrayType.Index] = indices.ToArray ();
            mesh.AddSurfaceFromArrays (Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial (surface, MaterialFor (material, secondary));
            surface++;
        }

        return surface;
    }

    private static StandardMaterial3D MaterialFor (uint material, bool secondary)
    {
        var table = secondary ? SecondaryMaterials : PrimaryMaterials;
        var slot = (int) material < table.Length ? (int) material : 1;
        var cached = table[slot];
        if (cached is not null && GodotObject.IsInstanceValid (cached))
        {
            return cached;
        }

        var anim = secondary ? SecondaryAnim[slot] : PrimaryAnim[slot];
        var opacity = secondary ? SecondaryOpacity[slot] : PrimaryOpacity[slot];
        var created = new StandardMaterial3D
        {
            AlbedoColor = new Color (1f, 1f, 1f, opacity),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            TextureRepeat = true,
        };
        var texture = LoadFrame (anim);
        if (texture is not null)
        {
            created.AlbedoTexture = texture;
        }

        table[slot] = created;
        return created;
    }

    private static Texture2D? LoadFrame (int anim)
    {
        if ((uint) anim >= (uint) FrameTextures.Length)
        {
            return null;
        }

        var cached = FrameTextures[anim];
        if (cached is not null && GodotObject.IsInstanceValid (cached))
        {
            return cached;
        }

        // waterAnimationTexture: "ww1_00" with digit 2 = animation id, digits 4-5 = frame
        var path = Path.Combine (@"d:\Games\Sfera_std\textures", $"ww{anim}_00.dds");
        var image = DecodeDxt1 (path);
        if (image is null)
        {
            if (!textureWarned)
            {
                textureWarned = true;
                GD.PushWarning ($"TerrainWater: {path} did not decode, water stays untextured");
            }

            return null;
        }

        var texture = ImageTexture.CreateFromImage (image);
        FrameTextures[anim] = texture;
        return texture;
    }

    // Image.Load returns unrecognized for these DXT1 headers
    private static Image? DecodeDxt1 (string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes (path);
        }
        catch (IOException)
        {
            return null;
        }

        if (bytes.Length < 128 || bytes[0] != (byte) 'D' || bytes[1] != (byte) 'D' || bytes[2] != (byte) 'S')
        {
            return null;
        }

        var height = BitConverter.ToInt32 (bytes, 12);
        var width = BitConverter.ToInt32 (bytes, 16);
        if (width <= 0 || height <= 0 || (width & 3) != 0 || (height & 3) != 0)
        {
            return null;
        }

        if (bytes[84] != (byte) 'D' || bytes[85] != (byte) 'X' || bytes[86] != (byte) 'T' || bytes[87] != (byte) '1')
        {
            return null;
        }

        var blocks = (width / 4) * (height / 4);
        if (128 + blocks * 8 > bytes.Length)
        {
            return null;
        }

        var pixels = new byte[width * height * 3];
        var offset = 128;
        Span<byte> colors = stackalloc byte[12];
        for (var blockY = 0; blockY < height; blockY += 4)
        {
            for (var blockX = 0; blockX < width; blockX += 4)
            {
                var color0 = BitConverter.ToUInt16 (bytes, offset);
                var color1 = BitConverter.ToUInt16 (bytes, offset + 2);
                var bits = BitConverter.ToUInt32 (bytes, offset + 4);
                offset += 8;
                colors.Clear ();
                Write565 (colors, 0, color0);
                Write565 (colors, 3, color1);
                if (color0 > color1)
                {
                    Mix (colors, 6, 0, 3, 2, 1);
                    Mix (colors, 9, 0, 3, 1, 2);
                }
                else
                {
                    Mix (colors, 6, 0, 3, 1, 1);
                }

                for (var py = 0; py < 4; py++)
                {
                    for (var px = 0; px < 4; px++)
                    {
                        var code = (int) ((bits >> (2 * (py * 4 + px))) & 3u);
                        var pixel = ((blockY + py) * width + blockX + px) * 3;
                        var source = code * 3;
                        pixels[pixel] = colors[source];
                        pixels[pixel + 1] = colors[source + 1];
                        pixels[pixel + 2] = colors[source + 2];
                    }
                }
            }
        }

        return Image.CreateFromData (width, height, false, Image.Format.Rgb8, pixels);
    }

    private static void Write565 (Span<byte> colors, int at, ushort packed)
    {
        var red = (packed >> 11) & 31;
        var green = (packed >> 5) & 63;
        var blue = packed & 31;
        colors[at] = (byte) ((red << 3) | (red >> 2));
        colors[at + 1] = (byte) ((green << 2) | (green >> 4));
        colors[at + 2] = (byte) ((blue << 3) | (blue >> 2));
    }

    private static void Mix (Span<byte> colors, int at, int first, int second, int firstWeight, int secondWeight)
    {
        var divisor = firstWeight + secondWeight;
        for (var channel = 0; channel < 3; channel++)
        {
            colors[at + channel] = (byte) (
                (colors[first + channel] * firstWeight + colors[second + channel] * secondWeight) / divisor);
        }
    }

    private static bool IsLive (WaterCell cell) =>
        cell.Material is > 0 and < 10 && float.IsFinite (cell.Height);

    private static WaterCell[]? ReadCells (string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes (path);
        }
        catch (IOException)
        {
            return null;
        }

        if (bytes.Length != CellCount * 8)
        {
            return null;
        }

        var cells = new WaterCell[CellCount];
        for (var i = 0; i < CellCount; i++)
        {
            var height = BitConverter.ToSingle (bytes, i * 8);
            var material = BitConverter.ToUInt32 (bytes, i * 8 + 4);
            cells[i] = new WaterCell (height, material);
        }

        return cells;
    }

    private static void EnsureFiles ()
    {
        if (filesReady)
        {
            return;
        }

        filesReady = true;
        var found = false;
        foreach (var dir in LandscapeDirectories ())
        {
            if (!Directory.Exists (dir))
            {
                continue;
            }

            found = true;
            foreach (var file in Directory.EnumerateFiles (dir, "*.wtr"))
            {
                WtrPaths.TryAdd (Path.GetFileNameWithoutExtension (file), file);
            }
        }

        if (!found)
        {
            GD.PushWarning ("TerrainWater: no client landscape folders, water planes stay empty");
        }
    }

    // Same stem as the .lnd, from the client landscape packs
    private static IEnumerable<string> LandscapeDirectories ()
    {
        const string clientRoot = @"d:\Games\Sfera_std";
        yield return Path.Combine (clientRoot, "landscape");
        yield return Path.Combine (clientRoot, "Landscape_hr");
        yield return Path.Combine (clientRoot, "Landscape_ph");
        yield return Path.Combine (clientRoot, "Landscape_rd");
    }

    private static int GridUnits (double world) =>
        (int) Math.Truncate (world * 0.12 + 100000.0) + 20000;

    private static int PatchIndex (double world) => GridUnits (world) / 12 - 10000;

    private static int LocalCell (double world)
    {
        var local = GridUnits (world) % CellsPerAxis;
        return local < 0 ? local + CellsPerAxis : local;
    }
}
