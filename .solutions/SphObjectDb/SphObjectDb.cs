using System.Text;
using Newtonsoft.Json;
using static GameObjectType;
using LocalizationEntryArray = System.Collections.Generic.Dictionary<Locale, string[]>;
using LocalizationEntryString = System.Collections.Generic.Dictionary<Locale, string>;

public static class SphObjectDb
{
    private static readonly char[] TabCharacter = { '\t' };
    public static readonly Dictionary<int, SphGameObject> GameObjectDataDb = new ();
    public static readonly Dictionary<GameObjectType, Dictionary<ItemSuffix, SphGameObject>> SuffixDataDb = new ();
    private const string langSuffixEnglish = "_e";
    private const string langSuffixItalian = "_i";
    private const string langSuffixPortuguese = "_p";

    public static readonly Dictionary<string, LocalizationEntryArray> LocalisationContent = new ();

    private static readonly Dictionary<string, string> AppSettings;

    public static readonly Dictionary<string, Dictionary<int, LocalizationEntryString>> ObjectNameToLocalizationMap =
        new ();

    private static readonly Dictionary<string, GameObjectType> prefFiles = new ()
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

    static SphObjectDb ()
    {
        using var configFile = File.OpenRead("sphdbsettings.json");
        using var configReader = new StreamReader(configFile);
        AppSettings = JsonConvert.DeserializeObject<Dictionary<string, string>>(configReader.ReadToEnd());

        var gameDataJsonFolder = AppSettings["GeneratedJsonOutputFolder"];
        var gameDataJsonPath = Path.Combine(gameDataJsonFolder, AppSettings["ObjectDataFileName"]);
        var localizationContentJsonPath = Path.Combine(gameDataJsonFolder, AppSettings["LocalizationContentFileName"]);
        var suffixDataJsonPath = Path.Combine(gameDataJsonFolder, AppSettings["SuffixDataFileName"]);
        var objectLocalizationJsonPath = Path.Combine(gameDataJsonFolder, AppSettings["ObjectLocalizationFileName"]);
        if (!File.Exists(gameDataJsonFolder)
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
            using var gameDataWriter = new StreamWriter(gameDataFile);
            var gameDataJson = JsonConvert.SerializeObject(GameObjectDataDb, Formatting.Indented);
            gameDataWriter.Write(gameDataJson);

            using var localeContentFile = File.OpenWrite(localizationContentJsonPath);
            using var localeContentWriter = new StreamWriter(localeContentFile);
            var localeContentJson = JsonConvert.SerializeObject(LocalisationContent, Formatting.Indented);
            localeContentWriter.Write(localeContentJson);

            using var objectLocaleFile = File.OpenWrite(objectLocalizationJsonPath);
            using var objectLocaleWriter = new StreamWriter(objectLocaleFile);
            var objectLocaleJson = JsonConvert.SerializeObject(ObjectNameToLocalizationMap, Formatting.Indented);
            objectLocaleWriter.Write(objectLocaleJson);

            using var suffixFile = File.OpenWrite(suffixDataJsonPath);
            using var suffixWriter = new StreamWriter(suffixFile);
            var suffixJson = JsonConvert.SerializeObject(SuffixDataDb, Formatting.Indented);
            suffixWriter.Write(suffixJson);
        }

        else
        {
            Console.WriteLine("Loading game data from preexisting json");
            using var gameDataFile = File.OpenRead(gameDataJsonPath);
            using var gameDataReader = new StreamReader(gameDataFile);
            GameObjectDataDb =
                JsonConvert.DeserializeObject<Dictionary<int, SphGameObject>>(gameDataReader.ReadToEnd()) ??
                throw new InvalidOperationException();

            using var localeContentFile = File.OpenRead(localizationContentJsonPath);
            using var localeContentReader = new StreamReader(localeContentFile);
            LocalisationContent =
                JsonConvert.DeserializeObject<Dictionary<string, LocalizationEntryArray>>(
                    localeContentReader.ReadToEnd()) ?? throw new InvalidOperationException();

            using var objectLocaleFile = File.OpenRead(objectLocalizationJsonPath);
            using var objectLocaleReader = new StreamReader(objectLocaleFile);
            ObjectNameToLocalizationMap =
                JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, LocalizationEntryString>>>(
                    objectLocaleReader.ReadToEnd()) ?? throw new InvalidOperationException();

            using var suffixFile = File.OpenRead(suffixDataJsonPath);
            using var suffixReader = new StreamReader(suffixFile);
            SuffixDataDb =
                JsonConvert.DeserializeObject<Dictionary<GameObjectType, Dictionary<ItemSuffix, SphGameObject>>>(
                    suffixReader.ReadToEnd()) ?? throw new InvalidOperationException();
        }
    }

