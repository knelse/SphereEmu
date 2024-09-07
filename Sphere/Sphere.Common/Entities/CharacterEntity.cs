using Sphere.Common.Enums;
using Sphere.Common.Helpers;
using Sphere.Common.Helpers.Extensions;
using Sphere.Common.Models;
using System.Numerics;
using System.Text;

namespace Sphere.Common.Entities
{
    public class CharacterEntity : BaseEntity
    {
        public static readonly int[] HealthAtTitle =
        [
            100, 150, 200, 250, 300, 350, 400, 450, 500, 550, 600, 650, 700, 750, 800, 850, 900, 950, 1000, 1050, 1100,
            1150, 1200, 1250, 1300, 1350, 1400, 1450, 1500, 1550, 1600, 1650, 1700, 1750, 1800, 1850, 1900, 1950, 2000,
            2050, 2100, 2150, 2200, 2250, 2300, 2350, 2400, 2450, 2500, 2550, 2600, 2650, 2700, 2750, 2800, 2850, 2900,
            2950, 3000, 3200
        ];

        public static readonly int[] HealthAtDegree =
        [
            100, 110, 120, 130, 150, 160, 170, 180, 190, 210, 220, 230, 240, 250, 270, 280, 290, 300, 310, 330, 340, 350,
            360, 370, 390, 400, 410, 420, 430, 450, 460, 470, 480, 490, 510, 520, 530, 540, 550, 570, 580, 590, 600, 610,
            630, 640, 650, 660, 670, 690, 700, 710, 720, 730, 750, 760, 770, 780, 790, 800
        ];

        public static readonly int[] MpAtTitle =
        [
            100, 100, 100, 100, 125, 125, 125, 125, 125, 150, 150, 150, 150, 150, 175, 175, 175, 175, 175, 200, 200, 200,
            200, 200, 225, 225, 225, 225, 225, 250, 250, 250, 250, 250, 275, 275, 275, 275, 275, 300, 300, 300, 300, 300,
            325, 325, 325, 325, 325, 350, 350, 350, 350, 350, 375, 375, 375, 375, 375, 400
        ];

        public static readonly int[] MpAtDegree =
        [
            100, 175, 250, 325, 400, 475, 550, 625, 700, 775, 850, 925, 1000, 1075, 1150, 1225, 1300, 1375, 1450, 1525,
            1600, 1675, 1750, 1825, 1900, 1975, 2050, 2125, 2200, 2275, 2350, 2425, 2500, 2575, 2650, 2725, 2800, 2875,
            2950, 3025, 3100, 3175, 3250, 3325, 3400, 3475, 3550, 3625, 3700, 3775, 3850, 3925, 4000, 4075, 4150, 4225,
            4300, 4375, 4450, 4650
        ];

        public static readonly int[] AvailableStatsPrimary =
        [
            4, 4, 4, 4, 6, 6, 6, 6, 6, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 12, 12, 12, 12,
            12, 12, 12, 12, 12, 12, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 16, 16, 16, 16, 16, 16, 16, 16, 18, 18, 20
        ];

        public static readonly int[] AvailableStatsSecondary =
        [
            0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 4, 0, 0, 0, 0, 4, 0, 0, 0, 0, 6, 0, 0,
            0, 0, 6, 0, 0, 0, 0, 8, 0, 0, 0, 0, 8, 0, 0, 0, 0, 10, 0, 0, 0, 0, 10
        ];

        public static readonly int[] StatBonusForResets =
        [
            1, 1, 1, 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 1, 1, 2, 1,
            1, 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 1, 1
        ];

        public static readonly int[] XpPerLevelBase =
        [
            50, 320, 1080, 2880, 7000, 10800, 15680, 22080, 29570, 39500, 50820, 64800, 81120, 100940, 123750, 151040,
            182070, 218700, 259920, 310000, 363800, 428300, 499900, 584600, 678100, 784200, 904000, 1038800, 1194200,
            1368000, 1561600, 1781800, 2025500, 2300400, 2609300, 2954900, 3340400, 3768800, 4251200, 4808000, 5430000,
            6121000, 6897000, 7763000, 8809000, 9988000, 11321000, 12810000, 14478000, 16513000, 18805000, 21416000,
            24354000, 27687000, 33895000, 41458000, 50684000, 61931000, 75607000, 92268000
        ];

