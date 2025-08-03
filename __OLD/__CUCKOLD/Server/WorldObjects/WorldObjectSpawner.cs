using System;
using System.IO;
using Godot;
using SphServer.Enums;
using SphServer.Server;

namespace SphServer;

public static class WorldObjectSpawner
{
    private static readonly PackedScene MonsterScene = (PackedScene) ResourceLoader.Load("res://Monster.tscn");

    private static readonly PackedScene AlchemyResourceScene =
        (PackedScene) ResourceLoader.Load("res://alchemy_resource.tscn");

    public static void InstantiateObjects ()
    {
        var mobData = File.ReadAllLines(@"Helpers\MonsterSpawnData\MonsterSpawnData.txt");
        foreach (var line in mobData)
        {
            try
            {
                var split = line.Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var x = float.Parse(split[3]);
                var y = float.Parse(split[4]);
                var z = float.Parse(split[5]);
                var angle = int.Parse(split[6]);
                var type = int.Parse(split[9]);
                var level = int.Parse(split[10]);
                if (level > 30)
                {
                    level = 1;
                }

                var monsterNode = MonsterScene.Instantiate<Monster>();
                monsterNode.MonsterType = MonsterTypeMapping.MonsterTypeToMonsterNameMapping[type];
                monsterNode.MonsterInstance =
                    new SphMonsterInstance(new SphMonsterData(SphereServer.SphGameObjectDb[type]), level, false);
                monsterNode.Angle = angle;
                monsterNode.Name = Enum.GetName(typeof (MonsterType), monsterNode.MonsterType);
                SphereServer.ServerNode.CallDeferred("add_child", monsterNode);
                monsterNode.Transform = new Transform3D(Basis.Identity, new Vector3(x, -y, z));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        var alchemyResourceData = File.ReadAllLines(@"Helpers\ItemOnGroundSpawnData\AlchemyResourceSpawnData.txt");
        foreach (var line in alchemyResourceData)
        {
            try
            {
                var split = line.Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var type = split[1];
                var x = float.Parse(split[3]);
                var y = float.Parse(split[4]);
                var z = float.Parse(split[5]);
                var angle = int.Parse(split[6]);
                var gameId = int.Parse(split[7]);

                var node = AlchemyResourceScene.Instantiate<AlchemyResource>();
                node.Angle = angle;
                node.GameObjectID = gameId;
                SphereServer.ServerNode.CallDeferred("add_child", node);
                node.Transform = new Transform3D(Basis.Identity, new Vector3(x, -y, z));
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
}