using System.Collections.Concurrent;
using System.Text.Json;
using SphServer.Server.GameplayLogic.Combat;
using SphServer.Shared.Logger;

namespace SphServer.Server.Config;

/// <summary>
///     Balance configs from <c>Config/Balance/&lt;name&gt;.json</c>, all loaded by
///     <see cref="PreloadAll" /> at server startup; <see cref="Get{T}" /> is then a lookup, cheap per hit.
///     Missing or invalid files never fall back to defaults — that would un-tune a mechanic invisibly.
/// </summary>
public static class BalanceConfig
{
    private static readonly (string Name, Type Type)[] KnownConfigs =
    [
        ("combat", typeof(CombatBalance))
    ];

    private static readonly ConcurrentDictionary<(string Name, Type Type), object> Loaded = new ();

    // Balance files carry provenance comments and trailing commas; keys match case-insensitively.
    private static readonly JsonSerializerOptions JsonReadOptions = new ()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Loads every known config once at startup; a broken file is logged, not thrown, so the rest still load.</summary>
    public static void PreloadAll ()
    {
        foreach (var (name, type) in KnownConfigs)
        {
            try
            {
                Loaded[(name, type)] = Load(name, type);
            }
            catch (Exception ex)
            {
                SphLogger.Error($"Failed to import balance config '{name}' as {type.Name}", ex);
            }
        }
    }

    /// <summary>Null when the import failed at startup; callers log and skip rather than throwing per hit.</summary>
    public static T? Get<T> (string name) where T : class
    {
        if (Loaded.TryGetValue((name, typeof(T)), out var config))
        {
            return (T) config;
        }

        SphLogger.Error(
            $"BalanceConfig.Get: '{name}' as {typeof(T).Name} is not available — either its import failed at " +
            "startup (see the log) or it is missing from BalanceConfig.KnownConfigs.");
        return null;
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

        // PreloadAll catches and logs this, so a
        // bad file fails once at startup instead of on every packet that reads the value.
        if (config is IValidatableBalanceConfig validatable)
        {
            validatable.Validate(configPath);
        }

        SphLogger.Info($"Loaded balance config '{name}' as {type.Name} from: {configPath}");
        return config;
    }

    /// <summary>
    ///     Walks up from BaseDirectory then CWD probing <c>Config/Balance/<file></c>, then falls
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
