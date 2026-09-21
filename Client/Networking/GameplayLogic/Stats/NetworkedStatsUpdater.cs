using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SphereHelpers.Extensions;
using SphServer.Client;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Client.Networking.GameplayLogic.Stats;

using static Stat;

public static class NetworkedStatsUpdater
{
    // Nameplate fields peers re-show when changed (entity_character).
    private static readonly HashSet<Stat> NameplateStats =
    [
        HpCurrent,
        KarmaType,
        TitleLevel,
        DegreeLevel,
        TitleRebirth,
        DegreeRebirth,
        GuildPlus64,
        GuildRank,
    ];

    // Classic 08 C0 field order from live: header is HpCurrent, then these, skip 47-50 and 57.
    private static readonly Stat[] SyncedStats =
    [
        HpCurrent,
        HpMax,
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
        Unk14,
        Unk15,
        PD,
        MD,
        PA,
        MA,
        IsInvisible,
        GuildPlus64,
        GuildRank,
        Unk23,
        HpMaxBase,
        MpMaxBase,
        SatietyMaxBase,
        StrengthBase,
        AgilityBase,
        AccuracyBase,
        EnduranceBase,
        EarthBase,
        AirBase,
        WaterBase,
        FireBase,
        Unk35,
        Unk36,
        TitleLevel,
        DegreeLevel,
        KarmaType,
        Karma,
        TitleXp,
        DegreeXp,
        StatsAvailable,
        TitleStatsAvailable,
        DegreeStatsAvailable,
        Gender,
        Unk51,
        TitleRebirth,
        DegreeRebirth,
        Unk54,
    ];

    private static readonly ConcurrentDictionary<ushort, Dictionary<Stat, int>> LastSentByClient = new();

    public static void Clear(ushort clientIndex) => LastSentByClient.TryRemove(clientIndex, out _);

    /// <summary>
    ///     Record that a stat was already pushed by another packet (e.g. CurrentMpUpdatePing)
    ///     so the next <see cref="Update"/> does not duplicate it.
    /// </summary>
    public static void MarkSent(CharacterDbEntry characterDbEntry, Stat stat)
    {
        var value = BuildFieldMap(characterDbEntry)[stat];
        var lastSent = LastSentByClient.GetOrAdd(characterDbEntry.ClientIndex, _ => new Dictionary<Stat, int>());
        lastSent[stat] = value;
    }

    /// <param name="refreshPeers">
    ///     When true, also pushes nameplate WriteIndexedStat to viewers if nameplate fields changed.
    ///     Set false when the caller already sends peer HP another way (e.g. ContMan ApplyHp).
    /// </param>
    /// <param name="full">
    ///     When true, send every synced field (login / force resync). Otherwise only changed fields.
    /// </param>
    public static void Update(CharacterDbEntry characterDbEntry, Action<byte[]>? send = null,
        bool refreshPeers = true, bool full = false, bool log = true)
    {
        var characterFieldMap = BuildFieldMap(characterDbEntry);
        var clientIndex = characterDbEntry.ClientIndex;
        var hasPrevious = LastSentByClient.TryGetValue(clientIndex, out var lastSent);
        var sendFull = full || !hasPrevious;

        var fieldsToSend = new List<Stat>();
        var includeHpCurrentHeader = false;

        if (sendFull)
        {
            includeHpCurrentHeader = true;
            foreach (var stat in SyncedStats)
            {
                if (stat != HpCurrent)
                {
                    fieldsToSend.Add(stat);
                }
            }
        }
        else
        {
            foreach (var stat in SyncedStats)
            {
                var current = characterFieldMap[stat];
                if (lastSent!.TryGetValue(stat, out var previous) && previous == current)
                {
                    continue;
                }

                if (stat == HpCurrent)
                {
                    includeHpCurrentHeader = true;
                }
                else
                {
                    fieldsToSend.Add(stat);
                }
            }

            // Live none→guild is i21+i22 together. Rank 0 is a real Candidate value, not "omit".
            if (GuildFieldChanged(lastSent!, characterFieldMap))
            {
                if (!fieldsToSend.Contains(GuildPlus64))
                {
                    fieldsToSend.Add(GuildPlus64);
                }

                if (!fieldsToSend.Contains(GuildRank))
                {
                    fieldsToSend.Add(GuildRank);
                }
            }

            if (!includeHpCurrentHeader && fieldsToSend.Count == 0)
            {
                return;
            }
        }

        if (!includeHpCurrentHeader && fieldsToSend.Count == 0)
        {
            return;
        }

        var client = ActiveClients.Get(characterDbEntry.ClientIndex);
        if (!SendStatFields(characterDbEntry, send, client, fieldsToSend, characterFieldMap,
                includeHpCurrentHeader, sendFull && !hasPrevious))
        {
            return;
        }

        LastSentByClient[clientIndex] = characterFieldMap.ToDictionary(kv => kv.Key, kv => kv.Value);

        var nameplateChanged = sendFull
                               || includeHpCurrentHeader
                               || fieldsToSend.Any(NameplateStats.Contains);
        if (refreshPeers && nameplateChanged)
        {
            client?.BroadcastNameplateRefreshToVisibleClients();
        }

        if (!log)
        {
            return;
        }

        var sent = fieldsToSend.Select(s => $"{s}={characterFieldMap[s]}");
        if (includeHpCurrentHeader)
        {
            sent = sent.Prepend($"{HpCurrent}={characterFieldMap[HpCurrent]}");
        }

        SphLogger.Info(
            $"Stat update for client ID: {characterDbEntry.ClientIndex} ({(sendFull ? "full" : "delta")}). " +
            $"Values: {string.Join(", ", sent)}");
    }

