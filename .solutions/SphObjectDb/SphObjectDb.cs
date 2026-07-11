using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SphServer.Helpers;
using static GameObjectType;
using LocalizationEntryArray = System.Collections.Generic.Dictionary<Locale, string[]>;
using LocalizationEntryString = System.Collections.Generic.Dictionary<Locale, string>;

public static class SphObjectDb
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Encoding Win1251Encoding;

    private static readonly char[] TabCharacter = { '\t' };
    public static readonly Dictionary<int, SphGameObject> GameObjectDataDb = new();
    public static readonly Dictionary<GameObjectType, Dictionary<ItemSuffix, SphGameObject>> SuffixDataDb = new();
    private const string langSuffixEnglish = "_e";
    private const string langSuffixItalian = "_i";
    private const string langSuffixPortuguese = "_p";

    public static readonly Dictionary<string, LocalizationEntryArray> LocalisationContent = new();

    private static readonly Dictionary<string, string> AppSettings;

    public static readonly Dictionary<string, Dictionary<int, LocalizationEntryString>> ObjectNameToLocalizationMap =
        new();

    private static readonly Dictionary<string, GameObjectType> prefFiles = new()
    {
        ["ar_armoru"] = Pref_Castle,
        ["ar_armor"] = Pref_Chestplate,
        ["wp_arbalest1"] = Pref_Crossbow,
        ["ar_armorf"] = Pref_Quest,
        ["ar_ring"] = Pref_Ring,
        ["ar_armor2"] = Pref_Robe,
        ["ar_shield"] = Pref_Shield,
        ["ar_amulet"] = Pref_AmuletBracelet,
        ["wp_axe1"] = Pref_AxeSword,
        ["ar_belt"] = Pref_BeltBootsGlovesHelmetPants
    };

    static SphObjectDb()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Win1251Encoding = Encoding.GetEncoding(1251);

            var configPath = FindConfigPath("appsettings.json");
            var configDir = GetConfigDirectory(configPath);

            Dictionary<string, string> configDict;
            if (File.Exists(configPath))
            {
                using var configFile = File.OpenRead(configPath);
                using var configReader = new StreamReader(configFile);
                configDict = JsonSerializer.Deserialize<Dictionary<string, string>>(configReader.ReadToEnd(), JsonReadOptions)
                             ?? throw new InvalidOperationException($"{configPath}: expected object root");
            }
            else
            {
                // Standalone / tool usage: allow running without a repo-root appsettings.json.
                // We derive paths relative to the closest parent folder that has required data folders.
                configDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var effectiveBaseDir = File.Exists(configPath)
                ? configDir
                : FindClosestDataRoot(Environment.CurrentDirectory)
                  ?? FindClosestDataRoot(AppContext.BaseDirectory)
                  ?? configDir;

            AppSettings = EnsureDefaultsAndNormalizePaths(configDict, effectiveBaseDir);

            // Allow minimal config: derive paths from RepositoryPath when explicit keys are absent.
            if (AppSettings.TryGetValue("RepositoryPath", out var repoPath) && !string.IsNullOrWhiteSpace(repoPath))
            {
                if (!AppSettings.ContainsKey("DecodedGameDataPath"))
                {
                    AppSettings["DecodedGameDataPath"] = Path.Combine(repoPath, "Sphere.GameDataDecode");
                }

                if (!AppSettings.ContainsKey("PacketDefinitionPath"))
                {
                    AppSettings["PacketDefinitionPath"] = Path.Combine(repoPath, "Sphere.PacketDefinitions");
                }
            }

            var gameDataJsonFolder = AppSettings["GeneratedJsonOutputFolder"];
            var gameDataJsonPath = Path.Combine(gameDataJsonFolder, AppSettings["ObjectDataFileName"]);
            var localizationContentJsonPath = Path.Combine(gameDataJsonFolder, AppSettings["LocalizationContentFileName"]);
            var suffixDataJsonPath = Path.Combine(gameDataJsonFolder, AppSettings["SuffixDataFileName"]);
            var objectLocalizationJsonPath = Path.Combine(gameDataJsonFolder, AppSettings["ObjectLocalizationFileName"]);
            if (!Directory.Exists(gameDataJsonFolder)
                || !File.Exists(localizationContentJsonPath)
                || !File.Exists(objectLocalizationJsonPath)
                || !File.Exists(suffixDataJsonPath)
                || File.GetLastWriteTimeUtc(gameDataJsonPath) < DateTime.UtcNow.AddHours(-72)
                || File.GetLastWriteTimeUtc(localizationContentJsonPath) < DateTime.UtcNow.AddHours(-72)
                || File.GetLastWriteTimeUtc(suffixDataJsonPath) < DateTime.UtcNow.AddHours(-72)
                || File.GetLastWriteTimeUtc(objectLocalizationJsonPath) < DateTime.UtcNow.AddHours(-72))
            {
                // regenerate json every 3 days to be safe
                Directory.CreateDirectory(gameDataJsonFolder);
                Console.WriteLine("Loading game data and generating json");
                LoadGameObjects();
                LoadLocalisationData();
                GenerateGameObjectLocale();
                LoadGameObjectLocalization();

                using var gameDataFile = File.OpenWrite(gameDataJsonPath);
                using var gameDataWriter = new StreamWriter(gameDataFile, Win1251Encoding);
                var gameDataJson = JsonSerializer.Serialize(GameObjectDataDb, JsonOptions);
                gameDataWriter.Write(gameDataJson);

                using var localeContentFile = File.OpenWrite(localizationContentJsonPath);
                using var localeContentWriter = new StreamWriter(localeContentFile, Win1251Encoding);
                var localeContentJson = JsonSerializer.Serialize(LocalisationContent, JsonOptions);
                localeContentWriter.Write(localeContentJson);

                using var objectLocaleFile = File.OpenWrite(objectLocalizationJsonPath);
                using var objectLocaleWriter = new StreamWriter(objectLocaleFile, Win1251Encoding);
                var objectLocaleJson = JsonSerializer.Serialize(ObjectNameToLocalizationMap, JsonOptions);
                objectLocaleWriter.Write(objectLocaleJson);

                using var suffixFile = File.OpenWrite(suffixDataJsonPath);
                using var suffixWriter = new StreamWriter(suffixFile, Win1251Encoding);
                var suffixJson = JsonSerializer.Serialize(SuffixDataDb, JsonOptions);
                suffixWriter.Write(suffixJson);
            }

            else
            {
                Console.WriteLine("Loading game data from preexisting json");
                using var gameDataFile = File.OpenRead(gameDataJsonPath);
                using var gameDataReader = new StreamReader(gameDataFile, Win1251Encoding, detectEncodingFromByteOrderMarks: true);
                GameObjectDataDb =
                    JsonSerializer.Deserialize<Dictionary<int, SphGameObject>>(gameDataReader.ReadToEnd(), JsonOptions)
                    ?? throw new InvalidOperationException();

                using var localeContentFile = File.OpenRead(localizationContentJsonPath);
                using var localeContentReader = new StreamReader(localeContentFile, Win1251Encoding, detectEncodingFromByteOrderMarks: true);
                LocalisationContent =
                    JsonSerializer.Deserialize<Dictionary<string, LocalizationEntryArray>>(
                        localeContentReader.ReadToEnd(), JsonOptions)
                    ?? throw new InvalidOperationException();

                using var objectLocaleFile = File.OpenRead(objectLocalizationJsonPath);
                using var objectLocaleReader = new StreamReader(objectLocaleFile, Win1251Encoding, detectEncodingFromByteOrderMarks: true);
                ObjectNameToLocalizationMap =
                    JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, LocalizationEntryString>>>(
                        objectLocaleReader.ReadToEnd(), JsonOptions)
                    ?? throw new InvalidOperationException();

                using var suffixFile = File.OpenRead(suffixDataJsonPath);
                using var suffixReader = new StreamReader(suffixFile, Win1251Encoding, detectEncodingFromByteOrderMarks: true);
                SuffixDataDb =
                    JsonSerializer.Deserialize<Dictionary<GameObjectType, Dictionary<ItemSuffix, SphGameObject>>>(
                        suffixReader.ReadToEnd(), JsonOptions)
                    ?? throw new InvalidOperationException();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading game data: {ex.Message}");
            Console.WriteLine($"StackTrace: {ex.StackTrace}");
            Console.WriteLine($"InnerException: {ex.InnerException?.Message}");
            Console.WriteLine($"InnerException StackTrace: {ex.InnerException?.StackTrace}");
            throw;
        }
    }

    private static string GetConfigDirectory(string configPath)
    {
        // If configPath is relative (or missing), anchor relative settings to the executable folder.
        if (Path.IsPathRooted(configPath))
        {
            return Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
        }

        return AppContext.BaseDirectory;
    }

    private static Dictionary<string, string> EnsureDefaultsAndNormalizePaths(
        Dictionary<string, string> settings,
        string baseDir)
    {
        var s = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);

        // Defaults that make sense for standalone tools.
        s.TryAdd("GeneratedJsonOutputFolder", ".generated");
        s.TryAdd("DecodedParamsFolderName", "params");
        s.TryAdd("DecodedLocaleFolderName", "language");
        s.TryAdd("ObjectDataFileName", "objectData.json");
        s.TryAdd("SuffixDataFileName", "suffixData.json");
        s.TryAdd("LocalizationContentFileName", "localizationContent.json");
        s.TryAdd("ObjectLocalizationFileName", "objectLocalization.json");

        // If no repo path is provided, assume game data/definitions are next to the executable.
        s.TryAdd("DecodedGameDataPath", "Sphere.GameDataDecode");
        s.TryAdd("PacketDefinitionPath", "Sphere.PacketDefinitions");

        // Normalize any relative paths to be relative to baseDir.
        foreach (var key in new[] { "GeneratedJsonOutputFolder", "DecodedGameDataPath", "PacketDefinitionPath" })
        {
            if (!s.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!Path.IsPathRooted(value))
            {
                s[key] = Path.GetFullPath(Path.Combine(baseDir, value));
            }
        }

        return s;
    }

    private static string? FindClosestDataRoot(string? startDir)
    {
        if (string.IsNullOrWhiteSpace(startDir))
        {
            return null;
        }

        try
        {
            startDir = Path.GetFullPath(startDir);
        }
        catch
        {
            // ignore; best-effort normalization
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dir = new DirectoryInfo(startDir);
        while (dir is not null && visited.Add(dir.FullName))
        {
            // Prefer an explicit config if present.
            if (File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
            {
                return dir.FullName;
            }

            // Otherwise, locate the standalone data layout.
            if (Directory.Exists(Path.Combine(dir.FullName, "Sphere.GameDataDecode"))
                || Directory.Exists(Path.Combine(dir.FullName, "Sphere.PacketDefinitions")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string FindConfigPath(string fileName)
    {
        // When running this project directly, the working directory is often `.solutions/SphObjectDb/bin/...`.
        // We want a single source of truth for config, so we search upwards for the repo-root `appsettings.json`.
        foreach (var startDir in new[]
                 {
                     Environment.CurrentDirectory,
                     AppContext.BaseDirectory,
                 })
        {
            if (string.IsNullOrWhiteSpace(startDir))
            {
                continue;
            }

            // Track visited per start root. Sharing this between roots can cause a later
            // root (e.g. the repo working directory) to be skipped if the earlier root
            // walked over common parents (e.g. Godot temp bin).
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        // Last resort: rely on current directory (will throw with a clear message if missing).
        return fileName;
    }

    private static void LoadGameObjects()
    {
        var gameDataPath = Path.Combine(AppSettings["DecodedGameDataPath"], AppSettings["DecodedParamsFolderName"]);
        var objectFiles = Directory.EnumerateFiles(gameDataPath, "group*").ToList();
        foreach (var objFile in objectFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(objFile);
            var firstUnderscore = fileName.IndexOf('_');
            var objKindName = fileName[(firstUnderscore + 1)..];
            var contents = File.ReadAllLines(objFile);

            foreach (var entry in contents)
            {
                var entrySplit = entry.Split(TabCharacter, StringSplitOptions.None);

                if (!Enum.TryParse<KarmaTypes>(entrySplit[8], out var minKarma)
                    || !Enum.TryParse<KarmaTypes>(entrySplit[9], out var maxKarma))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entrySplit[0]))
                {
                    // TODO: later gator
                    continue;
                }

                var isPref = fileName.EndsWith("pref");

                var gameId = FileFormatCulture.ParseInt(entrySplit[0]);
                var range = FileFormatCulture.ParseInt(entrySplit[38]);
                var duration = FileFormatCulture.ParseInt(entrySplit[42]);
                var objKind = isPref ? GameObjectKind.Pref : GameObjectDataHelper.GetKindBySphereName(objKindName);
                var tier = objKind == GameObjectKind.Monster || string.IsNullOrWhiteSpace(entrySplit[49]) ||
                           entrySplit[49].Length < 4
                    ? -1
                    : FileFormatCulture.ParseInt(entrySplit[49].Substring(2, 2)) + 1;
                var gameObj = new SphGameObject
                {
                    ObjectKind = objKind,
                    GameId = gameId,
                    SphereType = entrySplit[1],
                    GameObjectType = GameObjectDataHelper.GetTypeBySphereName(entrySplit[1]),
                    ModelNameGround = entrySplit[2],
                    ModelNameInventory = entrySplit[3],
                    HpCost = FileFormatCulture.ParseInt(entrySplit[4]),
                    MpCost = FileFormatCulture.ParseInt(entrySplit[5]),
                    TitleMinusOne = FileFormatCulture.ParseInt(entrySplit[6]),
                    DegreeMinusOne = FileFormatCulture.ParseInt(entrySplit[7]),
                    MinKarmaLevel = minKarma,
                    MaxKarmaLevel = maxKarma,
                    StrengthReq = FileFormatCulture.ParseInt(entrySplit[10]),
                    AgilityReq = FileFormatCulture.ParseInt(entrySplit[11]),
                    AccuracyReq = FileFormatCulture.ParseInt(entrySplit[12]),
                    EnduranceReq = FileFormatCulture.ParseInt(entrySplit[13]),
                    EarthReq = FileFormatCulture.ParseInt(entrySplit[14]),
                    AirReq = FileFormatCulture.ParseInt(entrySplit[15]),
                    WaterReq = FileFormatCulture.ParseInt(entrySplit[16]),
                    FireReq = FileFormatCulture.ParseInt(entrySplit[17]),
                    PAtkNegative = FileFormatCulture.ParseInt(entrySplit[18]),
                    MAtkNegativeOrHeal = FileFormatCulture.ParseInt(entrySplit[19]),
                    MPHeal = FileFormatCulture.ParseInt(entrySplit[20]),
                    t1 = FileFormatCulture.ParseInt(entrySplit[21]),
                    MaxHpUp = FileFormatCulture.ParseInt(entrySplit[22]),
                    MaxMpUp = FileFormatCulture.ParseInt(entrySplit[23]),
                    PAtkUpNegative = FileFormatCulture.ParseInt(entrySplit[24]),
                    PDefUp = FileFormatCulture.ParseInt(entrySplit[25]),
                    MDefUp = FileFormatCulture.ParseInt(entrySplit[26]),
                    StrengthUp = FileFormatCulture.ParseInt(entrySplit[27]),
                    AgilityUp = FileFormatCulture.ParseInt(entrySplit[28]),
                    AccuracyUp = FileFormatCulture.ParseInt(entrySplit[29]),
                    EnduranceUp = FileFormatCulture.ParseInt(entrySplit[30]),
                    EarthUp = FileFormatCulture.ParseInt(entrySplit[31]),
                    AirUp = FileFormatCulture.ParseInt(entrySplit[32]),
                    WaterUp = FileFormatCulture.ParseInt(entrySplit[33]),
                    FireUp = FileFormatCulture.ParseInt(entrySplit[34]),
                    MAtkUpNegative = FileFormatCulture.ParseInt(entrySplit[35]),
                    Weight = FileFormatCulture.ParseInt(entrySplit[36]),
                    Durability = FileFormatCulture.ParseInt(entrySplit[37]),
                    _range = range,
                    UseTime = FileFormatCulture.ParseInt(entrySplit[39]),
                    VendorCost = FileFormatCulture.ParseInt(entrySplit[40]),
                    MutatorId = FileFormatCulture.ParseInt(entrySplit[41]),
                    _duration = duration,
                    ReuseDelayHours = FileFormatCulture.ParseInt(entrySplit[43]),
                    t2 = FileFormatCulture.ParseInt(entrySplit[44]),
                    t3 = FileFormatCulture.ParseInt(entrySplit[45]),
                    t4 = FileFormatCulture.ParseInt(entrySplit[46]),
                    t5 = FileFormatCulture.ParseInt(entrySplit[47]),
                    TierRaw = entrySplit[48],
                    SuffixSetName = entrySplit.Length > 50 ? entrySplit[50] : string.Empty,
                    Tier = tier,
                    Range = range > 100 ? range % 100 : range,
                    Radius = range > 100 ? range / 100 : 0,
                    Duration = duration > 100 ? (duration - 100) * 5 : duration * 300
                };

                if (GameObjectDataDb.ContainsKey(gameId))
                {
                    // 4251: special case, no longer an "old" robe, now it's an event amulet
                    if (gameId == 4251 && GameObjectDataDb[gameId].GameObjectType == Robe)
                    {
                        GameObjectDataDb.Remove(gameId);
                    }
                    else
                    {
                        Console.WriteLine($"Duplicate object: {gameObj.ToDebugString()}");

                        continue;
                    }
                }

                if (isPref)
                {
                    if (!SuffixDataDb.ContainsKey(gameObj.GameObjectType))
                    {
                        SuffixDataDb.Add(gameObj.GameObjectType, new Dictionary<ItemSuffix, SphGameObject>());
                    }

                    var suffix = SphObjectDbHelper.TypeToSuffixIdMap[gameObj.GameObjectType][gameObj.GameId];
                    gameObj.Suffix = suffix;
                    SuffixDataDb[gameObj.GameObjectType][gameObj.Suffix] = gameObj;
                }
                else
                {
                    GameObjectDataDb.Add(gameId, gameObj);
                }
            }
        }
    }

    private static void LoadLocalisationData()
    {
        var localeDataPath = Path.Combine(AppSettings["DecodedGameDataPath"], AppSettings["DecodedLocaleFolderName"]);
        var langFiles = Directory.EnumerateFiles(localeDataPath);
        var win1251 = Win1251Encoding;

        foreach (var localeFile in langFiles)
        {
            var name = Path.GetFileNameWithoutExtension(localeFile);
            var locale = Locale.Russian;
            var removeSuffix = false;

            if (name.EndsWith(langSuffixEnglish))
            {
                locale = Locale.English;
                removeSuffix = true;
            }
            else if (name.EndsWith(langSuffixItalian))
            {
                locale = Locale.Italian;
                removeSuffix = true;
            }
            else if (name.EndsWith(langSuffixPortuguese))
            {
                locale = Locale.Portuguese;
                removeSuffix = true;
            }

            if (removeSuffix)
            {
                name = name[..^2];
            }

            if (!LocalisationContent.ContainsKey(name))
            {
                var dict = new Dictionary<Locale, string[]>
                {
                    [locale] = File.ReadAllLines(localeFile, win1251)
                };
                LocalisationContent[name] = dict;
            }
            else
            {
                LocalisationContent[name][locale] = File.ReadAllLines(localeFile, win1251);
            }
        }
    }

    private static void GenerateGameObjectLocale()
    {
        foreach (var (name, localeEntry) in LocalisationContent)
        {
            if (!ObjectNameToLocalizationMap.ContainsKey(name))
            {
                ObjectNameToLocalizationMap.Add(name, new Dictionary<int, LocalizationEntryString>());
            }

            foreach (var (locale, localeContent) in localeEntry)
            {
                // has specific name for specific gameIds
                var gameIdsFound = false;

                for (var i = 0; i < localeContent.Length; i++)
                {
                    if (!localeContent[i].StartsWith('#'))
                    {
                        if (!prefFiles.ContainsKey(name))
                        {
                            continue;
                        }

                        var split = localeContent[i].Split(" ", StringSplitOptions.RemoveEmptyEntries);
                        if (split.Length == 0 || !FileFormatCulture.TryParseInt(split[0], out var strId) || strId < 200)
                        {
                            continue;
                        }

                        // prefs

                        var pref = prefFiles[name];
                        var prefName = Enum.GetName(pref);

                        if (!ObjectNameToLocalizationMap.ContainsKey(prefName))
                        {
                            ObjectNameToLocalizationMap.Add(prefName, new Dictionary<int, LocalizationEntryString>());
                        }

                        if (!ObjectNameToLocalizationMap[prefName].ContainsKey(strId - 200))
                        {
                            ObjectNameToLocalizationMap[prefName].Add(strId - 200, new LocalizationEntryString());
                        }

                        ObjectNameToLocalizationMap[prefName][strId - 200][locale] = localeContent[i][4..];

                        continue;
                    }

                    gameIdsFound = true;
                    var mantraPrefix = locale switch
                    {
                        Locale.Russian => "Мантра",
                        _ => "Mantra"
                    };

                    if (localeContent[i].Contains('-'))
                    {
                        // range of ints
                        var bounds = localeContent[i][1..].Split('-');
                        var start = FileFormatCulture.ParseInt(bounds[0]);
                        var end = FileFormatCulture.ParseInt(bounds[1]) + 1;

                        for (var j = start; j < end; j++)
                        {
                            if (!ObjectNameToLocalizationMap[name].ContainsKey(j))
                            {
                                ObjectNameToLocalizationMap[name].Add(j, new LocalizationEntryString());
                            }

                            if (!name.Contains("mantra"))
                            {
                                ObjectNameToLocalizationMap[name][j][locale] = localeContent[i + 1][3..];
                            }
                            else
                            {
                                if (i + 4 < localeContent.Length && localeContent[i + 4].StartsWith("10 "))
                                {
                                    ObjectNameToLocalizationMap[name][j][locale] =
                                        mantraPrefix + " " + localeContent[i + 4][3..];
                                }
                                else
                                {
                                    ObjectNameToLocalizationMap[name][j][locale] = localeContent[i + 1][3..];
                                }
                            }
                        }
                    }
                    else
                    {
                        var id = FileFormatCulture.ParseInt(localeContent[i][1..]);

                        if (!ObjectNameToLocalizationMap[name].ContainsKey(id))
                        {
                            ObjectNameToLocalizationMap[name].Add(id, new LocalizationEntryString());
                        }

                        if (!name.Contains("mantra"))
                        {
                            ObjectNameToLocalizationMap[name][id][locale] = localeContent[i + 1][3..];
                        }
                        else
                        {
                            if (i + 4 < localeContent.Length && localeContent[i + 4].StartsWith("10 "))
                            {
                                ObjectNameToLocalizationMap[name][id][locale] =
                                    mantraPrefix + " " + localeContent[i + 4][3..];
                            }
                            else
                            {
                                ObjectNameToLocalizationMap[name][id][locale] = localeContent[i + 1][3..];
                            }
                        }
                    }
                }

                if (gameIdsFound)
                {
                    continue;
                }

                if (!ObjectNameToLocalizationMap[name].ContainsKey(-1))
                {
                    ObjectNameToLocalizationMap[name].Add(-1, new LocalizationEntryString());
                }

                ObjectNameToLocalizationMap[name][-1][locale] = localeContent[0][3..];
            }
        }
    }

    private static void LoadGameObjectLocalization()
    {
        foreach (var (gameId, gameObject) in GameObjectDataDb)
        {
            // this shouldn't happen
            if (!ObjectNameToLocalizationMap.ContainsKey(gameObject.SphereType))
            {
                Console.WriteLine($"ERROR: Missing localization for {gameObject.SphereType}");
            }

            var localizationData = ObjectNameToLocalizationMap[gameObject.SphereType];

            if (localizationData.ContainsKey(gameId))
            {
                gameObject.Localisation = localizationData[gameId];
            }
            else if (localizationData.ContainsKey(-1))
            {
                gameObject.Localisation = localizationData[-1];
            }
            else
            {
                // this shouldn't happen
                Console.WriteLine($"ERROR: Missing localization for {gameObject.SphereType} GID: {gameId}");
            }
        }

        foreach (var (objectType, suffixData) in SuffixDataDb)
        {
            foreach (var (_, gameObject) in suffixData)
            {
                var localeEntries = ObjectNameToLocalizationMap[Enum.GetName(objectType)][gameObject.GameId];
                foreach (var (locale, text) in localeEntries)
                {
                    gameObject.Localisation[locale] = text;
                }
            }
        }
    }
}