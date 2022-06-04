using emu.Helpers;

namespace emu.DataModels;

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

public class CharacterData
{
    public ushort PlayerIndex;
    public int DbId;
    public byte LookType = 0x7;
    public byte IsTurnedOff = 0x9;
    public ushort MaxHP = 100;
    public ushort MaxMP = 100;
    public ushort Strength = 0;
    public ushort Agility = 0;
    public ushort Accuracy = 0;
    public ushort Endurance = 0;
    public ushort Earth = 0;
    public ushort Air = 0;
    public ushort Water = 0;
    public ushort Fire = 0;
    public ushort PDef = 0;
    public ushort MDef = 0;
    public KarmaTypes Karma = KarmaTypes.Neutral;
    public ushort MaxSatiety = 100;
    public ushort TitleLevelMinusOne = 0;
    public ushort DegreeLevelMinusOne = 0;
    public uint TitleXP = 0;
    public uint DegreeXP = 0;
    public ushort CurrentSatiety = 50;
    public ushort CurrentHP = 100;
    public ushort CurrentMP = 100;
    public ushort AvailableTitleStats = 4;
    public ushort AvailableDegreeStats = 4;
    public bool IsGenderFemale = false;
    public string Name;
    public string ClanName;
    public byte FaceType;
    public byte HairStyle;
    public byte HairColor;
    public byte Tattoo;
    public byte BootModelId;
    public byte PantsModelId;
    public byte ArmorModelId;
    public byte HelmetModelId;
    public byte GlovesModelId;
    public bool IsNotQueuedForDeletion = true;
    public int? HelmetSlot = null;
    public int? AmuletSlot = null;
    public int? SpecSlot = null;
    public int? ArmorSlot = null;
    public int? ShieldSlot = null;
    public int? BeltSlot = null;
    public int? GlovesSlot = null;
    public int? LeftBraceletSlot = null;
    public int? PantsSlot = null;
    public int? RightBraceletSlot = null;
    public int? TopLeftRingSlot = null;
    public int? TopRightRingSlot = null;
    public int? BottomLeftRingSlot = null;
    public int? BottomRightRingSlot = null;
    public int? BootsSlot = null;
    public int? LeftSpecialSlot1 = null;
    public int? LeftSpecialSlot2 = null;
    public int? LeftSpecialSlot3 = null;
    public int? LeftSpecialSlot4 = null;
    public int? LeftSpecialSlot5 = null; // spec ability 1
    public int? LeftSpecialSlot6 = null; // spec ability 2
    public int? LeftSpecialSlot7 = null; // spec ability 3
    public int? LeftSpecialSlot8 = null;
    public int? LeftSpecialSlot9 = null;
    public int? WeaponSlot = null;
    public int? AmmoSlot = null;
    public int? MapBookSlot = null;
    public int? RecipeBookSlot = null;
    public int? MantraBookSlot = null;
    public int? InkpotSlot = null;
    public int? IslandTokenSlot = null;
    public int? SpeedhackMantraSlot = null;
    public int? MoneySlot = null;
    public int? TravelbagSlot = null;
    public int? KeySlot1 = null;
    public int? KeySlot2 = null;
    public int? MissionSlot = null;
    public int? InventorySlot1 = null;
    public int? InventorySlot2 = null;
    public int? InventorySlot3 = null;
    public int? InventorySlot4 = null;
    public int? InventorySlot5 = null;
    public int? InventorySlot6 = null;
    public int? InventorySlot7 = null;
    public int? InventorySlot8 = null;
    public int? InventorySlot9 = null;
    public int? InventorySlot10 = null;
    public int Money = 0;
    public int SpecLevelMinusOne = 0;
    public SpecTypes SpecType = SpecTypes.None;
    public ClanRank ClanRank = 0;

    public double X;
    public double Y = 150;
    public double Z;
    public double T;

