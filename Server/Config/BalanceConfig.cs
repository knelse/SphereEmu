using System.Collections.Concurrent;
using System.Text.Json;
using SphServer.Shared.Logger;

namespace SphServer.Server.Config;

/// <summary>
///     Loads and caches balance configs from <c>Config/Balance/&lt;name&gt;.json</c>; cheap to call per hit.
///     Missing or invalid files throw — a silent default would un-tune a mechanic invisibly.
/// </summary>
public static class BalanceConfig
{
    private static readonly ConcurrentDictionary<(string Name, Type Type), object> Cache = new ();

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    // Balance files carry provenance comments and trailing commas; keys match case-insensitively.
    private static readonly JsonSerializerOptions JsonReadOptions = new ()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    public static T Get<T> (string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Balance config name must be non-empty, e.g. \"combat\" for Config/Balance/combat.json.",
                nameof(name));
        }

        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Balance config name '{name}' must not include the .json extension — pass \"{name[..^5]}\".",
                nameof(name));
        }

        if (name.IndexOfAny(InvalidNameChars) >= 0 || name.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                $"Balance config name '{name}' must be a bare file name without path separators.",
                nameof(name));
        }

        return (T) Cache.GetOrAdd((name, typeof(T)), static key => Load(key.Name, key.Type));
    }

    private static object Load (string name, Type type)
    {
        var fileName = name + ".json";
        var probedPaths = new List<string>();
        var configPath = FindBalanceConfigPath(fileName, probedPaths);

        if (configPath is null)
        {
            throw new FileNotFoundException(
                $"Balance config '{name}' not found (expected {Path.Combine("Config", "Balance", fileName)}). " +
                $"Searched:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", probedPaths)}{Environment.NewLine}" +
                "BalanceConfig has no silent defaults on purpose: a missing config must fail loudly " +
                "instead of un-tuning the mechanic invisibly.",
                fileName);
        }

        object? config;
        try
        {
            using var configFile = File.OpenRead(configPath);
            config = JsonSerializer.Deserialize(configFile, type, JsonReadOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Balance config '{configPath}' is not valid JSON for {type.Name}: {ex.Message}", ex);
        }

        if (config is null)
        {
            throw new InvalidDataException(
                $"Balance config '{configPath}' deserialized to null for {type.Name} — the file must contain a JSON object.");
        }

        SphLogger.Info($"Loaded balance config '{name}' as {type.Name} from: {configPath}");
        return config;
    }

    /// <summary>
    ///     Walks up from BaseDirectory then CWD probing <c>Config/Balance/&lt;file&gt;</c>, then falls
    ///     back to <c>RepositoryPath</c> — Godot's CWD and build output location vary per run mode.
    /// </summary>
    private static string? FindBalanceConfigPath (string fileName, List<string> probedPaths)
    {
        var relativePath = Path.Combine("Config", "Balance", fileName);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var startDir in new[]
                 {
                     AppContext.BaseDirectory,
                     Environment.CurrentDirectory
                 })
        {
            if (string.IsNullOrWhiteSpace(startDir))
            {
                continue;
            }

            var dir = new DirectoryInfo(startDir);
            while (dir is not null && visited.Add(dir.FullName))
            {
                var candidate = Path.Combine(dir.FullName, relativePath);
                probedPaths.Add(candidate);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        var fallback = Path.Combine(ServerConfig.AppConfig.RepositoryPath, relativePath);
        probedPaths.Add(fallback);
        return File.Exists(fallback) ? fallback : null;
    }
}
