using System.Text;
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

public class CharacterData
{
    public ushort PlayerIndex;
    public byte LookType = 0x7;
    public byte IsTurnedOff = 0x9;
    public ushort MaxHP;
    public ushort MaxMP;
    public ushort Strength;
    public ushort Agility;
    public ushort Accuracy;
    public ushort Endurance;
    public ushort Earth;
    public ushort Air;
    public ushort Water;
    public ushort Fire;
    public ushort PDef;
    public ushort MDef;
    public KarmaTypes Karma;
    public ushort MaxSatiety;
    public ushort TitleLevelMinusOne;
    public ushort DegreeLevelMinusOne;
    public uint TitleXP;
    public uint DegreeXP;
    public ushort CurrentSatiety;
    public ushort CurrentHP;
    public ushort CurrentMP;
    public ushort AvailableTitleStats;
    public ushort AvailableDegreeStats;
    public bool IsGenderFemale;
    /// <summary>
    /// 19 chars max?
    /// </summary>
    public string Name;
    public byte FaceType;
    public byte HairStyle;
    public byte HairColor;
    public byte Tattoo;
    public byte Boots;
    public byte Pants;
    public byte Armor;
    public byte Helmet;
    public byte Gloves;
    public bool IsNotQueuedForDeletion = true;

    public double x;
    public double y;
    public double z;
    public double t;

