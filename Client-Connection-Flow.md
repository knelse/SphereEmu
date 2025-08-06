# Client Connection Flow

This document describes the step-by-step client connection flow based on client states and their handlers.

## Overview

When a client connects to the SphereEmu server, it progresses through a series of states. Each state represents a specific phase in the connection lifecycle.

## Connection States

The client states are defined in `Client/State/ClientState.cs`:

```csharp
public enum ClientState
{
    I_AM_BREAD,                                    // Initial connection state
    INIT_READY_FOR_INITIAL_DATA,                  // Ready to receive initial server data
    INIT_WAITING_FOR_LOGIN_DATA,                  // Waiting for login credentials
    INIT_WAITING_FOR_CHARACTER_SELECT,            // Waiting for character selection
    INIT_WAITING_FOR_CLIENT_INGAME_ACK,          // Waiting for client acknowledgment
    INIT_NEW_DUNGEON_TELEPORT_DELAY,             // Teleport delay state
    INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT,     // Ready to initialize teleport
    INIT_NEW_DUNGEON_TELEPORT_INITIATED,         // Teleport initiated
    INGAME_DEFAULT                                // Fully in-game state
}
```

## Step-by-Step Connection Flow

### 1. TCP Connection Establishment
- **Location**: `Server/SphereServer.cs` → `_Process()` method
- **Handler**: `Server/Handlers/ConnectionHandler.cs`
- **Process**:
  1. Server listens on configured port via `TcpServer`
  2. When connection is available, `tcpServer.TakeConnection()` is called
  3. `ConnectionHandler.Handle(streamPeer)` is invoked
  4. New `SphereClient` instance is created and configured
  5. Client is added to active client collections
  6. Client state is initialized to `I_AM_BREAD`

### 2. State: I_AM_BREAD
- **Purpose**: Initial connection state
- **Handler**: Client networking handlers (location varies)
- **Transitions to**: `INIT_READY_FOR_INITIAL_DATA`
- **Process**:
  - Client connection is established
  - Basic client setup is performed
  - Server prepares to send initial data

### 3. State: INIT_READY_FOR_INITIAL_DATA
- **Purpose**: Client is ready to receive server initialization data
- **Handler**: BeforeGame handlers
- **Transitions to**: `INIT_WAITING_FOR_LOGIN_DATA`
- **Process**:
  - Server sends initial configuration and game data
  - Client receives server capabilities and settings
  - Handshake process begins

### 4. State: INIT_WAITING_FOR_LOGIN_DATA
- **Purpose**: Waiting for client to provide login credentials
- **Handler**: `Server/Login/Auth/LoginManager.cs`
- **Transitions to**: `INIT_WAITING_FOR_CHARACTER_SELECT`
- **Process**:
  - Client sends login credentials (username/password)
  - Server validates credentials via `LoginManager.CheckLoginAndGetPlayer()`
  - If valid, player data is loaded from database
  - If new player, account is created with default settings

### 5. State: INIT_WAITING_FOR_CHARACTER_SELECT
- **Purpose**: Waiting for character selection or creation
- **Handler**: Character selection handlers
- **Transitions to**: `INIT_WAITING_FOR_CLIENT_INGAME_ACK`
- **Process**:
  - Server sends available characters for the account
  - Client displays character selection screen
  - Client can create new character, select existing, or delete characters
  - Character data is validated and processed

### 6. State: INIT_WAITING_FOR_CLIENT_INGAME_ACK
- **Purpose**: Waiting for client acknowledgment to enter game
- **Handler**: Ingame initialization handlers
- **Transitions to**: `INIT_NEW_DUNGEON_TELEPORT_DELAY`
- **Process**:
  - Server prepares character for world entry
  - Character stats, inventory, and position are loaded
  - Client acknowledges readiness to enter game world

### 7. State: INIT_NEW_DUNGEON_TELEPORT_DELAY
- **Purpose**: Delay before teleport initialization
- **Handler**: Teleport handlers
- **Transitions to**: `INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT`
- **Process**:
  - Brief delay to ensure client is ready
  - Server prepares teleport data
  - World state is synchronized

### 8. State: INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT
- **Purpose**: Ready to initialize character teleport
- **Handler**: Teleport handlers
- **Transitions to**: `INIT_NEW_DUNGEON_TELEPORT_INITIATED`
- **Process**:
  - Server calculates spawn position (using config values)
  - Teleport packet is prepared with coordinates
  - Client is notified of impending teleport

### 9. State: INIT_NEW_DUNGEON_TELEPORT_INITIATED
- **Purpose**: Teleport process has been initiated
- **Handler**: Teleport handlers
- **Transitions to**: `INGAME_DEFAULT`
- **Process**:
  - Character is placed in game world at spawn coordinates
  - Initial game state data is sent to client
  - Character becomes visible to other players

### 10. State: INGAME_DEFAULT
- **Purpose**: Fully connected and in-game
- **Handler**: InGame handlers (movement, chat, combat, etc.)
- **Transitions to**: None (terminal state)
- **Process**:
  - Character is fully functional in game world
  - All game mechanics are available
  - Continuous packet processing for gameplay

## State Management

### State Transitions
- State transitions are managed by `Client/State/ClientStateManager.cs`
- Each state transition is handled by specific methods
- Transitions are sequential and follow the defined order
- Invalid state transitions are prevented

### Handler Architecture
- **BeforeGame Handlers**: Handle pre-game states (login, character select)
- **InGame Handlers**: Handle gameplay states (movement, combat, interactions)
- **Connection Handlers**: Handle connection establishment and cleanup

### Error Handling
- Connection drops can occur at any state
- Each state has timeout mechanisms
- Invalid packets for current state are rejected
- Graceful degradation and cleanup on errors

## Configuration Impact

Several configuration values affect the connection flow:

- **Spawn Coordinates**: `Spawn_X`, `Spawn_Y`, `Spawn_Z`, `Spawn_Angle` from `appsettings.json`
- **Default Money**: `Spawn_Money` for new characters
- **Network Settings**: `ReceiveBufferSize` for packet handling
- **Debug Settings**: `DebugMode` affects logging verbosity

## Logging and Debugging

- Each state transition is logged for debugging
- Client IDs are tracked throughout the flow
- Separate log levels for console (Info+) and file (Debug+)
- Connection failures and timeouts are logged as errors

This flow ensures a robust and predictable client connection process, with clear separation of concerns and comprehensive error handling at each stage.