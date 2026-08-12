using System;
using Godot;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.InGame;

public class PingHandler(StreamPeerTcp streamPeerTcp, ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public StreamPeerTcp _ { get; } = streamPeerTcp;
    private const double MovementBroadcastDelta = 0.1;
    private const int PingFrameLength = 0x26;
    private const int CoordPayloadOffset = 21;
    private const int CoordPayloadLength = 17;
    private const int PongEchoOffset = 9;
    private const int PongEchoLength = 21;

    private readonly SphereTimer fifteenSecondPing = new(15, true,
        () => clientConnection.SendPacket(CommonPackets.FifteenSecondPing(localId)));

    private readonly SphereTimer sixSecondPing =
        new(6, true, () => clientConnection.SendPacket(CommonPackets.SixSecondPing(localId)));

    private readonly SphereTimer threeSecondPing =
        new(3, true, () => clientConnection.SendPacket(CommonPackets.TransmissionEndPacket));

    private ushort counter;
    private byte[]? previousCoordPayload;
    private bool pingShouldXorTopBit;

    public async Task Handle(byte[] frame, double delta)
    {
        var buffer = frame;
        if (buffer.Length < PingFrameLength || buffer[0] != PingFrameLength)
        {
            return;
        }

        var coordPayload = buffer.AsSpan(CoordPayloadOffset, CoordPayloadLength);
        var coordsChanged = previousCoordPayload is null
                            || !coordPayload.SequenceEqual(previousCoordPayload);

        if (coordsChanged)
        {
            if (CoordsHelper.HasPingCoordMarker(buffer))
            {
                var coords = CoordsHelper.GetCoordsFromPingBytes(buffer);
                var currentCharacter = clientConnection.GetSelectedCharacter();
                if (currentCharacter is not null && CoordsHelper.ArePingCoordsInWorldBounds(coords))
                {
                    var moved = MovementDeltaExceedsThreshold(coords, currentCharacter);
                    currentCharacter.X = coords.x;
                    currentCharacter.Y = -coords.y;
                    currentCharacter.Z = -coords.z;
                    currentCharacter.Angle = coords.turn;
                    ClientStateEvents.RaiseCharacterChanged(localId);

                    if (moved)
                    {
                        clientConnection.EnqueueClientEvent(new CurrentClientPositionChangedEvent());
                    }
                }
            }

            previousCoordPayload = [.. coordPayload];
        }

        var pongEcho = buffer.AsSpan(PongEchoOffset, PongEchoLength);
        var xored = pongEcho[5];
        if (pingShouldXorTopBit)
        {
            xored ^= 0b10000000;
        }

        if (counter == 0)
        {
            var first = (ushort)((pongEcho[7] << 8) + pongEcho[6]);
            first -= 0xE001;
            counter = (ushort)(0xE001 + first / 12);
        }

        var pong = new byte[13];
        pongEcho[..5].CopyTo(pong);
        pong[5] = xored;
        pong[6] = SphereDbEntrySerializerBase.MinorByte(counter);
        pong[7] = SphereDbEntrySerializerBase.MajorByte(counter);
        pongEcho.Slice(8, 4).CopyTo(pong.AsSpan(8));

        clientConnection.SendPacket(Packet.ToByteArray(pong, 1));
        pingShouldXorTopBit = !pingShouldXorTopBit;
        counter++;

        // overflow
        if (counter < 0xE001)
        {
            counter = 0xE001;
        }
    }

    public async Task Keepalive(double delta)
    {
        fifteenSecondPing.Tick(delta);
        sixSecondPing.Tick(delta);
        threeSecondPing.Tick(delta);
    }

    private static bool MovementDeltaExceedsThreshold(WorldCoords coords, CharacterDbEntry character)
    {
        // Y and Z coords are negated for Godot
        return Math.Abs(coords.x - character.X) > MovementBroadcastDelta
               || Math.Abs(coords.y + character.Y) > MovementBroadcastDelta
               || Math.Abs(coords.z + character.Z) > MovementBroadcastDelta
               || Math.Abs(coords.turn - character.Angle) > MovementBroadcastDelta;
    }
}