    private static SortedDictionary<Stat, int> BuildFieldMap(CharacterDbEntry characterDbEntry)
    {
        return new SortedDictionary<Stat, int>
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
            [Unk14] = 1,
            [Unk15] = 1,
            [PD] = characterDbEntry.PDef,
            [MD] = characterDbEntry.MDef,
            [PA] = characterDbEntry.PAtk,
            [MA] = characterDbEntry.MAtk,
            [IsInvisible] = 0,
            // i21 membership is guild+64 (65..90, in_specNN). Never send 64: CheckRights
            // indexes buf_7068[i21-65] if i21>0.
            [GuildPlus64] = characterDbEntry.Guild == Guild.None
                ? 0
                : (int)characterDbEntry.Guild + 64,
            [GuildRank] = characterDbEntry.Guild == Guild.None
                ? 0
                : characterDbEntry.GuildLevelMinusOne,
            [Unk23] = 0,
            [HpMaxBase] = characterDbEntry.MaxHPBase,
            [MpMaxBase] = characterDbEntry.MaxMPBase,
            [SatietyMaxBase] = characterDbEntry.MaxSatiety,
            [StrengthBase] = characterDbEntry.BaseStrength,
            [AgilityBase] = characterDbEntry.BaseAgility,
            [AccuracyBase] = characterDbEntry.BaseAccuracy,
            [EnduranceBase] = characterDbEntry.BaseEndurance,
            [EarthBase] = characterDbEntry.BaseEarth,
            [AirBase] = characterDbEntry.BaseAir,
            [WaterBase] = characterDbEntry.BaseWater,
            [FireBase] = characterDbEntry.BaseFire,
            [Unk35] = 1,
            [Unk36] = 1,
            [TitleLevel] = characterDbEntry.TitleMinusOne % CharacterDataHelper.LevelsPerCycle,
            [DegreeLevel] = characterDbEntry.DegreeMinusOne % CharacterDataHelper.LevelsPerCycle,
            [KarmaType] = (int)characterDbEntry.Karma,
            [Karma] = characterDbEntry.KarmaCount,
            [TitleXp] = (int)characterDbEntry.TitleXP,
            [DegreeXp] = (int)characterDbEntry.DegreeXP,
            [TitleStatsAvailable] = Math.Max(0, characterDbEntry.AvailableTitleStats),
            [DegreeStatsAvailable] = Math.Max(0, characterDbEntry.AvailableDegreeStats),
            [StatsAvailable] = Math.Max(0, characterDbEntry.AvailableTitleStats)
                              + Math.Max(0, characterDbEntry.AvailableDegreeStats),
            [Gender] = characterDbEntry.IsGenderFemale ? 1 : 0,
            [ClanRankType] = (int)characterDbEntry.ClanRank,
            [Unk51] = 0,
            [TitleRebirth] = characterDbEntry.TitleMinusOne / CharacterDataHelper.LevelsPerCycle,
            [DegreeRebirth] = characterDbEntry.DegreeMinusOne / CharacterDataHelper.LevelsPerCycle,
            [Unk54] = 1,
            [Money] = characterDbEntry.Money,
        };
    }

    private static bool GuildFieldChanged(Dictionary<Stat, int> lastSent, SortedDictionary<Stat, int> current)
    {
        return !lastSent.TryGetValue(GuildPlus64, out var prevGuild)
               || prevGuild != current[GuildPlus64]
               || !lastSent.TryGetValue(GuildRank, out var prevRank)
               || prevRank != current[GuildRank];
    }

    private static bool SendStatFields(CharacterDbEntry characterDbEntry, Action<byte[]>? send,
        SphereClient? client, List<Stat> fields, SortedDictionary<Stat, int> map, bool includeHpCurrentHeader,
        bool firstSync)
    {
        var guildWire = map[GuildPlus64];
        var guildInFields = fields.Contains(GuildPlus64) || fields.Contains(GuildRank);
        // Login dump keeps 21/22 in the live 08 C0. Mid-game Recalc dumps mixed them with combat
        // fields and used 3-bit rank 0; that is the shape that stopped applying i21.
        var sendGuildDelta = guildInFields && !firstSync;
        var liveFields = sendGuildDelta
            ? fields.Where(stat => stat is not GuildPlus64 and not GuildRank).ToList()
            : fields;
        if (sendGuildDelta)
        {
            if (!TrySend(characterDbEntry, BuildGuildDeltaPacket(characterDbEntry, map), send, client)
                || !TrySend(characterDbEntry,
                    CommonPackets.BuildPlayerSetStat(characterDbEntry.ClientIndex, (byte)GuildPlus64, guildWire),
                    send, client)
                || !TrySend(characterDbEntry,
                    CommonPackets.BuildPlayerSetStat(characterDbEntry.ClientIndex, (byte)GuildRank, map[GuildRank]),
                    send, client))
            {
                return false;
            }
        }

        if (!includeHpCurrentHeader && liveFields.Count == 0)
        {
            return true;
        }

        var classic = BuildClassicSetStatPacket(characterDbEntry, liveFields, map, includeHpCurrentHeader);
        return TrySend(characterDbEntry, classic, send, client);
    }

    /// <summary>
    ///     Short 08 C0 that used to apply guild: hp_current header, then i21/i22 at 7-bit minimum
    ///     (Candidate rank is 0; 3-bit 0 was the live-dump width that the native parser dropped).
    /// </summary>
    private static byte[] BuildGuildDeltaPacket(CharacterDbEntry characterDbEntry,
        SortedDictionary<Stat, int> map)
    {
        return BuildClassicSetStatPacket(characterDbEntry, [GuildPlus64, GuildRank], map,
            includeHpCurrentHeader: true, minValueBits: 7);
    }

    private static byte[] BuildClassicSetStatPacket(CharacterDbEntry characterDbEntry, List<Stat> fieldMarkers,
        SortedDictionary<Stat, int> characterFieldMap, bool includeHpCurrentHeader, int minValueBits = 3)
    {
        var divider = 0b0001011;
        var fieldMarker3Bit = 0b00;
        var fieldMarker7Bit = 0b01;
        var fieldMarker14Bit = 0b10;
        var fieldMarker31Bit = 0b11;
        // Live 08 C0 header: 14-bit tag 0x2002 (marker 0 = hp_current) + 14-bit current HP.
        var hpCurrentHeaderTag = 0b10000000000010;

        var stream = SphBitStream.GetWriteBitStream();

        stream.WriteUInt16(SphBitStream.ByteSwap(characterDbEntry.ClientIndex));
        stream.WriteBytes([0x08, 0xC0]);

        if (includeHpCurrentHeader)
        {
            var currentHp = Math.Clamp(characterFieldMap[HpCurrent], 0, 16383);
            stream.WriteUInt16((ushort)hpCurrentHeaderTag, 14);
            stream.WriteUInt16((ushort)currentHp, 14);
        }

        foreach (var field in fieldMarkers)
        {
            var statValue = characterFieldMap[field];
            var statValueAbs = Math.Abs(statValue);
            var fieldLength = statValueAbs <= 7 && minValueBits <= 3 ? 3 :
                statValueAbs <= 127 ? 7 :
                statValueAbs <= 16383 ? 14 : 31;
            var fieldLengthMarker = fieldLength switch
            {
                3 => fieldMarker3Bit,
                7 => fieldMarker7Bit,
                14 => fieldMarker14Bit,
                _ => fieldMarker31Bit
            };
            var negativeBit = statValue < 0 ? 1 : 0;
            var fieldSeparator = (ushort)((fieldLengthMarker << 14) + (negativeBit << 13) + ((int)field << 7) + divider);
            stream.WriteUInt16(fieldSeparator, 16);
            var valueBits = ObjectPacketTools.IntToBits((uint)statValueAbs, fieldLength);
            stream.WriteBits(valueBits, fieldLength);
        }

        return Packet.ToByteArray(stream.GetStreamData(), 3);
    }

    private static bool TrySend(CharacterDbEntry characterDbEntry, byte[] packet, Action<byte[]>? send,
        SphereClient? client)
    {
        if (send is not null)
        {
            send(packet);
            return true;
        }

        if (client is null)
        {
            SphLogger.Warning($"No client found by ID: {characterDbEntry.ClientIndex}");
            return false;
        }

        client.MaybeQueueNetworkPacketSend(packet);
        return true;
    }
}