        public static readonly double[] XpPerLevelDelta =
        [
            0, 40, 90, 180, 350, 450, 560, 690, 821.2, 987.4, 1155, 1350, 1560, 1802.5, 2062.5, 2360, 2677.5, 3037.5, 3420,
            3875, 5067.2, 6521.9, 8204.9, 10230.5, 12544.8, 15231.6, 18331.1, 21889, 26045.9, 30780, 36143.5, 42317.7,
            49256.5, 57171.7, 66164.4, 76334.9, 87798.4, 100666.6, 115272.9, 132220, 151311.6, 172699.7, 196885.3, 224068.4,
            256929.2, 294211.7, 336619.1, 384300, 438033.4, 503646.5, 577977.2, 663072.4, 759339.3, 869064.2, 1070773.9,
            1317772.2, 1620554.1, 1991402, 2444412.8, 2998709.8
        ];

        public Guid PlayerId { get; set; }

        public int Index { get; set; }

        public string Nickname { get; set; }

        public Gender Gender { get; set; }

        public int FaceType { get; set; }

        public int HairStyle { get; set; }

        public int HairColor { get; set; }

        public int Tattoo { get; set; }

        public int Satiety { get; set; } = 100;

        public int Karma { get; set; } = 0;

        public KarmaTier KarmaTier { get; set; } = KarmaTier.Neutral;

        public Experience Experience { get; set; } = new();

        public Level Level { get; set; } = new();

        public Attributes NaturalAttributes { get; set; } = new();

        public int CurrentHP { get; set; } = 100;

        public int BaseHP => HealthAtTitle[Level.RealTitleLvl] + HealthAtDegree[Level.RealDegreeLvl];

        public int MaxHP { get; set; } = 100;

        public int CurrentMP { get; set; } = 100;

        public int BaseMP => MpAtDegree[Level.RealDegreeLvl] + MpAtTitle[Level.RealTitleLvl];

        public int MaxMP { get; set; } = 100;

        public Coordinates Coordinates { get; set; }

        public Doll Doll { get; set; } = new Doll();

        public int Money { get; set; }

        public OccupationModel Occupation { get; set; } = new();

        public Clan Clan { get; set; }

        public ClanRank ClanRank { get; set; } = ClanRank.Neophyte;

        public ushort PDef { get; set; }

        public ushort MDef { get; set; }

        public int PAtk { get; set; }
        public int MAtk { get; set; }

        public int MaxSatiety => 100;

        public int SpareDegreePoints => TotalDegreeStatsPossible - NaturalAttributes.TotalDegree;

        public int SpareTitlePoints => TotalTitleStatsPossible - NaturalAttributes.TotalTitle;

        public int TotalTitleStatsPossible => 
            SumUpToLevel(AvailableStatsPrimary, Level.RealTitleLvl) +
            SumUpToLevel(AvailableStatsSecondary, Level.RealDegreeLvl) +
            (SumUpToLevel(StatBonusForResets, Level.RealTitleLvl) * Level.TitleRebirthCount);

        public int TotalDegreeStatsPossible =>
           SumUpToLevel(AvailableStatsPrimary, Level.RealDegreeLvl) +
           SumUpToLevel(AvailableStatsSecondary, Level.RealDegreeLvl) +
           (SumUpToLevel(StatBonusForResets, Level.RealDegreeLvl) * Level.DegreeRebirthCount);

