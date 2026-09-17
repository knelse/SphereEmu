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

    // Order matches the classic full SetStat payload (HpMax is the special header tag).
    private static readonly Stat[] SyncedStats =
    [
        HpMax,
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
        // Local CheckPing region 10 SetStat (also mirrored as classic 08 C0).
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
        bool refreshPeers = true, bool full = false)
    {
        var characterFieldMap = BuildFieldMap(characterDbEntry);
        var clientIndex = characterDbEntry.ClientIndex;
        var hasPrevious = LastSentByClient.TryGetValue(clientIndex, out var lastSent);
        var sendFull = full || !hasPrevious;

        var fieldsToSend = new List<Stat>();
        var includeHpMax = false;

        if (sendFull)
        {
            includeHpMax = true;
            foreach (var stat in SyncedStats)
            {
                if (stat != HpMax)
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

                if (stat == HpMax)
                {
                    includeHpMax = true;
                }
                else
                {
                    fieldsToSend.Add(stat);
                }
            }

            if (!includeHpMax && fieldsToSend.Count == 0)
            {
                return;
            }
        }

        var client = ActiveClients.Get(characterDbEntry.ClientIndex);
        // Classic 08 C0 keeps legacy UI paths. MBC region 10 writes g_rec_0C48 (CycleSend / Recalc).
        var classic = BuildClassicSetStatPacket(characterDbEntry, fieldsToSend, characterFieldMap, includeHpMax);
        if (!TrySend(characterDbEntry, classic, send, client))
        {
            return;
        }

        if (includeHpMax)
        {
            TrySend(characterDbEntry,
                CommonPackets.BuildPlayerSetStat(clientIndex, (byte)HpMax, characterFieldMap[HpMax]), send, client);
        }

        foreach (var field in fieldsToSend)
        {
            TrySend(characterDbEntry,
                CommonPackets.BuildPlayerSetStat(clientIndex, (byte)field, characterFieldMap[field]), send, client);
        }

        LastSentByClient[clientIndex] = characterFieldMap.ToDictionary(kv => kv.Key, kv => kv.Value);

        var nameplateChanged = sendFull
                               || includeHpMax
                               || fieldsToSend.Any(NameplateStats.Contains);
        if (refreshPeers && nameplateChanged)
        {
            client?.BroadcastNameplateRefreshToVisibleClients();
        }

        var sent = fieldsToSend.Select(s => $"{s}={characterFieldMap[s]}");
        if (includeHpMax)
        {
            sent = sent.Prepend($"{HpMax}={characterFieldMap[HpMax]}");
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
    }

    private static byte[] BuildClassicSetStatPacket(CharacterDbEntry characterDbEntry, List<Stat> fieldMarkers,
        SortedDictionary<Stat, int> characterFieldMap, bool includeHpMax)
    {
        var divider = 0b0001011;
        var fieldMarker7Bit = 0b01;
        var fieldMarker14Bit = 0b10;
        var fieldMarker31Bit = 0b11;
        var hpMaxMarker = 0b10000000100010;

        var stream = SphBitStream.GetWriteBitStream();

        stream.WriteUInt16(SphBitStream.ByteSwap(characterDbEntry.ClientIndex));
        stream.WriteBytes([0x08, 0xC0]);

        if (includeHpMax)
        {
            stream.WriteUInt16((ushort)hpMaxMarker, 14);
            stream.WriteUInt16(characterDbEntry.MaxHP, 14);
        }

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
