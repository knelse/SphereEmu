namespace SphServer.Client.State;

public class ClientStateManager (bool isNewPlayer)
{
    private ClientState currentState = ClientState.I_AM_BREAD;
    public ClientState CurrentState => currentState;

    public bool IsInGameState ()
    {
        return currentState >= ClientState.INGAME_DEFAULT;
    }

    public void Transition ()
    {
        switch (currentState)
        {
            case ClientState.I_AM_BREAD:
                currentState = ClientState.INIT_READY_FOR_INITIAL_DATA;
                break;
            case ClientState.INIT_READY_FOR_INITIAL_DATA:
                currentState = ClientState.INIT_WAITING_FOR_LOGIN_DATA;
                break;
            case ClientState.INIT_WAITING_FOR_LOGIN_DATA:
                currentState = ClientState.INIT_WAITING_FOR_CHARACTER_SELECT;
                break;
            case ClientState.INIT_WAITING_FOR_CHARACTER_SELECT:
                currentState = ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK;
                break;
            case ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK:
                currentState = isNewPlayer
                    ? ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY
                    : ClientState.INGAME_DEFAULT;
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
    }
}