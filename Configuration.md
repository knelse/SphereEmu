# Configuration Guide

This document explains all configuration options available in SphereEmu.

## Configuration Files

### `appsettings.json`
Main server configuration file with all runtime settings.

```json
{
  "PacketPartPath": "D:\\SphereDev\\SphereSource\\source\\sphPacketDefinitions",
  "PacketDefinitionPath": "D:\\SphereDev\\SphereSource\\source\\sphPacketDefinitions",
  "LiteDbConnectionString": "Filename=d:\\SphereDev\\_sphereStuff\\sph.db;Connection=shared;",
  "Port": "25860",
  "LogPath": "logs\\server.log",
  "DebugMode": "true",
  "ObjectVisibilityDistance": "100.0",
  "ReceiveBufferSize": "1024",
  "CurrentCharacterInventoryId": "40961",
  "Spawn_X": "80.0",
  "Spawn_Y": "150.0",
  "Spawn_Z": "200.0",
  "Spawn_Angle": "0.75",
  "Spawn_Money": "99999999"
}
```

## Configuration Options

### Network Settings

#### `Port` (ushort, default: 25860)
- **Purpose**: TCP port the server listens on for client connections
- **Example**: `"Port": "25860"`
- **Notes**: Must be available and not blocked by firewall

#### `ReceiveBufferSize` (int, default: 1024)
- **Purpose**: Buffer size for receiving network packets
- **Example**: `"ReceiveBufferSize": "2048"`
- **Notes**: Larger buffers can handle bigger packets but use more memory

### Database Settings

#### `LiteDbConnectionString` (string)
- **Purpose**: Connection string for LiteDB database
- **Example**: `"LiteDbConnectionString": "Filename=./data/sph.db;Connection=shared;"`
- **Notes**: 
  - `Filename`: Path to database file (created if doesn't exist)
  - `Connection=shared`: Allows multiple connections to same database

### Packet Processing

#### `PacketPartPath` (string)
- **Purpose**: Directory containing packet part definitions
- **Example**: `"PacketPartPath": "./packets/parts"`
- **Notes**: Used for packet serialization/deserialization

#### `PacketDefinitionPath` (string)
- **Purpose**: Directory containing packet definitions
- **Example**: `"PacketDefinitionPath": "./packets/definitions"`
- **Notes**: Defines packet structure and format

### Logging Settings

#### `LogPath` (string, default: "logs\\server.log")
- **Purpose**: Base path for log files (timestamped files will be created)
- **Example**: `"LogPath": "logs\\server.log"`
- **Notes**: 
  - Creates timestamped files like `server_20241220_143022.log`
  - Automatically manages log rotation (keeps latest 20 files)
  - Directory is created automatically if it doesn't exist

#### `DebugMode` (bool, default: true)
- **Purpose**: Enables additional debug logging and features
- **Example**: `"DebugMode": "false"`
- **Notes**: 
  - `true`: More verbose logging, debug features enabled
  - `false`: Production mode, minimal debug output

### Game World Settings

#### `ObjectVisibilityDistance` (float, default: 100.0)
- **Purpose**: Distance at which objects become visible to players
- **Example**: `"ObjectVisibilityDistance": "150.0"`
- **Notes**: Affects performance vs. gameplay visibility tradeoff

#### `CurrentCharacterInventoryId` (int, default: 0xA001/40961)
- **Purpose**: Default inventory ID for character items
- **Example**: `"CurrentCharacterInventoryId": "40962"`
- **Notes**: Hexadecimal value 0xA001 = decimal 40961

### Spawn Settings

#### `Spawn_X` (float, default: 80.0)
- **Purpose**: Default X coordinate for new character spawn
- **Example**: `"Spawn_X": "100.5"`

#### `Spawn_Y` (float, default: 150.0)
- **Purpose**: Default Y coordinate for new character spawn
- **Example**: `"Spawn_Y": "200.0"`
- **Notes**: Y is typically the vertical/height coordinate

#### `Spawn_Z` (float, default: 200.0)
- **Purpose**: Default Z coordinate for new character spawn
- **Example**: `"Spawn_Z": "250.0"`

#### `Spawn_Angle` (float, default: 0.75)
- **Purpose**: Default facing angle for new character spawn
- **Example**: `"Spawn_Angle": "1.57"`
- **Notes**: Angle in radians (0.75 ≈ 43 degrees)

#### `Spawn_Money` (int, default: 99999999)
- **Purpose**: Starting money for new characters
- **Example**: `"Spawn_Money": "10000"`
- **Notes**: Game currency amount

## Configuration Management

### Automatic Defaults
- If `appsettings.json` doesn't exist, it's created with default values
- Missing configuration keys are automatically added with defaults
- Server logs when configuration is created or updated

### Runtime Behavior
- Configuration is loaded once at server startup
- Changes to `appsettings.json` require server restart
- Invalid values will cause server startup failure with error message

### Environment-Specific Configuration
You can create multiple configuration files for different environments:

```bash
appsettings.json          # Default/Production
appsettings.dev.json      # Development
appsettings.test.json     # Testing
```

### Validation
- Numeric values are validated at startup
- Invalid paths will cause initialization errors
- Database connection is tested during startup

## Best Practices

### Development Settings
```json
{
  "Port": "25860",
  "DebugMode": "true",
  "LogPath": "logs\\dev-server.log",
  "Spawn_Money": "999999",
  "ObjectVisibilityDistance": "200.0"
}
```

### Production Settings
```json
{
  "Port": "25860",
  "DebugMode": "false",
  "LogPath": "logs\\server.log",
  "Spawn_Money": "1000",
  "ObjectVisibilityDistance": "100.0",
  "ReceiveBufferSize": "2048"
}
```

### Security Considerations
- Use appropriate file permissions for configuration files
- Database files should not be publicly accessible
- Consider using environment variables for sensitive data
- Regular backup of database files is recommended

### Performance Tuning
- **Higher `ReceiveBufferSize`**: Better for high-traffic servers
- **Lower `ObjectVisibilityDistance`**: Better performance, less visibility
- **Disable `DebugMode`**: Better performance in production
- **Adjust spawn coordinates**: Based on your game world layout

## Troubleshooting

### Common Issues

1. **Port already in use**: Change `Port` to an available port
2. **Database access denied**: Check file permissions on database path
3. **Log directory creation failed**: Ensure write permissions to log directory
4. **Invalid numeric values**: Check format matches expected type (int/float/bool)

### Error Messages
- Configuration errors are logged to console during startup
- Database connection errors are logged with specific details
- Network binding errors include port and system error information

### Validation
The server validates all configuration values at startup and will log specific errors for:
- Invalid numeric formats
- Inaccessible file paths
- Network configuration issues
- Database connection problems