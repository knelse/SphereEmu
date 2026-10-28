using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Helpers.Networking;
using static KarmaTypes;
using static SphServer.Helpers.Continents;
using static SphServer.Helpers.Cities;
using static SphServer.Helpers.Castles;
using static SphServer.Helpers.PoiType;

// ReSharper disable RedundantArgumentDefaultValue

namespace SphServer.Helpers;

public enum Continents
{
    Гиперион,
    Харон,
    Феб,
    Родос
}

public enum Cities
{
    // Hyperion
    Шипстоун,
    Бангвиль,
    Торвил,
    Санпул,

    // Haron
    Номрад,
    Гифес,

    // Phoebe
    Умрад,

    // Rodos
    Анхельм
}

public enum Castles
{
    // Hyperion
    Льеж = 0,
    Фьеф = 1,
    Арис = 2,
    Латор = 3,
    Эйкум_Кас = 4,
    Гедеон = 5,
    Шателье = 6,
    Туанод = 7,
    Пельтье = 8,
    Каре_Рояль = 9,
    Блессендор = 10,
    Терноваль = 11,
    Аммалаэль = 12,
    Каблак = 13,
    Дэванагари = 14,
    Сабулат = 15,
    Деффенсат = 16,
    Айонат = 17,
    Триумфалер = 18,

    // Haron
    Багарнак = 19,
    Кабрад = 20,
    Сет = 21,
    Лендер = 22,
    Келес = 23,
    Шепростан = 24,
    Оркобьен = 25,
    Кануак = 26,
    Алдарнон = 27,
    Йонг = 28,

    // Phoebe
    Элек = 29,
    Гавот = 30,
    Кандур = 31,

    // Rodos
    Иммертель = 32,
    Нарцисс = 33,
    Ранден = 34,
    Ниргун = 35,
    Гелгивинн = 36,
    Иль_Суильи_Руа = 37,

    // Etc
    Черная_Башня = 999,
    UNKNOWN = 12345
}

public enum PoiType
{
    SharedDungeon,
    Cemetery,
    CityCenter,
    RespawnPoint,
    TeleportPoint,
    CastleTeleportPointOwner,
    CastleTeleportPointEnemy,
    CastleInnerArea,
    CastleDungeon,
    Other,
    Event
}

// NB: commented coords are mostly door exits for doors with keys and such

public static class SavedCoords
{
    public static readonly Dictionary<Continents, Dictionary<Cities, Dictionary<KarmaTypes, List<WorldCoords>>>>
        RespawnPoints =
            new ()
            {
                [Гиперион] = new Dictionary<Cities, Dictionary<KarmaTypes, List<WorldCoords>>>
                {
                    [Шипстоун] = new ()
                    {
                        [Очень_Плохая] = [new WorldCoords (1195.01, 159.49, -2194.56, 0.785)],
                        [Плохая] = [new WorldCoords (2283.88, 159.39, 1215.43, 0.636)],
                        [Нейтральная] =
                        [
                            new WorldCoords (2598.04, 159.85, 1395.37, -3.947),

                        ],
                        [Хорошая] = [new WorldCoords (2517.25, 159.71, 1282.07, -0.857)]
                    },
                    [Бангвиль] = new ()
                    {
                        [Очень_Плохая] = [new WorldCoords (1195.01, 159.49, -2194.56, 0.785)],
                        [Плохая] = [new WorldCoords (1965.45, 159.84, -297.69, 0.755)],
                        [Нейтральная] =
                        [
                            new WorldCoords (1821.50, 159.40, -414.93, -2.277),
                        ],
                        [Хорошая] = [new WorldCoords (1909.46, 159.18, -476.22, -0.625)]
                    },
                    [Санпул] = new ()
                    {
                        [Очень_Плохая] = [new WorldCoords (1195.01, 159.49, -2194.56, 0.785)],
                        [Плохая] = [new WorldCoords (493.12, 159.85, -1673.47, 0.803)],
                        [Нейтральная] =
                        [
                            new WorldCoords (492.29, 159.79, -1335.35, 0.774),
                        ],
                        [Хорошая] = [new WorldCoords (482.58, 159.81, -1228.56, -3.981)]
                    },
                    [Торвил] = new ()
                    {
                        [Очень_Плохая] = [new WorldCoords (1195.01, 159.49, -2194.56, 0.785)],
                        [Плохая] = [new WorldCoords (2489.00, 159.68, -2182.09, -0.662)],
                        [Нейтральная] =
                        [
                            new WorldCoords (2376.74, 158.48, -2368.31, -3.929)
                        ],
                        [Хорошая] = [new WorldCoords (2277.15, 157.36, -2346.60, -2.124)]
                    }
                },
                [Харон] = new Dictionary<Cities, Dictionary<KarmaTypes, List<WorldCoords>>>
                {
                    [Номрад] = new ()
                    {
                        [Очень_Плохая] = [
                            new WorldCoords (-3304.70, 404.35, 2401.58, 0.651),
                            new WorldCoords (-1072.65, 409.73, 2198.79, -0.759)],
                        [Плохая] = [new WorldCoords (-2713.01, 397.11, 1866.17, -0.160)],
                        [Нейтральная] =
                        [
                            new WorldCoords (-2623.26, 405.90, 2131.69, -3.929),
                        ],
                        [Хорошая] = [new WorldCoords (-2722.85, 404.78, 2153.40, -2.124)]
                    },
                    [Гифес] = new ()
                    {
                        [Очень_Плохая] = [
                            new WorldCoords (-248.02, 403.87, 2641.62, -0.123),
                            new WorldCoords (2658.00, 405.34, 2650.55, -3.897),
                        ],
                        [Плохая] = [new WorldCoords (1803.03, 404.75, 3091.08, -0.391)],
                        [Нейтральная] =
                        [
                            new WorldCoords (1821.50, 406.60, 3285.07, -2.277),

                        ],
                        [Хорошая] = [new WorldCoords (1909.46, 405.93, 3223.78, -0.625)]
                    }
                },
                [Феб] = new Dictionary<Cities, Dictionary<KarmaTypes, List<WorldCoords>>>
                {
                    [Умрад] = new ()
                    {
                        [Очень_Плохая] = [
                            new WorldCoords (-1910.33, -91.14, -295.29, 0.870),
                            new WorldCoords (-1180.07, -90.69, 612.58, 0.792),
                        ],
                        [Плохая] = [new WorldCoords (-2461.29, -95.03, 505.36, -2.266),],
                        [Нейтральная] =
                        [
                            new WorldCoords (-2132.77, -91.91, 551.01, -2.437),
                        ],
                        [Хорошая] = [new WorldCoords (-2034.10, -92.54, 375.97, -0.450)]
                    }
                },
                [Родос] = new Dictionary<Cities, Dictionary<KarmaTypes, List<WorldCoords>>>
                {
                    [Анхельм] = new ()
                    {
                        [Очень_Плохая] = [
                            new WorldCoords (-3561.77, -340.58, 1190.51, -2.764),
                            new WorldCoords (-3403.40, -346.02, -2460.19, -1.481)
                        ],
                        // [Нейтральная] =
                        // [
                        //     new WorldCoords (-3090.62, -341.24, 371.74, 3.142),
                        //     new WorldCoords (-2897.47, -345.29, -1280.03, 3.142),
                        //     new WorldCoords (-2689.46, -351.00, -1641.84, 1.558),
                        // ]
                    }
                }
            };

