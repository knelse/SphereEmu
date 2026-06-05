using BitStreams;
using SphereHelpers.Extensions;
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
    Other
}

public static class SavedCoords
{
    public static readonly Dictionary<Continents, Dictionary<Cities, Dictionary<KarmaTypes, WorldCoords>>>
        RespawnPoints =
            new ()
            {
                [Гиперион] = new Dictionary<Cities, Dictionary<KarmaTypes, WorldCoords>>
                {
                    [Шипстоун] = new ()
                    {
                        [Очень_Плохая] = new WorldCoords(1194, 159.9, -2194, -4)
                    },
                    [Санпул] = new ()
                    {
                        [Плохая] = new WorldCoords(3648, 155, -3455),
                        [Очень_Плохая] = new WorldCoords(1194, 159.9, -2194, -4)
                    },
                    [Торвил] = new ()
                    {
                        [Плохая] = new WorldCoords(2489, 159.7, -2181, 0),
                        [Очень_Плохая] = new WorldCoords(1194, 159.9, -2194, -4)
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
                    ["Остров_Выбора"] = new WorldCoords(1300.33, 1499.32, 35.94, 5.87),
                    ["Латор_Каблак"] = new WorldCoords(-300, 1500, 106, 3.1415),
                    ["Аммалаэль"] = new WorldCoords(33, 1500, 145, 4.85),
                    ["Фьеф_Респ_Меди"] = new WorldCoords(-99, 1499.7, 33, 5.87),
                    ["Санпул_Карма_Север"] = new WorldCoords(-2105, 1498, 100, 4.8),
                    ["Бангвиль"] = new WorldCoords(-1500, 1499.7, 105.85, 3.14),
                    ["Каре_Рояль_Север"] = new WorldCoords(-1293, 1499, 100, 1.57),
                    ["Каре_Рояль_Юг"] = new WorldCoords(-1061, 1499, 100, 4.68),
                    ["Хортонский_Лес"] = new WorldCoords(-1894, 1498, 100, 1.58),
                    ["Санпул_Лес"] = new WorldCoords(233, 1499.7, 54, 4.7),
                    ["Горы_Дорога_Санпул_Торвил"] = new WorldCoords(-1735, 1498, 100, 4.7),
                    ["Торвил_Кладб"] = new WorldCoords(433, 1499.7, 100, 4.66),
                    ["Шипстоун_Север"] = new WorldCoords(-2499, 1498, 105, 3),
                    ["Блессендор"] = new WorldCoords(-706, 1499, 100, 4.71)
                },
                [Cemetery] = new ()
                {
                    [nameof (Шипстоун)] = new WorldCoords(-3900, 1509, 106, 3.1),
                    [nameof (Бангвиль)] = new WorldCoords(-3753, 1509, 100, 1.6),
                    [nameof (Торвил)] = new WorldCoords(-3055, 1499, 106, 3.1),
                    [nameof (Санпул)] = new WorldCoords(-2900, 1499.5, 106.34, 3.14)
                },
                [CityCenter] = new ()
                {
                    [nameof (Шипстоун)] = new WorldCoords(2614, 157.8, 1293),
                    [nameof (Бангвиль)] = new WorldCoords(1882, 155.6, -407),
                    [nameof (Торвил)] = new WorldCoords(2292, 155.3, -2386),
                    [nameof (Санпул)] = new WorldCoords(422, 153.3, -1284)
                },
                [TeleportPoint] = new ()
                {
                    ["Северная_Дорога"] = new WorldCoords(3031, 159, 2095, -2.5),
                    ["Северо_Западный_Край"] = new WorldCoords(502, 159, 1132, 4.6),
                    ["Дорога_Шипстоун_Санпул"] = new WorldCoords(1902, 159, 1161, 4.6),
                    ["Дорога_Шипстоун_Бангвиль"] = new WorldCoords(2107, 159, 936, -7.3),
                    ["Северо_Запад"] = new WorldCoords(3785, 159, 918, -5.8),
                    ["Умрадский_Лес"] = new WorldCoords(2256, 159, 614, 3.14),
                    ["Серебряный_Лес"] = new WorldCoords(945, 159, 505, 3.36),
                    ["Дорога_Шипстоун_Торвил"] = new WorldCoords(2904, 159, 550, 11),
                    ["Горы_Шипстоун_Бангвиль"] = new WorldCoords(2592, 159, 365, -1.5),
                    ["Озеро_Темер"] = new WorldCoords(3726, 159, -57, -1.2),
                    ["Остров_Гебер_Озеро_Темер"] = new WorldCoords(3371, 161, -374, -5.87),
                    ["Дорога_Санпул_Шипстоун_Озеро_Вортекс"] = new WorldCoords(316, 159, -285, 3.14),
                    ["Дорога_Санпул_Бангвиль_Озеро_Вортекс"] = new WorldCoords(1303, 159, -355, -0.16),
                    ["Остров_Форос_Озеро_Вортекс"] = new WorldCoords(751, 160, -653, 5.87),
                    ["Остров_Дейрос_Озеро_Вортекс"] = new WorldCoords(1368, 160, -1525, 5.87),
                    ["Устье_Реки_Нерей"] = new WorldCoords(1606, 159, -693, 1.57),
                    ["Восточный_Лес"] = new WorldCoords(3652, 164, -1533, 5.87),
                    ["Устье_Реки_Диомы"] = new WorldCoords(2227, 159, -1684, 3.14),
                    ["Хортонский_Лес"] = new WorldCoords(300, 159, -1945, 0),
                    ["Горы_Дорога_Санпул_Торвил"] = new WorldCoords(828, 157.4, -2729, 1.57),
                    ["Остров_Патрос_Озеро_Атласное"] = new WorldCoords(1556, 162, -2826, 7.5),
                    ["Юго_Восточный_Горный_Карман"] = new WorldCoords(2711, 159.7, -2860, 0.38),
                    ["Койтонский_Лес_Север"] = new WorldCoords(446, 159.7, -3308, 0),
                    ["Юго_Западный_Край"] = new WorldCoords(147, 158, -3737, 0),
                    ["Мост_Тантал"] = new WorldCoords(1197, 159.9, 1315, 0),
                    ["Обол"] = new WorldCoords(1144.42, 157.94, 1632.75, 0),
                    ["Обратно_С_Харона"] = new WorldCoords(1141.93, 157.94, 1630.65, -1.53)
                },
                [CastleTeleportPointOwner] = new ()
                {
                    [nameof (Льеж)] = new WorldCoords(1853, 157.6, 37.87, 0),
                    [nameof (Фьеф)] = new WorldCoords(2652, 157.9, -166, 0),
                    [nameof (Арис)] = new WorldCoords(3040.17, 158, 1048.2, 0),
                    [nameof (Латор)] = new WorldCoords(1955.33, 157.83, 1440.44, 0),
                    [nameof (Эйкум_Кас)] = new WorldCoords(1339.54, 158, 358.9, 0),
                    [nameof (Гедеон)] = new WorldCoords(456.12, 157.88, -2157.8, 0),
                    [nameof (Шателье)] = new WorldCoords(946.4, 157.42, -1251, 0),
                    [nameof (Туанод)] = new WorldCoords(3457.45, 157.43, 2453.48, 0),
                    [nameof (Пельтье)] = new WorldCoords(2130.39, 158.12, -857.56, 0),
                    [nameof (Каре_Рояль)] = new WorldCoords(3473.75, 158, -1250.21, 0),
                    [nameof (Блессендор)] = new WorldCoords(2044.53, 157.47, -3443.86, 0),
                    [nameof (Терноваль)] = new WorldCoords(3748.72, 157.56, 752.58, 0),
                    [nameof (Аммалаэль)] = new WorldCoords(543.89, 157.43, 455.16, 0),
                    // [nameof (Каблак)] = new WorldCoords(1588, 159.6, 950, -4.66),
                    [nameof (Дэванагари)] = new WorldCoords(2451.88, 157.95, -1648.78, 0),
                    //[nameof (Сабулат)] = new WorldCoords(1148, 159.9, -1792, 0),
                    [nameof (Деффенсат)] = new WorldCoords(354.8, 157.36, -3637.70, 0),
                    [nameof (Айонат)] = new WorldCoords(3361.56, 157.38, -2342.95, 0),
                    [nameof (Триумфалер)] = new WorldCoords(1557.58, 158, -2358.67, 0)
                },
                [CastleTeleportPointEnemy] = new ()
                {
                    [nameof (Льеж)] = new WorldCoords(1894, 159.9, 47, 1.51),
                    [nameof (Фьеф)] = new WorldCoords(2641, 159.9, -192, 0.1),
                    [nameof (Арис)] = new WorldCoords(3016, 158.5, 1054, -1.51),
                    [nameof (Латор)] = new WorldCoords(1994, 159.9, 1445, -4.7),
                    [nameof (Эйкум_Кас)] = new WorldCoords(1333, 158.7, 382, -2.76),
                    [nameof (Гедеон)] = new WorldCoords(446, 159.9, -2193, 0.2),
                    [nameof (Шателье)] = new WorldCoords(914, 159.6, -1273, -1.28),
                    [nameof (Туанод)] = new WorldCoords(3445, 159.9, 2405, 0),
                    [nameof (Пельтье)] = new WorldCoords(2094, 159.9, -841, -1.57),
                    [nameof (Каре_Рояль)] = new WorldCoords(3500, 159.9, -1285, 0),
                    [nameof (Блессендор)] = new WorldCoords(2014, 159, -3463, -1.27),
                    [nameof (Терноваль)] = new WorldCoords(3775, 159.9, 791, -3.71),
                    [nameof (Аммалаэль)] = new WorldCoords(533, 159, 485, -2.6),
                    [nameof (Каблак)] = new WorldCoords(1588, 159.6, 950, -4.66),
                    [nameof (Дэванагари)] = new WorldCoords(2420, 159, -1683, 0),
                    [nameof (Сабулат)] = new WorldCoords(1148.88, 159.82, -1792.69, 0),
                    [nameof (Деффенсат)] = new WorldCoords(334, 159, -3609, -2.6),
                    [nameof (Айонат)] = new WorldCoords(3395, 159.9, -2349, 1.54),
                    [nameof (Триумфалер)] = new WorldCoords(1548, 159.6, -2388, 0)
                },
                [CastleInnerArea] = new ()
                {
                    [nameof (Льеж)] = new WorldCoords(-3302.64, 1099.65, -3898.71, 4.71),
                    [nameof (Фьеф)] = new WorldCoords(-2697, 1095, -3907.45, 0),
                    [nameof (Арис)] = new WorldCoords(-2489.678, 1099.12, -3897, 1.57),
                    [nameof (Латор)] = new WorldCoords(-2300, 1096.6, -3910.1, 0),
                    [nameof (Эйкум_Кас)] = new WorldCoords(-2100, 1096.63, -3889.87, 3.14),
                    [nameof (Гедеон)] = new WorldCoords(-3899.59, 1098.42, 1092.85, 0),
                    [nameof (Шателье)] = new WorldCoords(-3707, 1095, 1097, 4.71), // fix
                    [nameof (Туанод)] = new WorldCoords(-3497, 1099, 1098, 1.57), // fix
                    [nameof (Пельтье)] = new WorldCoords(-3310.1, 1096.64, 1100, 4.71),
                    [nameof (Каре_Рояль)] = new WorldCoords(-3097.78, 1095.82, 1091.78, 1.57),
                    [nameof (Блессендор)] = new WorldCoords(-2907.5, 1095.72, 1096.97, 4.71),
                    [nameof (Терноваль)] = new WorldCoords(-2693, 1096, 1108, 3.14),
                    [nameof (Аммалаэль)] = new WorldCoords(-2508, 1099, 1104, -2),
                    [nameof (Каблак)] = new WorldCoords(-2306, 1096, 1108.17, 3.14),
                    [nameof (Дэванагари)] = new WorldCoords(-2099, 1090.74, 1104.25, 3.14),
                    [nameof (Сабулат)] = new WorldCoords(-1892.55, 1096.10, 1084.39, 0),
                    [nameof (Деффенсат)] = new WorldCoords(-1705.3, 1100.85, 1108.72, 3.14),
                    [nameof (Айонат)] = new WorldCoords(-1485.61, 1098.36, 1092.55, 1.57),
                    [nameof (Триумфалер)] = new WorldCoords(-1304, 1091, 1094, 0)
                },
                [CastleDungeon] = new ()
                {
                    [nameof (Льеж)] = new WorldCoords(104.07, 1699, -3500, 1.57),
                    [nameof (Фьеф)] = new WorldCoords(900.07, 1701.4, -3498.29, 3.14),
                    [nameof (Арис)] = new WorldCoords(297.57, 1700, -3500, 4.71),
                    [nameof (Латор)] = new WorldCoords(1070, 1701.45, -3437.18, 3.14),
                    [nameof (Эйкум_Кас)] = new WorldCoords(1300.12, 1700.64, -3565.27, 0),
                    [nameof (Гедеон)] = new WorldCoords(1434.91, 1700.64, -3454.77, 4.71),
                    [nameof (Шателье)] = new WorldCoords(500, 1700.34, -3497.64, 3.14),
                    [nameof (Туанод)] = new WorldCoords(2901.14, 1701.64, -3500, 1.57),
                    [nameof (Пельтье)] = new WorldCoords(700, 1700.77, -3499, 3.14),
                    [nameof (Каре_Рояль)] = new WorldCoords(3143, 1701, -3500, 4.71),
                    [nameof (Блессендор)] = new WorldCoords(1633.67, 1700.64, -3469.96, 4.71),
                    [nameof (Терноваль)] = new WorldCoords(3300, 1700, -3495, 3.11),
                    [nameof (Аммалаэль)] = new WorldCoords(3499, 1701, -3501, 5.87),
                    [nameof (Каблак)] = new WorldCoords(1833.3, 1700.64, -3544.75, 4.71),
                    [nameof (Дэванагари)] = new WorldCoords(2034.23, 1700.4, -3499.84, 4.71),
                    [nameof (Сабулат)] = new WorldCoords(2288.34, 1701, -3455, 1.57),
                    [nameof (Деффенсат)] = new WorldCoords(3698.24, 1701.64, -3500, 4.71),
                    [nameof (Айонат)] = new WorldCoords(2543.84, 1701.64, -3500, 4.71),
                    [nameof (Триумфалер)] = new WorldCoords(2700, 1701.64, -3501.81, 0)
                },
                [Other] = new ()
                {
                    ["Остров_Выбора"] = new WorldCoords(3307, 159, 3611, -1.27),
                    ["Арена"] = new WorldCoords(3648, 155, -3455)
                }
            },
            [Харон] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [CityCenter] = new ()
                {
                    [nameof (Номрад)] = new WorldCoords(-2723.59, 404.75, 2110.18, -1.31),
                    [nameof (Гифес)] = new WorldCoords(1872.19, 402.77, 3293.8, -1.73)
                },
                [CastleTeleportPointOwner] = new ()
                {
                    [nameof (Багарнак)] = new WorldCoords(1312.57, 402.11, 3708.51, 0),
                    [nameof (Кабрад)] = new WorldCoords(-707.80, 402.82, 3513.24, 0),
                    [nameof (Сет)] = new WorldCoords(2701.46, 366.13, 3302.87, 0),
                    [nameof (Лендер)] = new WorldCoords(-2496.2, 402.54, 3086.18, 0),
                    [nameof (Келес)] = new WorldCoords(-706.1, 402.18, 2705.16, 0),
                    [nameof (Шепростан)] = new WorldCoords(502.73, 402.78, 2683.3, 0),
                    [nameof (Оркобьен)] = new WorldCoords(1305.33, 402.57, 2290.44, 0),
                    [nameof (Кануак)] = new WorldCoords(-3111.06, 366.11, 2093.8, 0),
                    [nameof (Алдарнон)] = new WorldCoords(-1509.54, 402.6, 1904.65, 0),
                    [nameof (Йонг)] = new WorldCoords(-301.27, 402.3, 1302.58, 0)
                },
                [CastleTeleportPointEnemy] = new ()
                {
                    // [nameof (Багарнак)] = new WorldCoords(1894, 159.9, 47, 1.51)
                },
                [CastleInnerArea] = new ()
                {
                    [nameof (Багарнак)] = new WorldCoords(-1099.59, 1098.43, 1092.85, 0),
                    [nameof (Кабрад)] = new WorldCoords(-883.91, 1094, 1125.14, -4),
                    [nameof (Сет)] = new WorldCoords(-697.19, 1099.58, 1098.49, 1.57),
                    [nameof (Лендер)] = new WorldCoords(-510.1, 1096.64, 1099.94, 1.57),
                    [nameof (Келес)] = new WorldCoords(-297.78, 1095.82, 1091.78, 1.57),
                    [nameof (Шепростан)] = new WorldCoords(-107.5, 1095.72, 1096.97, 1.57),
                    [nameof (Оркобьен)] = new WorldCoords(106.29, 1096, 1108.16, 0),
                    [nameof (Кануак)] = new WorldCoords(291.63, 1099.01, 1104.42, -2.26),
                    [nameof (Алдарнон)] = new WorldCoords(493.98, 1096.03, 1108.17, 0),
                    [nameof (Йонг)] = new WorldCoords(700.98, 1090.74, 1104.25, 0)
                },
                [CastleDungeon] = new ()
                {
                    [nameof (Багарнак)] = new WorldCoords(-3694.2, 1700.71, -3299, 1.57), // сломан
                    [nameof (Кабрад)] = new WorldCoords(-1834.14, 1699.84, -3254.85, 1.57), // сломан
                    [nameof (Сет)] = new WorldCoords(-3894.46, 1700.65, -3299.18, 1.57), // сломан
                    [nameof (Лендер)] = new WorldCoords(-3294.54, 1700.62, -3299, 1.57), // сломан
                    [nameof (Келес)] = new WorldCoords(-1765.72, 1700.72, -3315.87, 1.57),
                    [nameof (Шепростан)] = new WorldCoords(-1580.09, 1700.24, -3284.97, 1.57),
                    [nameof (Оркобьен)] = new WorldCoords(-3095.44, 1770.27, -3300.79, 1.57),
                    [nameof (Кануак)] = new WorldCoords(3905.77, 1700.72, -3499.04, 1.57),
                    [nameof (Алдарнон)] = new WorldCoords(-2899.08, 1700.74, -3305.44, 0),
                    [nameof (Йонг)] = new WorldCoords(-1264.34, 1710.73, -3253.98, 1.57)
                },
                [Other] = new ()
                {
                    ["Черная Башня"] = new WorldCoords(21.65, 409.5, 1982.18, 0.42)
                }
            },
            [Феб] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [CityCenter] = new ()
                {
                    [nameof (Умрад)] = new WorldCoords(-1993, -104.5, 457)
                },
                [CastleTeleportPointOwner] = new ()
                {
                    [nameof (Элек)] = new WorldCoords(-2596.07, -96.8, -314.45, 0),
                    [nameof (Гавот)] = new WorldCoords(-1406.10, -97.2, 705.16, 0),
                    [nameof (Кандур)] = new WorldCoords(-601.27, -97, -297.41, 0)
                },
                [CastleTeleportPointEnemy] = new ()
                {
                    // [nameof (Багарнак)] = new WorldCoords(1894, 159.9, 47, 1.51)
                },
                [CastleInnerArea] = new ()
                {
                    [nameof (Элек)] = new WorldCoords(900.40, 1098.43, 1092.85, 0),
                    [nameof (Гавот)] = new WorldCoords(1116.08, 1094.02, 1125.14, -3.99),
                    [nameof (Кандур)] = new WorldCoords(1302.80, 1099.58, 1098.49, 1.57)
                },
                [CastleDungeon] = new ()
                {
                    [nameof (Элек)] = new WorldCoords(-1079.28, 1699.84, -3314.97, 1.57),
                    [nameof (Гавот)] = new WorldCoords(-899.91, 1699.64, -3218.54, -3.06),
                    [nameof (Кандур)] = new WorldCoords(-699.93, 1699.63, -3293.61, -3.02)
                },
                [SharedDungeon] = new ()
                {
                    ["Фебоданж 1"] = new WorldCoords(-3899.97, 1299.45, -1893.74, 3.14),
                    ["Фебоданж 2"] = new WorldCoords(-3700.62, 1301.3, -1895.9, 3.14),
                    ["Фебоданж 3"] = new WorldCoords(-3494, 1270.64, -1900, 1.57),
                    ["Фебоданж 4"] = new WorldCoords(-3396.51, 1310.61, -1885.09, -1.57)
                }
            },
            [Родос] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [CityCenter] = new ()
                {
                    [nameof (Анхельм)] = new WorldCoords(-3397, -340, -813, -3)
                },
                [CastleTeleportPointOwner] = new ()
                {
                    [nameof (Иммертель)] = new WorldCoords(-3406.10, -345.8, -94.8, 0),
                    [nameof (Нарцисс)] = new WorldCoords(-2403.49, -345.87, -1300.99, 0),
                    [nameof (Ранден)] = new WorldCoords(-3596.99, -345.53, -2112.12, 0),
                    [nameof (Ниргун)] = new WorldCoords(-3201.27, -345.79, 702.58, 0),
                    [nameof (Гелгивинн)] = new WorldCoords(-2606.10, -347.2, -2094.83, 0),
                    [nameof (Иль_Суильи_Руа)] = new WorldCoords(-1060.35, -374.21, -1569.71, 0)
                },
                [CastleTeleportPointEnemy] = new ()
                {
                    // [nameof (Багарнак)] = new WorldCoords(1894, 159.9, 47, 1.51),
                    [nameof (Иль_Суильи_Руа)] = new WorldCoords(-1212, -356.2, -1703, 0)
                },
                [CastleInnerArea] = new ()
                {
                    [nameof (Иммертель)] = new WorldCoords(1500.58, 1098.43, 1093.24, 0),
                    [nameof (Нарцисс)] = new WorldCoords(1692.65, 1095.72, 1097.21, 1.57),
                    [nameof (Ранден)] = new WorldCoords(1902.24, 1099.65, 1098.63, 1.57),
                    [nameof (Ниргун)] = new WorldCoords(-3201.27, -345.79, 702.58, 0),
                    [nameof (Гелгивинн)] = new WorldCoords(-3201.27, -345.79, 702.58, 0),
                    [nameof (Иль_Суильи_Руа)] = new WorldCoords(2533.35, 1068.27, 1095.46, -1.64)
                },
                [CastleDungeon] = new ()
                {
                    [nameof (Иммертель)] = new WorldCoords(-434.05, 1699.84, -3255.02, 1.57),
                    [nameof (Нарцисс)] = new WorldCoords(-299.15, 1700.71, -3305.82, 0),
                    [nameof (Ранден)] = new WorldCoords(-165.94, 1700.69, -3315.90, -1.57),
                    [nameof (Ниргун)] = new WorldCoords(19.89, 1700.24, -3284.98, -1.57),
                    [nameof (Гелгивинн)] = new WorldCoords(-3201.27, -345.79, 702.58, 0)
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

        var a_abs = Math.Abs(a);
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
        var mul = Math.Pow(2, (int) Math.Log(a_abs, 2));
        var numToEncode = (int) (0b100000000000000000000000 * (a_abs / mul + 1));

        var a_2 = (byte) (((numToEncode & 0b111111110000000000000000) >> 16) + (steps % 2 == 1 ? 0b10000000 : 0));
        var a_1 = (byte) ((numToEncode & 0b1111111100000000) >> 8);
        var a_0 = (byte) (numToEncode & 0b11111111);

        return new[] { a_0, a_1, a_2, a_3 };
    }

    public static double DecodeServerCoordinate (byte[] input, int shift = 0)
    {
        var a = GetArrayWithoutBitShift(input, shift);
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
        var baseCoord = Math.Pow(2, scale - 58);

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
            a = a.Reverse().ToArray();
        }

        var stream = new BitStream(a);
        var fraction = stream.ReadInt64(23);
        var scale = stream.ReadByte();
        var sign = stream.ReadBit().AsBool() ? -1 : 1;

        if (scale == 126)
        {
            return 0.0;
        }

        var baseCoord = Math.Pow(2, scale - 127);

        return (1 + (float) fraction / 0b100000000000000000000000) * baseCoord * sign;
    }

    public static double DecodeClientCoordinate (byte[] a)
    {
        var x_scale = ((a[4] & 0b11111) << 3) + ((a[3] & 0b11100000) >> 5);

        if (x_scale == 126)
        {
            return 0.0;
        }

        var baseCoord = Math.Pow(2, x_scale - 127);
        var sign = (a[4] & 0b100000) > 0 ? -1 : 1;

        return (1 + (float) (((a[3] & 0b11111) << 18) + (a[2] << 10) + (a[1] << 2) +
                             ((a[0] & 0b11000000) >> 6)) / 0b100000000000000000000000) * baseCoord * sign;
    }

    public static WorldCoords GetCoordsFromPingBytes (byte[] rcvBuffer)
    {
        var x = DecodeClientCoordinate(rcvBuffer.AsSpan(21, 5).ToArray());
        var y = DecodeClientCoordinate(rcvBuffer.AsSpan(25, 5).ToArray());
        var z = DecodeClientCoordinate(rcvBuffer.AsSpan(29, 5).ToArray());
        var turn = DecodeClientCoordinate(rcvBuffer.AsSpan(33, 5).ToArray());

        return new WorldCoords(x, y, z, turn);
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
        var twoPowShift = (int) Math.Round(Math.Pow(2, shift));
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