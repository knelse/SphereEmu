using Godot;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Server.Debug.Parser;

// /mob spawns a REAL attackable Monster node at the character, unlike /clientmob which only
// sends a client-local template with no server WorldObject (it vanishes on relog). Follows the
// WorldObjectSpawner recipe so the node registers in ActiveWorldObjects and can be targeted.
public partial class ConsoleCommandParser
{
    private static class CombatSpawnTunables
    {
        public const int DefaultMonsterTypeId = 1000; // Палочник, the classic newbie mob (itemDb row 1000)
        public const int MinLevel = 1;
        public const int MaxLevel = 60;
        public const int MaxHpOverride = ushort.MaxValue; // level >= 2 spawn frame carries 16-bit HP
        public const int MaxHpOverrideLevelOne = byte.MaxValue; // level-1 template carries 8-bit HP
        public const string MonsterScenePath = "res://Godot/Scenes/Monster.tscn";
    }

    // /mob [type] [level] [hp] — spawn a real attackable Monster at the character. The optional hp
    // overrides Current/MaxHp for testing.
    private void SpawnRealMonster (string args)
    {
        const string usage =
            "Usage: /mob [<type: numeric id or Cyrillic name>] [<level 1-60>] [<hp>] - " +
            "default Палочник; e.g. /mob 1291 1 160 or /mob Палочник 2";

        if (!TryParseMobRealArgs(args, out var monsterTypeId, out var level, out var hpOverride, out var error))
        {
            SendFeedback($"{error} {usage}");
            return;
        }

        if (sphereClient is null)
        {
            SendFeedback("/mob needs a connected client.");
            return;
        }

        if (!GameObjectDb.Db.TryGetValue(monsterTypeId, out var gameObject))
        {
            SendFeedback($"Unknown monster type id {monsterTypeId}. {usage}");
            return;
        }

        if (!MonsterTypeMapping.MonsterTypeToMonsterNameMapping.TryGetValue(monsterTypeId, out var monsterType))
        {
            SendFeedback($"Game object {monsterTypeId} is not a spawnable monster type. {usage}");
            return;
        }

        if (!ClientWorldPosition.TryGetGodotWorldPosition(sphereClient, out var spawnPosition))
        {
            SendFeedback("/mob could not resolve your world position - is a character selected?");
            return;
        }

        var monsterScene = (PackedScene) ResourceLoader.Load(CombatSpawnTunables.MonsterScenePath);
        var monsterNode = monsterScene.Instantiate<Monster>();
        monsterNode.MonsterType = monsterType;
        var monsterInstance = new SphMonsterInstance(new SphMonsterData(gameObject), level, false);
        if (hpOverride is not null)
        {
            monsterInstance.MaxHp = hpOverride.Value;
            monsterInstance.CurrentHp = hpOverride.Value;
        }

        monsterNode.MonsterInstance = monsterInstance;
        monsterNode.Angle = WorldObject.CreateRandomSpawnAngle();
        monsterNode.Name = monsterType.ToString();
        monsterNode.ID = WorldObjectIndex.New();
        SphereServer.ServerNode.CallDeferred("add_child", monsterNode);
        monsterNode.Transform = new Transform3D(Basis.Identity, spawnPosition);

        SendFeedback($"Spawned real {monsterNode.Name} - id 0x{monsterNode.ID:X4} ({monsterNode.ID}), " +
                     $"type {monsterTypeId}, level {level}, HP {monsterInstance.CurrentHp}/{monsterInstance.MaxHp}.");
    }

    // Parses up to three positional tokens: type (numeric itemDb id or Cyrillic MonsterType name;
    // defaults to Палочник), level (1-60), and an optional HP override. Godot-free so it stays
    // unit-testable. A level-1 override above 255 is rejected because the level-1 spawn template
    // carries only 8-bit HP.
    public static bool TryParseMobRealArgs (string args, out int monsterTypeId, out int level,
        out int? hpOverride, out string? error)
    {
        monsterTypeId = CombatSpawnTunables.DefaultMonsterTypeId;
        level = CombatSpawnTunables.MinLevel;
        hpOverride = null;
        error = null;

        var tokens = (args ?? string.Empty).Split(' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length > 3)
        {
            error = "Too many arguments.";
            return false;
        }

        if (tokens.Length >= 1)
        {
            if (int.TryParse(tokens[0], out var parsedTypeId))
            {
                monsterTypeId = parsedTypeId;
            }
            else if (Enum.TryParse<MonsterType>(tokens[0], true, out var parsedType) &&
                     MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue(parsedType, out var mappedId))
            {
                monsterTypeId = mappedId;
            }
            else
            {
                error = $"Unknown monster type '{tokens[0]}'.";
                return false;
            }
        }

        if (tokens.Length >= 2)
        {
            if (!int.TryParse(tokens[1], out level) ||
                level is < CombatSpawnTunables.MinLevel or > CombatSpawnTunables.MaxLevel)
            {
                error = $"Level must be {CombatSpawnTunables.MinLevel}-{CombatSpawnTunables.MaxLevel}.";
                return false;
            }
        }

        if (tokens.Length == 3)
        {
            if (!int.TryParse(tokens[2], out var parsedHp) ||
                parsedHp is < 1 or > CombatSpawnTunables.MaxHpOverride)
            {
                error = $"HP override must be 1-{CombatSpawnTunables.MaxHpOverride}.";
                return false;
            }

            if (level <= 1 && parsedHp > CombatSpawnTunables.MaxHpOverrideLevelOne)
            {
                error = $"Level-1 mobs carry 8-bit HP in the spawn frame - use level >= 2 or " +
                        $"hp <= {CombatSpawnTunables.MaxHpOverrideLevelOne}.";
                return false;
            }

            hpOverride = parsedHp;
        }

        return true;
    }
}
