using System;
using System.Collections.Generic;
using LiteDB;
using SphServer.Helpers;
using static SphServer.Helpers.BitHelper;
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SphServer.DataModels
{
    public enum KarmaTypes : byte
    {
        VeryBad = 0x1,
        Bad = 0x2,
        Neutral = 0x3,
        Good = 0x4,
        Benign = 0x5
    }

    public enum SpecTypes : byte
    {
        None = 0x0,
        Assasin = 0x1,
        Crusader = 0x2,
        Inquisitor = 0x3,
        Hunter = 0x4,
        Archmage = 0x5,
        Barbarian = 0x6,
        Druid = 0x7,
        Thief = 0x8,
        MasterOfSteel = 0x9,
        Armorer = 0x10,
        Blacksmith = 0x11,
        Warlock = 0x12,
        Necromancer = 0x13,
        Bandier = 0x14,
    }

    public enum ClanRank : byte
    {
        Senior = 0x0,
        Seneschal = 0x1,
        Vassal = 0x2,
        Neophyte = 0x3
    }

    public enum Belongings : int
    {
        Helmet = 0,
        Amulet = 1,
        Shield = 2,
        Chestplate = 3,
        Gloves = 4,
        Belt = 5,
        BraceletLeft = 6,
        BraceletRight = 7,
        Ring_1 = 8,
        Ring_2 = 9,
        Ring_3 = 10,
        Ring_4 = 11,
        Pants = 12,
        Boots = 13,
        Guild = 14,
        MapBook = 15,
        RecipeBook = 16,
        MantraBook = 17,
        Inkpot = 20,
        Inventory_1 = 26,
        Inventory_2 = 27,
        Inventory_3 = 28,
        Inventory_4 = 29,
        Inventory_5 = 30,
        Inventory_6 = 31,
        Inventory_7 = 32,
        Inventory_8 = 33,
        Inventory_9 = 34,
        Inventory_10 = 35,
    }
    public partial class CharacterData : IGameEntity
    {
        [BsonIgnore]
        [Obsolete]
        public ushort Id { get; set; }
        public ushort Unknown { get; set; }
        public double X { get; set; }
        public double Y { get; set; } = 150;
        public double Z { get; set; }
        public double Turn { get; set; }
        public ushort CurrentHP { get; set; } = 100;
        public ushort MaxHP { get; set; } = 100;
        public ushort TypeID { get; set; }
        public byte TitleLevelMinusOne { get; set; }
        public byte DegreeLevelMinusOne { get; set; }
        
        [BsonIgnore]
        public SphGameObject SphGameObject { get; set; } = null!; // unused for now

        [BsonId]
        public int DbId { get; set; }
        
        public byte LookType { get; set; } = 0x7;
        public byte IsTurnedOff { get; set; } = 0x9;
        public ushort MaxMP { get; set; } = 100;
        public ushort Strength { get; set; }
        public ushort Agility { get; set; }
        public ushort Accuracy { get; set; }
        public ushort Endurance { get; set; }
        public ushort Earth { get; set; }
        public ushort Air { get; set; }
        public ushort Water { get; set; }
        public ushort Fire { get; set; }
        public ushort PDef { get; set; }
        public ushort MDef { get; set; }
        public KarmaTypes Karma { get; set; } = KarmaTypes.Neutral;
        public ushort MaxSatiety { get; set; } = 100;
        public uint TitleXP { get; set; }
        public uint DegreeXP { get; set; }
        public ushort CurrentSatiety { get; set; } = 50;
        public ushort CurrentMP { get; set; } = 100;
        public ushort AvailableTitleStats { get; set; } = 4;
        public ushort AvailableDegreeStats { get; set; } = 4;
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
        public int? HelmetSlot { get; set; }
        public int? AmuletSlot { get; set; }
        public int? SpecSlot { get; set; }
        public int? ArmorSlot { get; set; }
        public int? ShieldSlot { get; set; }
        public int? BeltSlot { get; set; }
        public int? GlovesSlot { get; set; }
        public int? LeftBraceletSlot { get; set; }
        public int? PantsSlot { get; set; }
        public int? RightBraceletSlot { get; set; }
        public int? TopLeftRingSlot { get; set; }
        public int? TopRightRingSlot { get; set; }
        public int? BottomLeftRingSlot { get; set; }
        public int? BottomRightRingSlot { get; set; }
        public int? BootsSlot { get; set; }
        public int? LeftSpecialSlot1 { get; set; }
        public int? LeftSpecialSlot2 { get; set; }
        public int? LeftSpecialSlot3 { get; set; }
        public int? LeftSpecialSlot4 { get; set; }
        public int? LeftSpecialSlot5 { get; set; } // spec ability 1
        public int? LeftSpecialSlot6 { get; set; } // spec ability 2
        public int? LeftSpecialSlot7 { get; set; } // spec ability 3
        public int? LeftSpecialSlot8 { get; set; }
        public int? LeftSpecialSlot9 { get; set; }
        public int? WeaponSlot { get; set; }
        public int? AmmoSlot { get; set; }
        public int? MapBookSlot { get; set; }
        public int? RecipeBookSlot { get; set; }
        public int? MantraBookSlot { get; set; }
        public int? InkpotSlot { get; set; }
        public int? IslandTokenSlot { get; set; }
        public int? SpeedhackMantraSlot { get; set; }
        public int? MoneySlot { get; set; }
        public int? TravelbagSlot { get; set; }
        public int? KeySlot1 { get; set; }
        public int? KeySlot2 { get; set; }
        public int? MissionSlot { get; set; }
        public int? InventorySlot1 { get; set; }
        public int? InventorySlot2 { get; set; }
        public int? InventorySlot3 { get; set; }
        public int? InventorySlot4 { get; set; }
        public int? InventorySlot5 { get; set; }
        public int? InventorySlot6 { get; set; }
        public int? InventorySlot7 { get; set; }
        public int? InventorySlot8 { get; set; }
        public int? InventorySlot9 { get; set; }
        public int? InventorySlot10 { get; set; }
        public int Money { get; set; }
        public int SpecLevelMinusOne { get; set; }
        public SpecTypes SpecType { get; set; } = SpecTypes.None;
        public ClanRank ClanRank { get; set; } = ClanRank.Neophyte;

        [BsonIgnore]
        public Client Client = null!;

        [BsonIgnore] 
        public Player Player = null!;

        // TODO: db
        [BsonIgnore]
        public Dictionary<Belongings, Item> Items = new();

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
            var strength1 = (byte)(((Strength & 0b111111) << 2) + ((MaxMP & 0b1100000000000000) >> 14));
            var strenth2 = (byte)((Strength & 0b11111111000000) >> 6);
            var agility1 = (byte)(((Agility & 0b111111) << 2) + ((Strength & 0b1100000000000000) >> 14));
            var agility2 = (byte)((Agility & 0b11111111000000) >> 6);
            var accuracy1 = (byte)(((Accuracy & 0b111111) << 2) + ((Agility & 0b1100000000000000) >> 14));
            var accuracy2 = (byte)((Accuracy & 0b11111111000000) >> 6);
            var endurance1 = (byte)(((Endurance & 0b111111) << 2) + ((Accuracy & 0b1100000000000000) >> 14));
            var endurance2 = (byte)((Endurance & 0b11111111000000) >> 6);
            var earth1 = (byte)(((Earth & 0b111111) << 2) + ((Endurance & 0b1100000000000000) >> 14));
            var earth2 = (byte)((Earth & 0b11111111000000) >> 6);
            var air1 = (byte)(((Air & 0b111111) << 2) + ((Earth & 0b1100000000000000) >> 14));
            var air2 = (byte)((Air & 0b11111111000000) >> 6);
            var water1 = (byte)(((Water & 0b111111) << 2) + ((Air & 0b1100000000000000) >> 14));
            var water2 = (byte)((Water & 0b11111111000000) >> 6);
            var fire1 = (byte)(((Fire & 0b111111) << 2) + ((Water & 0b1100000000000000) >> 14));
            var fire2 = (byte)((Fire & 0b11111111000000) >> 6);
            var pdef1 = (byte)(((PDef & 0b111111) << 2) + ((Fire & 0b1100000000000000) >> 14));
            var pdef2 = (byte)((PDef & 0b11111111000000) >> 6);
            var mdef1 = (byte)(((MDef & 0b111111) << 2) + ((PDef & 0b1100000000000000) >> 14));
            var mdef2 = (byte)((MDef & 0b11111111000000) >> 6);
            var karma1 = (byte)(((((byte)Karma) & 0b111111) << 2) + ((MDef & 0b1100000000000000) >> 14));
            var satietyMax1 = (byte)(((MaxSatiety & 0b111111) << 2) + ((((byte)Karma) & 0b11000000) >> 14));
            var satietyMax2 = (byte)((MaxSatiety & 0b11111111000000) >> 6);
            var titleLvl1 = (byte)(((TitleLevelMinusOne & 0b111111) << 2) + ((MaxSatiety & 0b1100000000000000) >> 14));
            var titleLvl2 = (byte)((TitleLevelMinusOne & 0b11111111000000) >> 6);
            var degreeLvl1 = (byte)(((DegreeLevelMinusOne & 0b111111) << 2) +
                              ((TitleLevelMinusOne & 0b1100000000000000) >> 14));
            var degreeLvl2 = (byte)((DegreeLevelMinusOne & 0b11111111000000) >> 6);
            var titleXp1 = (byte)(((TitleXP & 0b111111) << 2) + ((DegreeLevelMinusOne & 0b1100000000000000) >> 14));
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
                0x6C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(Player.Index), MinorByte(Player.Index), 0x08, 0x40, 
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
            var t = CoordsHelper.EncodeServerCoordinate(Turn);
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
                MajorByte(Player.Index),
                MinorByte(Player.Index),
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
            data.Add((byte)(HelmetSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(AmuletSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(ShieldSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(ArmorSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(GlovesSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(BeltSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(LeftBraceletSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(RightBraceletSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(TopLeftRingSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(TopRightRingSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(BottomLeftRingSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(BottomRightRingSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(PantsSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(BootsSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(SpecSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(MapBookSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(RecipeBookSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(MantraBookSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add((byte)(InkpotSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(MoneySlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(TravelbagSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(KeySlot1 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(KeySlot2 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(MissionSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot1 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot2 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot3 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot4 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot5 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot6 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot7 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot8 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot9 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(InventorySlot10 is null ? 0x00 : 0x04));
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
            data.Add((byte)(LeftSpecialSlot1 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(LeftSpecialSlot2 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(LeftSpecialSlot3 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(LeftSpecialSlot4 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(LeftSpecialSlot5 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(LeftSpecialSlot6 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(LeftSpecialSlot7 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(LeftSpecialSlot8 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(LeftSpecialSlot9 is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(AmmoSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add((byte)(SpeedhackMantraSlot is null ? 0x00 : 0x04));
            data.Add(0x00);
            data.Add(0x04);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0x04);
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
            var toEncode = DegreeLevelMinusOne * 100 + TitleLevelMinusOne;
            data.Add((byte)(((toEncode & 0b111111) << 2) + 2));
            data.Add((byte)((toEncode & 0b11111111000000) >> 6));

            data.Add(0x80);

            if (SpecType == SpecTypes.None)
            {
                data.Add(0x00);
            }
            else
            {
                data.Add((byte)((1 << 7) + (((byte)SpecType) << 1)));
            }

            data.Add((byte)(((Money & 0b1111) << 4) + SpecLevelMinusOne));
            data.Add((byte)((Money & 0b111111110000) >> 4));
            data.Add((byte)((Money & 0b11111111000000000000) >> 12));
            data.Add((byte)((Money & 0b1111111100000000000000000000) >> 20));
            data.Add((byte)((Money & 0b11110000000000000000000000000000) >> 28));

            var arr = data.ToArray();
            arr[0] = (byte)arr.Length;

            return arr;
        }

        public static CharacterData CreateNewCharacter(Player player, string name, bool isFemale, int face,
            int hairStyle, int hairColor, int tattoo)
        {
            return new CharacterData
            {
                Name = name,
                IsGenderFemale = isFemale,
                FaceType = (byte)face,
                HairStyle = (byte)hairStyle,
                HairColor = (byte)hairColor,
                Tattoo = (byte)tattoo,
                Player = player
            };
        }
        public byte[] GetTeleportByteArray(WorldCoords coords)
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
                0x1F, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(Player.Index), MinorByte(Player.Index), 0x08, 0x40, 0xE3, 0x01, 
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
                0xAB, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(Player.Index), MinorByte(Player.Index), 0x08, 0x40, 0xE3, 0x01, 
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
    }
}