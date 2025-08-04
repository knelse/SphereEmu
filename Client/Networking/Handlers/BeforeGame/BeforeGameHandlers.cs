using System;
using Godot;
using SphServer.Client.State;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public static class BeforeGameHandlers
{
    public static ISphereClientNetworkingHandler GetNextHandler (ClientState currentState, StreamPeerTcp streamPeerTcp,
        ushort localId, ClientConnection clientConnection)
    {
        switch (currentState)
        {
            case ClientState.I_AM_BREAD:
                return new HandshakeHandler(streamPeerTcp, localId, clientConnection);
            case ClientState.INIT_READY_FOR_INITIAL_DATA:
                return new ServerCredentialsHandler(streamPeerTcp, localId, clientConnection);
            case ClientState.INIT_WAITING_FOR_LOGIN_DATA:
                return new LoginDataHandler(streamPeerTcp, localId, clientConnection);
            case ClientState.INIT_WAITING_FOR_CHARACTER_SELECT:
                currentState = ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK;
                break;
            case ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK:
                break;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY:
                currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT;
                break;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT:
                currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_INITIATED;
                break;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_INITIATED:
                currentState = ClientState.INGAME_DEFAULT;
                break;
            case ClientState.INGAME_DEFAULT:
                // Final state, no transition
                break;
        }

        throw new NotImplementedException();
    }
}