using System;
using System.Threading.Tasks;
using Godot;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.Serializers;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.InGame;

public class PingHandler (StreamPeerTcp streamPeerTcp, ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private ushort counter;
    private string? pingPreviousClientPingString;
    private bool pingShouldXorTopBit;

    public async Task Keepalive (double delta)
    {
        FifteenSecondPing.Tick(delta);
        SixSecondPing.Tick(delta);
        ThreeSecondPing.Tick(delta);
    }

    private readonly SphereTimer FifteenSecondPing = new (15, true,
        () => streamPeerTcp.PutData(CommonPackets.FifteenSecondPing(localId)));

    private readonly SphereTimer SixSecondPing =
        new (6, true, () => streamPeerTcp.PutData(CommonPackets.SixSecondPing(localId)));

    private readonly SphereTimer ThreeSecondPing =
        new (3, true, () => streamPeerTcp.PutData(CommonPackets.TransmissionEndPacket));

    public async Task Handle (double delta)
    {
        var clientPingBytesForComparison = clientConnection.ReceiveBuffer[17..55];

        var clientPingBytesForPong = clientConnection.ReceiveBuffer[9..30];
        var clientPingBinaryStr =
            StringConvertHelpers.ByteArrayToBinaryString(clientPingBytesForComparison, false, true);

        // if (clientPingBinaryStr[0] == '0')
        // {
        //     // random different packet, idk
        //     return;
        // }

        if (string.IsNullOrEmpty(pingPreviousClientPingString))
        {
            pingPreviousClientPingString = clientPingBinaryStr;
        }

        else
        {
            var pingHasChanges = string.Compare(clientPingBinaryStr, pingPreviousClientPingString,
                StringComparison.Ordinal);

            if (pingHasChanges != 0)
            {
                var coords = CoordsHelper.GetCoordsFromPingBytes(clientConnection.ReceiveBuffer);

                var currentCharacter = clientConnection.GetSelectedCharacter();
                if (Math.Abs(coords.x - currentCharacter!.X) < 100000)
                {
                    currentCharacter.X = coords.x;
                    currentCharacter.Y = coords.y;
                    currentCharacter.Z = coords.z;
                    currentCharacter.Angle = coords.turn;
                }

                pingPreviousClientPingString = clientPingBinaryStr;
            }
        }

        var xored = clientPingBytesForPong[5];

        if (pingShouldXorTopBit)
        {
            xored ^= 0b10000000;
        }

        if (counter == 0)
        {
            var first = (ushort) ((clientPingBytesForPong[7] << 8) + clientPingBytesForPong[6]);
            first -= 0xE001;
            counter = (ushort) (0xE001 + first / 12);
        }

        var pong = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, xored, SphereDbEntrySerializerBase.MinorByte(counter),
            SphereDbEntrySerializerBase.MajorByte(counter), 0x00, 0x00, 0x00, 0x00, 0x00
        };

        Array.Copy(clientPingBytesForPong, pong, 5);
        Array.Copy(clientPingBytesForPong, 8, pong, 8, 4);
        streamPeerTcp.PutData(Packet.ToByteArray(pong, 1));
        pingShouldXorTopBit = !pingShouldXorTopBit;
        counter++;

        //overflow
        if (counter < 0xE001)
        {
            counter = 0xE001;
        }
    }
}