    public static readonly Dictionary<Continents, Dictionary<PoiType, Dictionary<string, WorldCoords>>> TeleportPoints =
        new ()
        {
            [Гиперион] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [SharedDungeon] = new ()
                {
                    ["Остров_Выбора"] = new WorldCoords (1300.33, 1499.32, 35.94, 5.87), // !
                    ["Латор_Каблак"] = new WorldCoords (-300.07, 1500.45, 106.58, 3.142),
                    ["Аммалаэль"] = new WorldCoords (33.65, 1500.51, 145.05, 4.712),
                    ["Фьеф_Респ_Меди"] = new WorldCoords (-99.99, 1500.50, 33.66, 0.000),
                    ["Санпул_Карма_Север"] = new WorldCoords (-2105.25, 1498.33, 100.06, 4.712),
                    ["Бангвиль"] = new WorldCoords (-1500.03, 1499.67, 105.85, 3.142),
                    ["Каре_Рояль_Север"] = new WorldCoords (-1293.73, 1499.57, 100.02, 1.571),
                    ["Каре_Рояль_Юг"] = new WorldCoords (-1061.08, 1499.53, 100.08, 4.712),
                    ["Хортонский_Лес"] = new WorldCoords (-1894.67, 1498.36, 99.83, 1.571),
                    ["Санпул_Лес"] = new WorldCoords (233.76, 1500.43, 54.87, 4.712),
                    ["Горы_Дорога_Санпул_Торвил"] = new WorldCoords (-1735.26, 1498.33, 100.16, 4.712),
                    ["Торвил_Кладб"] = new WorldCoords (433.53, 1500.45, 100.01, 4.712),
                    ["Шипстоун_Север"] = new WorldCoords (-2499.72, 1498.35, 105.21, 3.142),
                    ["Блессендор"] = new WorldCoords (-706.14, 1499.56, 100.01, 4.712)
                },
                [Cemetery] = new ()
                {
                    [nameof (Шипстоун)] = new WorldCoords (-3900.14, 1500.53, 106.71, 3.142),
                    [nameof (Бангвиль)] = new WorldCoords (-3753.69, 1509.56, 99.90, 1.571),
                    [nameof (Торвил)] = new WorldCoords (-3054.96, 1499.56, 106.30, 3.142),
                    [nameof (Санпул)] = new WorldCoords (-2900.06, 1499.51, 106.34, 3.142)
                },
                [CityCenter] = new ()
                {
                    [nameof (Шипстоун)] = new WorldCoords (2609.36, 157.39, 1291.10, 0.498),
                    // ["Шипстоун_4439"] = new WorldCoords (2600.87, 157.48, 1299.54, -4.539),
                    // ["Шипстоун_6003"] = new WorldCoords (2621.57, 154.23, 1306.82, 4.712),
                    // ["Шипстоун_6002"] = new WorldCoords (2621.86, 154.25, 1310.68, 4.712),
                    // ["Шипстоун_7401"] = new WorldCoords (2602.49, 156.88, 1310.99, -2.519),
                    [nameof (Бангвиль)] = new WorldCoords (1872.20, 155.37, -406.19, -1.734),
                    // ["Бангвиль_4440"] = new WorldCoords (1886.45, 155.67, -400.88, -0.015),
                    // ["Бангвиль_7400"] = new WorldCoords (1886.95, 155.46, -388.92, -3.382),
                    // ["Бангвиль_3000"] = new WorldCoords (1881.80, 155.36, -383.37, -3.010),
                    [nameof (Торвил)] = new WorldCoords (2290.30, 155.29, -2388.89, -0.889),
                    // ["Торвил_4442"] = new WorldCoords (2308.16, 155.26, -2387.49, 1.071),
                    // ["Торвил_7402"] = new WorldCoords (2301.90, 155.29, -2403.86, -4.311),
                    [nameof (Санпул)] = new WorldCoords (427.74, 153.20, -1278.08, -4.144),
                    // ["Санпул_3034"] = new WorldCoords (432.46, 157.31, -1261.38, -2.340),
                    // ["Санпул_6013"] = new WorldCoords (408.13, 150.14, -1277.16, 0.714),
                    // ["Санпул_6012"] = new WorldCoords (410.82, 150.17, -1274.23, 0.975),
                    // ["Санпул_6011"] = new WorldCoords (413.62, 150.14, -1271.68, 0.945),
                    // ["Санпул_6014"] = new WorldCoords (405.24, 150.19, -1279.82, 0.863),
                    // ["Санпул_7403"] = new WorldCoords (422.41, 153.37, -1295.34, -4.275),
                },
                [TeleportPoint] = new ()
                {
                    ["Северная_Дорога"] = new WorldCoords (3033.55, 159.74, 2095.32, 0.000),
                    ["Северо_Западный_Край"] = new WorldCoords (502.63, 159.76, 1132.38, 4.712),
                    ["Дорога_Шипстоун_Санпул"] = new WorldCoords (1902.18, 159.76, 1161.43, 4.712),
                    ["Дорога_Шипстоун_Бангвиль"] = new WorldCoords (2109.28, 159.70, 933.39, 0.000),
                    ["Северо_Запад"] = new WorldCoords (3785.69, 159.60, 918.86, 0.000),
                    ["Умрадский_Лес"] = new WorldCoords (2256.25, 159.64, 614.49, 3.142),
                    ["Серебряный_Лес"] = new WorldCoords (948.95, 159.73, 504.55, 3.142),
                    ["Дорога_Шипстоун_Торвил"] = new WorldCoords (2908.61, 159.78, 544.81, 4.712),
                    ["Горы_Шипстоун_Бангвиль"] = new WorldCoords (2588.46, 159.79, 364.32, 4.712),
                    ["Озеро_Темер"] = new WorldCoords (3726.80, 158.90, -57.16, 1.261),
                    ["Остров_Гебер_Озеро_Темер"] = new WorldCoords (3371.89, 160.98, -374.27, 0.000),
                    ["Дорога_Санпул_Шипстоун_Озеро_Вортекс"] = new WorldCoords (316.78, 159.58, -285.09, 3.142),
                    ["Дорога_Санпул_Бангвиль_Озеро_Вортекс"] = new WorldCoords (1303.69, 159.76, -355.68, 0.000),
                    ["Остров_Форос_Озеро_Вортекс"] = new WorldCoords (751.37, 160.54, -653.57, 0.000),
                    ["Остров_Дейрос_Озеро_Вортекс"] = new WorldCoords (1368.84, 160.08, -1525.45, 0.000),
                    ["Устье_Реки_Нерей"] = new WorldCoords (1608.02, 159.72, -692.39, 1.571),
                    ["Восточный_Лес"] = new WorldCoords (3652.38, 163.92, -1533.03, 0.000),
                    ["Устье_Реки_Диомы"] = new WorldCoords (2227.98, 159.54, -1684.39, 3.142),
                    ["Хортонский_Лес"] = new WorldCoords (300.81, 159.70, -1945.26, -0.848),
                    ["Горы_Дорога_Санпул_Торвил"] = new WorldCoords (828.03, 157.23, -2729.79, 1.571),
                    ["Остров_Патрос_Озеро_Атласное"] = new WorldCoords (1556.19, 161.77, -2825.39, 1.571),
                    ["Юго_Восточный_Горный_Карман"] = new WorldCoords (2711.39, 159.58, -2860.90, 0.000),
                    ["Койтонский_Лес_Север"] = new WorldCoords (446.02, 159.71, -3308.64, 0.000),
                    ["Юго_Западный_Край"] = new WorldCoords (147.29, 158.18, -3737.08, 0.000),
                    ["Мост_Тантал"] = new WorldCoords (1197.46, 159.87, 1315.20, 0.000),
                    ["Обол"] = new WorldCoords (1144.43, 157.90, 1632.75, -0.770),
                    ["Обратно_С_Харона"] = new WorldCoords (1141.94, 157.43, 1630.66, -1.531)
                },
                [CastleTeleportPointOwner] = new ()
                {
                    [nameof (Льеж)] = new WorldCoords (1853, 157.6, 37.87, 0),
                    [nameof (Фьеф)] = new WorldCoords (2652, 157.9, -166, 0),
                    [nameof (Арис)] = new WorldCoords (3040.17, 158, 1048.2, 0),
                    [nameof (Латор)] = new WorldCoords (1955.33, 157.83, 1440.44, 0),
                    // ["Латор_1488"] = new WorldCoords (1941.13, 146.12, 1427.16, -2.336),
                    [nameof (Эйкум_Кас)] = new WorldCoords (1339.54, 158, 358.9, 0),
                    [nameof (Гедеон)] = new WorldCoords (456.12, 157.88, -2157.8, 0),
                    [nameof (Шателье)] = new WorldCoords (946.4, 157.42, -1251, 0),
                    [nameof (Туанод)] = new WorldCoords (3457.45, 157.43, 2453.48, 0),
                    [nameof (Пельтье)] = new WorldCoords (2130.39, 158.12, -857.56, 0),
                    [nameof (Каре_Рояль)] = new WorldCoords (3473.75, 158, -1250.21, 0),
                    [nameof (Блессендор)] = new WorldCoords (2044.53, 157.47, -3443.86, 0),
                    [nameof (Терноваль)] = new WorldCoords (3748.72, 157.56, 752.58, 0),
                    [nameof (Аммалаэль)] = new WorldCoords (543.89, 157.43, 455.16, 0),
                    // [nameof (Каблак)] = new WorldCoords(1588, 159.6, 950, -4.66),
                    [nameof (Дэванагари)] = new WorldCoords (2451.88, 157.95, -1648.78, 0),
                    //[nameof (Сабулат)] = new WorldCoords(1148, 159.9, -1792, 0),
                    [nameof (Деффенсат)] = new WorldCoords (354.8, 157.36, -3637.70, 0),
                    [nameof (Айонат)] = new WorldCoords (3361.56, 157.38, -2342.95, 0),
                    [nameof (Триумфалер)] = new WorldCoords (1557.58, 158, -2358.67, 0),
                },
                [CastleTeleportPointEnemy] = new ()
                {
                    [nameof (Льеж)] = new WorldCoords (1894, 159.9, 47, 1.51),
                    [nameof (Фьеф)] = new WorldCoords (2641, 159.9, -192, 0.1),
                    // ["Фьеф_1901"] = new WorldCoords (2641.82, 159.79, -192.72, -0.104),
                    [nameof (Арис)] = new WorldCoords (3016, 158.5, 1054, -1.51),
                    [nameof (Латор)] = new WorldCoords (1994, 159.9, 1445, -4.7),
                    [nameof (Эйкум_Кас)] = new WorldCoords (1333, 158.7, 382, -2.76),
                    [nameof (Гедеон)] = new WorldCoords (446, 159.9, -2193, 0.2),
                    [nameof (Шателье)] = new WorldCoords (914, 159.6, -1273, -1.28),
                    // ["Шателье_1906"] = new WorldCoords (914.59, 159.53, -1273.83, -1.302),
                    [nameof (Туанод)] = new WorldCoords (3445, 159.9, 2405, 0),
                    [nameof (Пельтье)] = new WorldCoords (2094, 159.9, -841, -1.57),
                    [nameof (Каре_Рояль)] = new WorldCoords (3500, 159.9, -1285, 0),
                    [nameof (Блессендор)] = new WorldCoords (2014, 159, -3463, -1.27),
                    [nameof (Терноваль)] = new WorldCoords (3775, 159.9, 791, -3.71),
                    [nameof (Аммалаэль)] = new WorldCoords (533, 159, 485, -2.6),
                    [nameof (Каблак)] = new WorldCoords (1588, 159.6, 950, -4.66),
                    [nameof (Дэванагари)] = new WorldCoords (2420, 159, -1683, 0),
                    [nameof (Сабулат)] = new WorldCoords (1148.88, 159.82, -1792.69, 0),
                    [nameof (Деффенсат)] = new WorldCoords (334, 159, -3609, -2.6),
                    [nameof (Айонат)] = new WorldCoords (3395, 159.9, -2349, 1.54),
                    [nameof (Триумфалер)] = new WorldCoords (1548, 159.6, -2388, 0),
                    // ["Триумфалер_1918"] = new WorldCoords (1548.71, 159.45, -2388.89, 0.000),
                },
                [CastleInnerArea] = new ()
                {
                    [nameof (Льеж)] = new WorldCoords (-3302.64, 1099.65, -3898.71, 4.71),
                    // ["Льеж_6401"] = new WorldCoords (-3294.98, 1095.82, -3893.97, -4.059),
                    // ["Льеж_6400"] = new WorldCoords (-3296.34, 1095.83, -3892.23, -3.959),
                    [nameof (Фьеф)] = new WorldCoords (-2697, 1095, -3907.45, 0),
                    // ["Фьеф_1147"] = new WorldCoords (-2693.73, 1102.88, -3925.80, 0.000),
                    [nameof (Арис)] = new WorldCoords (-2489.678, 1099.12, -3897, 1.57),
                    [nameof (Латор)] = new WorldCoords (-2300, 1096.6, -3910.1, 0),
                    [nameof (Эйкум_Кас)] = new WorldCoords (-2100, 1096.63, -3889.87, 3.14),
                    // ["Эйкум_Кас_6406"] = new WorldCoords (-2118.75, 1098.47, -3890.85, 3.142),
                    [nameof (Гедеон)] = new WorldCoords (-3899.59, 1098.42, 1092.85, 0),
                    // ["Гедеон_6409"] = new WorldCoords (-3894.84, 1095.89, 1106.06, -3.936),
                    // ["Гедеон_6408"] = new WorldCoords (-3896.40, 1095.86, 1108.15, -3.876),
                    // ["Гедеон_6407"] = new WorldCoords (-3898.86, 1095.90, 1108.60, 3.142),
                    [nameof (Шателье)] = new WorldCoords (-3707, 1095, 1097, 4.71),
                    // ["Шателье_1220"] = new WorldCoords (-3701.55, 1090.64, 1104.25, -3.000),
                    [nameof (Туанод)] = new WorldCoords (-3497, 1099, 1098, 1.57),
                    [nameof (Пельтье)] = new WorldCoords (-3310.1, 1096.64, 1100, 4.71),
                    [nameof (Каре_Рояль)] = new WorldCoords (-3097.78, 1095.82, 1091.78, 1.57),
                    [nameof (Блессендор)] = new WorldCoords (-2907.5, 1095.72, 1096.97, 4.71),
                    [nameof (Терноваль)] = new WorldCoords (-2693, 1096, 1108, 3.14),
                    [nameof (Аммалаэль)] = new WorldCoords (-2508, 1099, 1104, -2),
                    // ["Аммалаэль_6428"] = new WorldCoords (-2494.04, 1096.77, 1099.80, -4.129),
                    // ["Аммалаэль_6429"] = new WorldCoords (-2493.09, 1096.77, 1094.69, 1.477),
                    // ["Аммалаэль_6430"] = new WorldCoords (-2494.92, 1096.77, 1092.00, 0.000),
                    [nameof (Каблак)] = new WorldCoords (-2306, 1096, 1108.17, 3.14),
                    [nameof (Дэванагари)] = new WorldCoords (-2099, 1090.74, 1104.25, 3.14),
                    [nameof (Сабулат)] = new WorldCoords (-1892.55, 1096.10, 1084.39, 0),
                    [nameof (Деффенсат)] = new WorldCoords (-1705.3, 1100.85, 1108.72, 3.14),
                    // ["Деффенсат_6442"] = new WorldCoords (-1705.30, 1095.79, 1094.17, -0.785),
                    // ["Деффенсат_6441"] = new WorldCoords (-1703.72, 1095.79, 1092.09, -0.584),
                    // ["Деффенсат_6440"] = new WorldCoords (-1702.08, 1095.79, 1091.24, 0.134),
                    [nameof (Айонат)] = new WorldCoords (-1485.61, 1098.36, 1092.55, 1.57),
                    // ["Айонат_6445"] = new WorldCoords (-1483.82, 1098.32, 1104.41, 1.571),
                    // ["Айонат_6444"] = new WorldCoords (-1486.16, 1098.36, 1112.17, -4.107),
                    [nameof (Триумфалер)] = new WorldCoords (-1304, 1091, 1094, 0)
                },
                [CastleDungeon] = new ()
                {
                    [nameof (Льеж)] = new WorldCoords (104.08, 1699.07, -3499.93, 1.571),
                    [nameof (Фьеф)] = new WorldCoords (900.07, 1701.47, -3498.30, 3.142),
                    [nameof (Арис)] = new WorldCoords (297.57, 1699.79, -3500.08, 4.712),
                    [nameof (Латор)] = new WorldCoords (1070.02, 1701.19, -3437.19, 3.142),
                    [nameof (Эйкум_Кас)] = new WorldCoords (1300.13, 1700.39, -3565.28, 0.000),
                    [nameof (Гедеон)] = new WorldCoords (1434.91, 1700.37, -3454.77, 4.712),
                    [nameof (Шателье)] = new WorldCoords (499.98, 1699.95, -3497.64, 3.142),
                    [nameof (Туанод)] = new WorldCoords (2901.15, 1701.13, -3499.95, 1.571),
                    [nameof (Пельтье)] = new WorldCoords (699.89, 1700.48, -3499.07, 3.142),
                    [nameof (Каре_Рояль)] = new WorldCoords (3143.85, 1701.10, -3499.99, 4.712),
                    [nameof (Блессендор)] = new WorldCoords (1633.67, 1700.46, -3469.97, 4.712),
                    [nameof (Терноваль)] = new WorldCoords (3299.87, 1700.20, -3495.89, 3.142),
                    [nameof (Аммалаэль)] = new WorldCoords (3499.95, 1701.05, -3501.81, 0.000),
                    [nameof (Каблак)] = new WorldCoords (1833.31, 1700.59, -3544.76, 4.712),
                    [nameof (Дэванагари)] = new WorldCoords (2034.23, 1700.41, -3499.85, 4.712),
                    [nameof (Сабулат)] = new WorldCoords (2288.34, 1700.87, -3455.03, 1.571),
                    [nameof (Деффенсат)] = new WorldCoords (3698.24, 1701.05, -3500.03, 4.712),
                    [nameof (Айонат)] = new WorldCoords (2543.85, 1701.10, -3499.99, 4.712),
                    [nameof (Триумфалер)] = new WorldCoords (2699.95, 1701.05, -3501.81, 0.000)
                },
                [Other] = new ()
                {
                    ["Остров_Выбора"] = new WorldCoords (3307, 159, 3611, -1.27),
                    ["Арена"] = new WorldCoords (3648, 155, -3455),
                    ["Арена_Респ"] = new WorldCoords (3703.45, 159.80, -3448.50, -4.170),
                    // ["Арена_Респ_7010"] = new WorldCoords (3702.50, 159.91, -3448.03, -2.798),
                    // ["Арена_Респ_7210"] = new WorldCoords (3702.44, 159.92, -3449.65, -0.253),
                    // ["Арена_Респ_7110"] = new WorldCoords (3701.90, 159.91, -3448.84, -1.347),
                },
                [Event] = new ()
                {
                    ["Белая_комната_Воздуха"] = new WorldCoords (2299.85, 1499.85, 29.29, 3.142),
                    // ["Белая_комната_Воздуха_13213"] = new WorldCoords (2299.85, 1499.85, 29.29, 3.142),
                    ["Белая_комната_Воды"] = new WorldCoords (3099.97, 1499.82, 29.79, 3.142)
                }
            },
            [Харон] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [CityCenter] = new ()
                {
                    [nameof (Номрад)] = new WorldCoords (-2723.59, 404.54, 2110.18, -1.317),
                    [nameof (Гифес)] = new WorldCoords (1872.20, 402.57, 3293.81, -1.734)
                },
                [CastleTeleportPointOwner] = new ()
                {
                    [nameof (Багарнак)] = new WorldCoords (1312.57, 402.11, 3708.51, 0),
                    // ["Багарнак_12241"] = new WorldCoords (1305.63, 402.05, 3710.26, 4.712),
                    [nameof (Кабрад)] = new WorldCoords (-707.80, 402.82, 3513.24, 0),
                    // ["Кабрад_12343"] = new WorldCoords (-711.89, 402.73, 3510.41, 0.000),
                    [nameof (Сет)] = new WorldCoords (2701.46, 366.13, 3302.87, 0),
                    // ["Сет_12365"] = new WorldCoords (2701.22, 365.95, 3318.03, 0.000),
                    [nameof (Лендер)] = new WorldCoords (-2496.2, 402.54, 3086.18, 0),
                    [nameof (Келес)] = new WorldCoords (-706.1, 402.18, 2705.16, 0),
                    // ["Келес_12317"] = new WorldCoords (-690.52, 402.55, 2694.66, -1.884),
                    [nameof (Шепростан)] = new WorldCoords (502.73, 402.78, 2683.3, 0),
                    [nameof (Оркобьен)] = new WorldCoords (1305.33, 402.57, 2290.44, 0),
                    // ["Оркобьен_12488"] = new WorldCoords (1291.13, 390.87, 2277.16, -2.336),
                    [nameof (Кануак)] = new WorldCoords (-3111.06, 366.11, 2093.8, 0),
                    // ["Кануак_12385"] = new WorldCoords (-3101.51, 366.18, 2094.95, 3.142),
                    [nameof (Алдарнон)] = new WorldCoords (-1509.54, 402.6, 1904.65, 0),
                    [nameof (Йонг)] = new WorldCoords (-301.27, 402.3, 1302.58, 0),
                    // ["Йонг_12327"] = new WorldCoords (-291.86, 402.30, 1297.16, 0.000),
                },
                [CastleTeleportPointEnemy] = new ()
                {
                    // [nameof (Багарнак)] = new WorldCoords(1894, 159.9, 47, 1.51)
                },
                [CastleInnerArea] = new ()
                {
                    [nameof (Багарнак)] = new WorldCoords (-1099.59, 1098.43, 1092.85, 0),
                    // ["Багарнак_6451"] = new WorldCoords (-1094.84, 1095.89, 1106.06, -3.936),
                    // ["Багарнак_6450"] = new WorldCoords (-1096.40, 1095.86, 1108.15, -3.876),
                    // ["Багарнак_6449"] = new WorldCoords (-1098.86, 1095.90, 1108.60, 3.142),
                    [nameof (Кабрад)] = new WorldCoords (-883.91, 1094, 1125.14, -4), // сломан
                    // ["Кабрад_6454"] = new WorldCoords (-896.24, 1093.94, 1129.80, -2.020),
                    // ["Кабрад_6453"] = new WorldCoords (-898.49, 1093.90, 1126.28, -2.080),
                    // ["Кабрад_6452"] = new WorldCoords (-899.06, 1093.96, 1121.12, -0.990),
                    [nameof (Сет)] = new WorldCoords (-697.19, 1099.58, 1098.49, 1.57),
                    // ["Сет_1187"] = new WorldCoords (-705.21, 1100.33, 1108.43, 3.142),
                    [nameof (Лендер)] = new WorldCoords (-510.1, 1096.64, 1099.94, 1.57),
                    [nameof (Келес)] = new WorldCoords (-297.78, 1095.82, 1091.78, 1.57),
                    [nameof (Шепростан)] = new WorldCoords (-107.5, 1095.72, 1096.97, 1.57),
                    [nameof (Оркобьен)] = new WorldCoords (106.29, 1096, 1108.16, 0),
                    [nameof (Кануак)] = new WorldCoords (291.63, 1099.01, 1104.42, -2.26),
                    // ["Кануак_6470"] = new WorldCoords (305.96, 1096.77, 1099.80, -4.129),
                    // ["Кануак_6471"] = new WorldCoords (306.91, 1096.77, 1094.69, 1.477),
                    // ["Кануак_6472"] = new WorldCoords (305.08, 1096.77, 1092.00, 0.000),
                    [nameof (Алдарнон)] = new WorldCoords (493.98, 1096.03, 1108.17, 0),
                    [nameof (Йонг)] = new WorldCoords (700.98, 1090.74, 1104.25, 0),
                },
                [CastleDungeon] = new ()
                {
                    [nameof (Багарнак)] = new WorldCoords (-3694.21, 1700.54, -3299.07, 1.168),
                    [nameof (Кабрад)] = new WorldCoords (-1834.15, 1699.73, -3254.85, 1.571),
                    [nameof (Сет)] = new WorldCoords (-3894.46, 1700.65, -3299.18, 1.153),
                    [nameof (Лендер)] = new WorldCoords (-3294.54, 1700.62, -3299.04, 1.507),
                    [nameof (Келес)] = new WorldCoords (-1765.73, 1700.59, -3315.88, -1.555),
                    [nameof (Шепростан)] = new WorldCoords (-1580.09, 1700.01, -3284.97, 4.712),
                    [nameof (Оркобьен)] = new WorldCoords (-3095.45, 1700.12, -3300.80, 1.283),
                    [nameof (Кануак)] = new WorldCoords (3905.78, 1700.62, -3499.04, 1.354),
                    [nameof (Алдарнон)] = new WorldCoords (-2899.09, 1700.62, -3305.45, 0.000),
                    [nameof (Йонг)] = new WorldCoords (-1264.35, 1710.63, -3253.98, -4.695)
                },
                [Other] = new ()
                {
                    ["Черная_Башня"] = new WorldCoords (21.66, 409.42, 1982.19, 0.420),
                }
            },
            [Феб] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [CityCenter] = new ()
                {
                    [nameof (Умрад)] = new WorldCoords (-1997.38, -104.41, 453.74, -0.547),
                    // ["Умрад_6033"] = new WorldCoords (-1986.07, -103.82, 450.00, 0.878),
                    // ["Умрад_6025"] = new WorldCoords (-1979.73, -107.41, 448.73, 3.142),
                    // ["Умрад_6006"] = new WorldCoords (-1970.93, -109.70, 451.70, 1.571),
                    // ["Умрад_6026"] = new WorldCoords (-1966.39, -107.39, 448.32, 3.142),
                },
                [CastleTeleportPointOwner] = new ()
                {
                    [nameof (Элек)] = new WorldCoords (-2596.07, -96.8, -314.45, 0),
                    [nameof (Гавот)] = new WorldCoords (-1406.10, -97.2, 705.16, 0),
                    [nameof (Кандур)] = new WorldCoords (-601.27, -97, -297.41, 0),
                },
                [CastleTeleportPointEnemy] = new ()
                {
                    // [nameof (Багарнак)] = new WorldCoords(1894, 159.9, 47, 1.51)
                },
                [CastleInnerArea] = new ()
                {
                    [nameof (Элек)] = new WorldCoords (900.40, 1098.43, 1092.85, 0),
                    // ["Элек_6481"] = new WorldCoords (905.16, 1095.89, 1106.06, -3.936),
                    // ["Элек_6480"] = new WorldCoords (903.60, 1095.86, 1108.15, -3.876),
                    // ["Элек_6479"] = new WorldCoords (901.14, 1095.90, 1108.60, 3.142),
                    [nameof (Гавот)] = new WorldCoords (1116.08, 1094.02, 1125.14, -3.99),
                    // ["Гавот_6484"] = new WorldCoords (1103.76, 1093.94, 1129.80, -2.020),
                    // ["Гавот_6483"] = new WorldCoords (1101.51, 1093.90, 1126.28, -2.080),
                    // ["Гавот_6482"] = new WorldCoords (1100.94, 1093.96, 1121.12, -0.990),
                    [nameof (Кандур)] = new WorldCoords (1302.80, 1099.58, 1098.49, 1.57)
                },
                [CastleDungeon] = new ()
                {
                    [nameof (Элек)] = new WorldCoords (-1079.29, 1699.54, -3314.97, -4.579),
                    [nameof (Гавот)] = new WorldCoords (-899.92, 1699.45, -3218.55, -3.065),
                    [nameof (Кандур)] = new WorldCoords (-699.94, 1699.64, -3293.61, -3.024)
                },
                [SharedDungeon] = new ()
                {
                    ["Фебоданж_1"] = new WorldCoords (-3899.97, 1299.46, -1893.74, -2.995),
                    ["Фебоданж_2"] = new WorldCoords (-3700.65, 1300.56, -1894.27, -2.868),
                    // ["Фебоданж_2_13200"] = new WorldCoords (-3700.65, 1300.56, -1894.27, -2.868),
                    ["Фебоданж_3"] = new WorldCoords (-3493.37, 1270.52, -1900.11, -4.565),
                    ["Фебоданж_4"] = new WorldCoords (-3396.49, 1310.54, -1884.86, -1.473)
                }
            },
            [Родос] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [CityCenter] = new ()
                {
                    [nameof (Анхельм)] = new WorldCoords (-3397.44, -340.22, -813.00, -3.093),
                    // ["Анхельм_1995"] = new WorldCoords (-3397.47, -340.22, -813.18, -3.093),
                    // ["Анхельм_1994"] = new WorldCoords (-3397.46, -340.22, -813.27, -3.093),
                    // ["Анхельм_1813"] = new WorldCoords (-3397.44, -340.05, -814.50, 3.142),
                    // ["Анхельм_7307"] = new WorldCoords (-3400.32, -339.46, -830.60, -3.397),
                },
                [CastleTeleportPointOwner] = new ()
                {
                    [nameof (Иммертель)] = new WorldCoords (-3406.10, -345.8, -94.8, 0),
                    [nameof (Нарцисс)] = new WorldCoords (-2403.49, -345.87, -1300.99, 0),
                    [nameof (Ранден)] = new WorldCoords (-3596.99, -345.53, -2112.12, 0),
                    [nameof (Ниргун)] = new WorldCoords (-3201.27, -345.79, 702.58, 0),
                    [nameof (Гелгивинн)] = new WorldCoords (-2606.10, -347.2, -2094.83, 0),
                    [nameof (Иль_Суильи_Руа)] = new WorldCoords (-1060.35, -374.21, -1569.71, 0),
                    // ["Иль_Суильи_Руа_4337"] = new WorldCoords (-1042.78, -374.13, -1546.50, -0.357)
                },
                [CastleTeleportPointEnemy] = new ()
                {
                    [nameof (Иль_Суильи_Руа)] = new WorldCoords (-1212, -356.2, -1703, 0),
                    // ["Иль_Суильи_Руа_1937"] = new WorldCoords (-1286.41, -340.33, -1766.07, -0.815)
                },
                [CastleInnerArea] = new ()
                {
                    [nameof (Иммертель)] = new WorldCoords (1500.58, 1098.43, 1093.24, 0), // !
                    // ["Иммертель_6488"] = new WorldCoords (1501.14, 1095.90, 1108.60, 3.142),
                    [nameof (Нарцисс)] = new WorldCoords (1692.65, 1095.72, 1097.21, 1.57),
                    [nameof (Ранден)] = new WorldCoords (1902.24, 1099.65, 1098.63, 1.57),
                    [nameof (Ниргун)] = new WorldCoords (2090.52, 1096.41, 1100.05, -1.47),
                    [nameof (Гелгивинн)] = new WorldCoords (2309.9, 1096.1, 1098.9, 1.57),
                    [nameof (Иль_Суильи_Руа)] = new WorldCoords (2533.35, 1068.27, 1095.46, -1.64),
                    // ["Иль_Суильи_Руа_6495"] = new WorldCoords (2525.66, 1067.94, 1086.64, -2.016),
                    // ["Иль_Суильи_Руа_6493"] = new WorldCoords (2535.91, 1066.75, 1080.87, 0.000),
                    // ["Иль_Суильи_Руа_6494"] = new WorldCoords (2531.86, 1066.81, 1080.70, 0.000),
                },
                [CastleDungeon] = new ()
                {
                    [nameof (Иммертель)] = new WorldCoords (-434.05, 1699.84, -3255.02, 1.57),
                    [nameof (Нарцисс)] = new WorldCoords (-299.15, 1700.71, -3305.82, 0),
                    [nameof (Ранден)] = new WorldCoords (-165.94, 1700.69, -3315.90, -1.57),
                    [nameof (Ниргун)] = new WorldCoords (19.89, 1700.24, -3284.98, -1.57),
                    [nameof (Гелгивинн)] = new WorldCoords (-3201.27, -345.79, 702.58, 0),
                }
            }
        };
}

