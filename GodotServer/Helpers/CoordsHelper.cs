using System;
using System.Collections.Generic;
using static KarmaTypes;
using static SphServer.Helpers.Continents;
using static SphServer.Helpers.Cities;
using static SphServer.Helpers.Castles;
using static SphServer.Helpers.PoiType;
// ReSharper disable RedundantArgumentDefaultValue

namespace SphServer.Helpers
{
    public enum Continents
    {
        Hyperion,
        Haron,
        Phoebe,
        Rodos
    }

    public enum Cities
    {
        // Hyperion
        Shipstone,
        Bangville,
        Torweal,
        Sunpool,
        // Haron
        Nomrad,
        Gifes,
        // Phoebe
        Umrad,
        // Rodos
        Anhelm
    }

    public enum Castles
    {
        // Hyperion
        Shatelier,
        Aris,
        Peltier,
        Liege,
        Ayonat,
        Fief,
        Triumfaler,
        Sabulat,
        Lator,
        EikumKas,
        Blessendor,
        Gedeon,
        Devanagari,
        Kablak,
        Deffensat,
        Ternoval,
        Tuanod,
        Ammalael,
        KareRoyal,
        // Haron
        Set,
        Kanuak,
        Aldarnon,
        Bagarnak,
        Orkobien,
        Lender,
        Keles,
        Kabrad,
        Iong,
        Sheprostan,
        // Phoebe
        Gavot,
        Elek,
        Kandur,
        // Rodos
        Immertel,
        Narciss,
        Randen,
        Nirgun,
        Gelgivinn,
        IlSuilieRua
    }

    public enum PoiType
    {
        SharedDungeon,
        Cemetery,
        CityCenter,
        RespawnPoint,
        TeleportPoint,
        CastleTeleportPoint,
        CastleInnerArea,
        CastleDungeon,
        Other
    }
    public static class SavedCoords
    {
        public static Dictionary<Continents, Dictionary<Cities, Dictionary<KarmaTypes, WorldCoords>>> RespawnPoints =
            new()
            {
                [Hyperion] = new ()
                {
                    [Shipstone] = new ()
                    {
                        [VeryBad] = new(1194, 159.9, -2194, -4)
                    },
                    [Sunpool] = new ()
                    {
                        [Bad] = new(3648, 155, -3455),
                        [VeryBad] = new(1194, 159.9, -2194, -4)
                    },
                    [Torweal] = new ()
                    {
                        [Bad] = new(2489, 159.7, -2181, 0),
                        [VeryBad] = new(1194, 159.9, -2194, -4)
                    }
                }
            };
        
