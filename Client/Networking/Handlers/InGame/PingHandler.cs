using System.Threading.Tasks;
using Godot;
using SphServer.Shared.Networking;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers;

public class PingHandler (StreamPeerTcp streamPeerTcp, ushort localId)
    : ISphereClientNetworkingHandler
{
    private ushort counter;
    private string? pingPreviousClientPingString;
    private bool pingShouldXorTopBit;

    private async Task Keepalive (double delta)
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
        await Keepalive(delta);
    }
}