public class WorldCoords
{
    public readonly double turn;
    public readonly double x;
    public readonly double y;
    public readonly double z;

    public WorldCoords (double x1, double y1, double z1, double turn1 = 0)
    {
        x = x1;
        y = y1;
        z = z1;
        turn = turn1;
    }

    public override string ToString ()
    {
        return $"{x:F1}, {y:F1}, {z:F1}, {turn:F2}";
    }

    public string ToDebugString ()
    {
        return "X: " + x + " Y: " + y + " Z: " + z + " Turn: " + turn;
    }
}

public static class CoordsHelper
{
    public static byte[] EncodeServerCoordinate (double a)
    {
        var scale = 69;

        var a_abs = Math.Abs (a);
        var a_temp = a_abs;

        var steps = 0;

        if ((int) a_abs == 0)
        {
            scale = 58;
        }

        else if (a_temp < 2048)
        {
            while (a_temp < 2048)
            {
                a_temp *= 2;
                steps += 1;
            }

            scale -= (steps + 1) / 2;

            if (scale < 0)
            {
                scale = 58;
            }
        }
        else
        {
            while (a_temp > 4096)
            {
                a_temp /= 2;
                steps += 1;
            }

            scale += steps / 2;
        }

        var a_3 = (byte) (((a < 0 ? 1 : 0) << 7) + scale);
        var mul = Math.Pow (2, (int) Math.Log (a_abs, 2));
        var numToEncode = (int) (0b100000000000000000000000 * (a_abs / mul + 1));

        var a_2 = (byte) (((numToEncode & 0b111111110000000000000000) >> 16) + (steps % 2 == 1 ? 0b10000000 : 0));
        var a_1 = (byte) ((numToEncode & 0b1111111100000000) >> 8);
        var a_0 = (byte) (numToEncode & 0b11111111);

        return new[] { a_0, a_1, a_2, a_3 };
    }

