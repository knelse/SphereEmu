# SphereEmu Documentation

Welcome to the SphereEmu project documentation. This is a C# game server emulator built with Godot.

Documentation is handled by Cursor (Claude 4 Sonnet), so it may or may not be awful.

## Table of Contents

1. [Architecture](#architecture)
2. [Client Connection Flow](Client-Connection-Flow)
3. [File Structure Documentation](File-Structure)
4. [Configuration](Configuration)
5. [Development Setup](Development-Setup)

## Architecture

The project follows a modular architecture with clear separation of concerns:

- **Server**: Core server functionality, connection handling, and configuration
- **Client**: Client connection management and state handling
- **Shared**: Common functionality used across server and client
- **Godot**: Game engine integration and scene management
- **System**: System-level utilities and extensions

## Quick Start

1. Configure `appsettings.json` with your database and server settings
2. Run the server - it will listen on the configured port
3. Clients can connect and will be managed through the state system
4. Check logs for connection and error information

For detailed information, see the individual documentation pages linked above.