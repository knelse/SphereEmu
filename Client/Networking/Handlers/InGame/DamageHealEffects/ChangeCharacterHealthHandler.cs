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
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;
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

        var selfId = character.EntityId;
        var entityId = clientEvent.EntityId == Guid.Empty ? selfId : clientEvent.EntityId;
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
            if (air)
            {
                // hit_type 2, second -1: gMsg 752, death gMsg 753, and that second is the MP delta
                sphereClient.MaybeQueueNetworkPacketSend (
                    CommonPackets.BuildPlayerReceiveHit (sphereClient.EntityID, 2, healthDiff, -1, flags: 7));
            }
            else
            {
                sphereClient.MaybeQueueNetworkPacketSend (
                    CommonPackets.BuildPlayerReceiveHit (sphereClient.EntityID, clientEvent.KillerProcessId,
                        hpDelta: -100000, secondDelta: 0, flags: 7));
            }
        }
        else if (air)
        {
            sphereClient.MaybeQueueNetworkPacketSend (
                CommonPackets.BuildPlayerReceiveHit (sphereClient.EntityID, 2, healthDiff, -1, 0));
        }
        else
        {
            var originId = clientEvent.KillerProcessId != Guid.Empty
                ? clientEvent.KillerProcessId
                : entityId == selfId ? sphereClient.EntityID : entityId;
            sphereClient.MaybeQueueNetworkPacketSend (
                BuildHealthChangePacket (sphereClient.EntityID, originId, healthDiff, originId));
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
    public static byte[] BuildHealthChangePacket (Guid entityId, Guid targetEntityId, int healthDiff,
        Guid? originEntityId = null)
    {
        var stream = SphBitStream.GetWriteBitStream ();
        var origin = originEntityId ?? targetEntityId;

        if (targetEntityId != entityId && ActiveWorldObjects.Get (targetEntityId) is Monster)
        {
            stream.WriteGuid (targetEntityId);
            stream.WriteUInt16 ((ushort) ObjectType.Monster, 10);
            stream.WriteByte (0, 1);
            stream.WriteByte ((byte) EntityActionType.ATTACK, 8);
            stream.WriteUInt16 (0x0003, 16);
            stream.WriteByte (0, 3);
            stream.WriteBytes ([0x00, 0x80, 0xA0, 0x03, 0x00, 0x00, 0xE0]);
            stream.WriteByte (0b0111, 4);
        }

        stream.WriteGuid (entityId);
        stream.WriteUInt16 ((ushort) ObjectType.Player, 12);
        stream.WriteByte (9, 7);
        stream.WriteGuid (origin);
        CommonPackets.WriteMbcVarint (stream, healthDiff);
        CommonPackets.WriteMbcVarint (stream, 0);
        stream.WriteByte (0, 3);

        return Packet.ToByteArray (stream.GetStreamData (), 3);
    }
}
