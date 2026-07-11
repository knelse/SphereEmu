using System;
using System.IO;
using Godot;
using SphServer.Helpers;
using SphServer.Server;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Sphere.Game.WorldObject.Spawner;

/// <summary>Spawn data uses origin-style world space (X right, Y down, Z forward); Godot is Y up, forward = -Z.</summary>
public static class WorldObjectSpawner
{
    private static readonly PackedScene MonsterScene =
        (PackedScene)ResourceLoader.Load("res://Godot/Scenes/Monster.tscn");

    private static readonly PackedScene AlchemyResourceScene =
        (PackedScene)ResourceLoader.Load("res://Godot/Scenes/alchemy_resource.tscn");

    public static void InstantiateObjects()
    {
        var mobData = File.ReadAllLines(@"Sphere.Game\SpawnData\MonsterSpawnData.txt");
        foreach (var line in mobData)
        {
            try
            {
                var split = line.Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var x = FileFormatCulture.ParseFloat(split[3]);
                var y = FileFormatCulture.ParseFloat(split[4]);
                var z = FileFormatCulture.ParseFloat(split[5]);
                var angle = FileFormatCulture.ParseInt(split[6]);
                var type = FileFormatCulture.ParseInt(split[9]);
                var level = FileFormatCulture.ParseInt(split[10]);
                if (level > 30)
                {
                    level = 1;
                }

                var monsterNode = MonsterScene.Instantiate<Monster>();
                monsterNode.MonsterType = MonsterTypeMapping.MonsterTypeToMonsterNameMapping[type];
                monsterNode.MonsterInstance =
                    new SphMonsterInstance(new SphMonsterData(GameObjectDb.Db[type]), level, false);
                monsterNode.Angle = angle;
                monsterNode.Name = Enum.GetName(typeof(MonsterType), monsterNode.MonsterType);
                SphereServer.ServerNode.CallDeferred("add_child", monsterNode);
                monsterNode.Transform = new Transform3D(Basis.Identity, OriginPositionToGodot(x, y, z));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        var alchemyResourceData = File.ReadAllLines(@"Sphere.Game\SpawnData\AlchemyResourceSpawnData.txt");
        foreach (var line in alchemyResourceData)
        {
            try
            {
                var split = line.Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var type = split[1];
                var x = FileFormatCulture.ParseFloat(split[3]);
                var y = FileFormatCulture.ParseFloat(split[4]);
                var z = FileFormatCulture.ParseFloat(split[5]);
                var angle = FileFormatCulture.ParseInt(split[6]);
                var gameId = FileFormatCulture.ParseInt(split[7]);

                var node = AlchemyResourceScene.Instantiate<AlchemyResource>();
                node.Angle = angle;
                node.GameObjectID = gameId;
                SphereServer.ServerNode.CallDeferred("add_child", node);
                node.Transform = new Transform3D(Basis.Identity, OriginPositionToGodot(x, y, z));
                node.ObjectType = Enum.TryParse(type, out ObjectType objectType)
                    ? objectType
                    : ObjectType.AlchemyMetal;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }

    private static Vector3 OriginPositionToGodot(float x, float y, float z) => new(x, -y, -z);
}