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
    public uint Unknown1 = 0x4c4c0f;
    public bool IsGenderFemale;
    public byte Unknown2 = 0;
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
    public byte ShouldBeZeroes = 0;
    public ulong Unknown3 = 0x1fffffffc00c0;
    public bool IsNotQueuedForDeletion = true;
    public byte Unknown4 = 0;

    /// <summary>
    /// TODO: it's horrible, rewrite
    /// </summary>
    public byte[] ToByteArray()
    {
        var sb = new StringBuilder();

        sb.Append("000000");
        sb.AppendBinary(true);
        sb.AppendBinary(IsNotQueuedForDeletion);
        sb.Append("11111111");
        sb.Append("11111111");
        sb.Append("11111111");
        sb.Append("11111100");
        sb.Append("00000000");
        sb.Append("11000000");
        sb.Append("11000000");
        sb.Append("000000");
        sb.AppendBinaryPadTrim(Gloves, true);
        sb.AppendBinaryPadTrim(Helmet, true);
        sb.AppendBinaryPadTrim(Armor, true);
        sb.AppendBinaryPadTrim(Pants, true);
        sb.AppendBinaryPadTrim(Boots, true);
        sb.AppendBinaryPadTrim(Tattoo, true);
        sb.AppendBinaryPadTrim(HairColor, true);
        sb.AppendBinaryPadTrim(HairStyle, true);
        sb.AppendBinaryPadTrim(FaceType, true);
        sb.Append(BitHelper.ByteArrayToBinaryString(BitHelper.EncodeName(Name).Reverse().ToArray()).PadLeft(152, '0'));
        sb.Append("00");
        sb.Append("00000");
        sb.AppendBinary(IsGenderFemale);
        sb.Append("00");
        sb.Append("00010011");
        sb.Append("00010011");
        sb.Append("00000011");
        sb.Append("000101");
        sb.AppendBinaryPadTrim(AvailableDegreeStats);
        sb.AppendBinaryPadTrim(AvailableTitleStats);
        sb.AppendBinaryPadTrim(CurrentMP);
        sb.AppendBinaryPadTrim(CurrentHP);
        sb.AppendBinaryPadTrim(CurrentSatiety);
        sb.AppendBinaryPadTrim(DegreeXP);
        sb.AppendBinaryPadTrim(TitleXP);
        sb.AppendBinaryPadTrim(DegreeLevelMinusOne);
        sb.AppendBinaryPadTrim(TitleLevelMinusOne);
        sb.AppendBinaryPadTrim(MaxSatiety);
        sb.AppendBinaryPadTrim((byte)Karma);
        sb.AppendBinaryPadTrim(MDef);
        sb.AppendBinaryPadTrim(PDef);
        sb.AppendBinaryPadTrim(Fire);
        sb.AppendBinaryPadTrim(Water);
        sb.AppendBinaryPadTrim(Air);
        sb.AppendBinaryPadTrim(Earth);
        sb.AppendBinaryPadTrim(Endurance);
        sb.AppendBinaryPadTrim(Accuracy);
        sb.AppendBinaryPadTrim(Agility);
        sb.AppendBinaryPadTrim(Strength);
        sb.AppendBinaryPadTrim(MaxMP);
        sb.AppendBinaryPadTrim(MaxHP);
        sb.Append("01");
        sb.AppendBinaryPadTrim(IsTurnedOff, true, 4);
        sb.AppendBinaryPadTrim(LookType, true, 4);
        var sbrev = new StringBuilder();
        var str = sb.ToString().Reverse().ToArray();

        for (var i = 0; i < str.Length; i += 8)
        {
            sbrev.Append(new string (str[i..(i + 8)].Reverse().ToArray()));
        }
        
        return BitHelper.BinaryStringToByteArray(sbrev.ToString());
        
    }

    public static byte[] GetEmptyCharacterData(ushort playerIndex)
    {
        return CommonPackets.CreateNewCharacterData(playerIndex);
    }
}