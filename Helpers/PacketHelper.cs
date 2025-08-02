using System;
using System.Collections.Generic;
using System.IO;
using BitStreams;
using SphServer.DataModels;
using SphServer.Packets;
using static SphServer.Helpers.BitHelper;

namespace SphServer.Helpers;

using static Stat;

public static class PacketHelper
{
    public static void UpdateStatsForClient (Character character)
    {
        var divider = 0b0001011;
        var fieldMarker7Bit = 0b01;
        var fieldMarker14Bit = 0b10;
        var fieldMarker31Bit = 0b11;

        // to write 0x08 0xC0 instead
        var hpMaxMarker = 0b10000000100010;

        // TODO: change char fields and game objects to dict too, maybe
        var fieldMarkers = new Dictionary<Stat, int>
        {
            // // [HpCurrent] =				0b000000,
            // already used // // // // [HpMax] =					0b000001,
            // not applicable? [MpCurrent] =				0b000010,
            [MpMax] = 0b000011,
            [SatietyCurrent] = 0b000100,
            [SatietyMax] = 0b000101,
            [Strength] = 0b000110,
            [Agility] = 0b000111,
            [Accuracy] = 0b001000,
            [Endurance] = 0b001001,
            [Earth] = 0b001010,
            [Air] = 0b001011,
            [Water] = 0b001100,
            [Fire] = 0b001101,
            [PD] = 0b010000,
            [MD] = 0b010001,
            // // [IsInvisible] =				0b010100, later
            [TitleLevel] = 0b100101,
            [DegreeLevel] = 0b100110,
            [KarmaType] = 0b100111,
            [Karma] = 0b101000,
            [TitleXp] = 0b101001,
            [DegreeXp] = 0b101010,
            [TitleStatsAvailable] = 0b101100,
            [DegreeStatsAvailable] = 0b101101,
            [ClanRankType] = 0b101111,
            [Money] = 0b111001,
            [PA] = 0b010010,
            [MA] = 0b010011
        };

        var characterFieldMap = new Dictionary<Stat, int>
        {
            [HpCurrent] = character.CurrentHP,
            [HpMax] = character.MaxHP,
            [MpCurrent] = character.CurrentMP,
            [MpMax] = character.MaxMP,
            [SatietyCurrent] = character.CurrentSatiety,
            [SatietyMax] = character.MaxSatiety,
            [Strength] = character.CurrentStrength,
            [Agility] = character.CurrentAgility,
            [Accuracy] = character.CurrentAccuracy,
            [Endurance] = character.CurrentEndurance,
            [Earth] = character.CurrentEarth,
            [Air] = character.CurrentAir,
            [Water] = character.CurrentWater,
            [Fire] = character.CurrentFire,
            [PD] = character.PDef,
            [MD] = character.MDef,
            [TitleLevel] = character.TitleMinusOne,
            [DegreeLevel] = character.DegreeMinusOne,
            [KarmaType] = (int) character.Karma,
            [Karma] = character.KarmaCount,
            [TitleXp] = (int) character.TitleXP,
            [DegreeXp] = (int) character.DegreeXP,
            [TitleStatsAvailable] = character.AvailableTitleStats,
            [DegreeStatsAvailable] = character.AvailableDegreeStats,
            [ClanRankType] = (int) character.ClanRank,
            [Money] = character.Money,
            [PA] = character.PAtk,
            [MA] = character.MAtk
        };

        var memoryStream = new MemoryStream();
        var stream = new BitStream(memoryStream)
        {
            AutoIncreaseStream = true
        };

        stream.WriteBytes([MajorByte(character.ClientIndex), MinorByte(character.ClientIndex), 0x08, 0xC0], 4, true);
        stream.WriteUInt16((ushort) hpMaxMarker, 14);
        stream.WriteUInt16(character.MaxHP, 14);

        foreach (var (field, marker) in fieldMarkers)
        {
            var statValue = characterFieldMap[field];
            var statValueAbs = Math.Abs(statValue);
            var fieldLength = statValueAbs <= 127 ? 7 :
                statValueAbs <= 16383 ? 14 : 31;
            var fieldLengthMarker = fieldLength switch
            {
                7 => fieldMarker7Bit,
                14 => fieldMarker14Bit,
                _ => fieldMarker31Bit
            };
            var negativeBit = statValue < 0 ? 1 : 0;
            var fieldSeparator = (ushort) ((fieldLengthMarker << 14) + (negativeBit << 13) + (marker << 7) + divider);
            stream.WriteUInt16(fieldSeparator);
            var valueBits = ObjectPacketTools.IntToBits((uint) statValueAbs, fieldLength);
            stream.WriteBits(valueBits, fieldLength);
        }

        var client = SphereServer.GetClient(character.ClientIndex);

        client?.StreamPeer.PutData(Packet.ToByteArray(stream.GetStreamData(), 3));
        Console.WriteLine("Stat update");
    }
}