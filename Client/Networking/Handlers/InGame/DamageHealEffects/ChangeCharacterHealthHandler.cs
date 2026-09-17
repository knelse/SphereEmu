using System;
using System.Threading.Tasks;
using SphereHelpers.Extensions;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.ClientEvents;
using SphServer.Shared.Networking;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;

/// <summary>
///     Sole owner of self HP apply + client health packets.
///     Combat/admin only enqueue <see cref="CharacterHealthChangeEvent"/>.
/// </summary>
public sealed class ChangeCharacterHealthHandler(SphereClient sphereClient) : IClientEventHandler
{
    public Task HandleAsync(CharacterHealthChangeEvent clientEvent)
    {
        var character = sphereClient.CurrentCharacter;
        if (character is null || clientEvent.HealthDiff == 0)
        {
            return Task.CompletedTask;
        }

        var selfId = character.ClientIndex;
        var entityId = clientEvent.EntityId == 0 ? selfId : clientEvent.EntityId;
        var hpBefore = character.CurrentHP;
        // Killing blow: apply/send exactly the remainder to 0 (never overshoot current HP).
        int hpAfter;
        int healthDiff;
        if (clientEvent.HealthDiff < 0 && hpBefore + clientEvent.HealthDiff <= 0)
        {
            healthDiff = -hpBefore;
            hpAfter = 0;
        }
        else
        {
            hpAfter = Math.Clamp(hpBefore + clientEvent.HealthDiff, 0, character.MaxHP);
            healthDiff = hpAfter - hpBefore;
        }

        if (healthDiff == 0)
        {
            return Task.CompletedTask;
        }

        character.CurrentHP = (ushort)hpAfter;
        var becameDead = hpBefore > 0 && hpAfter <= 0;

        // Classic ATTACK echo (UI hit). ContMan delta keeps MBC g_rec_0C48 aligned (clamped on wire).
        // NetworkedStatsUpdater then absolute-writes SetStat so client Recalc cannot leave HP < 0 or drift.
        sphereClient.MaybeQueueNetworkPacketSend(BuildHealthChangePacket(selfId, entityId, healthDiff));
        sphereClient.MaybeQueueNetworkPacketSend(
            CommonPackets.BuildPlayerApplyHpDelta(selfId, selfId, healthDiff));
        NetworkedStatsUpdater.Update(character, refreshPeers: false);
        sphereClient.BroadcastHpToVisibleClients(healthDiff, character.CurrentHP,
            includeMax: hpBefore <= 0 && hpAfter > 0);

        if (becameDead)
        {
            sphereClient.SchedulePlayerRespawn();
        }

        sphereClient.SaveCharacter();
        return Task.CompletedTask;
    }

    Task IClientEventHandler.HandleAsync(ClientQueuedEvent clientEvent) =>
        HandleAsync((CharacterHealthChangeEvent)clientEvent);

    /// <summary>
    ///     Classic ATTACK health-change blob (same wire as the old byte-packed builder).
    /// </summary>
    public static byte[] BuildHealthChangePacket(ushort localId, ushort entityId, int healthDiff)
    {
        var stream = SphBitStream.GetWriteBitStream();
        var playerId = SphBitStream.ByteSwap(localId);
        var originId = SphBitStream.ByteSwap(entityId);
        var magnitude = Math.Abs(healthDiff);
        var hpMod = healthDiff < 0 ? 0b1110 : 0b1100;

        stream.WriteUInt16(entityId, 16);
        stream.WriteByte(0, 2);
        stream.WriteUInt16((ushort)ObjectType.Monster, 10);
        stream.WriteByte(0, 1);
        stream.WriteByte((byte)EntityActionType.ATTACK, 8);
        stream.WriteUInt16(0x0003, 16);
        stream.WriteByte(0, 3);
        stream.WriteBytes([0x00, 0x80, 0xA0, 0x03, 0x00, 0x00, 0xE0]);

        stream.WriteByte(0b0111, 4);
        stream.WriteUInt16(playerId, 16);
        stream.WriteByte(0, 3);
        stream.WriteByte(1, 1);

        stream.WriteBytes([0x00, 0x24]);

        stream.WriteByte(0, 1);
        stream.WriteUInt16(originId, 15);

        stream.WriteByte((byte)(hpMod | ((entityId >> 15) & 1)), 4);
        stream.WriteUInt32((uint)magnitude, 20);

        return Packet.ToByteArray(stream.GetStreamData(), 3);
    }
}