    public byte[] ToCharacterListByteArray()
    {
        var nameEncodedWithPadding = new byte[19];
        var nameEncoded = Server.Win1251.GetBytes(Name);;
        Array.Copy(nameEncoded, nameEncodedWithPadding, nameEncoded.Length);

        // 0x79 - look type
        var hpMax1 = (byte)(((MaxHP & 0b111111) << 2) + 1);
        var hpMax2 = (byte)((MaxHP & 0b11111111000000) >> 6);
        var mpMax1 = (byte)(((MaxMP & 0b111111) << 2) + ((MaxHP & 0b1100000000000000) >> 14));
        var mpMax2 = (byte)((MaxMP & 0b11111111000000) >> 6);
        var str1 = (byte)(((Strength & 0b111111) << 2) + ((MaxMP & 0b1100000000000000) >> 14));
        var str2 = (byte)((Strength & 0b11111111000000) >> 6);
        var agi1 = (byte)(((Agility & 0b111111) << 2) + ((Strength & 0b1100000000000000) >> 14));
        var agi2 = (byte)((Agility & 0b11111111000000) >> 6);
        var acc1 = (byte)(((Accuracy & 0b111111) << 2) + ((Agility & 0b1100000000000000) >> 14));
        var acc2 = (byte)((Accuracy & 0b11111111000000) >> 6);
        var end1 = (byte)(((Endurance & 0b111111) << 2) + ((Accuracy & 0b1100000000000000) >> 14));
        var end2 = (byte)((Endurance & 0b11111111000000) >> 6);
        var ert1 = (byte)(((Earth & 0b111111) << 2) + ((Endurance & 0b1100000000000000) >> 14));
        var ert2 = (byte)((Earth & 0b11111111000000) >> 6);
        var air1 = (byte)(((Air & 0b111111) << 2) + ((Earth & 0b1100000000000000) >> 14));
        var air2 = (byte)((Air & 0b11111111000000) >> 6);
        var wat1 = (byte)(((Water & 0b111111) << 2) + ((Air & 0b1100000000000000) >> 14));
        var wat2 = (byte)((Water & 0b11111111000000) >> 6);
        var fir1 = (byte)(((Fire & 0b111111) << 2) + ((Water & 0b1100000000000000) >> 14));
        var fir2 = (byte)((Fire & 0b11111111000000) >> 6);
        var pd1 = (byte)(((PDef & 0b111111) << 2) + ((Fire & 0b1100000000000000) >> 14));
        var pd2 = (byte)((PDef & 0b11111111000000) >> 6);
        var md1 = (byte)(((MDef & 0b111111) << 2) + ((PDef & 0b1100000000000000) >> 14));
        var md2 = (byte)((MDef & 0b11111111000000) >> 6);
        var krm1 = (byte)(((((byte)Karma) & 0b111111) << 2) + ((MDef & 0b1100000000000000) >> 14));
        var satMax1 = (byte)(((MaxSatiety & 0b111111) << 2) + ((((byte)Karma) & 0b11000000) >> 14));
        var satMax2 = (byte)((MaxSatiety & 0b11111111000000) >> 6);
        var tit1 = (byte)(((TitleLevelMinusOne & 0b111111) << 2) + ((MaxSatiety & 0b1100000000000000) >> 14));
        var tit2 = (byte)((TitleLevelMinusOne & 0b11111111000000) >> 6);
        var deg1 = (byte)(((DegreeLevelMinusOne & 0b111111) << 2) + ((TitleLevelMinusOne & 0b1100000000000000) >> 14));
        var deg2 = (byte)((DegreeLevelMinusOne & 0b11111111000000) >> 6);
        var txp1 = (byte)(((TitleXP & 0b111111) << 2) + ((DegreeLevelMinusOne & 0b1100000000000000) >> 14));
        var txp2 = (byte)((TitleXP & 0b11111111000000) >> 6);
        var txp3 = (byte)((TitleXP & 0b1111111100000000000000) >> 14);
        var txp4 = (byte)((TitleXP & 0b111111110000000000000000000000) >> 22);
        var dxp1 = (byte)(((DegreeXP & 0b111111) << 2) + ((TitleXP & 0b11000000000000000000000000000000) >> 30));
        var dxp2 = (byte)((DegreeXP & 0b11111111000000) >> 6);
        var dxp3 = (byte)((DegreeXP & 0b1111111100000000000000) >> 14);
        var dxp4 = (byte)((DegreeXP & 0b111111110000000000000000000000) >> 22);
        var satCur1 = (byte)(((CurrentSatiety & 0b111111) << 2) +
                             ((DegreeXP & 0b11000000000000000000000000000000) >> 30));
        var satCur2 = (byte)((CurrentSatiety & 0b11111111000000) >> 6);
        var hpCur1 = (byte)(((CurrentHP & 0b111111) << 2) + ((CurrentSatiety & 0b1100000000000000) >> 14));
        var hpCur2 = (byte)((CurrentHP & 0b11111111000000) >> 6);
        var mpCur1 = (byte)(((CurrentMP & 0b111111) << 2) + ((CurrentHP & 0b1100000000000000) >> 14));
        var mpCur2 = (byte)((CurrentMP & 0b11111111000000) >> 6);
        var titleStats1 = (byte)(((AvailableTitleStats & 0b111111) << 2) + ((CurrentMP & 0b1100000000000000) >> 14));
        var titleStats2 = (byte)((AvailableTitleStats & 0b11111111000000) >> 6);
        var degStats1 = (byte)(((AvailableDegreeStats & 0b111111) << 2) +
                               ((AvailableTitleStats & 0b1100000000000000) >> 14));
        var degStats2 = (byte)((AvailableDegreeStats & 0b11111111000000) >> 6);
        var degStats3 = (byte)(((0b111010 << 2) + ((AvailableDegreeStats & 0b1100000000000000) >> 14)));
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

        var lookType = (byte) (IsNotQueuedForDeletion ? 0x79 : 0x19);

        var charDataBytes = new byte[]
        {
            0x6c, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, BitHelper.GetSecondByte(PlayerIndex),
            BitHelper.GetFirstByte(PlayerIndex), 0x08, 0x40, 0x60, lookType, hpMax1, hpMax2, mpMax1, mpMax2, str1, str2,
            agi1, agi2, acc1, acc2, end1, end2, ert1, ert2, air1, air2, wat1, wat2, fir1, fir2, pd1, pd2, md1, md2,
            krm1, satMax1, satMax2, tit1, tit2, deg1, deg2, txp1, txp2, txp3, txp4, dxp1, dxp2, dxp3, dxp4, satCur1,
            satCur2, hpCur1, hpCur2, mpCur1, mpCur2, titleStats1, titleStats2, degStats1, degStats2, degStats3, 0xc0,
            0xc8, 0xc8, isFemale1, name1, name2, name3, name4, name5, name6, name7, name8, name9, name10, name11,
            name12, name13, name14, name15, name16, name17, name18, name19, face1, hairStyle1, hairColor1, tattoo1, 
            bootsModelId, pantsModelId, armorModelId, helmetModelId, glovesModelId1, glovesModelId2, 0xc0, 0xc0, 0x00, 0xfc, 0xff, 0xff, 0xff, isNotDeleted1, 
            0x00, 0x00, 0x00, 0x00
        };

        return charDataBytes;
    }

