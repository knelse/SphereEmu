# File Structure Documentation

This document provides a comprehensive overview of all files in the SphereEmu project, including their purpose and method signatures.

## Root Directory

### Configuration Files
- **`appsettings.json`**: Server configuration file containing database connections, network settings, spawn coordinates, and debug options
- **`sphdbsettings.json`**: Database-specific settings and connection parameters
- **`project.godot`**: Godot engine project configuration file
- **`SphServer.sln`**: Visual Studio solution file
- **`SphServer.csproj`**: C# project file with dependencies and build configuration

### Data Files
- **`MonsterSpawnData.txt`**: Monster spawn definitions and locations
- **`AlchemyResourceSpawnData.txt`**: Alchemy resource spawn points and data
- **`sphereclient_patched.exe`**: Client executable for testing

## Server Directory (`/Server`)

### Core Server Files

#### `Server/SphereServer.cs`
**Purpose**: Main server class that manages TCP connections and initializes all server components.

**Methods**:
- `override void _Ready()`: Initializes server, database, and connection handlers
- `override void _Process(double delta)`: Main server loop that handles incoming connections
- `static void InitializeCollections()`: Initializes database collections
- `static void SetupTcpServer()`: Configures and starts the TCP server on specified port

### Configuration (`/Server/Config`)

#### `Server/Config/ServerConfig.cs`
**Purpose**: Manages application configuration loading from JSON files with automatic defaults.

**Classes**:
- `AppConfig`: Configuration data model with all server settings
- `ServerConfig`: Static configuration manager

**Methods**:
- `static AppConfig Get()`: Loads configuration from appsettings.json with fallback defaults
- `static void CreateDefaultAppConfig(string configPath)`: Creates default configuration file
- `static Dictionary<string, string> GetDefaultAppConfigDict()`: Returns default configuration values
- `static void SaveAppConfig(string configPath, Dictionary<string, string> config)`: Saves configuration to file

### Connection Handling (`/Server/Handlers`)

#### `Server/Handlers/ConnectionHandler.cs`
**Purpose**: Handles new client connections and creates client instances.

**Methods**:
- `void Handle(StreamPeerTcp streamPeer)`: Processes new client connection, creates SphereClient instance, and adds to active collections

### Authentication (`/Server/Login/Auth`)

#### `Server/Login/Auth/LoginManager.cs`
**Purpose**: Manages player authentication, account creation, and password validation.

**Methods**:
- `static PlayerDbEntry? CheckLoginAndGetPlayer(string login, string password, ushort playerIndex, bool createOnNewLogin = true)`: Validates login credentials and returns player data
- `static bool IsNameValid(string name)`: Checks if character name is available
- `static string GetHashedString(string str)`: Creates secure password hash using PBKDF2
- `static bool EqualsHashed(string password, string hashedPassword)`: Verifies password against stored hash

## Client Directory (`/Client`)

### Core Client Files

#### `Client/SphereClient.cs`
**Purpose**: Represents a connected client and manages their state throughout the connection lifecycle.

**Methods**:
- `void Setup(StreamPeerTcp streamPeer, ushort localId)`: Initializes client with network connection and ID
- `override void _Ready()`: Client initialization when added to scene tree
- `override void _Process(double delta)`: Client update loop for packet processing
- Various state management and packet handling methods

### State Management (`/Client/State`)

#### `Client/State/ClientState.cs`
**Purpose**: Defines all possible client connection states.

**Enum Values**:
- `I_AM_BREAD`: Initial connection state
- `INIT_READY_FOR_INITIAL_DATA`: Ready for server data
- `INIT_WAITING_FOR_LOGIN_DATA`: Awaiting login credentials
- `INIT_WAITING_FOR_CHARACTER_SELECT`: Character selection phase
- `INIT_WAITING_FOR_CLIENT_INGAME_ACK`: Waiting for game entry confirmation
- `INIT_NEW_DUNGEON_TELEPORT_DELAY`: Teleport preparation
- `INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT`: Teleport initialization
- `INIT_NEW_DUNGEON_TELEPORT_INITIATED`: Teleport execution
- `INGAME_DEFAULT`: Fully connected and in-game

#### `Client/State/ClientStateManager.cs`
**Purpose**: Manages client state transitions in sequential order.

**Methods**:
- `ClientState CurrentState { get; }`: Gets current client state
- `void Transition()`: Advances to next state in sequence
- `void TransitionTo[StateName]()`: Individual state transition methods (private)

