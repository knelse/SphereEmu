using System;
using Godot;
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
    public StreamPeerTcp _ { get; }
    private const double MovementBroadcastDelta = 0.1;
    // msg300 keepalive with _player PositionStream (region 11): array4 count + 4 IEEE floats.
    private const int PingFrameLength = 0x26;
    private const int CoordPayloadOffset = 21;
    private const int CoordPayloadLength = 17;
    private const int PongEchoOffset = 9;
    private const int PongEchoLength = 21;

    private readonly ushort localId;
    private readonly ClientConnection clientConnection;
    private readonly CharacterVitalRegen vitalRegen = new ();
    private readonly SphereTimer fifteenSecondPing;
    // One 6s tick: Recalc once, MP keepalive, HP SetStat if needed.
    private readonly SphereTimer vitalRegenTick;
    // CheckPing resets on region 11. WaitReinc stops client 0x26s, so this pong keeps going
    private readonly SphereTimer deadPositionStreamTimer;

    private ushort counter;
    private byte[]? previousCoordPayload;
    private byte[]? lastPongEcho;
    private bool pingShouldXorTopBit;
    private bool wasDead;
    private double deadSeconds;

    public PingHandler (StreamPeerTcp streamPeerTcp, ushort localId, ClientConnection clientConnection)
    {
        _ = streamPeerTcp;
        this.localId = localId;
        this.clientConnection = clientConnection;
        fifteenSecondPing = new (15, true,
            () => clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.FifteenSecondPing (localId)));
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
                    ClientStateEvents.RaiseCharacterChanged (localId);

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
            CommonPackets.BuildPlayerEmptyPositionStream (SphBitStream.ByteSwap (localId), character.X,
                -character.Y, -character.Z));
    }

    private void SendPositionStreamPong (byte[] pongEcho)
    {
        var xored = pongEcho[5];
        if (pingShouldXorTopBit)
        {
            xored ^= 0b10000000;
        }

        if (counter == 0)
        {
            var first = (ushort) ((pongEcho[7] << 8) + pongEcho[6]);
            first -= 0xE001;
            counter = (ushort) (0xE001 + first / 12);
        }

        var pong = new byte[13];
        pongEcho.AsSpan (0, 5).CopyTo (pong);
        pong[5] = xored;
        pong[6] = SphereDbEntrySerializerBase.MinorByte (counter);
        pong[7] = SphereDbEntrySerializerBase.MajorByte (counter);
        pongEcho.AsSpan (8, 4).CopyTo (pong.AsSpan (8));

        clientConnection.MaybeScheduleNetworkPacketSend (Packet.ToByteArray (pong, 1));
        pingShouldXorTopBit = !pingShouldXorTopBit;
        counter++;

        // overflow
        if (counter < 0xE001)
        {
            counter = 0xE001;
        }
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
            CommonPackets.CurrentMpUpdatePing (localId, character.CurrentMP));
        NetworkedStatsUpdater.MarkSent (character, Stat.MpCurrent);

        if (character.CurrentHP != hpBefore)
        {
            NetworkedStatsUpdater.Update (character, log: false);
        }

        if (changed)
        {
            ClientStateEvents.RaiseCharacterChanged (localId);
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
