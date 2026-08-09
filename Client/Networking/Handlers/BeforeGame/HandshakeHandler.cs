using System.Threading.Tasks;
using Godot;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class HandshakeHandler(ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    // TODO make reconnect work
    private readonly bool reconnect = false;

    public async Task Handle(byte[] frame, double delta)
    {
        SphLogger.Info($"CLI {localId:X4}: Ready to load initial data");

        clientConnection.SendPacket(reconnect
            ? CommonPackets.ReadyToLoadInitialDataReconnect
            : CommonPackets.ReadyToLoadInitialData);

        clientConnection.MoveToNextBeforeGameStage();
    }
}