using System;
using Godot;
using SphServer.Client.State;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public static class BeforeGameHandlers
{
    public static ISphereClientNetworkingHandler? GetHandlerForState (ClientState currentState, StreamPeerTcp streamPeerTcp,
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
                return new CharacterSelectHandler(streamPeerTcp, localId, clientConnection);
            case ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK:
                return new IngameAckHandler(streamPeerTcp, localId, clientConnection);
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY:
                break;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT:
                break;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_INITIATED:
                break;
            case ClientState.INGAME_DEFAULT:
                return null;
                break;
        }

        throw new NotImplementedException();
    }
}