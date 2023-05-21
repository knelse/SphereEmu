using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using SphServer.Helpers;
using static SphServer.Helpers.BitHelper;
using static SphServer.Helpers.CharacterDataHelper;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SphServer.DataModels
{
    // TODO: skip unnecessary fields for serialization
    public class Character
    {
        public int Id { get; set; }
        [BsonIgnore]
        public int ClientLocalId { get; set; }
        public byte LookType { get; set; }
        public byte IsTurnedOff { get; set; }
        public ushort MaxMP { get; set; }
        public ushort BaseStrength { get; set; }
        public ushort CurrentStrength { get; set; }
        public ushort BaseAgility { get; set; }
        public ushort CurrentAgility { get; set; }
        public ushort BaseAccuracy { get; set; }
        public ushort CurrentAccuracy { get; set; }
        public ushort BaseEndurance { get; set; }
        public ushort CurrentEndurance { get; set; }
        public ushort BaseEarth { get; set; }
        public ushort CurrentEarth { get; set; }
        public ushort BaseAir { get; set; }
        public ushort CurrentAir { get; set; }
        public ushort BaseWater { get; set; }
        public ushort CurrentWater { get; set; }
        public ushort BaseFire { get; set; }
        public ushort CurrentFire { get; set; }
        public ushort MaxSatiety { get; set; }
        public uint TitleXP { get; set; }
        public uint DegreeXP { get; set; }
        public ushort CurrentSatiety { get; set; }
        public ushort CurrentMP { get; set; }
        public ushort AvailableTitleStats { get; set; }
        public ushort AvailableDegreeStats { get; set; }
        public bool IsGenderFemale { get; set; }
        public string Name { get; set; } = "Test";
        [BsonRef("Clans")] 
        public Clan? Clan { get; set; } = Clan.DefaultClan;
        public byte FaceType { get; set; }
        public byte HairStyle { get; set; }
        public byte HairColor { get; set; }
        public byte Tattoo { get; set; }
        public byte BootModelId { get; set; }
        public byte PantsModelId { get; set; }
        public byte ArmorModelId { get; set; }
        public byte HelmetModelId { get; set; }
        public byte GlovesModelId { get; set; }
        public bool IsNotQueuedForDeletion { get; set; } = true;
        public int Money { get; set; }
        public int GuildLevelMinusOne { get; set; }
        public Guild Guild { get; set; } = Guild.None;
        public ClanRank ClanRank { get; set; } = ClanRank.Neophyte;
        public ushort ClientIndex { get; set; }
        public double X { get; set; }
        public double Y { get; set; } = 150;
        public double Z { get; set; }
        public double Angle { get; set; }
        public int TitleMinusOne { get; set; }
        public int DegreeMinusOne { get; set; }
        public ushort CurrentHP { get; set; } = 100;
        public ushort MaxHP { get; set; } = 100;
        public ushort PDef { get; set; }
        public ushort MDef { get; set; }
        public KarmaTier Karma { get; set; } = KarmaTier.Neutral;
        public Dictionary<BelongingSlot, int> Items { get; set; } = new();
        public int PAtk { get; set; }
        public int MAtk { get; set; }
        public int KarmaCount { get; set; }

        public int MaxHPBase => HealthAtTitle[TitleMinusOne % 60] + HealthAtDegree[DegreeMinusOne % 60] - 100;
        public int MaxMPBase => MpAtTitle[TitleMinusOne % 60] + MpAtDegree[DegreeMinusOne % 60] - 100;
        public ulong XpToLevelUp => GetXpToLevelUp();
        
        public readonly Item Fists = new ()
        {
            ObjectKind = GameObjectKind.Fists,
            ObjectType = GameObjectType.Fists
        };

        public Character()
        {
            LookType = 0x7;
            IsTurnedOff = 0x9;
            CurrentHP = (ushort) MaxHPBase;
            MaxHP = (ushort) MaxHPBase;
            CurrentMP = (ushort) MaxMPBase;
            MaxMP = (ushort) MaxMPBase;
            CurrentSatiety = 100;
            MaxSatiety = 100;
            AvailableDegreeStats = (ushort) AvailableStatsPrimary[0];
            AvailableTitleStats = (ushort) AvailableStatsPrimary[0];
            Fists.Id = MainServer.ItemCollection.Insert(Fists);
        }

        public void LevelUp(int newTitleLevel, int newDegreeLevel)
        {
            if (newTitleLevel > TitleMinusOne)
            {
                var bonusStatsFromReset = TitleMinusOne / 60 * StatBonusForResets[TitleMinusOne];
                AvailableTitleStats = (ushort)(AvailableTitleStats + AvailableStatsPrimary[newTitleLevel] + bonusStatsFromReset);
                AvailableDegreeStats = (ushort)(AvailableDegreeStats + AvailableStatsSecondary[newTitleLevel]);
            }
            else if (newDegreeLevel > DegreeMinusOne)
            {
                var bonusStatsFromReset = DegreeMinusOne / 60 * StatBonusForResets[DegreeMinusOne];
                AvailableDegreeStats = (ushort)(AvailableDegreeStats + AvailableStatsPrimary[newTitleLevel] + bonusStatsFromReset);
                AvailableTitleStats = (ushort)(AvailableTitleStats + AvailableStatsSecondary[newTitleLevel]);
                
            }
        }

        public byte[] ToCharacterListByteArray()
        {
            var nameEncodedWithPadding = new byte[19];
            var nameEncoded = MainServer.Win1251!.GetBytes(Name);
            Array.Copy(nameEncoded, nameEncodedWithPadding, nameEncoded.Length);

            // 0x79 - look type
            var hpMax1 = (byte)(((MaxHP & 0b111111) << 2) + 1);
            var hpMax2 = (byte)((MaxHP & 0b11111111000000) >> 6);
            var mpMax1 = (byte)(((MaxMP & 0b111111) << 2) + ((MaxHP & 0b1100000000000000) >> 14));
            var mpMax2 = (byte)((MaxMP & 0b11111111000000) >> 6);
            var strength1 = (byte)(((CurrentStrength & 0b111111) << 2) + ((MaxMP & 0b1100000000000000) >> 14));
            var strenth2 = (byte)((CurrentStrength & 0b11111111000000) >> 6);
            var agility1 = (byte)(((CurrentAgility & 0b111111) << 2) + ((CurrentStrength & 0b1100000000000000) >> 14));
            var agility2 = (byte)((CurrentAgility & 0b11111111000000) >> 6);
            var accuracy1 = (byte)(((CurrentAccuracy & 0b111111) << 2) + ((CurrentAgility & 0b1100000000000000) >> 14));
            var accuracy2 = (byte)((CurrentAccuracy & 0b11111111000000) >> 6);
            var endurance1 = (byte)(((CurrentEndurance & 0b111111) << 2) + ((CurrentAccuracy & 0b1100000000000000) >> 14));
            var endurance2 = (byte)((CurrentEndurance & 0b11111111000000) >> 6);
            var earth1 = (byte)(((CurrentEarth & 0b111111) << 2) + ((CurrentEndurance & 0b1100000000000000) >> 14));
            var earth2 = (byte)((CurrentEarth & 0b11111111000000) >> 6);
            var air1 = (byte)(((CurrentAir & 0b111111) << 2) + ((CurrentEarth & 0b1100000000000000) >> 14));
            var air2 = (byte)((CurrentAir & 0b11111111000000) >> 6);
            var water1 = (byte)(((CurrentWater & 0b111111) << 2) + ((CurrentAir & 0b1100000000000000) >> 14));
            var water2 = (byte)((CurrentWater & 0b11111111000000) >> 6);
            var fire1 = (byte)(((CurrentFire & 0b111111) << 2) + ((CurrentWater & 0b1100000000000000) >> 14));
            var fire2 = (byte)((CurrentFire & 0b11111111000000) >> 6);
            var pdef1 = (byte)(((PDef & 0b111111) << 2) + ((CurrentFire & 0b1100000000000000) >> 14));
            var pdef2 = (byte)((PDef & 0b11111111000000) >> 6);
            var mdef1 = (byte)(((MDef & 0b111111) << 2) + ((PDef & 0b1100000000000000) >> 14));
            var mdef2 = (byte)((MDef & 0b11111111000000) >> 6);
            var karma1 = (byte)(((((byte)Karma) & 0b111111) << 2) + ((MDef & 0b1100000000000000) >> 14));
            var satietyMax1 = (byte)(((MaxSatiety & 0b111111) << 2) + ((((byte)Karma) & 0b11000000) >> 14));
            var satietyMax2 = (byte)((MaxSatiety & 0b11111111000000) >> 6);
            var titleLvl1 = (byte)(((TitleMinusOne & 0b111111) << 2) + ((MaxSatiety & 0b1100000000000000) >> 14));
            var titleLvl2 = (byte)((TitleMinusOne & 0b11111111000000) >> 6);
            var degreeLvl1 = (byte)(((DegreeMinusOne & 0b111111) << 2) +
                              ((TitleMinusOne & 0b1100000000000000) >> 14));
            var degreeLvl2 = (byte)((DegreeMinusOne & 0b11111111000000) >> 6);
            var titleXp1 = (byte)(((TitleXP & 0b111111) << 2) + ((DegreeMinusOne & 0b1100000000000000) >> 14));
            var titleXp2 = (byte)((TitleXP & 0b11111111000000) >> 6);
            var titleXp3 = (byte)((TitleXP & 0b1111111100000000000000) >> 14);
            var titleXp4 = (byte)((TitleXP & 0b111111110000000000000000000000) >> 22);
            var degreeXp1 = (byte)(((DegreeXP & 0b111111) << 2) + ((TitleXP & 0b11000000000000000000000000000000) >> 30));
            var degreeXp2 = (byte)((DegreeXP & 0b11111111000000) >> 6);
            var degreeXp3 = (byte)((DegreeXP & 0b1111111100000000000000) >> 14);
            var degreeXp4 = (byte)((DegreeXP & 0b111111110000000000000000000000) >> 22);
            var satietyCurrent1 = (byte)(((CurrentSatiety & 0b111111) << 2) +
                                 ((DegreeXP & 0b11000000000000000000000000000000) >> 30));
            var satietyCurrent2 = (byte)((CurrentSatiety & 0b11111111000000) >> 6);
            var hpCurrent1 = (byte)(((CurrentHP & 0b111111) << 2) + ((CurrentSatiety & 0b1100000000000000) >> 14));
            var hpCurrent2 = (byte)((CurrentHP & 0b11111111000000) >> 6);
            var mpCurrent1 = (byte)(((CurrentMP & 0b111111) << 2) + ((CurrentHP & 0b1100000000000000) >> 14));
            var mpCurrent2 = (byte)((CurrentMP & 0b11111111000000) >> 6);
            var titleStats1 =
                (byte)(((AvailableTitleStats & 0b111111) << 2) + ((CurrentMP & 0b1100000000000000) >> 14));
            var titleStats2 = (byte)((AvailableTitleStats & 0b11111111000000) >> 6);
            var degreeStats1 = (byte)(((AvailableDegreeStats & 0b111111) << 2) +
                                   ((AvailableTitleStats & 0b1100000000000000) >> 14));
            var degreeStats2 = (byte)((AvailableDegreeStats & 0b11111111000000) >> 6);
            var degreeStats3 = (byte)(((0b111010 << 2) + ((AvailableDegreeStats & 0b1100000000000000) >> 14)));
            var isFemale1 = (byte)((IsGenderFemale ? 1 : 0) << 2);
            var name1 = (byte)(((nameEncodedWithPadding[0] & 0b111111) << 2));
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
            var bootsModelId = (byte)(((BootModelId & 0b111111) << 2) + ((Tattoo & 0b11000000) >> 6));
            var pantsModelId = (byte)(((PantsModelId & 0b111111) << 2) + ((BootModelId & 0b11000000) >> 6));
            var armorModelId = (byte)(((ArmorModelId & 0b111111) << 2) + ((PantsModelId & 0b11000000) >> 6));
            var helmetModelId = (byte)(((HelmetModelId & 0b111111) << 2) + ((ArmorModelId & 0b11000000) >> 6));
            var glovesModelId1 = (byte)(((GlovesModelId & 0b111111) << 2) + ((HelmetModelId & 0b11000000) >> 6));
            var glovesModelId2 = (byte)((GlovesModelId & 0b11000000) >> 6);
            var isNotDeleted1 = (byte)(((IsNotQueuedForDeletion ? 1 : 0) << 1) + 1);

            var lookType = (byte)(IsNotQueuedForDeletion ? 0x79 : 0x19);

            var charDataBytes = new byte[]
            {
                0x6C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(ClientIndex), MinorByte(ClientIndex), 0x08, 0x40, 
                0x60, lookType, hpMax1, hpMax2, mpMax1, mpMax2, strength1, strenth2, agility1, agility2, accuracy1, 
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

        public byte[] ToGameDataByteArray()
        {
            var nameEncoded = MainServer.Win1251!.GetBytes(Name);
            var x = CoordsHelper.EncodeServerCoordinate(X);
            var y = CoordsHelper.EncodeServerCoordinate(-Y);
            var z = CoordsHelper.EncodeServerCoordinate(Z);
            var t = CoordsHelper.EncodeServerCoordinate(Angle);
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
                MajorByte(ClientIndex),
                MinorByte(ClientIndex),
                0x08,
                0x00,
                (byte)(((nameLen & 0b111) << 5) + 2),
                (byte)(((nameEncoded[0] & 0b111) << 5) + ((nameLen & 0b11111000) >> 3))
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
                var clanNameEncoded = MainServer.Win1251.GetBytes(Clan!.Name);
                var clanNameLength = clanNameEncoded.Length;
                data.Add((byte)((clanNameLength & 0b111) << 5));
                data.Add((byte)(((clanNameEncoded[0] & 0b1111111) << 1) + ((clanNameLength & 0b1000) >> 3)));

                for (var i = 1; i < clanNameLength; i++)
                {
                    data.Add((byte)(((clanNameEncoded[i] & 0b1111111) << 1) +
                                    ((clanNameEncoded[i - 1] & 0b10000000) >> 7)));
                }

                data.Add((byte)(((0b01100000) + (((byte)ClanRank) << 1) + ((clanNameEncoded[^1] & 0b10000000) >> 7))));
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
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Helmet) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Amulet) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Shield) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Chestplate) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Gloves)  ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Belt)  ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.BraceletLeft) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.BraceletRight) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Ring_1) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Ring_2) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Ring_3) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Ring_4) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Pants) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Boots) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Guild) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.MapBook) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.RecipeBook) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.MantraBook) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inkpot) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Money) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Backpack) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Key_1) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Key_2) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Mission) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_1) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_2) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_3) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_4) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_5) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_6)? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_7) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_8) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_9) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Inventory_10) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Special_1) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Special_2) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Special_3) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Special_4) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Special_5) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Special_6) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Special_7) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Special_8) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Special_9) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.Ammo) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(IsItemSlotEmpty(BelongingSlot.SpeedhackMantra) ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0xF0);

            for (var i = 0; i < 150; i++)
            {
                data.Add(0x00);
            }

            data.Add((byte)(((CurrentHP & 0b111) << 5) + 0b10011));
            data.Add((byte)((CurrentHP & 0b11111111000) >> 3));
            data.Add((byte)(((MaxHP & 0b11) << 6) + (0b100 << 3) + ((CurrentHP & 0b11100000000000) >> 11)));
            data.Add((byte)((MaxHP & 0b1111111100) >> 2));
            data.Add((byte)((((byte)Karma) << 4) + ((MaxHP & 0b11110000000000) >> 10)));
            var toEncode = DegreeMinusOne * 100 + TitleMinusOne;
            data.Add((byte)(((toEncode & 0b111111) << 2) + 2));
            data.Add((byte)((toEncode & 0b11111111000000) >> 6));

            data.Add(0x80);

            if (Guild == Guild.None)
            {
                data.Add(0x00);
            }
            else
            {
                data.Add((byte)((1 << 7) + (((byte)Guild) << 1)));
            }

            data.Add((byte)(((Money & 0b1111) << 4) + GuildLevelMinusOne));
            data.Add((byte)((Money & 0b111111110000) >> 4));
            data.Add((byte)((Money & 0b11111111000000000000) >> 12));
            data.Add((byte)((Money & 0b1111111100000000000000000000) >> 20));
            data.Add((byte)((Money & 0b11110000000000000000000000000000) >> 28));

            var arr = data.ToArray();
            arr[0] = (byte)arr.Length;

            return arr;
        }

        public static Character CreateNewCharacter(ushort clientIndex, string name, bool isFemale, int face,
            int hairStyle, int hairColor, int tattoo)
        {
            return new Character
            {
                Name = name,
                IsGenderFemale = isFemale,
                FaceType = (byte)face,
                HairStyle = (byte)hairStyle,
                HairColor = (byte)hairColor,
                Tattoo = (byte)tattoo,
                ClientIndex = clientIndex
            };
        }
        public byte[] GetTeleportByteArray(WorldCoords coords)
        {

            var x = CoordsHelper.EncodeServerCoordinate(coords.x);
            var y = CoordsHelper.EncodeServerCoordinate(coords.y);
            var z = CoordsHelper.EncodeServerCoordinate(coords.z);
            var t = CoordsHelper.EncodeServerCoordinate(coords.turn);
            var x_1 = ((x[0] & 0b111) << 5) + 0b00010;
            var x_2 = ((x[1] & 0b111) << 5) + ((x[0] & 0b11111000) >> 3);
            var x_3 = ((x[2] & 0b111) << 5) + ((x[1] & 0b11111000) >> 3);
            var x_4 = ((x[3] & 0b111) << 5) + ((x[2] & 0b11111000) >> 3);
            var y_1 = ((y[0] & 0b111) << 5) + ((x[3] & 0b11111000) >> 3);
            var y_2 = ((y[1] & 0b111) << 5) + ((y[0] & 0b11111000) >> 3);
            var y_3 = ((y[2] & 0b111) << 5) + ((y[1] & 0b11111000) >> 3);
            var y_4 = ((y[3] & 0b111) << 5) + ((y[2] & 0b11111000) >> 3);
            var z_1 = ((z[0] & 0b111) << 5) + ((y[3] & 0b11111000) >> 3);
            var z_2 = ((z[1] & 0b111) << 5) + ((z[0] & 0b11111000) >> 3);
            var z_3 = ((z[2] & 0b111) << 5) + ((z[1] & 0b11111000) >> 3);
            var z_4 = ((z[3] & 0b111) << 5) + ((z[2] & 0b11111000) >> 3);
            var t_1 = ((t[0] & 0b111) << 5) + ((z[3] & 0b11111000) >> 3);
            var t_2 = ((t[1] & 0b111) << 5) + ((t[0] & 0b11111000) >> 3);
            var t_3 = ((t[2] & 0b111) << 5) + ((t[1] & 0b11111000) >> 3);
            var t_4 = ((t[3] & 0b111) << 5) + ((t[2] & 0b11111000) >> 3);
            var t_5 = 0b10100000 + ((t[3] & 0b11111000) >> 3);
            
            var tpBytes = new byte[]
            {
                0x1F, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(ClientIndex), MinorByte(ClientIndex), 0x08, 0x40, 0xE3, 0x01, 
                (byte)x_1, (byte)x_2, (byte)x_3, (byte)x_4, (byte)y_1, (byte)y_2, (byte)y_3, (byte)y_4, (byte)z_1, 
                (byte)z_2, (byte)z_3, (byte)z_4, (byte)t_1, (byte)t_2, (byte)t_3, (byte)t_4, (byte)t_5, 0x00
            };
            return tpBytes;
        }

        public byte[] GetNewPlayerDungeonTeleportAndUpdateStatsByteArray(WorldCoords coords)
        {

            var x = CoordsHelper.EncodeServerCoordinate(coords.x);
            var y = CoordsHelper.EncodeServerCoordinate(-coords.y);
            var z = CoordsHelper.EncodeServerCoordinate(coords.z);
            var t = CoordsHelper.EncodeServerCoordinate(coords.turn);
            var x_1 = ((x[0] & 0b111) << 5) + 0b00010;
            var x_2 = ((x[1] & 0b111) << 5) + ((x[0] & 0b11111000) >> 3);
            var x_3 = ((x[2] & 0b111) << 5) + ((x[1] & 0b11111000) >> 3);
            var x_4 = ((x[3] & 0b111) << 5) + ((x[2] & 0b11111000) >> 3);
            var y_1 = ((y[0] & 0b111) << 5) + ((x[3] & 0b11111000) >> 3);
            var y_2 = ((y[1] & 0b111) << 5) + ((y[0] & 0b11111000) >> 3);
            var y_3 = ((y[2] & 0b111) << 5) + ((y[1] & 0b11111000) >> 3);
            var y_4 = ((y[3] & 0b111) << 5) + ((y[2] & 0b11111000) >> 3);
            var z_1 = ((z[0] & 0b111) << 5) + ((y[3] & 0b11111000) >> 3);
            var z_2 = ((z[1] & 0b111) << 5) + ((z[0] & 0b11111000) >> 3);
            var z_3 = ((z[2] & 0b111) << 5) + ((z[1] & 0b11111000) >> 3);
            var z_4 = ((z[3] & 0b111) << 5) + ((z[2] & 0b11111000) >> 3);
            var t_1 = ((t[0] & 0b111) << 5) + ((z[3] & 0b11111000) >> 3);
            var t_2 = ((t[1] & 0b111) << 5) + ((t[0] & 0b11111000) >> 3);
            var t_3 = ((t[2] & 0b111) << 5) + ((t[1] & 0b11111000) >> 3);
            var t_4 = ((t[3] & 0b111) << 5) + ((t[2] & 0b11111000) >> 3);
            var t_5 = 0b10100000 + ((t[3] & 0b11111000) >> 3);

            var tpBytes = new byte[]
            {
                0xAB, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(ClientIndex), MinorByte(ClientIndex), 0x08, 0x40, 0xE3, 0x01, 
                (byte)x_1, (byte)x_2, (byte)x_3, (byte)x_4, (byte)y_1, (byte)y_2, (byte)y_3, (byte)y_4, (byte)z_1, 
                (byte)z_2, (byte)z_3, (byte)z_4, (byte)t_1, (byte)t_2, (byte)t_3, (byte)t_4, (byte)t_5, 0x20, 0x08,
                0x39, 0xED, 0xA8, 0x00, 0xC8, 0x00, 0x00, 0x00, 0x0B, 0x40, 0xE7, 0x45, 0x20, 0xF7, 0x42, 0x10, 0x79, 
                0x31, 0x88, 0xBC, 0x20, 0x24, 0x5B, 0x14, 0x22, 0x2F, 0x0C, 0x60, 0x71, 0x00, 0x0B, 0x04, 0x58, 0x24, 
                0xC0, 0x42, 0x01, 0x16, 0x0B, 0xB0, 0x60, 0x80, 0x45, 0x03, 0x2C, 0x1C, 0x64, 0xF1, 0x20, 0x0B, 0x08, 
                0x58, 0x44, 0xC0, 0x42, 0x02, 0x16, 0x13, 0xB0, 0xA0, 0x80, 0x45, 0x05, 0x2C, 0x2C, 0x60, 0x71, 0x01, 
                0x0B, 0x4C, 0xE4, 0x45, 0x26, 0xF2, 0x42, 0x13, 0x79, 0xB1, 0x01, 0x0B, 0x0E, 0x58, 0x74, 0xC0, 0xC2, 
                0x03, 0x16, 0x1F, 0xB0, 0x00, 0x81, 0x45, 0x08, 0x2C, 0x44, 0x60, 0x31, 0x22, 0x0B, 0x12, 0x59, 0x94, 
                0xC0, 0xC2, 0x04, 0x16, 0x27, 0xB6, 0x40, 0x81, 0x45, 0x0A, 0x2C, 0x54, 0x60, 0xB1, 0x0A, 0xB1, 0x60, 
                0xC1, 0x45, 0x0B, 0x2E, 0x5C, 0x60, 0x31, 0x03, 0x0B, 0x1A, 0x58, 0xD4, 0xC0, 0xC2, 0x06, 0x1B, 0x12, 
                0x02, 0xF6, 0x02
            };
            
            return tpBytes;
        }

        public bool HasEmptyInventorySlot(GameObjectType gameObjectType = GameObjectType.Unknown)
        {
            return FindEmptyInventorySlot != null;
        }

        public BelongingSlot? FindEmptyInventorySlot(GameObjectType gameObjectType = GameObjectType.Unknown)
        {
            // TODO: equipped slots, bags, etc
            var lookup = new List<BelongingSlot>
            {
                BelongingSlot.Inventory_1,
                BelongingSlot.Inventory_2,
                BelongingSlot.Inventory_3,
                BelongingSlot.Inventory_4,
                BelongingSlot.Inventory_5,
                BelongingSlot.Inventory_6,
                BelongingSlot.Inventory_7,
                BelongingSlot.Inventory_8,
                BelongingSlot.Inventory_9,
                BelongingSlot.Inventory_10
            };

            foreach (var slot in lookup)
            {
                if (IsItemSlotEmpty(slot))
                {
                    return slot;
                }
            }

            return null;
        }

        public bool IsItemSlotEmpty(BelongingSlot belongingSlot)
        {
            return !Items.ContainsKey(belongingSlot);
        }

        private ulong GetXpToLevelUp()
        {
            if (TitleMinusOne % 60 == 59 && DegreeMinusOne % 60 == 59) return 1;
            var minLevel = Math.Min(TitleMinusOne, DegreeMinusOne);
            var maxLevel = Math.Max(TitleMinusOne, DegreeMinusOne);

            return (ulong)(XpPerLevelBase[maxLevel] + XpPerLevelDelta[maxLevel] * minLevel);
        }

        public bool CanUseItem(Item item)
        {
            // TODO: actual check
            return true;
        }

        public bool UpdateCurrentStats()
        {
            var slotsToUpdate = new HashSet<BelongingSlot>
            {
                BelongingSlot.Amulet, BelongingSlot.Belt, BelongingSlot.Boots, BelongingSlot.Chestplate,
                BelongingSlot.Gloves, BelongingSlot.Guild, BelongingSlot.Helmet, BelongingSlot.Pants,
                BelongingSlot.Ring_1, BelongingSlot.Ring_2, BelongingSlot.Ring_3, BelongingSlot.Ring_4,
                BelongingSlot.Shield, BelongingSlot.BraceletLeft, BelongingSlot.BraceletRight
            };

            var str = (int) BaseStrength;
            var agi = (int) BaseAgility;
            var acc = (int) BaseAccuracy;
            var end = (int) BaseEndurance;
            var ear = (int) BaseEarth;
            var wat = (int) BaseWater;
            var air = (int) BaseAir;
            var fir = (int) BaseFire;
            // TODO: satiety not calculated atm
            var hpMax = MaxHPBase;
            var mpMax = MaxMPBase;
            var pdef = 0;
            var mdef = 0;
            var patk = 0;
            var matk = 0;

            foreach (var slot in slotsToUpdate)
            {
                if (!Items.ContainsKey(slot))
                {
                    continue;
                }

                var item = MainServer.ItemCollection.FindById(Items[slot]);
                if (item is null || !CanUseItem(item))
                {
                    continue;
                }
                
                str += item.StrengthUp;
                agi += item.AgilityUp;
                acc += item.AccuracyUp;
                end += item.EnduranceUp;
                ear += item.EarthUp;
                wat += item.WaterUp;
                air += item.AirUp;
                fir += item.FireUp;
                hpMax += item.MaxHpUp;
                mpMax += item.MaxMpUp;
                pdef += item.PDefUp;
                mdef += item.MDefUp;
                patk += item.PAtkUpNegative;
                matk += item.MAtkUpNegative;
            }
            
            CurrentStrength = (ushort) str;
            CurrentAgility = (ushort) agi;
            CurrentAccuracy = (ushort) acc;
            CurrentEndurance = (ushort) end;
            CurrentEarth = (ushort) ear;
            CurrentWater = (ushort) wat;
            CurrentAir = (ushort) air;
            CurrentFire = (ushort) fir;
            CurrentHP = (ushort) Math.Min(CurrentHP, hpMax);
            CurrentMP = (ushort) Math.Min(CurrentMP, mpMax);
            MaxHP = (ushort) hpMax;
            MaxMP = (ushort) mpMax;
            PDef = (ushort) pdef;
            MDef = (ushort) mdef;
            PAtk = (ushort) patk;
            MAtk = (ushort) matk;

            // TODO: character state shouldn't be updated in starting dungeon
            // MainServer.CharacterCollection.Update(Id, this);

            // Would be cool but Rider console doesn't support emoji until 2023.1 or smth
            // Console.WriteLine($"💪{CurrentStrength} 🦵{CurrentAgility} 👀{CurrentAccuracy} 🏃{CurrentEndurance} 🌍{CurrentEarth} " +
            //                   $"💧{CurrentWater} ⛅{CurrentAir} 🔥{CurrentFire} 💖{CurrentHP}/{MaxHP} 💙{CurrentMP}/{MaxMP} " +
            //                   $"🧿{PDef} 🛐{MDef} 🪓{PAtk} 💥{MAtk}");
            Console.WriteLine($"STR {CurrentStrength} AGI {CurrentAgility} ACC {CurrentAccuracy} END {CurrentEndurance} EAR {CurrentEarth} " +
                              $"WAT {CurrentWater} AIR {CurrentAir} FIR {CurrentFire} HP {CurrentHP}/{MaxHP} MP {CurrentMP}/{MaxMP} " +
                              $"PD {PDef} MD {MDef} PA {PAtk} MA {MAtk}");

            return true;
        }
    }
}