using System;
using System.Threading.Tasks;
using Godot;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class ServerCredentialsHandler (StreamPeerTcp streamPeerTcp, ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private readonly SphereTimer WaitForClientTimer = new (0.01, false, () =>
    {
        SphLogger.Info($"CLI {localId:X4}: Connection initialized");
        streamPeerTcp.PutData(CommonPackets.ServerCredentials(localId));
        Console.WriteLine($"SRV {localId:X4}: Credentials sent");
        clientConnection.MoveToNextBeforeGameStage();
    });

    public async Task Handle (double delta)
    {
        if (clientConnection.GetIncomingData() == 0)
        {
            return;
        }

        WaitForClientTimer.Tick(delta);
        Console.WriteLine(delta);
    }
}