        public static readonly Dictionary<Continents, Dictionary<PoiType, Dictionary<string, WorldCoords>>> TeleportPoints = new()
        {
            [Hyperion] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [SharedDungeon] = new()
                {
                    ["Choice"] = new(1299, 1499, 35, 5.87),
                    ["LatorKablak"] = new(-300, 1500, 106, 3.1415),
                    ["Ammalael"] = new(33, 1500, 145, 4.85),
                    ["FiefCopperSpawn"] = new(-99, 1500, 33, 5.87),
                    ["SunpoolKarmaNorthern"] = new(-2105, 1498, 100, 4.8),
                    ["Bangville"] = new(-1500, 1499, 105, 3),
                    ["KareRoyalNorth"] = new(-1293, 1499, 100, 1.57),
                    ["KareRoyalSouth"] = new(-1061, 1499, 100, 4.68),
                    ["HortonForest"] = new(-1894, 1498, 100, 1.58),
                    ["SunpoolForest"] = new(233, 1500, 54, 4.7),
                    ["SunpoolTorwealRoadMountains"] = new(-1735, 1498, 100, 4.7),
                    ["TorwealCemetery"] = new(433, 1500, 100, 4.66),
                    ["ShipstoneNorth"] = new(-2499, 1498, 105, 3),
                    ["Blessendor"] = new(-706, 1499, 100, 4.71)
                },
                [Cemetery] = new ()
                {
                    [nameof(Shipstone)] = new(-3900, 1509, 106, 3.1),
                    [nameof(Bangville)] = new(-3753, 1509, 100, 1.6),
                    [nameof(Torweal)] = new(-3055, 1500, 106, 3.1),
                    // [nameof(Sunpool)] = new ()
                },
                [CityCenter] = new ()
                {
                    
                    [nameof(Shipstone)] = new (2614, 157.8, 1293),
                    [nameof(Bangville)] = new (1882, 155.6, -407),
                    [nameof(Torweal)] = new (2292, 155.3, -2386),
                    [nameof(Sunpool)] = new (422, 153.3, -1284)
                },
                [TeleportPoint] = new ()
                {
                    ["NothernRoad"] = new(3031, 159, 2095, -2.5),
                    ["HorthWesternEdge"] = new(502, 159, 1132, 4.6),
                    ["ShipstoneSunpoolRoad"] = new(1902, 159, 1161, 4.6),
                    ["ShipstoneBangvilleRoad"] = new(2107, 159, 936, -7.3),
                    ["NorthEast"] = new(3785, 159, 918, -5.8),
                    ["UmradForest"] = new(2256, 159, 614, 3.14),
                    ["SilverForest"] = new(945, 159, 505, 3.36),
                    ["ShipstoneTorwealRoad"] = new(2904, 159, 550, 11),
                    ["ShipstoneBangvilleMountains"] = new(2592, 159, 365, -1.5),
                    ["TemerLake"] = new(3726, 159, -57, -1.2),
                    ["HeberIslandTemerLake"] = new(3371, 161, -374, -5.87),
                    ["SunpoolShipstoneRoadNearVortexLake"] = new(316, 159, -285, 3.14),
                    ["SunpoolBangvilleRoadNearVortexLake"] = new(1303, 159, -355, -0.16),
                    ["SunpooPhorosIslandVortexLake"] = new(751, 160, -653, 5.87),
                    ["SunpooDeirosIslandVortexLake"] = new(1368, 160, -1525, 5.87),
                    ["NereyRiverMouth"] = new(1606, 159, -693, 1.57),
                    ["EasternForest"] = new(3652, 164, -1533, 5.87),
                    ["DiomaRiverMouth"] = new(2227, 159, -1684, 3.14),
                    ["HortonForest"] = new(300, 159, -1945, 0),
                    ["SunpoolTorwealRoadMountains"] = new(828, 157.4, -2729, 1.57),
                    ["PatrosIslandAtlasLake"] = new(1556, 162, -2826, 7.5),
                    ["SouthEasternMountainPocket"] = new(2711, 159.7, -2860, 0.38),
                    ["KoytonForestNorth"] = new(446, 159.7, -3308, 0),
                    ["SouthWesternEdge"] = new(147, 158, -3737, 0),
                    ["TantalBridge"] = new(1197, 159.9, 1315, 0),
                    ["BackFromHaron"] = new(1142, 157.9, 1630, -1.43),
                },
                [CastleTeleportPoint] = new ()
                {
                    [nameof(Aris)] = new(3016, 158.5, 1054, -1.51),
                    [nameof(Liege)] = new(1894, 159.9, 47, 1.51),
                    [nameof(Fief)] = new(2641, 159.9, -192, 0.1),
                    [nameof(Shatelier)] = new(914, 159.6, -1273, -1.28),
                    [nameof(Peltier)] = new(2094, 159.9, -841, -1.57),
                    [nameof(Triumfaler)] = new(1548, 159.6, -2388, 0),
                    [nameof(Sabulat)] = new(1148, 159.9, -1792, 0),
                    [nameof(Devanagari)] = new(2420, 159, -1683, 0),
                    [nameof(Kablak)] = new(1588, 159.6, 950, 4.70),
                    [nameof(Gedeon)] = new(446, 159.9, -2193, 0.2),
                    [nameof(EikumKas)] = new(1333, 158.7, 382, -2.76),
                    [nameof(Lator)] = new(1994, 159.9, 1445, -4.7),
                    [nameof(Blessendor)] = new(2014, 159, -3463, -1.27),
                    [nameof(Ayonat)] = new(3395, 159.9, -2349, 1.54),
                    [nameof(Tuanod)] = new(3445, 159.9, 2405, 0),
                    [nameof(Ammalael)] = new(533, 159, 485, -2.6),
                    [nameof(Deffensat)] = new(334, 159, -3609, -2.6),
                    [nameof(KareRoyal)] = new WorldCoords(3500, 159.9, -1285, 0),
                    [nameof(Ternoval)] = new WorldCoords(3775, 159.9, 791, -3.71)
                },
                [CastleInnerArea] = new ()
                {
                    [nameof(Triumfaler)] =  new(-1304, 1091, 1094, 0),
                    [nameof(Ayonat)] =      new(-1485, 1098, 1092, 1.57),
                    [nameof(Deffensat)] =   new(-1705, 1100, 1108, 3.14),
                    [nameof(Sabulat)] =     new(-1892, 1096, 1084, 0),
                    [nameof(Devanagari)] =  new(-2099, 1090, 1104, 3.14), // no dungeon?
                    [nameof(Kablak)] =      new(-2306, 1096, 1108, 3.14),
                    [nameof(Ammalael)] =    new(-2508, 1099, 1104, -2),
                    [nameof(Ternoval)] =    new(-2693, 1096, 1108, 3.14),
                    [nameof(Blessendor)] =  new(-2907, 1095, 1096, 4.71),
                    [nameof(KareRoyal)] =   new(-3097, 1095, 1091, 1.57),
                    [nameof(Peltier)] =     new(-3310, 1096, 1100, 4.71),
                    [nameof(Tuanod)] =      new(-3497, 1099, 1098, 1.57),
                    [nameof(Shatelier)] =   new(-3707, 1095, 1097, 4.71),
                    [nameof(Gedeon)] =      new(-3900, 1098, 1092, 0),
                    // different Z
                    [nameof(EikumKas)] =    new(-2100, 1096, -3998, 3.14),
                    [nameof(Lator)] =       new(-2300, 1096, -3910, 0),
                    [nameof(Aris)] =        new(-2489, 1099, -3897, -1.57),
                    [nameof(Fief)] =        new(-2697, 1095, -3907, 0), // no dungeon?
                    
                    // 3100 1100 -3900 tavern
                    [nameof(Liege)] =       new(-3302, 1099, -3898, 4.71),
                },
                [CastleDungeon] = new ()
                {
                    [nameof(Liege)] =       new( 104, 1699, -3500, 1.57),
                    [nameof(Aris)] =        new( 297, 1700, -3500, 4.71),
                    [nameof(Shatelier)] =   new( 500, 1700, -3497, 3.14),
                    [nameof(Peltier)] =     new( 700, 1700, -3499, 3.14),
                    [nameof(Lator)] =       new(1070, 1701, -3437, 3.14),
                    [nameof(EikumKas)] =    new(1300, 1700, -3565, 0),
                    [nameof(Gedeon)] =      new(1434, 1700, -3454, 4.71),
                    [nameof(Blessendor)] =  new(1633, 1700, -3469, 4.71),
                    [nameof(Kablak)] =      new(1833, 1700, -3544, 4.71),
                    // 2050 -1700 -3500 something
                    [nameof(Sabulat)] =     new(2288, 1701, -3455, 1.57),
                    [nameof(Ayonat)] =      new(2543, 1701, -3500, 4.71),
                    [nameof(Triumfaler)] =  new(2700, 1700, -3500, 0),
                    [nameof(Tuanod)] =      new(2901, 1701, -3500, 1.57),
                    [nameof(KareRoyal)] =   new(3143, 1701, -3500, 4.71),
                    [nameof(Ternoval)] =    new(3300, 1700, -3495, 3.11),
                    [nameof(Ammalael)] =    new(3499, 1701, -3501, 5.87),
                    [nameof(Deffensat)] =   new(3698, 1701, -3500, 4.8),
                    
                },
                [Other] = new ()
                {
                    ["ChoiceIsland"] = new(3307, 159, 3611, -1.27),
                    ["Arena"] = new(3648, 155, -3455)
                }
            },
            [Haron] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
            },
            [Phoebe] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [CityCenter] = new ()
                {
                    [nameof(Umrad)] = new (-1993, -104.5, 457),
                }
                
            },
            [Rodos] = new Dictionary<PoiType, Dictionary<string, WorldCoords>>
            {
                [CityCenter] = new ()
                {
                    [nameof(Anhelm)] = new(-3397, -340, -813, -3),
                },
                [CastleTeleportPoint] = new ()
                {
                    [nameof(IlSuilieRua)] = new(-1212, -356, -1703)
                },
                
            }
        };
    }
    public class WorldCoords
    {
        public readonly double x;
        public readonly double y;
        public readonly double z;
        public readonly double turn;

        public WorldCoords(double x1, double y1, double z1, double turn1 = 0)
        {
            x = x1;
            y = y1;
            z = z1;
            turn = turn1;
        }

        public string ToDebugString()
        {
            return "X: " + x + " Y: " + y + " Z: " + z + " Turn: " + turn;
        }
    }

    public static class CoordsHelper
    {
        public static byte[] EncodeServerCoordinate(double a)
        {
            var scale = 69;

            var a_abs = Math.Abs(a);
            var a_temp = a_abs;

            var steps = 0;

            if (((int)a_abs) == 0)
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

            var a_3 = (byte)(((a < 0 ? 1 : 0) << 7) + scale);
            var mul = Math.Pow(2, ((int)Math.Log(a_abs, 2)));
            var numToEncode = (int)(0b100000000000000000000000 * (a_abs / mul + 1));

            var a_2 = (byte)(((numToEncode & 0b111111110000000000000000) >> 16) + (steps % 2 == 1 ? 0b10000000 : 0));
            var a_1 = (byte)((numToEncode & 0b1111111100000000) >> 8);
            var a_0 = (byte)(numToEncode & 0b11111111);

            return new[] { a_0, a_1, a_2, a_3 };
        }

        public static double DecodeServerCoordinate(byte[] input, int shift = 0)
        {
            var a = BitHelper.GetArrayWithoutBitShift(input, shift);
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

            return sign * (1 + ((double)numToEncode) / 0b100000000000000000000000) * baseCoord;
        }

        public static double DecodeClientCoordinate(byte[] a)
        {
            var x_scale = ((a[4] & 0b11111) << 3) + ((a[3] & 0b11100000) >> 5);

            if (x_scale == 126)
            {
                return 0.0;
            }

            var baseCoord = Math.Pow(2, x_scale - 127);
            var sign = (a[4] & 0b100000) > 0 ? -1 : 1;

            return ((1 + ((float)(((a[3] & 0b11111) << 18) + (a[2] << 10) + (a[1] << 2) +
                                  ((a[0] & 0b11000000) >> 6))) / 0b100000000000000000000000) * baseCoord) * sign;
        }

        public static WorldCoords GetCoordsFromPingBytes(byte[] rcvBuffer)
        {
            var x = DecodeClientCoordinate(rcvBuffer.AsSpan(21, 5).ToArray());
            var y = -DecodeClientCoordinate(rcvBuffer.AsSpan(25, 5).ToArray());
            var z = DecodeClientCoordinate(rcvBuffer.AsSpan(29, 5).ToArray());
            var turn = DecodeClientCoordinate(rcvBuffer.AsSpan(33, 5).ToArray());

            return new WorldCoords(x, y, z, turn);
        }
    }
}