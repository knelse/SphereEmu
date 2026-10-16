using System;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.InGame;

public class PingHandler : ISphereClientNetworkingHandler
{
    private const double MovementBroadcastDelta = 0.1;
    // msg300 keepalive with _player PositionStream (region 11): array4 count + 4 IEEE floats.
    private const int PingFrameLength = 0x34;
    private const int CoordPayloadOffset = 35;
    private const int CoordPayloadLength = 17;
    private const int PongEchoOffset = 9;
    private const int PongEchoLength = 35;

    private readonly Guid entityId;
    private readonly ClientConnection clientConnection;
    private readonly CharacterVitalRegen vitalRegen = new ();
    private readonly SphereTimer fifteenSecondPing;
    // One 6s tick: Recalc once, MP keepalive, HP SetStat if needed.
    private readonly SphereTimer vitalRegenTick;
    // CheckPing resets on region 11. WaitReinc stops client 0x26s, so this pong keeps going
    private readonly SphereTimer deadPositionStreamTimer;

    private byte[]? previousCoordPayload;
    private byte[]? lastPongEcho;
    private bool wasDead;
    private double deadSeconds;

    public PingHandler (Guid entityId, ClientConnection clientConnection)
    {
        this.entityId = entityId;
        this.clientConnection = clientConnection;
        fifteenSecondPing = new (15, true,
            () => clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.FifteenSecondPing (entityId)));
        vitalRegenTick = new (6, true, SyncVitalsAfterRegen);
        deadPositionStreamTimer = new (2, true, SendDeadPositionStream);
    }

    public async Task Handle (byte[] frame, double delta)
    {
        var buffer = frame;
        if (buffer.Length < PingFrameLength || buffer[0] != PingFrameLength)
        {
            return;
        }

        var coordPayload = buffer.AsSpan (CoordPayloadOffset, CoordPayloadLength);
        var coordsChanged = previousCoordPayload is null
                            || !coordPayload.SequenceEqual (previousCoordPayload);

        if (coordsChanged)
        {
            if (CoordsHelper.HasPingCoordMarker (buffer))
            {
                var coords = CoordsHelper.GetCoordsFromPingBytes (buffer);
                var currentCharacter = clientConnection.GetSelectedCharacter ();
                if (currentCharacter is not null && CoordsHelper.ArePingCoordsInWorldBounds (coords))
                {
                    var moved = MovementDeltaExceedsThreshold (coords, currentCharacter);
                    currentCharacter.X = coords.x;
                    currentCharacter.Y = -coords.y;
                    currentCharacter.Z = -coords.z;
                    currentCharacter.Angle = coords.turn;
                    ClientStateEvents.RaiseCharacterChanged (entityId);

                    if (moved)
                    {
                        clientConnection.EnqueueClientEvent (new CurrentClientPositionChangedEvent ());
                    }
                }
            }

            previousCoordPayload = [.. coordPayload];
        }

        var pongEcho = buffer.AsSpan (PongEchoOffset, PongEchoLength);
        lastPongEcho = pongEcho.ToArray ();
        SendPositionStreamPong (lastPongEcho);
    }

    public async Task Keepalive (double delta)
    {
        fifteenSecondPing.Tick (delta);
        TickDeadPositionStream (delta);

        // Regen waits until the client is sending in-world positions
        if (!clientConnection.HasSeenFirstPositionKeepalive)
        {
            return;
        }

        vitalRegenTick.Tick (delta);
    }

    private void TickDeadPositionStream (double delta)
    {
        var character = clientConnection.GetSelectedCharacter ();
        var dead = character is not null && character.CurrentHP == 0;
        if (dead && !wasDead)
        {
            SendDeadPositionStream ();
            deadPositionStreamTimer.Rearm (2);
            deadSeconds = 0;
        }

        wasDead = dead;
        if (!dead)
        {
            return;
        }

        deadPositionStreamTimer.Tick (delta);
        deadSeconds += delta;
        // CycleSend yields while current HP is 0
        if (deadSeconds >= 5)
        {
            deadSeconds = 0;
            clientConnection.ReviveAfterDeath ();
        }
    }

    private void SendDeadPositionStream ()
    {
        var character = clientConnection.GetSelectedCharacter ();
        if (character is null || character.CurrentHP > 0)
        {
            return;
        }

        if (lastPongEcho is not null)
        {
            SendPositionStreamPong (lastPongEcho);
            return;
        }

        clientConnection.MaybeScheduleNetworkPacketSend (
            CommonPackets.BuildPlayerEmptyPositionStream (entityId, character.X,
                -character.Y, -character.Z));
    }

    private void SendPositionStreamPong (byte[] pongEcho)
    {
        var currentCharacter = clientConnection.GetSelectedCharacter ();
        clientConnection.MaybeScheduleNetworkPacketSend (
            CommonPackets.BuildPlayerEmptyPositionStream (entityId,
                currentCharacter?.X ?? 0,
                -(currentCharacter?.Y ?? 0),
                -(currentCharacter?.Z ?? 0)));
    }

    private void SyncVitalsAfterRegen ()
    {
        var character = clientConnection.GetSelectedCharacter ();
        if (character is null)
        {
            return;
        }

        var hpBefore = character.CurrentHP;
        var changed = vitalRegen.ApplyOnce (character);

        // MP keepalive carries the (possibly regenerated) MP value.
        clientConnection.MaybeScheduleNetworkPacketSend (
            CommonPackets.CurrentMpUpdatePing (entityId, character.CurrentMP));
        NetworkedStatsUpdater.MarkSent (character, Stat.MpCurrent);

        if (character.CurrentHP != hpBefore)
        {
            NetworkedStatsUpdater.Update (character, log: false);
        }

        if (changed)
        {
            ClientStateEvents.RaiseCharacterChanged (entityId);
        }

        // The 6s tick writes vitals even when they did not change
        character.PersistVitals ();
    }

    private static bool MovementDeltaExceedsThreshold (WorldCoords coords, CharacterDbEntry character)
    {
        // Y and Z coords are negated for Godot
        return Math.Abs (coords.x - character.X) > MovementBroadcastDelta
               || Math.Abs (coords.y + character.Y) > MovementBroadcastDelta
               || Math.Abs (coords.z + character.Z) > MovementBroadcastDelta
               || Math.Abs (coords.turn - character.Angle) > MovementBroadcastDelta;
    }
}
