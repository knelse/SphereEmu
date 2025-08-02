using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SphServer.Providers;

public class AppConfig
{
    public string PacketPartPath { get; set; } = @"D:\SphereDev\SphereSource\source\sphPacketDefinitions";
    public string PacketDefinitionPath { get; set; } = @"D:\SphereDev\SphereSource\source\sphPacketDefinitions";

    public string LiteDbConnectionString { get; set; } =
        @"Filename=d:\SphereDev\_sphereStuff\sph.db;Connection=shared;";

    public ushort Port { get; set; } = 25860;
    public string LogPath { get; set; } = @"logs\server.log";
}

public static class AppConfigProvider
{
    public static AppConfig Provide()
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

        foreach (var defaultSetting in defaultSettings)
        {
            if (!configDict.ContainsKey(defaultSetting.Key))
            {
                configDict[defaultSetting.Key] = defaultSetting.Value;
                configChanged = true;
            }
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
            LogPath = configDict.GetValueOrDefault("LogPath", @"logs\server.log")
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
            ["LogPath"] = @"logs\server.log"
        };
    }

    private static void SaveAppConfig(string configPath, Dictionary<string, string> config)
    {
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        File.WriteAllText(configPath, json);
        SphLogger.Info("Updated configuration file with missing default values");
    }
}