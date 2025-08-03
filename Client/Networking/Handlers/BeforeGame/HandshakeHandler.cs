using System.Threading.Tasks;
using Godot;
using SphServer.Providers;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class HandshakeHandler (StreamPeerTcp streamPeerTcp, ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    // TODO make reconnect work
    private readonly bool reconnect = false;

    public async Task Handle (double delta)
    {
        SphLogger.Info($"CLI {localId:X4}: Ready to load initial data");
        streamPeerTcp.PutData(reconnect
            ? CommonPackets.ReadyToLoadInitialDataReconnect
            : CommonPackets.ReadyToLoadInitialData);

        clientConnection.MoveToNextBeforeGameStage();
    }
}