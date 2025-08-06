using System;
using System.Collections.Generic;
using System.IO;
using BitStreams;
using Newtonsoft.Json;
using SphServer.Packets;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.GameplayLogic.Stats;

using static Stat;

public static class NetworkedStatsUpdater
{
    public static void Update (CharacterDbEntry characterDbEntry)
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
            [HpCurrent] = characterDbEntry.CurrentHP,
            [HpMax] = characterDbEntry.MaxHP,
            [MpCurrent] = characterDbEntry.CurrentMP,
            [MpMax] = characterDbEntry.MaxMP,
            [SatietyCurrent] = characterDbEntry.CurrentSatiety,
            [SatietyMax] = characterDbEntry.MaxSatiety,
            [Strength] = characterDbEntry.CurrentStrength,
            [Agility] = characterDbEntry.CurrentAgility,
            [Accuracy] = characterDbEntry.CurrentAccuracy,
            [Endurance] = characterDbEntry.CurrentEndurance,
            [Earth] = characterDbEntry.CurrentEarth,
            [Air] = characterDbEntry.CurrentAir,
            [Water] = characterDbEntry.CurrentWater,
            [Fire] = characterDbEntry.CurrentFire,
            [PD] = characterDbEntry.PDef,
            [MD] = characterDbEntry.MDef,
            [TitleLevel] = characterDbEntry.TitleMinusOne,
            [DegreeLevel] = characterDbEntry.DegreeMinusOne,
            [KarmaType] = (int) characterDbEntry.Karma,
            [Karma] = characterDbEntry.KarmaCount,
            [TitleXp] = (int) characterDbEntry.TitleXP,
            [DegreeXp] = (int) characterDbEntry.DegreeXP,
            [TitleStatsAvailable] = characterDbEntry.AvailableTitleStats,
            [DegreeStatsAvailable] = characterDbEntry.AvailableDegreeStats,
            [ClanRankType] = (int) characterDbEntry.ClanRank,
            [Money] = characterDbEntry.Money,
            [PA] = characterDbEntry.PAtk,
            [MA] = characterDbEntry.MAtk
        };

        var memoryStream = new MemoryStream();
        var stream = new BitStream(memoryStream)
        {
            AutoIncreaseStream = true
        };

        stream.WriteBytes(
            [MajorByte(characterDbEntry.ClientIndex), MinorByte(characterDbEntry.ClientIndex), 0x08, 0xC0], 4, true);
        stream.WriteUInt16((ushort) hpMaxMarker, 14);
        stream.WriteUInt16(characterDbEntry.MaxHP, 14);

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

        var client = ActiveClients.Get(characterDbEntry.ClientIndex);

        if (client is null)
        {
            SphLogger.Warning($"No client found by ID: {characterDbEntry.ClientIndex}");
            return;
        }

        client.MaybeQueueNetworkPacketSend(Packet.ToByteArray(stream.GetStreamData(), 3));
        var updatedStats = JsonConvert.SerializeObject(characterFieldMap);
        SphLogger.Info($"Stat update for client ID: {characterDbEntry.ClientIndex}. New stat values: {updatedStats}");
    }
}