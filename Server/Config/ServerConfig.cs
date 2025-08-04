using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SphServer.Shared.Logger;

namespace SphServer.Server.Config;

public class AppConfig
{
    public string PacketPartPath { get; init; } = @"D:\SphereDev\SphereSource\source\sphPacketDefinitions";
    public string PacketDefinitionPath { get; init; } = @"D:\SphereDev\SphereSource\source\sphPacketDefinitions";

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
    public float Spawn_Z { get; init; } = 200.0f;
    public float Spawn_Angle { get; init; } = 0.75f;
    public int Spawn_Money { get; init; } = 99999999;
}

public static class ServerConfig
{
    public static AppConfig AppConfig { get; set; }

    static ServerConfig()
    {
        AppConfig = Get();
    }

    public static AppConfig Get()
    {
        const string configPath = "appsettings.json";

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

        var configDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(configJson) ?? new();

        var defaultSettings = GetDefaultAppConfigDict();
        var configChanged = false;

        foreach (var defaultSetting in defaultSettings.Where(defaultSetting => !configDict.ContainsKey(defaultSetting.Key)))
        {
            configDict[defaultSetting.Key] = defaultSetting.Value;
            configChanged = true;
        }

        if (configChanged)
        {
            SaveAppConfig(configPath, configDict);
        }

        return new AppConfig
        {
            PacketPartPath = configDict.GetValueOrDefault("PacketPartPath",
                @"D:\SphereDev\SphereSource\source\sphPacketDefinitions"),
            PacketDefinitionPath = configDict.GetValueOrDefault("PacketDefinitionPath",
                @"D:\SphereDev\SphereSource\source\sphPacketDefinitions"),
            LiteDbConnectionString = configDict.GetValueOrDefault("LiteDbConnectionString",
                @"Filename=d:\SphereDev\_sphereStuff\sph.db;Connection=shared;"),
            Port = ushort.Parse(configDict.GetValueOrDefault("Port", "25860")),
            LogPath = configDict.GetValueOrDefault("LogPath", @"logs\server.log"),
            DebugMode = bool.Parse(configDict.GetValueOrDefault("DebugMode", "true")),
            ObjectVisibilityDistance = float.Parse(configDict.GetValueOrDefault("ObjectVisibilityDistance", "100.0")),
            ReceiveBufferSize = int.Parse(configDict.GetValueOrDefault("ReceiveBufferSize", "1024")),
            CurrentCharacterInventoryId = int.Parse(configDict.GetValueOrDefault("CurrentCharacterInventoryId", "40961")),
            Spawn_X = float.Parse(configDict.GetValueOrDefault("Spawn_X", "80.0")),
            Spawn_Y = float.Parse(configDict.GetValueOrDefault("Spawn_Y", "150.0")),
            Spawn_Z = float.Parse(configDict.GetValueOrDefault("Spawn_Z", "200.0")),
            Spawn_Angle = float.Parse(configDict.GetValueOrDefault("Spawn_Angle", "0.75")),
            Spawn_Money = int.Parse(configDict.GetValueOrDefault("Spawn_Money", "99999999"))
        };
    }

    private static void CreateDefaultAppConfig(string configPath)
    {
        var defaultConfig = GetDefaultAppConfigDict();
        var json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
        File.WriteAllText(configPath, json);
        SphLogger.Info($"Created default configuration file: {configPath}");
    }

    private static Dictionary<string, string> GetDefaultAppConfigDict()
    {
        return new()
        {
            ["PacketPartPath"] = @"D:\SphereDev\SphereSource\source\sphPacketDefinitions",
            ["PacketDefinitionPath"] = @"D:\SphereDev\SphereSource\source\sphPacketDefinitions",
            ["LiteDbConnectionString"] = @"Filename=d:\SphereDev\_sphereStuff\sph.db;Connection=shared;",
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
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        File.WriteAllText(configPath, json);
        SphLogger.Info("Updated configuration file with missing default values");
    }
}