using System;
using System.Threading.Tasks;
using Godot;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class ServerCredentialsHandler(ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private SphereTimer? WaitForClientTimer;

    public async Task Handle(byte[] frame, double delta)
    {
        if (WaitForClientTimer is not null)
        {
            WaitForClientTimer.Tick(delta);
        }

        if (frame.Length == 0)
        {
            return;
        }

        WaitForClientTimer = new(0.1, false, () =>
        {
            SphLogger.Info($"CLI {localId:X4}: Connection initialized");
            clientConnection.SendPacket(CommonPackets.ServerCredentials(localId));
            Console.WriteLine($"SRV {localId:X4}: Credentials sent");
            clientConnection.MoveToNextBeforeGameStage();
        });

        Console.WriteLine(delta);
    }
}