    public byte[] ToByteArray()
    {
        var nameEncodedWithPadding = new byte[19];
        var nameEncoded = BitHelper.EncodeName(Name);
        Array.Copy(nameEncoded, nameEncodedWithPadding, nameEncoded.Length);
        
        // 0x79 - look type
        var hpMax1 = (byte) (((MaxHP & 0b111111) << 2) + 1);
        var hpMax2 = (byte) ((MaxHP & 0b11111111000000) >> 6);
        var mpMax1 = (byte) (((MaxMP & 0b111111) << 2) + ((MaxHP & 0b1100000000000000) >> 14));
        var mpMax2 = (byte) ((MaxMP & 0b11111111000000) >> 6);
        var str1 = (byte) (((Strength & 0b111111) << 2) + ((MaxMP & 0b1100000000000000) >> 14));
        var str2 = (byte) ((Strength & 0b11111111000000) >> 6);
        var agi1 = (byte) (((Agility & 0b111111) << 2) + ((Strength & 0b1100000000000000) >> 14));
        var agi2 = (byte) ((Agility & 0b11111111000000) >> 6);
        var acc1 = (byte) (((Accuracy & 0b111111) << 2) + ((Agility & 0b1100000000000000) >> 14));
        var acc2 = (byte) ((Accuracy & 0b11111111000000) >> 6);
        var end1 = (byte) (((Endurance & 0b111111) << 2) + ((Accuracy & 0b1100000000000000) >> 14));
        var end2 = (byte) ((Endurance & 0b11111111000000) >> 6);
        var ert1 = (byte) (((Earth & 0b111111) << 2) + ((Endurance & 0b1100000000000000) >> 14));
        var ert2 = (byte) ((Earth & 0b11111111000000) >> 6);
        var air1 = (byte) (((Air & 0b111111) << 2) + ((Earth & 0b1100000000000000) >> 14));
        var air2 = (byte) ((Air & 0b11111111000000) >> 6);
        var wat1 = (byte) (((Water & 0b111111) << 2) + ((Air & 0b1100000000000000) >> 14));
        var wat2 = (byte) ((Water & 0b11111111000000) >> 6);
        var fir1 = (byte) (((Fire & 0b111111) << 2) + ((Water & 0b1100000000000000) >> 14));
        var fir2 = (byte) ((Fire & 0b11111111000000) >> 6);
        var pd1 = (byte) (((PDef & 0b111111) << 2) + ((Fire & 0b1100000000000000) >> 14));
        var pd2 = (byte) ((PDef & 0b11111111000000) >> 6);
        var md1 = (byte) (((MDef & 0b111111) << 2) + ((PDef & 0b1100000000000000) >> 14));
        var md2 = (byte) ((MDef & 0b11111111000000) >> 6);
        var krm1 = (byte) (((((byte) Karma) & 0b111111) << 2) + ((MDef & 0b1100000000000000) >> 14));
        var satMax1 = (byte) (((MaxSatiety & 0b111111) << 2) + ((((byte) Karma) & 0b11000000) >> 14));
        var satMax2 = (byte) ((MaxSatiety & 0b11111111000000) >> 6);
        var tit1 = (byte) (((TitleLevelMinusOne & 0b111111) << 2) + ((MaxSatiety & 0b1100000000000000) >> 14));
        var tit2 = (byte) ((TitleLevelMinusOne & 0b11111111000000) >> 6);
        var deg1 = (byte) (((DegreeLevelMinusOne & 0b111111) << 2) + ((TitleLevelMinusOne & 0b1100000000000000) >> 14));
        var deg2 = (byte) ((DegreeLevelMinusOne & 0b11111111000000) >> 6);
        var txp1 = (byte) (((TitleXP & 0b111111) << 2) + ((DegreeLevelMinusOne & 0b1100000000000000) >> 14));
        var txp2 = (byte) ((TitleXP & 0b11111111000000) >> 6);
        var txp3 = (byte) ((TitleXP & 0b1111111100000000000000) >> 14);
        var txp4 = (byte) ((TitleXP & 0b111111110000000000000000000000) >> 22);
        var dxp1 = (byte) (((DegreeXP & 0b111111) << 2) + ((TitleXP & 0b11000000000000000000000000000000) >> 30));
        var dxp2 = (byte) ((DegreeXP & 0b11111111000000) >> 6);
        var dxp3 = (byte) ((DegreeXP & 0b1111111100000000000000) >> 14);
        var dxp4 = (byte) ((DegreeXP & 0b111111110000000000000000000000) >> 22);
        var satCur1 = (byte) (((CurrentSatiety & 0b111111) << 2) + ((DegreeXP & 0b11000000000000000000000000000000) >> 30));
        var satCur2 = (byte) ((CurrentSatiety & 0b11111111000000) >> 6);
        var hpCur1 = (byte) (((CurrentHP & 0b111111) << 2) + ((CurrentSatiety & 0b1100000000000000) >> 14));
        var hpCur2 = (byte) ((CurrentHP & 0b11111111000000) >> 6);
        var mpCur1 = (byte) (((CurrentMP & 0b111111) << 2) + ((CurrentHP & 0b1100000000000000) >> 14));
        var mpCur2 = (byte) ((CurrentMP & 0b11111111000000) >> 6);
        var titleStats1 = (byte) (((AvailableTitleStats & 0b111111) << 2) + ((CurrentMP & 0b1100000000000000) >> 14));
        var titleStats2 = (byte) ((AvailableTitleStats & 0b11111111000000) >> 6);
        var degStats1 = (byte) (((AvailableDegreeStats & 0b111111) << 2) + ((AvailableTitleStats & 0b1100000000000000) >> 14));
        var degStats2 = (byte) ((AvailableDegreeStats & 0b11111111000000) >> 6);
        var degStats3 = (byte) (((0b111010 << 2) + ((AvailableDegreeStats & 0b1100000000000000) >> 14)));
        var isFemale1 = (byte) ((IsGenderFemale ? 1 : 0) << 2);
        var name1 = (byte) (((nameEncodedWithPadding[0] & 0b111111) << 2));
        var name2 = (byte) (((nameEncodedWithPadding[1] & 0b111111) << 2) + ((nameEncodedWithPadding[0] & 0b11000000) >> 6));
        var name3 = (byte) (((nameEncodedWithPadding[2] & 0b111111) << 2) + ((nameEncodedWithPadding[1] & 0b11000000) >> 6));
        var name4 = (byte) (((nameEncodedWithPadding[3] & 0b111111) << 2) + ((nameEncodedWithPadding[2] & 0b11000000) >> 6));
        var name5 = (byte) (((nameEncodedWithPadding[4] & 0b111111) << 2) + ((nameEncodedWithPadding[3] & 0b11000000) >> 6));
        var name6 = (byte) (((nameEncodedWithPadding[5] & 0b111111) << 2) + ((nameEncodedWithPadding[4] & 0b11000000) >> 6));
        var name7 = (byte) (((nameEncodedWithPadding[6] & 0b111111) << 2) + ((nameEncodedWithPadding[5] & 0b11000000) >> 6));
        var name8 = (byte) (((nameEncodedWithPadding[7] & 0b111111) << 2) + ((nameEncodedWithPadding[6] & 0b11000000) >> 6));
        var name9 = (byte) (((nameEncodedWithPadding[8] & 0b111111) << 2) + ((nameEncodedWithPadding[7] & 0b11000000) >> 6));
        var name10 = (byte) (((nameEncodedWithPadding[9] & 0b111111) << 2) + ((nameEncodedWithPadding[8] & 0b11000000) >> 6));
        var name11 = (byte) (((nameEncodedWithPadding[10] & 0b111111) << 2) + ((nameEncodedWithPadding[9] & 0b11000000) >> 6));
        var name12 = (byte) (((nameEncodedWithPadding[11] & 0b111111) << 2) + ((nameEncodedWithPadding[10] & 0b11000000) >> 6));
        var name13 = (byte) (((nameEncodedWithPadding[12] & 0b111111) << 2) + ((nameEncodedWithPadding[11] & 0b11000000) >> 6));
        var name14 = (byte) (((nameEncodedWithPadding[13] & 0b111111) << 2) + ((nameEncodedWithPadding[12] & 0b11000000) >> 6));
        var name15 = (byte) (((nameEncodedWithPadding[14] & 0b111111) << 2) + ((nameEncodedWithPadding[13] & 0b11000000) >> 6));
        var name16 = (byte) (((nameEncodedWithPadding[15] & 0b111111) << 2) + ((nameEncodedWithPadding[14] & 0b11000000) >> 6));
        var name17 = (byte) (((nameEncodedWithPadding[16] & 0b111111) << 2) + ((nameEncodedWithPadding[15] & 0b11000000) >> 6));
        var name18 = (byte) (((nameEncodedWithPadding[17] & 0b111111) << 2) + ((nameEncodedWithPadding[16] & 0b11000000) >> 6));
        var name19 = (byte) (((nameEncodedWithPadding[18] & 0b111111) << 2) + ((nameEncodedWithPadding[17] & 0b11000000) >> 6));
        var face1 = (byte) (((FaceType & 0b111111) << 2) + ((nameEncodedWithPadding[18] & 0b11000000) >> 6));
        var hairStyle1 = (byte) (((HairStyle & 0b111111) << 2) + ((FaceType & 0b11000000) >> 6));
        var hairColor1 = (byte) (((HairColor & 0b111111) << 2) + ((HairStyle & 0b11000000) >> 6));
        var tattoo1 = (byte) (((Tattoo & 0b111111) << 2) + ((HairColor & 0b11000000) >> 6));
        var boots1 = (byte) (((Boots & 0b111111) << 2) + ((Tattoo & 0b11000000) >> 6));
        var pants1 = (byte) (((Pants & 0b111111) << 2) + ((Boots & 0b11000000) >> 6));
        var armor1 = (byte) (((Armor & 0b111111) << 2) + ((Pants & 0b11000000) >> 6));
        var helmet1 = (byte) (((Helmet & 0b111111) << 2) + ((Armor & 0b11000000) >> 6));
        var gloves1 = (byte) (((Gloves & 0b111111) << 2) + ((Helmet & 0b11000000) >> 6));
        var gloves2 = (byte) ((Gloves & 0b11000000) >> 6);
        var isNotDeleted1 = (byte) (((IsNotQueuedForDeletion ? 1 : 0) << 1) + 1);
        
        var charDataBytes = new byte []
        {
            0x6c, 0x00, 0x2c, 0x01, 0x00, 0x00, 0x04, BitHelper.GetSecondByte(PlayerIndex), 
            BitHelper.GetFirstByte(PlayerIndex), 0x08, 0x40, 0x60, 0x79, hpMax1, hpMax2, mpMax1, mpMax2, str1, str2, 
            agi1, agi2, acc1, acc2, end1, end2, ert1, ert2, air1, air2, wat1, wat2, fir1, fir2, pd1, pd2, md1, md2,
            krm1, satMax1, satMax2, tit1, tit2, deg1, deg2, txp1, txp2, txp3, txp4, dxp1, dxp2, dxp3, dxp4, satCur1, 
            satCur2, hpCur1, hpCur2, mpCur1, mpCur2, titleStats1, titleStats2, degStats1, degStats2, degStats3, 0xc0,
            0xc8, 0xc8, isFemale1, name1, name2, name3, name4, name5, name6, name7, name8, name9, name10, name11, name12, 
            name13, name14, name15, name16, name17, name18, name19, face1, hairStyle1, hairColor1, tattoo1, boots1, 
            pants1, armor1, helmet1, gloves1, gloves2, 0xc0, 0xc0, 0x00, 0xfc, 0xff, 0xff, 0xff, isNotDeleted1, 0x00, 
            0x00, 0x00, 0x00
        };

        return charDataBytes;
    }
}