using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Server.Config;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game;
using SphServer.Sphere.Game.WorldObject;
using SphereServer = SphServer.Server.SphereServer;

namespace SphServer.Client;

/// <summary>
/// New characters start in the newbie dungeon: placed at the regular spawn, teleported in once the
/// client settles, then the dungeon geometry (labyr), the captured dungeon objects (guide, chest),
/// the tutorial triggers, the pedestal sword, the token, the exit portal, the vendor and the mob are
/// sent. The way out is the original one: use the token while standing on the portal
/// </summary>
public static class NewPlayerDungeon
{
    /// <summary>
    /// World coords of the captured instance spawn; the serializer flips y itself
    /// </summary>
    private static readonly WorldCoords TeleportCoords =
        new (-1098.69506835937500, -4501.61474609375000, 1900.05493164062500, 1.57079637050629);

    /// <summary>
    /// No sst file has geometry above y 2100: the client builds the dungeon from
    /// params\mazes\x000.mbd ('a' + 23), whose guide, sword, token and spawn markers match the
    /// captured objects when placed at this room center (GetRoomCoord grid, 200 units)
    /// </summary>
    private const ushort MazeId = 23000;

    private const float LabyrinthX = -1100f;
    private const float LabyrinthY = 4500f;
    private const float LabyrinthZ = 1900f;

    private const int StartingMoney = 10;
    private const ushort TutorialVendorId = 0x0B15;
    private static TutorialVendor? tutorialVendor;
    private const double TeleportDelaySeconds = 1;
    private const double WorldLoadPollSeconds = 0.25;
    private const double FirstTeleportMaxWaitSeconds = 10;
    private const double TeleportRetrySeconds = 5;
    private const int TeleportRetryCount = 24;
    private const double LoadDelaySeconds = 0.5;

    /// <summary>
    /// x000 npc47 marker (maze -3948.41 1101.51 -3880.92); the live capture has a Psoglavets
    /// monster standing exactly there that hits the newcomer
    /// </summary>
    private static readonly Vector3 DungeonMobWorld = new (-1148.41f, 4501.51f, 1919.08f);

    private const int DungeonMobTypeId = 1010;

    /// <summary>
    /// x000.mbd lays the maze out as one corridor of 15 m lbg_* cells, spawn first and exit last
    /// (maze -3900 -3900 is world -1100 1900); there is no navmesh, so the mob walks cell centers
    /// </summary>
    private static readonly Vector2[] MazeCells =
    [
        new (-1100, 1900), new (-1115, 1900), new (-1130, 1900), new (-1145, 1900), new (-1145, 1915),
        new (-1145, 1930), new (-1130, 1930), new (-1115, 1930), new (-1100, 1930), new (-1100, 1945)
    ];

    private const float MazeCellReach = 11f;

    /// <summary>
    /// How far off the line to the next cell the mob may stand and still skip its own cell center
    /// </summary>
    private const float MazeAxisTolerance = 1.5f;

    /// <summary>
    /// x000 portal1 marker (maze -3899.59 1101.43 -3852.13), the dungeon exit
    /// </summary>
    private static readonly Vector3 ExitPortalWorld = new (-1099.59f, 4501.43f, 1947.87f);

    private const float ExitPortalRadius = 2.5f;

    /// <summary>
    /// Torweal city center; the dungeon token is Token_Tutorial_Torweal
    /// </summary>
    private static readonly WorldCoords ExitCoords = new (2292, 155.3, -2386);

    /// <summary>
    /// Live wp_sword1st (weapon_starting_sword) on the pedestal, next to the x000 wp_sword1 marker
    /// (maze -3930.07 1100.82 -3899.86)
    /// </summary>
    private static readonly Vector3 StarterSwordWorld = new (-1130.093f, 4500.912f, 1900.147f);

    /// <summary>
    /// Кривой меч: wp_sword1st has no game object, and this is the weakest catalog sword
    /// </summary>
    private const int StarterSwordGameId = 1;

