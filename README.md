# SphereEmu
Boilerplate code to tinker with packets for Sphere (old Russian MMORPG) server emulator

Currently built with `Godot 4.* Mono`.

## Compatibility
Running Sphere on latest hardware (notably, RDNA 3 Radeon 7000 series) might turn out into a good old 15 fps in cities experience.
If that happens to you, dgVoodoo2 might help:
1. Grab the latest release from https://github.com/dege-diosg/dgVoodoo2
2. Unpack, run, add new profile for Sphere (click Add and navigate to game folder)
3. In the General tab, select Direct3D 12 (feature level 12.0) for Output API. Others didn't work for me, but your mileage may vary
4. In DirectX tab, select dgVoodoo Virtual 3D Accelerated Card for Videocard, 512-4096 MB for VRAM
5. Keep everything else as is or tweak as you like
6. Copy D3D9.dll (or all files to be safe) from MS\x86 to game folder (launcher does not care about added files)
7. Launch the game. If everything worked correctly, cities should jump from 15 to 150+ fps
8. If not, try different Output APIs and/or Videocards

## Running the server

1. Get and install prerequisities
   1. .NET 8.0 SDK
2. Download and build from source. 
3. Update appsettings.json and sphdbsettings.json for your setup.
4. Run `sphereclient_patched.lnk` for the client (it runs `sphereclient_patched.exe` with `/login` command line arg)
5. Launch the game, enter desired login and password. This should create a `Players` DB entry with your login and pwd hash
6. Next time, use those credentials or create a new user if you like

Multiplayer is technically supported at this moment, but everything would act strangely
