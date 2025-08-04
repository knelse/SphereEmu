using System.Threading.Tasks;
using Godot;
using SphServer.Client.Networking.Handlers;
using SphServer.Client.Networking.Handlers.BeforeGame;
using SphServer.Client.Networking.Handlers.InGame;
using SphServer.Server.Config;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;

namespace SphServer.Client.Networking;

public class ClientConnection (StreamPeerTcp streamPeerTcp, ushort localId, SphereClient sphereClient)
{
    private readonly PingHandler pingHandler = new (streamPeerTcp, localId);
    private ISphereClientNetworkingHandler? currentHandler;
    public readonly byte[] ReceiveBuffer = new byte [ServerConfig.AppConfig.ReceiveBufferSize];

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
        sphereClient.ClientStateManager.Transition();
        currentHandler =
            BeforeGameHandlers.GetHandlerForState(sphereClient.ClientStateManager.CurrentState, streamPeerTcp, localId,
                this);
        var handlerNameStr = currentHandler?.ToString() ?? "{none}";
        SphLogger.Info(
            $"New state: {sphereClient.ClientStateManager.CurrentState}. New handler: {handlerNameStr}. Client ID: {localId}");
    }

    public void Close ()
    {
        sphereClient.RemoveClient();
    }

    public int GetIncomingData ()
    {
        var temp = streamPeerTcp.GetPartialData(ServerConfig.AppConfig.ReceiveBufferSize);
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

    public void DeletePlayerCharacter (int index)
    {
        sphereClient.DeletePlayerCharacter(index);
    }

    public void CreatePlayerCharacter (CharacterDbEntry newCharacter, int index)
    {
        sphereClient.CreatePlayerCharacter(newCharacter, index);
    }

    public void SetSelectedCharacterIndex (int index)
    {
        sphereClient.SetSelectedCharacterIndex(index);
    }

    public CharacterDbEntry? GetSelectedCharacter ()
    {
        return sphereClient.GetSelecterCharacter();
    }
}