    /// <summary>
    /// Not a process id, so the client keeps the sword on the ground
    /// </summary>
    private const ushort GroundContainerId = 0xFF00;

    /// <summary>
    /// tutomsg triggers: the client opens language	utoNN.hts while the player stands near one.
    /// Numbers follow the tuto texts (greeting, sword, Псоглавец, merchant, powder, stats, exit);
    /// positions are the x000 "empty" markers along the route
    /// </summary>
    private static readonly (ushort MessageNumber, Vector3 World)[] TutorialPoints =
    [
        (1, new (-1099.4f, 4501.5f, 1899.9f)),
        (2, new (-1126.5f, 4501.5f, 1900.0f)),
        (3, new (-1144.9f, 4501.5f, 1904.8f)),
        (4, new (-1133.2f, 4501.5f, 1930.6f)),
        (5, new (-1117.5f, 4501.5f, 1930.1f)),
        (6, new (-1099.8f, 4501.5f, 1933.0f)),
        (7, new (-1099.6f, 4501.5f, 1945.2f))
    ];

    private static readonly ushort[] tutorialEntityIds = new ushort[TutorialPoints.Length];

    /// <summary>
    /// x000 token marker on its stone; the captured replay brings its own copy as 0B16, which is
    /// removed so only the server's token, whose pickup is answered, stays
    /// </summary>
    private static readonly Vector3 TokenWorld = new (-1099.616f, 4501.088f, 1943.19f);

    private const ushort ReplayTokenEntityId = 0x0B16;

    /// <summary>
    /// tokenst setup (ContMan 15, u24 LOCKID + u8 NUM): teleport point 1803 «В Торвил», one charge
    /// </summary>
    private static readonly byte[] TokenSetup = [0x0B, 0x07, 0x00, 1];

    private static ushort tokenEntityId;
    private static readonly HashSet<ushort> tokenShownTo = [];

    /// <summary>
    /// Clients holding the token leave the original way, by using it on the portal
    /// </summary>
    private static readonly HashSet<ushort> tokenTakenBy = [];
    private static ushort exitTeleportEntityId;

    /// <summary>
    /// When each client last left through the portal: its next position update can still be on
    /// the portal, and the entry teleport retries must not pull it back in
    /// </summary>
    private static readonly Dictionary<ushort, DateTime> lastExitAt = [];

    /// <summary>
    /// When the client's own position update last put it inside the maze; once it has, the
    /// teleport has landed and the retries stop, so a later /tp or exit is left alone
    /// </summary>
    private static readonly Dictionary<ushort, DateTime> lastInsideReportAt = [];
    private static ushort labyrinthEntityId;
    private static ushort starterSwordEntityId;
    /// <summary>
    /// The dungeon is private, so each character gets its own mob, by character id
    /// </summary>
    private static readonly Dictionary<int, Monster> dungeonMobs = [];

    /// <summary>
    /// Client indexes that still see the pedestal sword; each one may take it once per entry
    /// </summary>
    private static readonly HashSet<ushort> starterSwordShownTo = [];

    /// <summary>
    /// A new character stays in the dungeon, login after login, until it leaves with the token
    /// </summary>
    public static void PrepareNewCharacter (CharacterDbEntry character)
    {
        character.InNewPlayerDungeon = true;
        character.Money = StartingMoney;
    }

    /// <summary>
    /// The client hangs on the loading screen when the session starts inside the dungeon, so the
    /// character enters at the regular spawn and only the teleport moves it
    /// </summary>
    public static void PlaceCharacter (CharacterDbEntry character)
    {
        character.X = ServerConfig.AppConfig.Spawn_X;
        character.Y = -ServerConfig.AppConfig.Spawn_Y;
        character.Z = -ServerConfig.AppConfig.Spawn_Z;
        character.Angle = ServerConfig.AppConfig.Spawn_Angle;
    }

