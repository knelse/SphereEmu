using System;
using System.Threading.Tasks;
using Godot;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class ServerCredentialsHandler (Guid entityId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private SphereTimer? WaitForClientTimer;

    public async Task Handle (byte[] frame, double delta)
    {
        if (clientConnection.IsSecureResume)
        {
            if (WaitForClientTimer is not null)
            {
                WaitForClientTimer.Tick (delta);
                return;
            }

            WaitForClientTimer = new (0.1, false, () =>
            {
                SphLogger.Info ($"SRV {entityId}: Secure resume, character select");
                clientConnection.SendResumeCharacterList ();
                clientConnection.EnterCharacterSelect ();
            });
            return;
        }

        if (WaitForClientTimer is not null)
        {
            WaitForClientTimer.Tick (delta);
        }

        if (frame.Length == 0)
        {
            return;
        }

        WaitForClientTimer = new (0.1, false, () =>
        {
            SphLogger.Info ($"CLI {entityId}: Connection initialized");
            clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ServerCredentials (entityId));
            Console.WriteLine ($"SRV {entityId}: Credentials sent");
            clientConnection.MoveToNextBeforeGameStage ();
        });

        Console.WriteLine (delta);
    }
}
