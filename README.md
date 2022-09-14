# SphereEmu
Early WIP server emulator for Sphere (old Russian MMORPG)

Currently built with `Godot 3.4.4 Mono`, but there is an old plain-C# version in `emu` subfolder.

## Running the server
This is too involved as is, I will make it easier in future:
1. Get and install prerequisities
   1. Godot v3.4.4 with Mono included
   2. Mono runtime
   3. MSSQL 2019 (to be axed in favor of in-memory DB later)
2. Use provided SQL scripts to create the database and necessary tables
3. Download and unpack game client from the Releases page
   - Edit _connect.cfg_ if you wish to use different connection settings, but default should be ok
4. Build and run the Godot project
5. Launch the game, enter desired login and password. This should create a `sph/players` DB entry with your login and pwd hash
6. Next time, use those credentials or create a new user if you like

Multiplayer is technically supported at this moment, but everything would act strangely.

If you want to roam around the game world instead of going to the new player dungeon, navigate to `GodotServer\Client.cs` and comment _StreamPeer.PutData_ lines in _MoveToNewPlayerDungeonAsync_ method.

## File structure
Server code resides under `GodotServer`. Everything else is due for a cleanup, one day...

For the main server class, go to `GodotServer\MainServer.cs`. It's responsible for managing connections and game objects and creating a new Client object per every connected player.

For the client class, go to `GodotServer\Client.cs`. Right now, it's a bit of a mess, but it handles the player flow once they connect to the server:
1. Loading initial data and exchanging creds
2. Logging in
3. Getting character select screen data
4. Choosing / creating / deleting characters
5. Entering the game
6. Ingame loop

## Scene structure
Supports only the new player dungeon for the moment, I'm yet to figure out the game's terrain generation.

- MainServer
  - NewPlayerDungeon
    - Navmesh with geometry
    - Mob / loot
  - Client