    private static void MoveToDungeon (CharacterDbEntry character, WorldCoords target)
    {
        // DB keeps Y and Z negated relative to world coords; the target's y is already flipped
        character.X = target.x;
        character.Y = target.y;
        character.Z = -target.z;
        character.Angle = target.turn;
    }

    /// <summary>
    /// One id for every viewer: each client owns its own labyr process
    /// </summary>
    private static ushort GetLabyrinthEntityId ()
    {
        if (labyrinthEntityId == 0)
        {
            labyrinthEntityId = WorldObjectIndex.New ();
        }

        return labyrinthEntityId;
    }

    public static async void Start (SphereClient client)
    {
        try
        {
            // Client indexes are reused: a new session decides from the bag again whether the token
            // still lies on the stone
            if (client.CurrentCharacter is { } entering)
            {
                tokenTakenBy.Remove (entering.ClientIndex);
                tokenShownTo.Remove (entering.ClientIndex);
            }

            // A teleport that lands during the loading screen is dropped, and how long that screen
            // lasts varies (seconds on a warm client, half a minute in a VM; the world-loaded frame
            // does not always come). So the teleport is sent early, re-sent once the client reports
            // that frame, and re-sent while the client's own position updates still put it outside
            for (var waited = 0.0; !client.WorldLoaded && waited < FirstTeleportMaxWaitSeconds; waited += WorldLoadPollSeconds)
            {
                if (!await Delay (client, WorldLoadPollSeconds))
                {
                    return;
                }
            }

            if (!await Delay (client, TeleportDelaySeconds))
            {
                return;
            }

            var startedAt = DateTime.UtcNow;
            Enter (client, replayCapturedObjects: true);
            var sawWorldLoaded = client.WorldLoaded;
            for (var attempt = 1; attempt <= TeleportRetryCount; attempt++)
            {
                if (!await Delay (client, TeleportRetrySeconds) || client.CurrentCharacter is not { } character)
                {
                    return;
                }

                if (lastExitAt.TryGetValue (character.ClientIndex, out var exitedAt) && exitedAt > startedAt
                    || lastInsideReportAt.TryGetValue (character.ClientIndex, out var insideAt) && insideAt > startedAt)
                {
                    return;
                }

                var worldJustLoaded = client.WorldLoaded && !sawWorldLoaded;
                sawWorldLoaded = client.WorldLoaded;
                if (IsInside (character) && !worldJustLoaded)
                {
                    continue;
                }

                SphLogger.Info ($"New player dungeon: re-sending the teleport (attempt {attempt}) for {character.Name}. " +
                                $"Client ID: {character.ClientIndex:X4}");
                Enter (client, replayCapturedObjects: true);
            }
        }
        catch (Exception ex)
        {
            SphLogger.Error ("New player dungeon start failed", ex);
        }
    }

    /// <summary>
    /// False once the client is gone
    /// </summary>
    private static async global::System.Threading.Tasks.Task<bool> Delay (SphereClient client, double seconds)
    {
        await client.ToSignal (client.GetTree ().CreateTimer (seconds), SceneTreeTimer.SignalName.Timeout);
        return GodotObject.IsInstanceValid (client);
    }

    /// <summary>
    /// GM /npd entry; without the captured replay only the teleport, the geometry, the mob and the
    /// pedestal sword go out
    /// </summary>
    public static void Enter (SphereClient client, bool replayCapturedObjects) =>
        EnterAt (client, TeleportCoords, replayCapturedObjects);

    /// <summary>
    /// GM /npd x y z: a plain /tp inside the maze drops the client-built labyrinth, so the jump goes
    /// through the dungeon entry, which sends the geometry again
    /// </summary>
    public static void EnterAt (SphereClient client, double worldX, double worldY, double worldZ) =>
        EnterAt (client, new WorldCoords (worldX, -worldY, worldZ, TeleportCoords.turn), false);

