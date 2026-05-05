using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace SphServer.Tools.MbdConverter;

internal static class Program
{
    private const int NameFieldByteLength = 20;
    private const int ObjectStructByteLength = NameFieldByteLength + (6 * 4);

    private static int Main(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (options is null)
        {
            PrintUsage();
            return 1;
        }

        if (!Directory.Exists(options.ParamsRoot))
        {
            Console.Error.WriteLine($"Params folder not found: {options.ParamsRoot}");
            return 2;
        }

        Directory.CreateDirectory(options.OutputRoot);

        var files = Directory.EnumerateFiles(options.ParamsRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".mbd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mb", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("No .mbd/.mb files found.");
            return 0;
        }

        var converted = 0;
        foreach (var file in files)
        {
            try
            {
                var objects = ReadMbdObjects(file);
                var rel = Path.GetRelativePath(options.ParamsRoot, file);
                var relJson = Path.ChangeExtension(rel, ".json") ?? (rel + ".json");
                var jsonPath = Path.GetFullPath(Path.Combine(options.OutputRoot, relJson));
                var jsonDir = Path.GetDirectoryName(jsonPath);
                if (!string.IsNullOrEmpty(jsonDir))
                {
                    Directory.CreateDirectory(jsonDir);
                }

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(objects, jsonOptions);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);

                converted++;
                if (options.Verbose)
                {
                    Console.WriteLine($"Wrote {jsonPath} ({objects.Count} objects)");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed: {file}");
                Console.Error.WriteLine(ex.Message);
                if (options.FailFast)
                {
                    return 3;
                }
            }
        }

        Console.WriteLine($"Converted {converted}/{files.Count} file(s).");
        return 0;
    }

    private static List<MbdObject> ReadMbdObjects(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 4)
        {
            throw new InvalidDataException("File too small (expected at least 4 bytes).");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0, 2));
        var zeroes = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2, 2));
        if (zeroes != 0)
        {
            throw new InvalidDataException($"Expected header bytes 2..4 to be 0, got {zeroes}.");
        }

        var expectedMinLength = 4 + (count * ObjectStructByteLength);
        if (data.Length < expectedMinLength)
        {
            throw new InvalidDataException($"File too small for {count} objects (need >= {expectedMinLength} bytes, got {data.Length}).");
        }

        var list = new List<MbdObject>(count);
        var offset = 4;
        for (var i = 0; i < count; i++)
        {
            var span = data.AsSpan(offset, ObjectStructByteLength);
            var nameBytes = span[..NameFieldByteLength];
            var name = DecodeFixedString(nameBytes);

            var x = ReadSingleLe(span, NameFieldByteLength + (0 * 4));
            var y = ReadSingleLe(span, NameFieldByteLength + (1 * 4));
            var z = ReadSingleLe(span, NameFieldByteLength + (2 * 4));
            // Verified against legacy ObjectData JSON: the .mbd rotation float order is yaw, pitch, roll.
            var yaw = ReadSingleLe(span, NameFieldByteLength + (3 * 4));
            var pitch = ReadSingleLe(span, NameFieldByteLength + (4 * 4));
            var roll = ReadSingleLe(span, NameFieldByteLength + (5 * 4));

            list.Add(new MbdObject
            {
                name = name,
                x = x,
                y = y,
                z = z,
                pitch = pitch,
                yaw = yaw,
                roll = roll
            });

            offset += ObjectStructByteLength;
        }

        return list;
    }

    private static float ReadSingleLe(ReadOnlySpan<byte> span, int offset)
    {
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        return BitConverter.UInt32BitsToSingle(raw);
    }

    private static string DecodeFixedString(ReadOnlySpan<byte> bytes)
    {
        var nul = bytes.IndexOf((byte)0);
        if (nul >= 0)
        {
            bytes = bytes[..nul];
        }

        return Encoding.ASCII.GetString(bytes).Trim();
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            MbdConverter — convert Sphere.GameDataDecode params .mbd/.mb to .json

            Usage:
              MbdConverter [--params <path>] [--out <path>] [--verbose] [--fail-fast] [--help]

            Defaults:
              --params  <repoRoot>\Sphere.GameDataDecode\params
              --out     <repoRoot>\Godot\Terrain\ObjectDataJson
            """);
    }

    private sealed class CliOptions
    {
        public required string ParamsRoot { get; init; }
        public required string OutputRoot { get; init; }
        public bool Verbose { get; init; }
        public bool FailFast { get; init; }

        public static CliOptions? Parse(string[] args)
        {
            if (args.Length == 1 && args[0] is "-h" or "--help" or "-?")
            {
                return null;
            }

            var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
            var @params = Path.Combine(repoRoot, "Sphere.GameDataDecode", "params");
            var output = Path.Combine(repoRoot, "Godot", "Terrain", "ObjectDataJson");
            var verbose = false;
            var failFast = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--params" when i + 1 < args.Length:
                        @params = Path.GetFullPath(args[++i]);
                        break;
                    case "--out" when i + 1 < args.Length:
                        output = Path.GetFullPath(args[++i]);
                        break;
                    case "--verbose":
                        verbose = true;
                        break;
                    case "--fail-fast":
                        failFast = true;
                        break;
                }
            }

            return new CliOptions
            {
                ParamsRoot = @params,
                OutputRoot = output,
                Verbose = verbose,
                FailFast = failFast
            };
        }

        private static string FindRepoRoot(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Sphere.GameDataDecode")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return startDir;
        }
    }

    private sealed record MbdObject
    {
        public required string name { get; init; }
        public required float x { get; init; }
        public required float y { get; init; }
        public required float z { get; init; }
        public required float pitch { get; init; }
        public required float yaw { get; init; }
        public required float roll { get; init; }
    }
}
