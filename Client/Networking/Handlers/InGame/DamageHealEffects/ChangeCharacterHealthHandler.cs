using System;
using System.Threading.Tasks;
using SphereHelpers.Extensions;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.ClientEvents;
using SphServer.Shared.Networking;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;

/// <summary>
/// Only place that applies self HP and sends the health packets
/// </summary>
public sealed class ChangeCharacterHealthHandler (SphereClient sphereClient) : IClientEventHandler
{
    public Task HandleAsync (CharacterHealthChangeEvent clientEvent)
    {
        var character = sphereClient.CurrentCharacter;
        if (character is null || clientEvent.HealthDiff == 0)
        {
            return Task.CompletedTask;
        }

        var selfId = character.ClientIndex;
        // Position stream process is the swapped client index
        var playerProcess = SphBitStream.ByteSwap (selfId);
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
            hpAfter = Math.Clamp (hpBefore + clientEvent.HealthDiff, 0, character.MaxHP);
            healthDiff = hpAfter - hpBefore;
        }

        if (healthDiff == 0)
        {
            return Task.CompletedTask;
        }

        character.CurrentHP = (ushort) hpAfter;
        var becameDead = hpBefore > 0 && hpAfter <= 0;
        var air = clientEvent.Origin == DamageOriginSpecial.НехваткаВоздуха;
        if (air && character.CurrentMP > 0)
        {
            character.CurrentMP--;
        }

        if (becameDead)
        {
            // hit_type above 3 is the killer process; the client asks it for the name
            var hitType = clientEvent.KillerProcessId > 3 ? clientEvent.KillerProcessId : (ushort) 0;
            var hpDelta = -100000;
            var secondDelta = 0;
            if (air)
            {
                // hit_type 2, second -1: gMsg 752, death gMsg 753, and that second is the MP delta
                hitType = 2;
                hpDelta = healthDiff;
                secondDelta = -1;
            }

            sphereClient.MaybeQueueNetworkPacketSend (
                CommonPackets.BuildPlayerReceiveHit (playerProcess, hitType, hpDelta, secondDelta, flags: 7));
        }
        else if (air)
        {
            sphereClient.MaybeQueueNetworkPacketSend (
                CommonPackets.BuildPlayerReceiveHit (playerProcess, 2, healthDiff, -1, 0));
        }
        else
        {
            // ATTACK echo is the UI hit; the ContMan delta keeps g_rec_0C48 aligned
            sphereClient.MaybeQueueNetworkPacketSend (BuildHealthChangePacket (selfId, entityId, healthDiff));
            sphereClient.MaybeQueueNetworkPacketSend (
                CommonPackets.BuildPlayerApplyHpDelta (playerProcess, selfId, healthDiff));
        }

        NetworkedStatsUpdater.Update (character, refreshPeers: false);
        sphereClient.BroadcastHpToVisibleClients (healthDiff, character.CurrentHP,
            includeMax: hpBefore <= 0 && hpAfter > 0);

        if (becameDead)
        {
            sphereClient.DisarmUnderwaterBreath ();
            sphereClient.SchedulePlayerRespawn ();
        }

        sphereClient.SaveCharacter ();
        return Task.CompletedTask;
    }

    Task IClientEventHandler.HandleAsync (ClientQueuedEvent clientEvent) =>
        HandleAsync ((CharacterHealthChangeEvent) clientEvent);

    /// <summary>
    /// Classic ATTACK health blob; omit origin and the damaged entity is the name in parentheses
    /// </summary>
    public static byte[] BuildHealthChangePacket (ushort localId, ushort entityId, int healthDiff,
        ushort? originEntityId = null)
    {
        var stream = SphBitStream.GetWriteBitStream ();
        var playerId = SphBitStream.ByteSwap (localId);
        // Mob id stays raw. A player origin uses the swapped id, same as playerId
        var origin = originEntityId ?? entityId;
        var originWire = origin == localId ? playerId : origin;
        var magnitude = Math.Abs (healthDiff);
        var hpMod = healthDiff < 0 ? 0b1110 : 0b1100;

        stream.WriteUInt16 (entityId, 16);
        stream.WriteByte (0, 2);
        stream.WriteUInt16 ((ushort) ObjectType.Monster, 10);
        stream.WriteByte (0, 1);
        stream.WriteByte ((byte) EntityActionType.ATTACK, 8);
        stream.WriteUInt16 (0x0003, 16);
        stream.WriteByte (0, 3);
        stream.WriteBytes ([0x00, 0x80, 0xA0, 0x03, 0x00, 0x00, 0xE0]);

        stream.WriteByte (0b0111, 4);
        stream.WriteUInt16 (playerId, 16);
        stream.WriteByte (0, 3);
        stream.WriteByte (1, 1);

        stream.WriteBytes ([0x00, 0x24]);

        stream.WriteByte (0, 1);
        stream.WriteUInt16 (originWire, 15);

        stream.WriteByte ((byte) (hpMod | ((originWire >> 15) & 1)), 4);
        stream.WriteUInt32 ((uint) magnitude, 20);

        return Packet.ToByteArray (stream.GetStreamData (), 3);
    }
}