    public static double DecodeServerCoordinate (byte[] input, int shift = 0)
    {
        var a = GetArrayWithoutBitShift (input, shift);
        var scale = a[3] & 0b1111111;

        if (scale == 58)
        {
            return 0;
        }

        var sign = (a[3] & 0b10000000) > 0 ? -1 : 1;
        var stepsIsOdd = (a[2] & 0b10000000) > 0;

        if (stepsIsOdd)
        {
            scale -= 1;
        }

        var numToEncode = ((a[2] & 0b1111111) << 16) + (a[1] << 8) + a[0];
        var baseCoord = Math.Pow (2, scale - 58);

        return sign * (1 + (double) numToEncode / 0b100000000000000000000000) * baseCoord;
    }

    public static double DecodeClientCoordinateWithoutShift (byte[] a, bool shouldReverse = true)
    {
        if (a.Length < 4)
        {
            return 0;
        }

        if (shouldReverse)
        {
            // Prefer LINQ Reverse: byte[].Reverse() binds to Span.Reverse (void) on modern SDKs.
            a = a.AsEnumerable ().Reverse ().ToArray ();
        }

        return DecodeClientCoordinateFromBitStream (new BitStream (a));
    }

    public static double DecodeClientCoordinate (byte[] a)
    {
        var x_scale = ((a[4] & 0b11111) << 3) + ((a[3] & 0b11100000) >> 5);

        if (x_scale == 126)
        {
            return 0.0;
        }

        var baseCoord = Math.Pow (2, x_scale - 127);
        var sign = (a[4] & 0b100000) > 0 ? -1 : 1;

        return (1 + (float) (((a[3] & 0b11111) << 18) + (a[2] << 10) + (a[1] << 2) +
                             ((a[0] & 0b11000000) >> 6)) / 0b100000000000000000000000) * baseCoord * sign;
    }

