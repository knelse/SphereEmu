# Development Setup Guide

This guide covers setting up the development environment for SphereEmu.

## Prerequisites

### Required Software
- **.NET SDK 8.0 or later**: For C# compilation and runtime
- **Godot Engine 4.x**: For game engine functionality and scene management
- **Visual Studio 2022** or **JetBrains Rider**: Recommended IDEs with C# support
- **Git**: For version control

### Optional Tools
- **LiteDB Studio**: For database inspection and management
- **Wireshark**: For network packet analysis

## Project Setup

### 1. Clone Repository
```bash
git clone <repository-url>
cd SphereEmu
```

### 2. Restore NuGet Packages
```bash
dotnet restore SphServer.sln
```

### 3. IDE Configuration

#### Visual Studio 2022
1. Open `SphServer.sln`
2. Set startup project to main server project
3. Configure debugging settings:
   - Working Directory: Project root
   - Command Arguments: (none needed)
   - Environment Variables: `ASPNETCORE_ENVIRONMENT=Development`

#### JetBrains Rider
1. Open `SphServer.sln`
2. Configure Run Configuration:
   - Project: SphServer
   - Working Directory: `$ProjectFileDir$`
   - Environment: Development

### 4. Godot Integration
1. Open `project.godot` in Godot Engine
2. Ensure C# support is enabled
3. Build project in Godot

## Building and Running

### Command Line Build
```bash
# Debug build
dotnet build SphServer.sln

# Release build
dotnet build SphServer.sln -c Release
```

### IDE Build
- **Visual Studio**: Build → Build Solution (Ctrl+Shift+B)
- **Rider**: Build → Build Solution (Ctrl+F9)

### Running with Godot
1. Open project in Godot
2. Press F5 or click Play button
3. Select main scene when prompted

## Development Workflow

### 1. Code Organization
- Follow existing namespace conventions
- Place new server code in `/Server`
- Place new client code in `/Client`
- Place shared code in `/Shared`
- Use appropriate subdirectories for organization

### 2. Coding Standards
- Use C# naming conventions (PascalCase for public, camelCase for private)
- Add XML documentation for public methods
- Follow existing patterns for database access
- Use the logging system for debug output

## Debugging

### Server Debugging
1. Set breakpoints in IDE
2. Start debugging (F5)
3. Connect client to trigger breakpoints

### Client Connection Debugging
- Enable `DebugMode` in configuration
- Check console output for connection states
- Review log files for detailed packet information

### Database Debugging
- Use LiteDB Studio to inspect database contents
- Check database connection string in logs
- Verify file permissions on database directory

### Network Debugging
- Use Wireshark to capture packets on localhost
- Check server logs for connection attempts
- Verify firewall settings allow connections on configured port

## Troubleshooting Development Issues

### Build Errors
- **Missing references**: Run `dotnet restore`
- **Godot integration issues**: Rebuild in Godot editor
- **Version conflicts**: Check .NET SDK version

### Runtime Errors
- **Port binding failures**: Change port in configuration
- **Database access errors**: Check file permissions
- **Missing dependencies**: Verify NuGet packages restored

### Performance Issues
- **High memory usage**: Check for connection leaks
- **Slow startup**: Verify database and file access
- **Network timeouts**: Check firewall and network settings

## Contributing Guidelines

### Code Review Process
1. Create feature branch from main
2. Implement changes with appropriate tests
3. Update documentation if needed
4. Submit pull request with clear description
5. Address review feedback

### Commit Message Format
```
type(scope): description

Examples:
feat(server): add new authentication method
fix(client): resolve state transition bug
docs(wiki): update configuration guide
```

### Branch Naming
- `feature/description`: New features
- `fix/description`: Bug fixes
- `docs/description`: Documentation updates
- `refactor/description`: Code refactoring

This setup provides a complete development environment for working on SphereEmu, with proper debugging capabilities and development workflows.