        public byte[] ToBytes(ushort clientId)
        {
            var nameEncoded = Encoding.GetEncoding(1251).GetBytes(Nickname);
            var x = CoordinatesHelper.EncodeServerCoordinate(Coordinates.X);
            var y = CoordinatesHelper.EncodeServerCoordinate(-Coordinates.Y);
            var z = CoordinatesHelper.EncodeServerCoordinate(Coordinates.Z);
            var t = CoordinatesHelper.EncodeServerCoordinate(Coordinates.Angle);
            var nameLen = nameEncoded.Length + 1;
            var data = new List<byte>
            {
                0x00,
                0x01,
                0x2C,
                0x01,
                0x00,
                0x00,
                0x04,
                BitHelper.MajorByte(clientId),
                BitHelper.MinorByte(clientId),
                0x08,
                0x00,
                (byte) (((nameLen & 0b111) << 5) + 2),
                (byte) (((nameEncoded[0] & 0b111) << 5) + ((nameLen & 0b11111000) >> 3))
            };

            for (var i = 1; i < nameEncoded.Length; i++)
            {
                data.Add((byte)(((nameEncoded[i] & 0b111) << 5) + ((nameEncoded[i - 1] & 0b11111000) >> 3)));
            }

            data.Add((byte)((nameEncoded[^1] & 0b11111000) >> 3));

            if (Clan?.Id == null || Clan?.Id == Clan.DefaultClan.Id)
            {
                data.Add(0x00);
                data.Add(0x6E);
            }
            else
            {
                var clanNameEncoded = Encoding.GetEncoding(1251).GetBytes(Clan!.Name);
                var clanNameLength = clanNameEncoded.Length;
                data.Add((byte)((clanNameLength & 0b111) << 5));
                data.Add((byte)(((clanNameEncoded[0] & 0b1111111) << 1) + ((clanNameLength & 0b1000) >> 3)));

                for (var i = 1; i < clanNameLength; i++)
                {
                    data.Add((byte)(((clanNameEncoded[i] & 0b1111111) << 1) +
                                     ((clanNameEncoded[i - 1] & 0b10000000) >> 7)));
                }

                data.Add((byte)(0b01100000 + ((byte)ClanRank << 1) + ((clanNameEncoded[^1] & 0b10000000) >> 7)));
            }

            data.Add(0x1A);
            data.Add(0x98);
            data.Add(0x18);
            data.Add(0x19);
            data.AddRange(x);
            data.AddRange(y);
            data.AddRange(z);
            data.AddRange(t);
            data.Add(0x37);
            data.Add(0x0D);
            data.Add(0x79);
            data.Add(0x00);
            data.Add(0xF0);
            data.AddRange(Doll.ToBytes());
            data.Add(0xF0);
            
            data.AddRange(Enumerable.Repeat((byte)0x00, 150));

            data.Add((byte)(((CurrentHP & 0b111) << 5) + 0b10011));
            data.Add((byte)((CurrentHP & 0b11111111000) >> 3));
            data.Add((byte)(((MaxHP & 0b11) << 6) + (0b100 << 3) + ((CurrentHP & 0b11100000000000) >> 11)));
            data.Add((byte)((MaxHP & 0b1111111100) >> 2));
            data.Add((byte)(((byte)Karma << 4) + ((MaxHP & 0b11110000000000) >> 10)));
            data.AddRange(Level.ToBytes());
            data.Add(0x80);

            data.AddRange(Occupation.ToBytes());

            data.Add((byte)(((Money & 0b1111) << 4) + (Occupation.Level - 1)));
            data.Add((byte)((Money & 0b111111110000) >> 4));
            data.Add((byte)((Money & 0b11111111000000000000) >> 12));
            data.Add((byte)((Money & 0b1111111100000000000000000000) >> 20));
            data.Add((byte)((Money & 0b11110000000000000000000000000000) >> 28));

            var arr = data.ToArray();
            arr[0] = (byte)arr.Length;

            return arr;
        }