    /// 23-bit fraction, 8-bit scale, 1-bit sign
    public static double DecodeClientCoordinateFromBitStream (BitStream stream)
    {
        var fraction = stream.ReadInt64 (23);
        var scale = stream.ReadByte ();
        var sign = stream.ReadBit ().AsBool () ? -1 : 1;

        if (scale == 126)
        {
            return 0.0;
        }

        var baseCoord = Math.Pow (2, scale - 127);
        return (1 + (float) fraction / 0b100000000000000000000000) * baseCoord * sign;
    }

    /// Keepalive PositionStream marker: shifted bytes 19-20 are 01 60
    public static bool HasPingCoordMarker (byte[] rcvBuffer) =>
        rcvBuffer.Length >= 35
        && ClientPacketClassifier.ShiftedByte (rcvBuffer, 19) == 0x01
        && ClientPacketClassifier.ShiftedByte (rcvBuffer, 20) == 0x60;

    /// Client treats abs(x), abs(y), or abs(z) above 16000 as corrupt
    public static bool ArePingCoordsInWorldBounds (WorldCoords coords, double maxAbs = 16000.0) =>
        Math.Abs (coords.x) <= maxAbs
        && Math.Abs (coords.y) <= maxAbs
        && Math.Abs (coords.z) <= maxAbs;

