using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Terrain.Fill;

/// <summary>
///     Builds a bake-only physics world: GridMap terrain collision plus StaticBody colliders for every
///     terrain object placement (plants, rocks, buildings, props).
/// </summary>
public static class TerrainPhysicsColliderBuilder
{
    public const string CollidersRootNodeName = "PhysicsBakeColliders";
    public const uint BakeCollisionLayer = 1;

    public sealed class BuildStats
    {
        public int TerrainMeshItems { get; init; }
        public int TerrainCells { get; init; }
        public int ObjectBodies { get; init; }
        public int ObjectPlacements { get; init; }
        public int Plants { get; init; }
        public int Rocks { get; init; }
        public int BuildingsAndProps { get; init; }
        public int SkippedPlacements { get; init; }
    }

    public static BuildStats Build(
        Node3D parent,
        GridMap terrain,
        string objectDataDirectory,
        string modelsDirectory)
    {
        ClearExisting(parent);
        var root = new Node3D { Name = CollidersRootNodeName };
        parent.AddChild(root);

        var terrainMeshItems = EnsureTerrainMeshLibraryCollision(terrain);
        terrain.CollisionLayer = BakeCollisionLayer;
        terrain.CollisionMask = 0;

        var objectStats = BuildObjectColliders(root, objectDataDirectory, modelsDirectory);
        return new BuildStats
        {
            TerrainMeshItems = terrainMeshItems,
            TerrainCells = terrain.GetUsedCells().Count,
            ObjectBodies = objectStats.Bodies,
            ObjectPlacements = objectStats.Placements,
            Plants = objectStats.Plants,
            Rocks = objectStats.Rocks,
            BuildingsAndProps = objectStats.BuildingsAndProps,
            SkippedPlacements = objectStats.Skipped,
        };
    }

    public static void ClearExisting(Node3D parent)
    {
        var existing = parent.GetNodeOrNull(CollidersRootNodeName);
        existing?.QueueFree();
    }

    public static bool HasColliderRoot(Node node)
        => node.GetNodeOrNull(CollidersRootNodeName) is not null
           || node.FindChild(CollidersRootNodeName, recursive: true, owned: false) is not null;

    private static int EnsureTerrainMeshLibraryCollision(GridMap terrain)
    {
        var meshLibrary = terrain.MeshLibrary;
        if (meshLibrary is null)
        {
            return 0;
        }

        var updated = 0;
        foreach (var itemId in meshLibrary.GetItemList())
        {
            var mesh = meshLibrary.GetItemMesh(itemId);
            if (mesh is null)
            {
                continue;
            }

            var shape = mesh.CreateTrimeshShape();
            if (shape is null)
            {
                continue;
            }

            meshLibrary.SetItemShapes(itemId, [shape]);
            updated++;
        }

        return updated;
    }

    private static ObjectBuildStats BuildObjectColliders(
        Node3D root,
        string objectDataDirectory,
        string modelsDirectory)
    {
        var placements = TerrainObjectPlacementSource.LoadAll(objectDataDirectory);
        var stats = new ObjectBuildStats();
        var loggedProgress = 0;

        foreach (var placement in placements)
        {
            if (!WalkSurfaceObjectMeshCache.TryGetMeshParts(placement.ObjectName, modelsDirectory, out var meshParts)
                || meshParts is null
                || meshParts.Count == 0)
            {
                stats.Skipped++;
                continue;
            }

            var bodiesAdded = 0;
            foreach (var part in meshParts)
            {
                if (part.Mesh is null)
                {
                    continue;
                }

                var shape = part.Mesh.CreateTrimeshShape();
                if (shape is null)
                {
                    continue;
                }

                var body = new StaticBody3D
                {
                    Name = $"Bake_{placement.ObjectName}_{stats.Bodies}",
                    CollisionLayer = BakeCollisionLayer,
                    CollisionMask = 0,
                };
                root.AddChild(body);
                body.GlobalTransform = placement.WorldTransform * part.MeshLocalToRoot;
                var collision = new CollisionShape3D { Shape = shape };
                body.AddChild(collision);
                bodiesAdded++;
                stats.Bodies++;
            }

            if (bodiesAdded == 0)
            {
                stats.Skipped++;
                continue;
            }

            stats.Placements++;
            switch (placement.Category)
            {
                case TerrainObjectWalkCategory.Plant:
                    stats.Plants++;
                    break;
                case TerrainObjectWalkCategory.Rock:
                    stats.Rocks++;
                    break;
                default:
                    stats.BuildingsAndProps++;
                    break;
            }

            loggedProgress++;
            if (loggedProgress == 1 || loggedProgress % 5000 == 0 || loggedProgress == placements.Count)
            {
                GD.Print(
                    $"TerrainPhysicsColliderBuilder: object colliders {loggedProgress}/{placements.Count} "
                    + $"(bodies={stats.Bodies}, skipped={stats.Skipped})...");
            }
        }

        return stats;
    }

    private sealed class ObjectBuildStats
    {
        public int Bodies { get; set; }
        public int Placements { get; set; }
        public int Plants { get; set; }
        public int Rocks { get; set; }
        public int BuildingsAndProps { get; set; }
        public int Skipped { get; set; }
    }
}