        public byte[] ToCharacterListByteArray(ushort clientId)
        {
            var nameEncodedWithPadding = new byte[19];
            var nameEncoded = Encoding.GetEncoding(1251).GetBytes(Nickname);
            Array.Copy(nameEncoded, nameEncodedWithPadding, nameEncoded.Length);

            // 0x79 - look type
            var hpMax1 = (byte)(((MaxHP & 0b111111) << 2) + 1);
            var hpMax2 = (byte)((MaxHP & 0b11111111000000) >> 6);
            var mpMax1 = (byte)(((MaxMP & 0b111111) << 2) + ((MaxHP & 0b1100000000000000) >> 14));
            var mpMax2 = (byte)((MaxMP & 0b11111111000000) >> 6);
            var strength1 = (byte)(((NaturalAttributes.Strength & 0b111111) << 2) + ((MaxMP & 0b1100000000000000) >> 14));
            var strength2 = (byte)((NaturalAttributes.Strength & 0b11111111000000) >> 6);
            var agility1 = (byte)(((NaturalAttributes.Dexterity & 0b111111) << 2) + ((NaturalAttributes.Strength & 0b1100000000000000) >> 14));
            var agility2 = (byte)((NaturalAttributes.Dexterity & 0b11111111000000) >> 6);
            var accuracy1 = (byte)(((NaturalAttributes.Accuracy & 0b111111) << 2) + ((NaturalAttributes.Dexterity & 0b1100000000000000) >> 14));
            var accuracy2 = (byte)((NaturalAttributes.Accuracy & 0b11111111000000) >> 6);
            var endurance1 = (byte)(((NaturalAttributes.Endurance & 0b111111) << 2) + ((NaturalAttributes.Accuracy & 0b1100000000000000) >> 14));
            var endurance2 = (byte)((NaturalAttributes.Endurance & 0b11111111000000) >> 6);
            var earth1 = (byte)(((NaturalAttributes.Earth & 0b111111) << 2) + ((NaturalAttributes.Endurance & 0b1100000000000000) >> 14));
            var earth2 = (byte)((NaturalAttributes.Earth & 0b11111111000000) >> 6);
            var air1 = (byte)(((NaturalAttributes.Air & 0b111111) << 2) + ((NaturalAttributes.Earth & 0b1100000000000000) >> 14));
            var air2 = (byte)((NaturalAttributes.Air & 0b11111111000000) >> 6);
            var water1 = (byte)(((NaturalAttributes.Water & 0b111111) << 2) + ((NaturalAttributes.Air & 0b1100000000000000) >> 14));
            var water2 = (byte)((NaturalAttributes.Water & 0b11111111000000) >> 6);
            var fire1 = (byte)(((NaturalAttributes.Fire & 0b111111) << 2) + ((NaturalAttributes.Water & 0b1100000000000000) >> 14));
            var fire2 = (byte)((NaturalAttributes.Fire & 0b11111111000000) >> 6);
            var pdef1 = (byte)(((PDef & 0b111111) << 2) + ((NaturalAttributes.Fire & 0b1100000000000000) >> 14));
            var pdef2 = (byte)((PDef & 0b11111111000000) >> 6);
            var mdef1 = (byte)(((MDef & 0b111111) << 2) + ((PDef & 0b1100000000000000) >> 14));
            var mdef2 = (byte)((MDef & 0b11111111000000) >> 6);
            var karma1 = (byte)((((byte)Karma & 0b111111) << 2) + ((MDef & 0b1100000000000000) >> 14));
            var satietyMax1 = (byte)(((MaxSatiety & 0b111111) << 2) + (((byte)Karma & 0b11000000) >> 14));
            var satietyMax2 = (byte)((MaxSatiety & 0b11111111000000) >> 6);
            var titleLvl1 = (byte)(((Level.TitleLvl & 0b111111) << 2) + ((MaxSatiety & 0b1100000000000000) >> 14));
            var titleLvl2 = (byte)((Level.TitleLvl & 0b11111111000000) >> 6);
            var degreeLvl1 = (byte)(((Level.DegreeLvl & 0b111111) << 2) +
                                     ((Level.TitleLvl & 0b1100000000000000) >> 14));
            var degreeLvl2 = (byte)((Level.DegreeLvl & 0b11111111000000) >> 6);
            var titleXp1 = (byte)(((1 & 0b111111) << 2) + ((Level.DegreeLvl & 0b1100000000000000) >> 14));
            var titleXp2 = (byte)((1 & 0b11111111000000) >> 6);
            var titleXp3 = (byte)((1 & 0b1111111100000000000000) >> 14);
            var titleXp4 = (byte)((1 & 0b111111110000000000000000000000) >> 22);
            var degreeXp1 = (byte)(((1 & 0b111111) << 2) + ((1 & 0b11000000000000000000000000000000) >> 30));
            var degreeXp2 = (byte)((1 & 0b11111111000000) >> 6);
            var degreeXp3 = (byte)((1 & 0b1111111100000000000000) >> 14);
            var degreeXp4 = (byte)((1 & 0b111111110000000000000000000000) >> 22);
            var satietyCurrent1 = (byte)(((Satiety & 0b111111) << 2) +
                                          ((1 & 0b11000000000000000000000000000000) >> 30));
            var satietyCurrent2 = (byte)((Satiety & 0b11111111000000) >> 6);
            var hpCurrent1 = (byte)(((CurrentHP & 0b111111) << 2) + ((Satiety & 0b1100000000000000) >> 14));
            var hpCurrent2 = (byte)((CurrentHP & 0b11111111000000) >> 6);
            var mpCurrent1 = (byte)(((CurrentMP & 0b111111) << 2) + ((CurrentHP & 0b1100000000000000) >> 14));
            var mpCurrent2 = (byte)((CurrentMP & 0b11111111000000) >> 6);
            var titleStats1 =
                (byte)(((SpareTitlePoints & 0b111111) << 2) + ((CurrentMP & 0b1100000000000000) >> 14));
            var titleStats2 = (byte)((SpareTitlePoints & 0b11111111000000) >> 6);
            var degreeStats1 = (byte)(((SpareDegreePoints & 0b111111) << 2) +
                                       ((SpareDegreePoints & 0b1100000000000000) >> 14));
            var degreeStats2 = (byte)((SpareDegreePoints & 0b11111111000000) >> 6);
            var degreeStats3 = (byte)((0b111010 << 2) + ((SpareDegreePoints & 0b1100000000000000) >> 14));
            var isFemale1 = (byte)((byte)Gender << 2);
            var name1 = (byte)((nameEncodedWithPadding[0] & 0b111111) << 2);
            var name2 = (byte)(((nameEncodedWithPadding[1] & 0b111111) << 2) +
                                ((nameEncodedWithPadding[0] & 0b11000000) >> 6));
            var name3 = (byte)(((nameEncodedWithPadding[2] & 0b111111) << 2) +
                                ((nameEncodedWithPadding[1] & 0b11000000) >> 6));
            var name4 = (byte)(((nameEncodedWithPadding[3] & 0b111111) << 2) +
                                ((nameEncodedWithPadding[2] & 0b11000000) >> 6));
            var name5 = (byte)(((nameEncodedWithPadding[4] & 0b111111) << 2) +
                                ((nameEncodedWithPadding[3] & 0b11000000) >> 6));
            var name6 = (byte)(((nameEncodedWithPadding[5] & 0b111111) << 2) +
                                ((nameEncodedWithPadding[4] & 0b11000000) >> 6));
            var name7 = (byte)(((nameEncodedWithPadding[6] & 0b111111) << 2) +
                                ((nameEncodedWithPadding[5] & 0b11000000) >> 6));
            var name8 = (byte)(((nameEncodedWithPadding[7] & 0b111111) << 2) +
                                ((nameEncodedWithPadding[6] & 0b11000000) >> 6));
            var name9 = (byte)(((nameEncodedWithPadding[8] & 0b111111) << 2) +
                                ((nameEncodedWithPadding[7] & 0b11000000) >> 6));
            var name10 = (byte)(((nameEncodedWithPadding[9] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[8] & 0b11000000) >> 6));
            var name11 = (byte)(((nameEncodedWithPadding[10] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[9] & 0b11000000) >> 6));
            var name12 = (byte)(((nameEncodedWithPadding[11] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[10] & 0b11000000) >> 6));
            var name13 = (byte)(((nameEncodedWithPadding[12] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[11] & 0b11000000) >> 6));
            var name14 = (byte)(((nameEncodedWithPadding[13] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[12] & 0b11000000) >> 6));
            var name15 = (byte)(((nameEncodedWithPadding[14] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[13] & 0b11000000) >> 6));
            var name16 = (byte)(((nameEncodedWithPadding[15] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[14] & 0b11000000) >> 6));
            var name17 = (byte)(((nameEncodedWithPadding[16] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[15] & 0b11000000) >> 6));
            var name18 = (byte)(((nameEncodedWithPadding[17] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[16] & 0b11000000) >> 6));
            var name19 = (byte)(((nameEncodedWithPadding[18] & 0b111111) << 2) +
                                 ((nameEncodedWithPadding[17] & 0b11000000) >> 6));

            var face1 = (byte)(((FaceType & 0b111111) << 2) + ((nameEncodedWithPadding[18] & 0b11000000) >> 6));
            var hairStyle1 = (byte)(((HairStyle & 0b111111) << 2) + ((FaceType & 0b11000000) >> 6));
            var hairColor1 = (byte)(((HairColor & 0b111111) << 2) + ((HairStyle & 0b11000000) >> 6));
            var tattoo1 = (byte)(((Tattoo & 0b111111) << 2) + ((HairColor & 0b11000000) >> 6));
            var bootsModelId = (byte)(((0 & 0b111111) << 2) + ((Tattoo & 0b11000000) >> 6));
            var pantsModelId = (byte)(((0 & 0b111111) << 2) + ((0 & 0b11000000) >> 6));
            var armorModelId = (byte)(((0 & 0b111111) << 2) + ((0 & 0b11000000) >> 6));
            var helmetModelId = (byte)(((0 & 0b111111) << 2) + ((0 & 0b11000000) >> 6));
            var glovesModelId1 = (byte)(((0 & 0b111111) << 2) + ((0 & 0b11000000) >> 6));
            var glovesModelId2 = (byte)((0 & 0b11000000) >> 6);
            var isNotDeleted1 = (byte)(((false ? 1 : 0) << 1) + 1);

            var lookType = (byte)(true ? 0x79 : 0x19);

            var charDataBytes = new byte[]
            {
            0x6C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, BitHelper.MajorByte(clientId), BitHelper.MinorByte(clientId), 0x08, 0x40,
            0x60, lookType, hpMax1, hpMax2, mpMax1, mpMax2, strength1, strength2, agility1, agility2, accuracy1,
            accuracy2, endurance1, endurance2, earth1, earth2, air1, air2, water1, water2, fire1, fire2, pdef1,
            pdef2, mdef1, mdef2, karma1, satietyMax1, satietyMax2, titleLvl1, titleLvl2, degreeLvl1, degreeLvl2,
            titleXp1, titleXp2, titleXp3, titleXp4, degreeXp1, degreeXp2, degreeXp3, degreeXp4, satietyCurrent1,
            satietyCurrent2, hpCurrent1, hpCurrent2, mpCurrent1, mpCurrent2, titleStats1, titleStats2, degreeStats1,
            degreeStats2, degreeStats3, 0xC0, 0xC8, 0xC8, isFemale1, name1, name2, name3, name4, name5, name6,
            name7, name8, name9, name10, name11, name12, name13, name14, name15, name16, name17, name18, name19,
            face1, hairStyle1, hairColor1, tattoo1, bootsModelId, pantsModelId, armorModelId, helmetModelId,
            glovesModelId1, glovesModelId2, 0xC0, 0xC0, 0x00, 0xFC, 0xFF, 0xFF, 0xFF, isNotDeleted1, 0x00, 0x00,
            0x00, 0x00
            };

            return charDataBytes;
        }

        private static T SumUpToLevel<T>(T[] array, int level) where T : INumber<T>
        {
            return array[..(level + 1)].Aggregate((x, y) => x + y);
        }
    }
}