    /// After the marker: 2 alignment bits, array4 count 4, then x, y, z, angle as IEEE f32
    public static WorldCoords GetCoordsFromPingBytes (byte[] rcvBuffer)
    {
        if (rcvBuffer.Length < 52)
        {
            return new WorldCoords (0, 0, 0);
        }

        // Bit 280 (21*8 + 2 + 110): 4-bit word_count, then floats at bit 284.
        var stream = new BitStream (rcvBuffer);
        stream.SeekBitOffset (21 * 8 + 2 + 110);
        var wordCount = (int) stream.ReadUInt16 (4);
        if (wordCount < 4)
        {
            return new WorldCoords (0, 0, 0);
        }

        var x = ReadIeeeFloat32 (stream);
        var y = ReadIeeeFloat32 (stream);
        var z = ReadIeeeFloat32 (stream);
        var turn = ReadIeeeFloat32 (stream);
        return new WorldCoords (x, y, z, turn);
    }

    /// 32-bit IEEE float, little-endian bit order
    public static double ReadIeeeFloat32 (BitStream stream)
    {
        var bits = unchecked((uint) stream.ReadInt64 (32));
        return BitConverter.Int32BitsToSingle (unchecked((int) bits));
    }

    private static byte[] GetArrayWithoutBitShift (byte[] input, int shift = 0)
    {
        if (shift <= 0)
        {
            return input;
        }

        var result = new byte[input.Length - 1];
        // example for 3 bits
        // 11111111 -> 11111000 = 11111111 - 111 = (2^8 - 1) - (2^3 - 1) = 2^8 - 2^3 + 1
        // for specific shift: (2^8 - 1) - 2^shift + 1 = 2^8 - 2^shift
        // then >> by shift
        var twoPowShift = (int) Math.Round (Math.Pow (2, shift));
        var bitRemainder = twoPowShift - 1;
        var bitShift = 256 - twoPowShift;

        for (var i = 0; i < result.Length; i++)
        {
            var t1 = input[i] & bitShift;
            var t2 = input[i + 1] & bitRemainder;
            result[i] = (byte) (((input[i] & bitShift) >> shift) + ((input[i + 1] & bitRemainder) << (8 - shift)));
        }

        return result;
    }
}