    private static async void EnterAt (SphereClient client, WorldCoords target, bool replayCapturedObjects)
    {
        try
        {
            if (client.CurrentCharacter is not { } character)
            {
                return;
            }

            client.MaybeQueueNetworkPacketSend (
                new CharacterDbEntrySerializer (character).GetNewPlayerDungeonTeleportAndUpdateStatsByteArray (target));
            MoveToDungeon (character, target);
            client.SaveCharacter ();

            await client.ToSignal (client.GetTree ().CreateTimer (LoadDelaySeconds), SceneTreeTimer.SignalName.Timeout);
            if (!GodotObject.IsInstanceValid (client))
            {
                return;
            }

            client.MaybeQueueNetworkPacketSend (CommonPackets.BuildLabyrinthSpawn (
                client.GetLocalObjectId (GetLabyrinthEntityId ()), LabyrinthX, LabyrinthY, LabyrinthZ, MazeId));
            if (replayCapturedObjects)
            {
                client.MaybeQueueNetworkPacketSend (CommonPackets.LoadNewPlayerDungeon);
                // The replay carries the captured newcomer's stats (level 1, zeros) and money
                client.SendMoney ();
                NetworkedStatsUpdater.Update (character, client.MaybeQueueNetworkPacketSend, full: true);
            }

            SendTutorialObjects (client);
            SendStarterSword (client);
            EnsureTutorialVendor ();
            EnsureDungeonMob (character.Id);

            SphLogger.Info ($"New player dungeon loaded for {character.Name}. Client ID: {character.ClientIndex:X4}");
        }
        catch (Exception ex)
        {
            SphLogger.Error ("New player dungeon enter failed", ex);
        }
    }

    /// <summary>
    /// The tutorial triggers and the telep1 exit portal; labyr builds only walls, so the original
    /// server spawned these, along with the token on its stone
    /// </summary>
    private static void SendTutorialObjects (SphereClient client)
    {
        for (var i = 0; i < TutorialPoints.Length; i++)
        {
            if (tutorialEntityIds[i] == 0)
            {
                tutorialEntityIds[i] = WorldObjectIndex.New ();
            }

            var (number, world) = TutorialPoints[i];
            client.MaybeQueueNetworkPacketSend (CommonPackets.BuildContManSpawn (
                client.GetLocalObjectId (tutorialEntityIds[i]), ObjectType.Tutorial_Message, world.X, world.Y, world.Z,
                15, [(byte) number, (byte) (number >> 8)]));
        }

        if (exitTeleportEntityId == 0)
        {
            exitTeleportEntityId = WorldObjectIndex.New ();
        }

        if (tokenEntityId == 0)
        {
            tokenEntityId = WorldObjectIndex.New ();
        }

        client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (ReplayTokenEntityId));
        // A token already in this client's bag would jump back onto the stone if spawned again
        if (client.CurrentCharacter is { } viewer && !tokenTakenBy.Contains (viewer.ClientIndex)
            && !viewer.Items.Values.Any (id =>
                DbConnection.Items.FindById (id)?.WireObjectType == ObjectType.Token_Tutorial_Torweal))
        {
            client.MaybeQueueNetworkPacketSend (CommonPackets.BuildContManSpawn (
                client.GetLocalObjectId (tokenEntityId), ObjectType.Token_Tutorial_Torweal,
                TokenWorld.X, TokenWorld.Y, TokenWorld.Z, 15, TokenSetup));
            tokenShownTo.Add (viewer.ClientIndex);
        }

