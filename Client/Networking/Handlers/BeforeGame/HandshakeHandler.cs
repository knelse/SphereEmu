using System.Threading.Tasks;
using Godot;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class HandshakeHandler (Guid entityId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    // TODO make reconnect work
    private readonly bool reconnect = false;

    public async Task Handle (byte[] frame, double delta)
    {
        if (clientConnection.IsSecureResume)
        {
            SphLogger.Info ($"SRV {entityId}: UDP session skips the handshake");
            clientConnection.MoveToNextBeforeGameStage ();
            return;
        }

        SphLogger.Info ($"CLI {entityId}: Ready to load initial data");

        clientConnection.MaybeScheduleNetworkPacketSend (reconnect
            ? CommonPackets.ReadyToLoadInitialDataReconnect
            : CommonPackets.ReadyToLoadInitialData);

        clientConnection.MoveToNextBeforeGameStage ();
    }
}
