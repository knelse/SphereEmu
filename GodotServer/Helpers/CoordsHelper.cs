using System;

namespace SphServer.Helpers
{

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

        public static WorldCoords ShipstoneCenter => new (2614, 157.8, 1293);
        public static WorldCoords BangvilleCenter => new (1882, 155.6, -407);
        public static WorldCoords SunpoolCenter => new (422, 153.3, -1284);
        public static WorldCoords TorwealCenter => new (2292, 155.3, -2386);
        public static WorldCoords UmradCenter => new (-1993, -104.5, 457);
        public static WorldCoords AnhelmCenter => new(-3397, -340, -813, -3);
        public static WorldCoords IsleOfChoice => new(3307, 159, 3611, -1.27);
        public static WorldCoords Arena => new(3648, 155, -3455);
        public static WorldCoords RespawnPointSunpoolBad => new(3648, 155, -3455);
        public static WorldCoords TpPointHyperionCemeteryBangville => new(-3753, 1509, 100, 1.6);
        public static WorldCoords TpPointHyperionCemeteryShipstone => new(-3900, 1509, 106, 3.1);
        public static WorldCoords TpPointHyperionSharedDungeonLatorKablak => new(-300, 1500, 106, 3.1415);
        public static WorldCoords TpPointHyperionSharedDungeonAmmalael => new(33, 1500, 145, 4.85);
        public static WorldCoords TpPointHyperionSharedDungeonFiefCopperSpawn => new(-99, 1500, 33, 5.87);
        public static WorldCoords TpPointHyperionSharedDungeonSunpoolKarmaNorthern => new(-2105, 1498, 100, 4.8);
        public static WorldCoords TpPointHyperionSharedDungeonBangville => new(-1500, 1499, 105, 3);
        public static WorldCoords TpPointHyperionSharedDungeonChoice => new(1299, 1499, 35, 5.87);
        public static WorldCoords TpPointHyperionSharedDungeonKareRoyalNorth => new(-1293, 1499, 100, 1.57);
        public static WorldCoords TpPointHyperionSharedDungeonKareRoyalSouth => new(-1061, 1499, 100, 4.68);
        public static WorldCoords TpPointHyperionNothernRoad => new(3031, 159, 2095, -2.5);
        public static WorldCoords TpPointHyperionHorthWesternEdge => new(502, 159, 1132, 4.6);
        public static WorldCoords TpPointHyperionShipstoneSunpoolRoad => new(1902, 159, 1161, 4.6);
        public static WorldCoords TpPointHyperionShipstoneBangvilleRoad => new(2107, 159, 936, -7.3);
        public static WorldCoords TpPointHyperionNorthEast => new(3785, 159, 918, -5.8);
        public static WorldCoords TpPointHyperionUmradForest => new(2256, 159, 614, 3.14);
        public static WorldCoords TpPointHyperionSilverForest => new(945, 159, 505, 3.36);
        public static WorldCoords TpPointHyperionShipstoneTorwealRoad => new(2904, 159, 550, 11);
        public static WorldCoords TpPointHyperionShipstoneBangvilleMountains => new(2592, 159, 365, -1.5);
        public static WorldCoords TpPointHyperionTemerLake => new(3726, 159, -57, -1.2);
        public static WorldCoords TpPointHyperionHeberIslandTemerLake => new(3371, 161, -374, -5.87);
        public static WorldCoords TpPointHyperionSunpoolShipstoneRoadNearVortexLake => new(316, 159, -285, 3.14);
        public static WorldCoords TpPointHyperionSunpoolBangvilleRoadNearVortexLake => new(1303, 159, -355, -0.16);
        public static WorldCoords TpPointHyperionSunpooPhorosIslandVortexLake => new(751, 160, -653, 5.87);
        public static WorldCoords TpPointHyperionSunpooDeirosIslandVortexLake => new(1368, 160, -1525, 5.87);
        public static WorldCoords TpPointHyperionNereyRiverMouth => new(1606, 159, -693, 1.57);
        public static WorldCoords TpPointHyperionEasternForest => new(3652, 164, -1533, 5.87);
        public static WorldCoords TpPointHyperionDiomaRiverMouth => new(2227, 159, -1684, 3.14);
        public static WorldCoords TpPointHyperionHortonForest => new(300, 159, -1945, 0);
        public static WorldCoords TpPointCastleIlSu => new(-1212, -356, -1703);
        public static WorldCoords TpPointCastleAmmalael => new(533, 159, 485, -2.6);
        public static WorldCoords TpPointCastleAmmalaelInnerArea => new(-2508, 1099, 1104, -2);
        public static WorldCoords TpPointCastleAmmalaelDungeon => new(3499, 1701, -3501, 5.87);
        public static WorldCoords TpPointCastleDeffensat => new(334, 159, -3609, -2.6);
        public static WorldCoords TpPointCastleDeffensatInnerArea => new(-1705, 1100, 1108, 3.14);
        public static WorldCoords TpPointCastleDeffensatDungeon => new(3698, 1701, -3500, 4.8);
        public static WorldCoords TpPointCastleAris => new(3016, 158.5, 1054, -1.51);
        public static WorldCoords TpPointCastleArisInnerArea => new(-2489, 1099, -3897, -1.57);
        public static WorldCoords TpPointCastleArisDungeon => new(297, 1700, -3500, 4.71);
        public static WorldCoords Test => new (-7, 19, 2987);
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