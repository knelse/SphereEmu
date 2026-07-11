using System.Text.Json;
using SphServer.Helpers;
using SphServer.Shared.Logger;

namespace SphServer.Server.Config;

public class AppConfig
{
    public string RepositoryPath { get; init; } =
        @"D:\\SphereDev\\SphereSource\\SphereEmu";

    public string PacketDefinitionPath { get; init; } =
        @"D:\\SphereDev\\SphereSource\\SphereEmu\\Sphere.PacketDefinitions";

    public string DecodedGameDataPath { get; init; } =
        @"D:\\SphereDev\\SphereSource\\SphereEmu\\Sphere.GameDataDecode";

    public string LiteDbConnectionString { get; init; } =
        @"Filename=d:\SphereDev\_sphereStuff\sph.db;Connection=shared;";

    public ushort Port { get; init; } = 25860;
    public string LogPath { get; init; } = @"logs\server.log";
    public bool DebugMode { get; init; } = true;
    public float ObjectVisibilityDistance { get; init; } = 100.0f;
    public int ReceiveBufferSize { get; init; } = 1024;
    public int CurrentCharacterInventoryId { get; init; } = 0xA001;
    public float Spawn_X { get; init; } = 80.0f;
    public float Spawn_Y { get; init; } = 150.0f;
    public float Spawn_Z { get; init; } = -200.0f;
    public float Spawn_Angle { get; init; } = 0.75f;
    public int Spawn_Money { get; init; } = 99999999;
}

public static class ServerConfig
{
    private static readonly object AppConfigLock = new();
    private static AppConfig? _appConfig;

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public static AppConfig AppConfig
    {
        get
        {
            if (_appConfig is not null)
            {
                return _appConfig;
            }

            lock (AppConfigLock)
            {
                return _appConfig ??= Get();
            }
        }
        private set => _appConfig = value ?? new AppConfig();
    }

    // Note: no static ctor here on purpose. Godot can load types in unusual orders / threads;
    // lazy initialization avoids intermittent nulls and makes first-access deterministic.

    public static AppConfig Get()
    {
        try
        {
            var configPath = FindConfigPath("appsettings.json");

            if (!File.Exists(configPath))
            {
                SphLogger.Info($"Configuration file not found, creating default: {configPath}");
                CreateDefaultAppConfig(configPath);
            }
            else
            {
                SphLogger.Info($"Loading configuration from: {configPath}");
            }

            using var configFile = File.OpenRead(configPath);
            using var configReader = new StreamReader(configFile);
            var configJson = configReader.ReadToEnd();

            var configDict = JsonSerializer.Deserialize<Dictionary<string, string>>(configJson, JsonReadOptions) ??
                             new();

            var defaultSettings = GetDefaultAppConfigDict();
            var configChanged = false;

            foreach (var defaultSetting in defaultSettings.Where(defaultSetting =>
                         !configDict.ContainsKey(defaultSetting.Key)))
            {
                configDict[defaultSetting.Key] = defaultSetting.Value;
                configChanged = true;
            }

            if (configChanged)
            {
                SaveAppConfig(configPath, configDict);
            }

            var repositoryPath = configDict.GetValueOrDefault("RepositoryPath",
                @"D:\\SphereDev\\SphereSource\\SphereEmu");

            return new AppConfig
            {
                RepositoryPath = repositoryPath,
                PacketDefinitionPath = configDict.GetValueOrDefault("PacketDefinitionPath",
                    Path.Combine(repositoryPath, "Sphere.PacketDefinitions")),
                DecodedGameDataPath = configDict.GetValueOrDefault("DecodedGameDataPath",
                    Path.Combine(repositoryPath, "Sphere.GameDataDecode")),
                LiteDbConnectionString = configDict.GetValueOrDefault("LiteDbConnectionString",
                    @"Filename=d:\SphereDev\_sphereStuff\sph.db;Connection=shared;"),
                Port = FileFormatCulture.ParseUShort(configDict.GetValueOrDefault("Port", "25860")),
                LogPath = configDict.GetValueOrDefault("LogPath", @"logs\server.log"),
                DebugMode = bool.Parse(configDict.GetValueOrDefault("DebugMode", "true")),
                ObjectVisibilityDistance =
                    FileFormatCulture.ParseFloat(configDict.GetValueOrDefault("ObjectVisibilityDistance", "100.0")),
                ReceiveBufferSize = FileFormatCulture.ParseInt(configDict.GetValueOrDefault("ReceiveBufferSize", "1024")),
                CurrentCharacterInventoryId =
                    FileFormatCulture.ParseInt(configDict.GetValueOrDefault("CurrentCharacterInventoryId", "40961")),
                Spawn_X = FileFormatCulture.ParseFloat(configDict.GetValueOrDefault("Spawn_X", "80.0")),
                Spawn_Y = FileFormatCulture.ParseFloat(configDict.GetValueOrDefault("Spawn_Y", "150.0")),
                Spawn_Z = FileFormatCulture.ParseFloat(configDict.GetValueOrDefault("Spawn_Z", "200.0")),
                Spawn_Angle = FileFormatCulture.ParseFloat(configDict.GetValueOrDefault("Spawn_Angle", "0.75")),
                Spawn_Money = FileFormatCulture.ParseInt(configDict.GetValueOrDefault("Spawn_Money", "99999999"))
            };
        }
        catch (Exception ex)
        {
            SphLogger.Info($"Failed to load appsettings.json, using defaults. Error: {ex}");
            return new AppConfig();
        }
    }

    private static string FindConfigPath(string fileName)
    {
        // Godot's working directory may vary (editor/run/export), so search upwards from common roots.
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
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        return fileName;
    }

    private static void CreateDefaultAppConfig(string configPath)
    {
        var defaultConfig = GetDefaultAppConfigDict();
        var json = JsonSerializer.Serialize(defaultConfig, JsonWriteOptions);
        File.WriteAllText(configPath, json);
        SphLogger.Info($"Created default configuration file: {configPath}");
    }

    private static Dictionary<string, string> GetDefaultAppConfigDict()
    {
        return new()
        {
            ["RepositoryPath"] = @"D:\\SphereDev\\SphereSource\\SphereEmu",
            ["LiteDbConnectionString"] = @"Filename=sph.db;Connection=shared;",
            ["Port"] = "25860",
            ["LogPath"] = @"logs\server.log",
            ["DebugMode"] = "true",
            ["ObjectVisibilityDistance"] = "100.0",
            ["ReceiveBufferSize"] = "1024",
            ["CurrentCharacterInventoryId"] = "40961",
            ["Spawn_X"] = "80.0",
            ["Spawn_Y"] = "150.0",
            ["Spawn_Z"] = "200.0",
            ["Spawn_Angle"] = "0.75",
            ["Spawn_Money"] = "99999999"
        };
    }

    private static void SaveAppConfig(string configPath, Dictionary<string, string> config)
    {
        var json = JsonSerializer.Serialize(config, JsonWriteOptions);
        File.WriteAllText(configPath, json);
        SphLogger.Info("Updated configuration file with missing default values");
    }
}