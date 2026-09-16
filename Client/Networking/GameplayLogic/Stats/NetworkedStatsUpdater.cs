using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Client.Networking.GameplayLogic.Stats;

using static Stat;

public static class NetworkedStatsUpdater
{
    /// <param name="refreshPeers">
    ///     When true, also pushes nameplate WriteIndexedStat to viewers.
    ///     Set false when the caller already sends peer HP another way (e.g. ContMan ApplyHp).
    /// </param>
    public static void Update(CharacterDbEntry characterDbEntry, Action<byte[]>? send = null,
        bool refreshPeers = true)
    {
        var divider = 0b0001011;
        var fieldMarker7Bit = 0b01;
        var fieldMarker14Bit = 0b10;
        var fieldMarker31Bit = 0b11;

        // to write 0x08 0xC0 instead
        var hpMaxMarker = 0b10000000100010;

        var fieldMarkers = new List<Stat>
        {
            HpCurrent,
            MpCurrent,
            MpMax,
            SatietyCurrent,
            SatietyMax,
            Strength,
            Agility,
            Accuracy,
            Endurance,
            Earth,
            Air,
            Water,
            Fire,
            PD,
            MD,
            IsInvisible,
            GuildPlus64,
            GuildRank,
            TitleLevel,
            DegreeLevel,
            KarmaType,
            Karma,
            TitleXp,
            DegreeXp,
            TitleStatsAvailable,
            DegreeStatsAvailable,
            Gender,
            TitleRebirth,
            DegreeRebirth,
            ClanRankType,
            Money,
            PA,
            MA,
        };

        var characterFieldMap = new SortedDictionary<Stat, int>
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
            [IsInvisible] = 0, // static for now
            // MBC: no guild is i21=0 (reset defaults). Membership is guild+64 in 65..90 (in_specNN).
            // Sending 64 for Guild.None breaks ability checks that index buf[(i21-65)].
            [GuildPlus64] = characterDbEntry.Guild == Guild.None
                ? 0
                : (int)characterDbEntry.Guild + 64,
            [GuildRank] = characterDbEntry.Guild == Guild.None
                ? 0
                : characterDbEntry.GuildLevelMinusOne,
            [TitleLevel] = characterDbEntry.TitleMinusOne % CharacterDataHelper.LevelsPerCycle,
            [DegreeLevel] = characterDbEntry.DegreeMinusOne % CharacterDataHelper.LevelsPerCycle,
            [KarmaType] = (int)characterDbEntry.Karma,
            [Karma] = characterDbEntry.KarmaCount,
            [TitleXp] = (int)characterDbEntry.TitleXP,
            [DegreeXp] = (int)characterDbEntry.DegreeXP,
            [TitleStatsAvailable] = Math.Max(0, characterDbEntry.AvailableTitleStats),
            [DegreeStatsAvailable] = Math.Max(0, characterDbEntry.AvailableDegreeStats),
            [Gender] = characterDbEntry.IsGenderFemale ? 1 : 0,
            [TitleRebirth] = characterDbEntry.TitleMinusOne / CharacterDataHelper.LevelsPerCycle,
            [DegreeRebirth] = characterDbEntry.DegreeMinusOne / CharacterDataHelper.LevelsPerCycle,
            [ClanRankType] = (int)characterDbEntry.ClanRank,
            [Money] = characterDbEntry.Money,
            [PA] = characterDbEntry.PAtk,
            [MA] = characterDbEntry.MAtk
        };

        var stream = SphBitStream.GetWriteBitStream();

        stream.WriteUInt16(SphBitStream.ByteSwap(characterDbEntry.ClientIndex));
        stream.WriteBytes([0x08, 0xC0]);
        stream.WriteUInt16((ushort)hpMaxMarker, 14);
        stream.WriteUInt16(characterDbEntry.MaxHP, 14);

        foreach (var field in fieldMarkers)
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
            var fieldSeparator = (ushort)((fieldLengthMarker << 14) + (negativeBit << 13) + ((int)field << 7) + divider);
            stream.WriteUInt16(fieldSeparator);
            var valueBits = ObjectPacketTools.IntToBits((uint)statValueAbs, fieldLength);
            stream.WriteBits(valueBits, fieldLength);
        }

        var packet = Packet.ToByteArray(stream.GetStreamData(), 3);
        var client = ActiveClients.Get(characterDbEntry.ClientIndex);
        if (send is not null)
        {
            send(packet);
        }
        else if (client is null)
        {
            SphLogger.Warning($"No client found by ID: {characterDbEntry.ClientIndex}");
            return;
        }
        else
        {
            client.MaybeQueueNetworkPacketSend(packet);
        }

        // Peers read nameplate fields from entity_character (HP, karma type, levels,
        // rebirth, guild). Re-show keeps those in sync; karma count stays local-only.
        if (refreshPeers)
        {
            client?.BroadcastNameplateRefreshToVisibleClients();
        }

        var updatedStats = string.Join(", ", characterFieldMap.Select(kv => $"{kv.Key}={kv.Value}"));
        SphLogger.Info($"Stat update for client ID: {characterDbEntry.ClientIndex}. New stat values: {updatedStats}");
    }
}