### Networking (`/Client/Networking`)

#### `Client/Networking/ClientConnection.cs`
**Purpose**: Manages client network connection, packet sending/receiving, and connection lifecycle.

**Methods**:
- Connection establishment and teardown methods
- Packet sending and receiving methods
- Connection state monitoring

#### `Client/Networking/Handlers/ISphereClientNetworkingHandler.cs`
**Purpose**: Interface for client packet handlers.

**Methods**:
- `void Handle(...)`: Packet handling interface method

## Shared Directory (`/Shared`)

### Database (`/Shared/Db`)

#### Database Models (`/Shared/Db/DataModels`)
- **`CharacterDbEntry.cs`**: Character data model with stats, inventory, appearance
- **`PlayerDbEntry.cs`**: Player account data model
- **`ClanDbEntry.cs`**: Clan/guild data model
- **`ItemDbEntry.cs`**: Item data model
- **`MobDbEntry.cs`**: Monster/NPC data model

#### `Shared/Db/DbConnection.cs`
**Purpose**: Database connection management and collection access.

**Methods**:
- `static void Initialize(AppConfig config)`: Initializes LiteDB connection
- Various collection property accessors for different data types

### Logging (`/Shared/Logger`)

#### `Shared/Logger/SphLogger.cs`
**Purpose**: Centralized logging system with console and file output, automatic log rotation.

**Methods**:
- `static void Initialize(string? filePath, bool enableConsole, bool enableFile, LogLevel minConsoleLevel, LogLevel minFileLevel)`: Configures logging system
- `static void Debug(string message)`: Logs debug message
- `static void Info(string message)`: Logs info message  
- `static void Warning(string message)`: Logs warning message
- `static void Error(string message)`: Logs error message
- `static void Error(string message, Exception exception)`: Logs error with exception
- `static void CleanupOldLogFiles(string? logDirectory)`: Removes old log files, keeping latest 20
- `static string GenerateTimestampedLogPath(string originalPath)`: Creates timestamped log file names

### Game Data (`/Shared/GameData`)

#### Enums (`/Shared/GameData/Enums`)
- **`LootRarity.cs`**: Item rarity levels
- **`MonsterTypes.cs`**: Monster classification types

### Networking (`/Shared/Networking`)

#### Packets (`/Shared/Networking/Packets`)
- **`Packet.cs`**: Base packet class for network communication
- **`PacketPart.cs`**: Packet component/part system

### World State (`/Shared/WorldState`)
**Purpose**: Manages global world state and active entity collections.

**Collections**:
- Active clients management
- Active world objects tracking
- Global state synchronization

### Utilities (`/Shared/BitStream`)
**Purpose**: Binary data serialization and bit-level operations for network packets.

## Godot Directory (`/Godot`)

### Scenes (`/Godot/Scenes`)
- **`Client.tscn`**: Client scene template
- **`MainServer.tscn`**: Server scene configuration
- Various world object and UI scenes

### Scripts
- Godot-specific C# scripts for scene management
- UI controllers and world object behaviors

## System Directory (`/System`)

### Extensions and Utilities
- **Extension methods**: Helper methods for common operations
- **System utilities**: Low-level system integration
- **Helper classes**: Common functionality used across the project

## Build and Configuration Files

- **`.gitignore`**: Git version control exclusions
- **`LICENSE.md`**: Project license information
- **`README.md`**: Project overview and setup instructions
- **`.godot/`**: Godot engine cache and temporary files
- **`logs/`**: Runtime log files (auto-managed, keeps latest 20)

## Key Design Patterns

### Architecture Patterns
- **State Machine**: Client connection states
- **Repository Pattern**: Database access layers
- **Factory Pattern**: Client and object creation
- **Observer Pattern**: Event-driven networking

### Naming Conventions
- **Classes**: PascalCase (e.g., `SphereClient`)
- **Methods**: PascalCase (e.g., `HandleConnection`)
- **Fields/Properties**: camelCase for private, PascalCase for public
- **Constants**: UPPER_CASE for enum values
- **Files**: Match class names with `.cs` extension

### Configuration Management
- JSON-based configuration with automatic defaults
- Environment-specific settings support
- Runtime configuration updates for non-critical settings

This file structure provides clear separation of concerns, with server logic, client management, shared utilities, and game-specific functionality organized into logical directories.