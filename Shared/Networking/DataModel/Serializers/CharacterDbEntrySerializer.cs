using System;
using System.Collections.Generic;
using SphServer.Helpers;
using SphServer.Shared.Db.DataModels;
using SphServer.System;

namespace SphServer.Shared.Networking.Serializers;

public class CharacterDbEntrySerializer (CharacterDbEntry characterDbEntry) : SphereDbEntrySerializerBase
{
    public byte[] ToCharacterListByteArray ()
    {
        var nameEncodedWithPadding = new byte[19];
        var nameEncoded = SphEncoding.Win1251!.GetBytes(characterDbEntry.Name);
        Array.Copy(nameEncoded, nameEncodedWithPadding, nameEncoded.Length);

        // 0x79 - look type
        var hpMax1 = (byte) (((characterDbEntry.MaxHP & 0b111111) << 2) + 1);
        var hpMax2 = (byte) ((characterDbEntry.MaxHP & 0b11111111000000) >> 6);
        var mpMax1 = (byte) (((characterDbEntry.MaxMP & 0b111111) << 2) +
                             ((characterDbEntry.MaxHP & 0b1100000000000000) >> 14));
        var mpMax2 = (byte) ((characterDbEntry.MaxMP & 0b11111111000000) >> 6);
        var strength1 = (byte) (((characterDbEntry.CurrentStrength & 0b111111) << 2) +
                                ((characterDbEntry.MaxMP & 0b1100000000000000) >> 14));
        var strenth2 = (byte) ((characterDbEntry.CurrentStrength & 0b11111111000000) >> 6);
        var agility1 = (byte) (((characterDbEntry.CurrentAgility & 0b111111) << 2) +
                               ((characterDbEntry.CurrentStrength & 0b1100000000000000) >> 14));
        var agility2 = (byte) ((characterDbEntry.CurrentAgility & 0b11111111000000) >> 6);
        var accuracy1 = (byte) (((characterDbEntry.CurrentAccuracy & 0b111111) << 2) +
                                ((characterDbEntry.CurrentAgility & 0b1100000000000000) >> 14));
        var accuracy2 = (byte) ((characterDbEntry.CurrentAccuracy & 0b11111111000000) >> 6);
        var endurance1 = (byte) (((characterDbEntry.CurrentEndurance & 0b111111) << 2) +
                                 ((characterDbEntry.CurrentAccuracy & 0b1100000000000000) >> 14));
        var endurance2 = (byte) ((characterDbEntry.CurrentEndurance & 0b11111111000000) >> 6);
        var earth1 = (byte) (((characterDbEntry.CurrentEarth & 0b111111) << 2) +
                             ((characterDbEntry.CurrentEndurance & 0b1100000000000000) >> 14));
        var earth2 = (byte) ((characterDbEntry.CurrentEarth & 0b11111111000000) >> 6);
        var air1 = (byte) (((characterDbEntry.CurrentAir & 0b111111) << 2) +
                           ((characterDbEntry.CurrentEarth & 0b1100000000000000) >> 14));
        var air2 = (byte) ((characterDbEntry.CurrentAir & 0b11111111000000) >> 6);
        var water1 = (byte) (((characterDbEntry.CurrentWater & 0b111111) << 2) +
                             ((characterDbEntry.CurrentAir & 0b1100000000000000) >> 14));
        var water2 = (byte) ((characterDbEntry.CurrentWater & 0b11111111000000) >> 6);
        var fire1 = (byte) (((characterDbEntry.CurrentFire & 0b111111) << 2) +
                            ((characterDbEntry.CurrentWater & 0b1100000000000000) >> 14));
        var fire2 = (byte) ((characterDbEntry.CurrentFire & 0b11111111000000) >> 6);
        var pdef1 = (byte) (((characterDbEntry.PDef & 0b111111) << 2) +
                            ((characterDbEntry.CurrentFire & 0b1100000000000000) >> 14));
        var pdef2 = (byte) ((characterDbEntry.PDef & 0b11111111000000) >> 6);
        var mdef1 = (byte) (((characterDbEntry.MDef & 0b111111) << 2) +
                            ((characterDbEntry.PDef & 0b1100000000000000) >> 14));
        var mdef2 = (byte) ((characterDbEntry.MDef & 0b11111111000000) >> 6);
        var karma1 = (byte) ((((byte) characterDbEntry.Karma & 0b111111) << 2) +
                             ((characterDbEntry.MDef & 0b1100000000000000) >> 14));
        var satietyMax1 = (byte) (((characterDbEntry.MaxSatiety & 0b111111) << 2) +
                                  (((byte) characterDbEntry.Karma & 0b11000000) >> 14));
        var satietyMax2 = (byte) ((characterDbEntry.MaxSatiety & 0b11111111000000) >> 6);
        var titleLvl1 = (byte) (((characterDbEntry.TitleMinusOne & 0b111111) << 2) +
                                ((characterDbEntry.MaxSatiety & 0b1100000000000000) >> 14));
        var titleLvl2 = (byte) ((characterDbEntry.TitleMinusOne & 0b11111111000000) >> 6);
        var degreeLvl1 = (byte) (((characterDbEntry.DegreeMinusOne & 0b111111) << 2) +
                                 ((characterDbEntry.TitleMinusOne & 0b1100000000000000) >> 14));
        var degreeLvl2 = (byte) ((characterDbEntry.DegreeMinusOne & 0b11111111000000) >> 6);
        var titleXp1 = (byte) (((characterDbEntry.TitleXP & 0b111111) << 2) +
                               ((characterDbEntry.DegreeMinusOne & 0b1100000000000000) >> 14));
        var titleXp2 = (byte) ((characterDbEntry.TitleXP & 0b11111111000000) >> 6);
        var titleXp3 = (byte) ((characterDbEntry.TitleXP & 0b1111111100000000000000) >> 14);
        var titleXp4 = (byte) ((characterDbEntry.TitleXP & 0b111111110000000000000000000000) >> 22);
        var degreeXp1 = (byte) (((characterDbEntry.DegreeXP & 0b111111) << 2) +
                                ((characterDbEntry.TitleXP & 0b11000000000000000000000000000000) >> 30));
        var degreeXp2 = (byte) ((characterDbEntry.DegreeXP & 0b11111111000000) >> 6);
        var degreeXp3 = (byte) ((characterDbEntry.DegreeXP & 0b1111111100000000000000) >> 14);
        var degreeXp4 = (byte) ((characterDbEntry.DegreeXP & 0b111111110000000000000000000000) >> 22);
        var satietyCurrent1 = (byte) (((characterDbEntry.CurrentSatiety & 0b111111) << 2) +
                                      ((characterDbEntry.DegreeXP & 0b11000000000000000000000000000000) >> 30));
        var satietyCurrent2 = (byte) ((characterDbEntry.CurrentSatiety & 0b11111111000000) >> 6);
        var hpCurrent1 = (byte) (((characterDbEntry.CurrentHP & 0b111111) << 2) +
                                 ((characterDbEntry.CurrentSatiety & 0b1100000000000000) >> 14));
        var hpCurrent2 = (byte) ((characterDbEntry.CurrentHP & 0b11111111000000) >> 6);
        var mpCurrent1 = (byte) (((characterDbEntry.CurrentMP & 0b111111) << 2) +
                                 ((characterDbEntry.CurrentHP & 0b1100000000000000) >> 14));
        var mpCurrent2 = (byte) ((characterDbEntry.CurrentMP & 0b11111111000000) >> 6);
        var titleStats1 =
            (byte) (((characterDbEntry.AvailableTitleStats & 0b111111) << 2) +
                    ((characterDbEntry.CurrentMP & 0b1100000000000000) >> 14));
        var titleStats2 = (byte) ((characterDbEntry.AvailableTitleStats & 0b11111111000000) >> 6);
        var degreeStats1 = (byte) (((characterDbEntry.AvailableDegreeStats & 0b111111) << 2) +
                                   ((characterDbEntry.AvailableTitleStats & 0b1100000000000000) >> 14));
        var degreeStats2 = (byte) ((characterDbEntry.AvailableDegreeStats & 0b11111111000000) >> 6);
        var degreeStats3 =
            (byte) ((0b111010 << 2) + ((characterDbEntry.AvailableDegreeStats & 0b1100000000000000) >> 14));
        var isFemale1 = (byte) ((characterDbEntry.IsGenderFemale ? 1 : 0) << 2);
        var name1 = (byte) ((nameEncodedWithPadding[0] & 0b111111) << 2);
        var name2 = (byte) (((nameEncodedWithPadding[1] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[0] & 0b11000000) >> 6));
        var name3 = (byte) (((nameEncodedWithPadding[2] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[1] & 0b11000000) >> 6));
        var name4 = (byte) (((nameEncodedWithPadding[3] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[2] & 0b11000000) >> 6));
        var name5 = (byte) (((nameEncodedWithPadding[4] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[3] & 0b11000000) >> 6));
        var name6 = (byte) (((nameEncodedWithPadding[5] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[4] & 0b11000000) >> 6));
        var name7 = (byte) (((nameEncodedWithPadding[6] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[5] & 0b11000000) >> 6));
        var name8 = (byte) (((nameEncodedWithPadding[7] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[6] & 0b11000000) >> 6));
        var name9 = (byte) (((nameEncodedWithPadding[8] & 0b111111) << 2) +
                            ((nameEncodedWithPadding[7] & 0b11000000) >> 6));
        var name10 = (byte) (((nameEncodedWithPadding[9] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[8] & 0b11000000) >> 6));
        var name11 = (byte) (((nameEncodedWithPadding[10] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[9] & 0b11000000) >> 6));
        var name12 = (byte) (((nameEncodedWithPadding[11] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[10] & 0b11000000) >> 6));
        var name13 = (byte) (((nameEncodedWithPadding[12] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[11] & 0b11000000) >> 6));
        var name14 = (byte) (((nameEncodedWithPadding[13] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[12] & 0b11000000) >> 6));
        var name15 = (byte) (((nameEncodedWithPadding[14] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[13] & 0b11000000) >> 6));
        var name16 = (byte) (((nameEncodedWithPadding[15] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[14] & 0b11000000) >> 6));
        var name17 = (byte) (((nameEncodedWithPadding[16] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[15] & 0b11000000) >> 6));
        var name18 = (byte) (((nameEncodedWithPadding[17] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[16] & 0b11000000) >> 6));
        var name19 = (byte) (((nameEncodedWithPadding[18] & 0b111111) << 2) +
                             ((nameEncodedWithPadding[17] & 0b11000000) >> 6));

        var face1 = (byte) (((characterDbEntry.FaceType & 0b111111) << 2) +
                            ((nameEncodedWithPadding[18] & 0b11000000) >> 6));
        var hairStyle1 = (byte) (((characterDbEntry.HairStyle & 0b111111) << 2) +
                                 ((characterDbEntry.FaceType & 0b11000000) >> 6));
        var hairColor1 = (byte) (((characterDbEntry.HairColor & 0b111111) << 2) +
                                 ((characterDbEntry.HairStyle & 0b11000000) >> 6));
        var tattoo1 = (byte) (((characterDbEntry.Tattoo & 0b111111) << 2) +
                              ((characterDbEntry.HairColor & 0b11000000) >> 6));
        var bootsModelId = (byte) (((characterDbEntry.BootModelId & 0b111111) << 2) +
                                   ((characterDbEntry.Tattoo & 0b11000000) >> 6));
        var pantsModelId = (byte) (((characterDbEntry.PantsModelId & 0b111111) << 2) +
                                   ((characterDbEntry.BootModelId & 0b11000000) >> 6));
        var armorModelId = (byte) (((characterDbEntry.ArmorModelId & 0b111111) << 2) +
                                   ((characterDbEntry.PantsModelId & 0b11000000) >> 6));
        var helmetModelId = (byte) (((characterDbEntry.HelmetModelId & 0b111111) << 2) +
                                    ((characterDbEntry.ArmorModelId & 0b11000000) >> 6));
        var glovesModelId1 = (byte) (((characterDbEntry.GlovesModelId & 0b111111) << 2) +
                                     ((characterDbEntry.HelmetModelId & 0b11000000) >> 6));
        var glovesModelId2 = (byte) ((characterDbEntry.GlovesModelId & 0b11000000) >> 6);
        var isNotDeleted1 = (byte) (((characterDbEntry.IsNotQueuedForDeletion ? 1 : 0) << 1) + 1);

        var lookType = (byte) (characterDbEntry.IsNotQueuedForDeletion ? 0x79 : 0x19);

        var charDataBytes = new byte[]
        {
            0x6C, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(characterDbEntry.ClientIndex),
            MinorByte(characterDbEntry.ClientIndex), 0x08, 0x40,
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

    public byte[] ToGameDataByteArray ()
    {
        var nameEncoded = SphEncoding.Win1251!.GetBytes(characterDbEntry.Name);
        var x = CoordsHelper.EncodeServerCoordinate(characterDbEntry.X);
        var y = CoordsHelper.EncodeServerCoordinate(-characterDbEntry.Y);
        var z = CoordsHelper.EncodeServerCoordinate(characterDbEntry.Z);
        var t = CoordsHelper.EncodeServerCoordinate(characterDbEntry.Angle);
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
            MajorByte(characterDbEntry.ClientIndex),
            MinorByte(characterDbEntry.ClientIndex),
            0x08,
            0x00,
            (byte) (((nameLen & 0b111) << 5) + 2),
            (byte) (((nameEncoded[0] & 0b111) << 5) + ((nameLen & 0b11111000) >> 3))
        };

        for (var i = 1; i < nameEncoded.Length; i++)
        {
            data.Add((byte) (((nameEncoded[i] & 0b111) << 5) + ((nameEncoded[i - 1] & 0b11111000) >> 3)));
        }

        data.Add((byte) ((nameEncoded[^1] & 0b11111000) >> 3));

        if (characterDbEntry.Clan?.Id == null || characterDbEntry.Clan?.Id == ClanDbEntry.DefaultClanDbEntry.Id)
        {
            data.Add(0x00);
            data.Add(0x6E);
        }
        else
        {
            var clanNameEncoded = SphEncoding.Win1251.GetBytes(characterDbEntry.Clan!.Name);
            var clanNameLength = clanNameEncoded.Length;
            data.Add((byte) ((clanNameLength & 0b111) << 5));
            data.Add((byte) (((clanNameEncoded[0] & 0b1111111) << 1) + ((clanNameLength & 0b1000) >> 3)));

            for (var i = 1; i < clanNameLength; i++)
            {
                data.Add((byte) (((clanNameEncoded[i] & 0b1111111) << 1) +
                                 ((clanNameEncoded[i - 1] & 0b10000000) >> 7)));
            }

            data.Add((byte) (0b01100000 + ((byte) characterDbEntry.ClanRank << 1) +
                             ((clanNameEncoded[^1] & 0b10000000) >> 7)));
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
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Helmet) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Amulet) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Shield) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Chestplate) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Gloves) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Belt) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.BraceletLeft) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.BraceletRight) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Ring_1) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Ring_2) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Ring_3) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Ring_4) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Pants) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Boots) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Guild) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.MapBook) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.RecipeBook) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.MantraBook) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inkpot) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Money) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Backpack) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Key_1) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Key_2) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Mission) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_1) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_2) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_3) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_4) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_5) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_6) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_7) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_8) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_9) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Inventory_10) ? 0x00 : 0x04));
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
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Special_1) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Special_2) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Special_3) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Special_4) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Special_5) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Special_6) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Special_7) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Special_8) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Special_9) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.Ammo) ? 0x00 : 0x04));
        data.Add(0x00);
        data.Add((byte) (characterDbEntry.IsItemSlotEmpty(BelongingSlot.SpeedhackMantra) ? 0x00 : 0x04));
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

        data.Add((byte) (((characterDbEntry.CurrentHP & 0b111) << 5) + 0b10011));
        data.Add((byte) ((characterDbEntry.CurrentHP & 0b11111111000) >> 3));
        data.Add((byte) (((characterDbEntry.MaxHP & 0b11) << 6) + (0b100 << 3) +
                         ((characterDbEntry.CurrentHP & 0b11100000000000) >> 11)));
        data.Add((byte) ((characterDbEntry.MaxHP & 0b1111111100) >> 2));
        data.Add((byte) (((byte) characterDbEntry.Karma << 4) + ((characterDbEntry.MaxHP & 0b11110000000000) >> 10)));
        var toEncode = characterDbEntry.DegreeMinusOne * 100 + characterDbEntry.TitleMinusOne;
        data.Add((byte) (((toEncode & 0b111111) << 2) + 2));
        data.Add((byte) ((toEncode & 0b11111111000000) >> 6));

        data.Add(0x80);

        if (characterDbEntry.Guild == Guild.None)
        {
            data.Add(0x00);
        }
        else
        {
            data.Add((byte) ((1 << 7) + ((byte) characterDbEntry.Guild << 1)));
        }

        data.Add((byte) (((characterDbEntry.Money & 0b1111) << 4) + characterDbEntry.GuildLevelMinusOne));
        data.Add((byte) ((characterDbEntry.Money & 0b111111110000) >> 4));
        data.Add((byte) ((characterDbEntry.Money & 0b11111111000000000000) >> 12));
        data.Add((byte) ((characterDbEntry.Money & 0b1111111100000000000000000000) >> 20));
        data.Add((byte) ((characterDbEntry.Money & 0b11110000000000000000000000000000) >> 28));

        var arr = data.ToArray();
        arr[0] = (byte) arr.Length;

        return arr;
    }

    public byte[] GetTeleportByteArray (WorldCoords coords)
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
            0x1F, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(characterDbEntry.ClientIndex),
            MinorByte(characterDbEntry.ClientIndex), 0x08, 0x40, 0xE3,
            0x01,
            (byte) x_1, (byte) x_2, (byte) x_3, (byte) x_4, (byte) y_1, (byte) y_2, (byte) y_3, (byte) y_4, (byte) z_1,
            (byte) z_2, (byte) z_3, (byte) z_4, (byte) t_1, (byte) t_2, (byte) t_3, (byte) t_4, (byte) t_5, 0x00
        };
        return tpBytes;
    }

    public byte[] GetNewPlayerDungeonTeleportAndUpdateStatsByteArray (WorldCoords coords)
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
            0xAB, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MajorByte(characterDbEntry.ClientIndex),
            MinorByte(characterDbEntry.ClientIndex), 0x08, 0x40, 0xE3,
            0x01,
            (byte) x_1, (byte) x_2, (byte) x_3, (byte) x_4, (byte) y_1, (byte) y_2, (byte) y_3, (byte) y_4, (byte) z_1,
            (byte) z_2, (byte) z_3, (byte) z_4, (byte) t_1, (byte) t_2, (byte) t_3, (byte) t_4, (byte) t_5, 0x20, 0x08,
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