    private static void LoadGameObjects ()
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

                var gameId = int.Parse(entrySplit[0]);
                var range = int.Parse(entrySplit[38]);
                var duration = int.Parse(entrySplit[42]);
                var objKind = isPref ? GameObjectKind.Pref : GameObjectDataHelper.GetKindBySphereName(objKindName);
                var tier = objKind == GameObjectKind.Monster || string.IsNullOrWhiteSpace(entrySplit[49]) ||
                           entrySplit[49].Length < 4
                    ? -1
                    : int.Parse(entrySplit[49].Substring(2, 2)) + 1;
                var gameObj = new SphGameObject
                {
                    ObjectKind = objKind,
                    GameId = gameId,
                    SphereType = entrySplit[1],
                    GameObjectType = GameObjectDataHelper.GetTypeBySphereName(entrySplit[1]),
                    ModelNameGround = entrySplit[2],
                    ModelNameInventory = entrySplit[3],
                    HpCost = int.Parse(entrySplit[4]),
                    MpCost = int.Parse(entrySplit[5]),
                    TitleMinusOne = int.Parse(entrySplit[6]),
                    DegreeMinusOne = int.Parse(entrySplit[7]),
                    MinKarmaLevel = minKarma,
                    MaxKarmaLevel = maxKarma,
                    StrengthReq = int.Parse(entrySplit[10]),
                    AgilityReq = int.Parse(entrySplit[11]),
                    AccuracyReq = int.Parse(entrySplit[12]),
                    EnduranceReq = int.Parse(entrySplit[13]),
                    EarthReq = int.Parse(entrySplit[14]),
                    AirReq = int.Parse(entrySplit[15]),
                    WaterReq = int.Parse(entrySplit[16]),
                    FireReq = int.Parse(entrySplit[17]),
                    PAtkNegative = int.Parse(entrySplit[18]),
                    MAtkNegativeOrHeal = int.Parse(entrySplit[19]),
                    MPHeal = int.Parse(entrySplit[20]),
                    t1 = int.Parse(entrySplit[21]),
                    MaxHpUp = int.Parse(entrySplit[22]),
                    MaxMpUp = int.Parse(entrySplit[23]),
                    PAtkUpNegative = int.Parse(entrySplit[24]),
                    PDefUp = int.Parse(entrySplit[25]),
                    MDefUp = int.Parse(entrySplit[26]),
                    StrengthUp = int.Parse(entrySplit[27]),
                    AgilityUp = int.Parse(entrySplit[28]),
                    AccuracyUp = int.Parse(entrySplit[29]),
                    EnduranceUp = int.Parse(entrySplit[30]),
                    EarthUp = int.Parse(entrySplit[31]),
                    AirUp = int.Parse(entrySplit[32]),
                    WaterUp = int.Parse(entrySplit[33]),
                    FireUp = int.Parse(entrySplit[34]),
                    MAtkUpNegative = int.Parse(entrySplit[35]),
                    Weight = int.Parse(entrySplit[36]),
                    Durability = int.Parse(entrySplit[37]),
                    _range = range,
                    UseTime = int.Parse(entrySplit[39]),
                    VendorCost = int.Parse(entrySplit[40]),
                    MutatorId = int.Parse(entrySplit[41]),
                    _duration = duration,
                    ReuseDelayHours = int.Parse(entrySplit[43]),
                    t2 = int.Parse(entrySplit[44]),
                    t3 = int.Parse(entrySplit[45]),
                    t4 = int.Parse(entrySplit[46]),
                    t5 = int.Parse(entrySplit[47]),
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

    private static void LoadLocalisationData ()
    {
        var localeDataPath = Path.Combine(AppSettings["DecodedGameDataPath"], AppSettings["DecodedLocaleFolderName"]);
        var langFiles = Directory.EnumerateFiles(localeDataPath);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var Win1251 = Encoding.GetEncoding(1251);

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
                    [locale] = File.ReadAllLines(localeFile, Win1251)
                };
                LocalisationContent[name] = dict;
            }
            else
            {
                LocalisationContent[name][locale] = File.ReadAllLines(localeFile, Win1251);
            }
        }
    }

    private static void GenerateGameObjectLocale ()
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
                        if (split.Length == 0 || !int.TryParse(split[0], out var strId) || strId < 200)
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
                        var start = int.Parse(bounds[0]);
                        var end = int.Parse(bounds[1]) + 1;

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
                        var id = int.Parse(localeContent[i][1..]);

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

    private static void LoadGameObjectLocalization ()
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