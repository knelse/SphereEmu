using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Cached mesh parts extracted from terrain object GLB scenes for walk-surface height bakes.
/// </summary>
public static class WalkSurfaceObjectMeshCache
{
    private static readonly Dictionary<string, IReadOnlyList<WalkSurfaceObjectMeshPart>?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Clear()
    {
        Cache.Clear();
    }

    public static bool TryGetMeshParts(string objectName, string modelsDirectory, out IReadOnlyList<WalkSurfaceObjectMeshPart> parts)
    {
        parts = [];
        if (Cache.TryGetValue(objectName, out var cached))
        {
            if (cached is null)
            {
                return false;
            }

            parts = cached;
            return parts.Count > 0;
        }

        var computed = TryLoadMeshParts(objectName, modelsDirectory);
        Cache[objectName] = computed;
        if (computed is null || computed.Count == 0)
        {
            return false;
        }

        parts = computed;
        return true;
    }

    private static IReadOnlyList<WalkSurfaceObjectMeshPart>? TryLoadMeshParts(string objectName, string modelsDirectory)
    {
        var baseDir = modelsDirectory.TrimEnd('/') + "/";
        PackedScene? scene = null;
        foreach (var ext in new[] { "glb", "gltf" })
        {
            var path = $"{baseDir}{objectName}.{ext}";
            if (!ResourceLoader.Exists(path))
            {
                continue;
            }

            scene = ResourceLoader.Load<PackedScene>(path);
            break;
        }

        if (scene is null)
        {
            return null;
        }

        var root = scene.Instantiate<Node3D>();
        try
        {
            var list = new List<WalkSurfaceObjectMeshPart>();
            CollectMeshes(root, root, list);
            return list.Count == 0 ? null : list;
        }
        finally
        {
            root.QueueFree();
        }
    }

    private static void CollectMeshes(Node node, Node3D root, List<WalkSurfaceObjectMeshPart> list)
    {
        if (node is MeshInstance3D meshInstance && meshInstance.Mesh is { } mesh)
        {
            if (!HasSkeletonAncestor(meshInstance))
            {
                list.Add(new WalkSurfaceObjectMeshPart(mesh, ComputeTransformRelativeToRoot(meshInstance, root)));
            }
        }

        foreach (var child in node.GetChildren())
        {
            CollectMeshes(child, root, list);
        }
    }

    private static bool HasSkeletonAncestor(Node node)
    {
        var parent = node.GetParent();
        while (parent is not null)
        {
            if (parent is Skeleton3D)
            {
                return true;
            }

            parent = parent.GetParent();
        }

        return false;
    }

    private static Transform3D ComputeTransformRelativeToRoot(Node3D node, Node3D root)
    {
        var transform = Transform3D.Identity;
        var current = node;
        while (!ReferenceEquals(current, root) && current is not null)
        {
            transform = current.Transform * transform;
            current = current.GetParent() as Node3D;
        }

        return transform;
    }
}

public readonly struct WalkSurfaceObjectMeshPart
{
    public WalkSurfaceObjectMeshPart(Mesh mesh, Transform3D meshLocalToRoot)
    {
        Mesh = mesh;
        MeshLocalToRoot = meshLocalToRoot;
    }

    public Mesh Mesh { get; }
    public Transform3D MeshLocalToRoot { get; }
}