    public byte[] ToGameDataByteArray()
    {
        var nameEncoded = Server.Win1251.GetBytes(Name);
        var x = CoordsHelper.EncodeServerCoordinate(X);
        var y = CoordsHelper.EncodeServerCoordinate(Y);
        var z = CoordsHelper.EncodeServerCoordinate(Z);
        var t = CoordsHelper.EncodeServerCoordinate(T);
        var nameLen = nameEncoded.Length + 1;
        var data = new List<byte>
        {
            0x00,
            0x01,
            0x2c,
            0x01,
            0x00,
            0x00,
            0x04,
            BitHelper.GetSecondByte(PlayerIndex),
            BitHelper.GetFirstByte(PlayerIndex),
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
        Console.WriteLine(Convert.ToHexString(data.ToArray()));

        if (string.IsNullOrWhiteSpace(ClanName))
        {
            data.Add(0x00);
            data.Add(0x6e);
        }
        else
        {
            var clanNameEncoded = Server.Win1251.GetBytes(ClanName);
            var clanNameLength = clanNameEncoded.Length;
            data.Add((byte) ((clanNameLength & 0b111) << 5));
            data.Add((byte)(((clanNameEncoded[0] & 0b1111111) << 1) + ((clanNameLength & 0b1000) >> 3)));

            for (var i = 1; i < clanNameLength; i++)
            {
                data.Add((byte)(((clanNameEncoded[i] & 0b1111111) << 1) + ((clanNameEncoded[i - 1] & 0b10000000) >> 7)));
            }
            
            data.Add((byte)(((0b01100000) + (((byte) ClanRank) << 1) + ((clanNameEncoded[^1] & 0b10000000) >> 7))));
        }
        Console.WriteLine(Convert.ToHexString(data.ToArray()));

        data.Add(0x1a);
        data.Add(0x98);
        data.Add(0x18);
        data.Add(0x19);
        data.AddRange(x);
        data.AddRange(y);
        data.AddRange(z);
        data.AddRange(t);
        data.Add(0x37);
        data.Add(0x0d);
        data.Add(0x79);
        data.Add(0x00);
        data.Add(0xf0);
        data.Add((byte) (HelmetSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (AmuletSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (ShieldSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (ArmorSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (GlovesSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (BeltSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (LeftBraceletSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (RightBraceletSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (TopLeftRingSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (TopRightRingSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (BottomLeftRingSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (BottomRightRingSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (PantsSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (BootsSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (SpecSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (MapBookSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (RecipeBookSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (MantraBookSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.Add((byte) (InkpotSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (MoneySlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (TravelbagSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (KeySlot1 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (KeySlot2 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (MissionSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot1 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot2 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot3 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot4 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot5 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot6 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot7 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot8 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot9 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (InventorySlot10 is null ? 0x00 : 0x04));
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
        data.Add((byte) (LeftSpecialSlot1 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (LeftSpecialSlot2 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (LeftSpecialSlot3 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (LeftSpecialSlot4 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (LeftSpecialSlot5 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (LeftSpecialSlot6 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (LeftSpecialSlot7 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (LeftSpecialSlot8 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (LeftSpecialSlot9 is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (AmmoSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (SpeedhackMantraSlot is null ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add(0x04);
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x04);
        data.Add(0x00);
        data.Add(0xf0);

        for (var i = 0; i < 150; i++)
        {
            data.Add(0x00);
        }
        
        data.Add((byte)(((CurrentHP & 0b111) << 5) + 0b10011));
        data.Add((byte)((CurrentHP & 0b11111111000) >> 3));
        data.Add((byte)(((MaxHP & 0b11) << 6) + (0b100 << 3) + ((CurrentHP & 0b11100000000000) >> 11)));
        data.Add((byte)((MaxHP & 0b1111111100) >> 2));
        data.Add((byte)((((byte) Karma) << 4) + ((MaxHP & 0b11110000000000) >> 10)));
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
            data.Add((byte)((1 << 7) + (((byte) SpecType) << 1)));
        }
        data.Add((byte)(((Money & 0b1111) << 4) + SpecLevelMinusOne));
        data.Add((byte)((Money & 0b111111110000) >> 4));
        data.Add((byte)((Money & 0b11111111000000000000) >> 12));
        data.Add((byte)((Money & 0b1111111100000000000000000000) >> 20));
        data.Add((byte)((Money & 0b11110000000000000000000000000000) >> 28));

        var arr = data.ToArray();
        arr[0] = (byte) arr.Length;
        
        return arr;
    }

    public static CharacterData CreateNewCharacter(ushort playerIndex, string name, bool isFemale, int face, int hairStyle,
        int hairColor, int tattoo)
    {
        return new CharacterData
        {
            PlayerIndex = playerIndex,
            Name = name,
            IsGenderFemale = isFemale,
            FaceType = (byte)face,
            HairStyle = (byte)hairStyle,
            HairColor = (byte)hairColor,
            Tattoo = (byte)tattoo
        };
    }

}