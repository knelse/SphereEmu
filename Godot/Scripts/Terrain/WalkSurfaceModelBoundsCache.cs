using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Cached GLB mesh bounds for terrain object footprint radii.
/// </summary>
public static class WalkSurfaceModelBoundsCache
{
    public const float DefaultPlantRadiusMeters = 1.5f;
    public const float DefaultRockRadiusMeters = 2.5f;
    public const float DefaultOtherRadiusMeters = 2f;
    public const float DefaultExtraInstancedRadiusMeters = 2f;
    private const float RadiusMarginMeters = 0.25f;

    private static readonly Dictionary<string, ModelBounds?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static float ResolveFootprintRadiusMeters(
        TerrainObjectPlacement placement,
        string modelsDirectory)
    {
        var categoryDefault = placement.Category switch
        {
            TerrainObjectWalkCategory.Plant => DefaultPlantRadiusMeters,
            TerrainObjectWalkCategory.Rock => DefaultRockRadiusMeters,
            TerrainObjectWalkCategory.ExtraInstanced => DefaultExtraInstancedRadiusMeters,
            _ => DefaultOtherRadiusMeters,
        };

        if (!TryResolveFootprintHalfExtents(placement, modelsDirectory, out var halfExtentX, out var halfExtentZ))
        {
            return categoryDefault;
        }

        var circumscribed = Mathf.Sqrt(halfExtentX * halfExtentX + halfExtentZ * halfExtentZ);
        return Mathf.Max(categoryDefault, circumscribed);
    }

    public static bool TryResolveFootprintHalfExtents(
        TerrainObjectPlacement placement,
        string modelsDirectory,
        out float halfExtentX,
        out float halfExtentZ)
    {
        halfExtentX = halfExtentZ = 0f;
        if (!TryGetUnscaledFootprintHalfExtents(
                placement.ObjectName,
                modelsDirectory,
                placement.Category,
                out var unscaledHalfExtentX,
                out var unscaledHalfExtentZ))
        {
            return false;
        }

        var basis = placement.WorldTransform.Basis;
        var scaleX = basis.Column0.Length();
        var scaleZ = basis.Column2.Length();
        halfExtentX = unscaledHalfExtentX * scaleX;
        halfExtentZ = unscaledHalfExtentZ * scaleZ;
        return true;
    }

    public static bool TryGetUnscaledFootprintHalfExtents(
        string objectName,
        string modelsDirectory,
        TerrainObjectWalkCategory category,
        out float halfExtentX,
        out float halfExtentZ)
    {
        halfExtentX = halfExtentZ = 0f;
        var categoryDefault = category switch
        {
            TerrainObjectWalkCategory.Plant => DefaultPlantRadiusMeters,
            TerrainObjectWalkCategory.Rock => DefaultRockRadiusMeters,
            TerrainObjectWalkCategory.ExtraInstanced => DefaultExtraInstancedRadiusMeters,
            _ => DefaultOtherRadiusMeters,
        };

        if (!TryGetModelBounds(objectName, modelsDirectory, out var bounds))
        {
            halfExtentX = halfExtentZ = categoryDefault;
            return true;
        }

        halfExtentX = Mathf.Max(categoryDefault, bounds.LocalExtentX + RadiusMarginMeters);
        halfExtentZ = Mathf.Max(categoryDefault, bounds.LocalExtentZ + RadiusMarginMeters);
        return true;
    }

    private static bool TryGetModelBounds(string objectName, string modelsDirectory, out ModelBounds bounds)
    {
        bounds = default;
        if (Cache.TryGetValue(objectName, out var cached))
        {
            if (!cached.HasValue)
            {
                return false;
            }

            bounds = cached.Value;
            return true;
        }

        var computed = TryComputeModelBounds(objectName, modelsDirectory);
        Cache[objectName] = computed;
        if (!computed.HasValue)
        {
            return false;
        }

        bounds = computed.Value;
        return true;
    }

    private static ModelBounds? TryComputeModelBounds(string objectName, string modelsDirectory)
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
            if (!TryGetCombinedLocalBounds(root, out var combined))
            {
                return null;
            }

            return new ModelBounds(combined.Size.X * 0.5f, combined.Size.Z * 0.5f);
        }
        finally
        {
            root.QueueFree();
        }
    }

    private static bool TryGetCombinedLocalBounds(Node3D root, out Aabb combined)
    {
        combined = default;
        var found = false;

        foreach (var child in root.FindChildren("*", "MeshInstance3D", recursive: true, owned: false))
        {
            if (child is not MeshInstance3D meshInstance || meshInstance.Mesh is null)
            {
                continue;
            }

            if (HasSkeletonAncestor(meshInstance))
            {
                continue;
            }

            var localAabb = meshInstance.GetAabb();
            if (localAabb.Size == Vector3.Zero)
            {
                localAabb = meshInstance.Mesh.GetAabb();
            }

            if (localAabb.Size == Vector3.Zero)
            {
                continue;
            }

            var toRoot = ComputeTransformRelativeToRoot(meshInstance, root);
            var transformed = TransformAabb(toRoot, localAabb);
            combined = found ? combined.Merge(transformed) : transformed;
            found = true;
        }

        return found;
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

    private static Aabb TransformAabb(Transform3D transform, Aabb aabb)
    {
        var p = aabb.Position;
        var s = aabb.Size;
        var corners = new[]
        {
            new Vector3(p.X, p.Y, p.Z),
            new Vector3(p.X + s.X, p.Y, p.Z),
            new Vector3(p.X, p.Y + s.Y, p.Z),
            new Vector3(p.X, p.Y, p.Z + s.Z),
            new Vector3(p.X + s.X, p.Y + s.Y, p.Z),
            new Vector3(p.X + s.X, p.Y, p.Z + s.Z),
            new Vector3(p.X, p.Y + s.Y, p.Z + s.Z),
            new Vector3(p.X + s.X, p.Y + s.Y, p.Z + s.Z),
        };

        var first = transform * corners[0];
        var min = first;
        var max = first;
        for (var i = 1; i < corners.Length; i++)
        {
            var corner = transform * corners[i];
            min = new Vector3(Mathf.Min(min.X, corner.X), Mathf.Min(min.Y, corner.Y), Mathf.Min(min.Z, corner.Z));
            max = new Vector3(Mathf.Max(max.X, corner.X), Mathf.Max(max.Y, corner.Y), Mathf.Max(max.Z, corner.Z));
        }

        return new Aabb(min, max - min);
    }

    private readonly struct ModelBounds(float localExtentX, float localExtentZ)
    {
        public float LocalExtentX { get; } = localExtentX;
        public float LocalExtentZ { get; } = localExtentZ;
    }
}