        client.MaybeQueueNetworkPacketSend (CommonPackets.BuildContManSpawn (
            client.GetLocalObjectId (exitTeleportEntityId), ObjectType.Teleport,
            ExitPortalWorld.X, ExitPortalWorld.Y, ExitPortalWorld.Z));
    }

    /// <summary>
    /// The character's own mob, seen and fought by nobody else; respawned on the next entry once killed
    /// </summary>
    private static void EnsureDungeonMob (int characterId)
    {
        if (dungeonMobs.TryGetValue (characterId, out var existing) && GodotObject.IsInstanceValid (existing)
            && !existing.IsDead)
        {
            return;
        }

        if (!GameObjectDb.Db.TryGetValue (DungeonMobTypeId, out var gameObject) || gameObject is null
            || !MonsterTypeMapping.MonsterTypeToMonsterNameMapping.TryGetValue (DungeonMobTypeId, out var monsterType))
        {
            SphLogger.Warning ($"New player dungeon: monster type {DungeonMobTypeId} is not spawnable");
            return;
        }

        var scene = (PackedScene) ResourceLoader.Load ("res://Godot/Scenes/Monster.tscn");
        var monster = scene.Instantiate<Monster> ();
        monster.MonsterType = monsterType;
        monster.MonsterInstance = new SphMonsterInstance (new SphMonsterData (gameObject), 1, NamedBossRank.None);
        monster.Angle = WorldObject.CreateRandomSpawnAngle ();
        monster.Name = $"NewPlayerDungeon_{monsterType}";
        monster.PrivateToCharacterId = characterId;
        // Waypoints are floor heights: the walk adds the model's origin offset on top of them, so a
        // waypoint at the mob's own height would lift it by that offset on every repath
        var spawnY = -DungeonMobWorld.Y;
        var floorY = spawnY - monster.GetSpawnOriginYOffset (spawnY);
        monster.PathOverride = (from, to) => FindMazePath (from, to, floorY);
        SphereServer.ServerNode.CallDeferred ("add_child", monster);
        // Godot space keeps Y and Z negated, like CharacterDbEntry.Origin
        monster.Transform = new Transform3D (Basis.Identity,
            new Vector3 (DungeonMobWorld.X, -DungeonMobWorld.Y, -DungeonMobWorld.Z));
        dungeonMobs[characterId] = monster;
        SphLogger.Info ($"New player dungeon: spawned {monsterType} for character {characterId}");
    }

    /// <summary>
    /// Godot-space waypoints through the corridor cells; within one cell the walk is straight
    /// </summary>
    private static IReadOnlyList<Vector3>? FindMazePath (Vector3 from, Vector3 to, float floorY)
    {
        var start = FindMazeCell (from);
        var goal = FindMazeCell (to);
        if (start < 0 || goal < 0)
        {
            return null;
        }

        var path = new List<Vector3> ();
        if (start != goal)
        {
            var step = Math.Sign (goal - start);
            // Going back to its own center first would make each repath pull the mob backwards
            var first = IsOnMazeAxis (from, start, start + step) ? start + step : start;
            for (var i = first; i != goal; i += step)
            {
                path.Add (new Vector3 (MazeCells[i].X, floorY, -MazeCells[i].Y));
            }

            // Likewise its target's center only matters when the target is round the cell's corner
            if (!IsOnMazeAxis (to, goal, goal - step))
            {
                path.Add (new Vector3 (MazeCells[goal].X, floorY, -MazeCells[goal].Y));
            }
        }

        path.Add (new Vector3 (to.X, floorY, to.Z));
        return path;
    }

    private static int FindMazeCell (Vector3 godot)
    {
        var world = new Vector2 (godot.X, -godot.Z);
        var best = -1;
        var bestDistance = MazeCellReach;
        for (var i = 0; i < MazeCells.Length; i++)
        {
            var distance = world.DistanceTo (MazeCells[i]);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static bool IsOnMazeAxis (Vector3 godot, int cell, int next)
    {
        var world = new Vector2 (godot.X, -godot.Z);
        var axis = MazeCells[next] - MazeCells[cell];
        var along = (world - MazeCells[cell]).Dot (axis.Normalized ());
        var off = (world - MazeCells[cell] - axis.Normalized () * along).Length ();
        return along >= 0 && off <= MazeAxisTolerance;
    }

    private static void EnsureTutorialVendor ()
    {
        if (tutorialVendor is not null && GodotObject.IsInstanceValid (tutorialVendor))
        {
            return;
        }

        if (ActiveWorldObjects.Get (TutorialVendorId) is not null ||
            !SphObjectDb.GameObjectDataDb.TryGetValue (601, out var catalog))
        {
            return;
        }

        tutorialVendor = new TutorialVendor
        {
            ID = TutorialVendorId,
            Name = "NewPlayerDungeonVendor",
            NpcType = NpcType.TradeAlchemy,
            VendorItemTierMin = 1,
            VendorItemTierMax = 1
        };
        var powder = ItemDbEntry.CreateFromGameObject (SphGameObject.CreateFromGameObject (catalog));
        powder.Id = WorldObjectIndex.NewItem ();
        powder.ItemCount = 1;
        powder.ParentContainerId = TutorialVendorId;
        tutorialVendor.ItemsOnSale.Add (powder);
        SphereServer.ServerNode.CallDeferred (Node.MethodName.AddChild, tutorialVendor);
    }

    /// <summary>
    /// The captured replay only moves the sword (TransformUpdate 0B09), its spawn frame is cut off,
    /// so the spawn goes in the weapon_starting_sword.spdp shape: no game id, ground container
    /// </summary>
    private static void SendStarterSword (SphereClient client)
    {
        if (client.CurrentCharacter is not { } character)
        {
            return;
        }

        if (starterSwordEntityId == 0)
        {
            starterSwordEntityId = WorldObjectIndex.New ();
        }

        client.MaybeQueueNetworkPacketSend (ItemRecordEncoder.EncodeWithoutGameId (
            client.GetLocalObjectId (starterSwordEntityId), (int) ObjectType.Weapon_Starting_Sword, GroundContainerId,
            StarterSwordWorld.X, StarterSwordWorld.Y, StarterSwordWorld.Z));
        starterSwordShownTo.Add (character.ClientIndex);
    }

    /// <summary>
    /// Null once the client took the sword or never got it
    /// </summary>
    public static ushort? StarterSwordIdFor (SphereClient client)
    {
        if (starterSwordEntityId == 0 || client.CurrentCharacter is not { } character
                                      || !starterSwordShownTo.Contains (character.ClientIndex))
        {
            return null;
        }

        return client.GetLocalObjectId (starterSwordEntityId);
    }

    /// <summary>
    /// Null once the client took the token or never got it
    /// </summary>
    public static ushort? TokenIdFor (SphereClient client) =>
        tokenEntityId != 0 && client.CurrentCharacter is { } character && tokenShownTo.Contains (character.ClientIndex)
            ? client.GetLocalObjectId (tokenEntityId)
            : null;

    /// <summary>
    /// The token on the stone is a client object; taking it removes that object and puts a real
    /// Token_Tutorial_Torweal item into a free bag cell, which the exit later consumes
    /// </summary>
    public static void TakeToken (SphereClient client)
    {
        if (TokenIdFor (client) is not { } localTokenId || client.CurrentCharacter is not { } character)
        {
            return;
        }

        if (character.FindEmptyInventorySlot () is not { } slot)
        {
            SphLogger.Info ($"New player dungeon: {character.Name} cannot take the token, inventory full");
            return;
        }

        tokenShownTo.Remove (character.ClientIndex);
        tokenTakenBy.Add (character.ClientIndex);
        var playerId = character.ClientIndex;
        var token = new ItemDbEntry
        {
            Id = WorldObjectIndex.NewItem (),
            ObjectType = ObjectType.Token_Tutorial_Torweal,
            ItemCount = 1,
            Weight = 10
        };
        DbConnection.SaveItem (token);
        character.Items[slot] = token.Id;
        client.SaveCharacter ();
        client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (localTokenId));
        client.MaybeQueueNetworkPacketSend (ItemRecordEncoder.Encode (token,
            SphServer.Shared.BitStream.SphBitStream.ByteSwap (playerId)));
        if (ItemSlotReserve.Build (playerId, slot, token.Id) is { } reserve)
        {
            client.MaybeQueueNetworkPacketSend (reserve);
        }

        SphLogger.Info ($"New player dungeon: {character.Name} took the token [{localTokenId:X4}] into {slot}");
    }

    /// <summary>
    /// The pedestal object goes away for this client only, and a regular Кривой меч takes its place
    /// in the inventory, since the client cannot carry a weapon without a game id
    /// </summary>
    public static void TakeStarterSword (SphereClient client)
    {
        if (StarterSwordIdFor (client) is not { } localSwordId || client.CurrentCharacter is not { } character)
        {
            return;
        }

        if (character.FindEmptyInventorySlot () is null
            || !SphObjectDb.GameObjectDataDb.TryGetValue (StarterSwordGameId, out var catalog))
        {
            // A drag already lifted the sword off the pedestal, so it is put back
            SendStarterSword (client);
            SphLogger.Info ($"New player dungeon: {character.Name} cannot take the starter sword, inventory full");
            return;
        }

        starterSwordShownTo.Remove (character.ClientIndex);
        client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (localSwordId));

        var item = ItemDbEntry.CreateFromGameObject (SphGameObject.CreateFromGameObject (catalog));
        item.ItemCount = 1;
        item.Id = WorldObjectIndex.NewItem ();
        DbConnection.SaveItem (item);
        var slot = client.PutItemInInventoryAndDeclare (item);
        SphLogger.Info ($"New player dungeon: {character.Name} took the starter sword [{item.Id:X4}] into {slot}");
    }

    /// <summary>
    /// Over the x000 maze footprint (about 60 units square around the labyr center)
    /// </summary>
    public static bool IsInside (CharacterDbEntry? character)
    {
        if (character is null)
        {
            return false;
        }

        // Any height above 2100: no static geometry exists up there, and a character lost in the
        // void drifts far above the maze
        return Math.Abs (character.X - LabyrinthX) < 100 && -character.Y > 2100
                                                         && Math.Abs (-character.Z - LabyrinthZ) < 100;
    }

    public static void CheckExit (SphereClient client)
    {
        if (client.CurrentCharacter is { } character && IsInside (character))
        {
            lastInsideReportAt[character.ClientIndex] = DateTime.UtcNow;
        }
    }

    public static bool UseExitToken (SphereClient client, ItemDbEntry token, ushort targetId)
    {
        if (client.CurrentCharacter is not { } character || !IsInside (character) ||
            targetId != client.GetLocalObjectId (exitTeleportEntityId) ||
            !character.Items.ContainsValue (token.Id))
        {
            return false;
        }

        var world = new Vector3 ((float) character.X, (float) -character.Y, (float) -character.Z);
        if (world.DistanceTo (ExitPortalWorld) > ExitPortalRadius)
        {
            return false;
        }

        foreach (var slot in character.Items.Where (x => x.Value == token.Id).Select (x => x.Key).ToList ())
        {
            character.Items.Remove (slot);
            if (ItemSlotReserve.Build (character.ClientIndex, slot, ItemSlotReserve.NoItem) is { } clear)
            {
                client.MaybeQueueNetworkPacketSend (clear);
            }
        }
        DbConnection.Items.Delete (token.Id);
        WorldObjectIndex.Release ((ushort) token.Id);
        client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity ((ushort) token.Id));
        tokenTakenBy.Remove (character.ClientIndex);

        lastExitAt[character.ClientIndex] = DateTime.UtcNow;
        // Removing a tutomsg closes its window, which otherwise stays open after the exit
        foreach (var id in tutorialEntityIds)
        {
            if (id != 0)
            {
                client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (client.GetLocalObjectId (id)));
            }
        }

        client.MaybeQueueNetworkPacketSend (new CharacterDbEntrySerializer (character).GetTeleportByteArray (ExitCoords));
        character.X = ExitCoords.x;
        character.Y = -ExitCoords.y;
        character.Z = -ExitCoords.z;
        character.InNewPlayerDungeon = false;
        client.SaveCharacter ();
        SphLogger.Info ($"New player dungeon: {character.Name} used token {token.Id:X4} on the exit portal");
        return true;
    }
}
