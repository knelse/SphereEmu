using System.Threading.Tasks;
using Godot;
using SphServer.Client.Networking.Handlers;
using SphServer.Client.Networking.Handlers.BeforeGame;
using SphServer.DataModels;
using SphServer.Providers;

namespace SphServer.Client.Networking;

public class ClientConnection (StreamPeerTcp streamPeerTcp, ushort localId, SphereClient sphereClient)
{
    private readonly PingHandler pingHandler = new (streamPeerTcp, localId);
    private ISphereClientNetworkingHandler? currentHandler;
    public readonly byte[] ReceiveBuffer = new byte [Constants.RECEIVE_BUFFER_SIZE];

    public async Task Process (double delta)
    {
        currentHandler ??= new HandshakeHandler(streamPeerTcp, localId, this);

        if (sphereClient.ClientStateManager.IsInGameState())
        {
            // TODO: ping handler should be the "default" ingame handler
            await pingHandler.Keepalive(delta);
            await pingHandler.Handle(delta);
        }

        else
        {
            await currentHandler.Handle(delta);
        }
    }

    public void MoveToNextBeforeGameStage ()
    {
        SphLogger.Info(
            $"Client moved from state: {sphereClient.ClientStateManager.CurrentState}. Client ID: {localId}");
        currentHandler =
            BeforeGameHandlers.GetNextHandler(sphereClient.ClientStateManager.CurrentState, streamPeerTcp, localId,
                this);
        sphereClient.ClientStateManager.Transition();
    }

    public void Close ()
    {
        sphereClient.RemoveClient();
    }

    public int GetIncomingData ()
    {
        var temp = streamPeerTcp.GetPartialData(Constants.RECEIVE_BUFFER_SIZE);
        var arr = (byte[]?) temp[1];

        var i = 0;

        if (arr is not null)
        {
            for (; i < arr.Length; i++)
            {
                ReceiveBuffer[i] = arr[i];
            }
        }

        return i;
    }

    public void SetPlayerDbEntry (PlayerDbEntry? entry)
    {
        sphereClient.SetPlayerDbEntry(entry);
    }
}