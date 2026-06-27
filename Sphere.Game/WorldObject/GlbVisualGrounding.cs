using System;
using System.Collections.Generic;
using Godot;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Shared GLB mesh bounds and feet-on-ground offset used by <see cref="WorldObject" /> and
///     <see cref="MonsterMultiMeshVisuals" />.
/// </summary>
internal static class GlbVisualGrounding
{
	public const string DefaultModelsDirectory = "res://Godot/Models/";

	private static readonly Dictionary<string, float?> ModelHeightCache = new(StringComparer.OrdinalIgnoreCase);

	public static void ApplyGroundOffset(Node3D glbRoot)
	{
		if (!TryGetCombinedMeshBoundsInRootSpace(glbRoot, out var combined))
		{
			return;
		}

		var minY = combined.Position.Y;
		if (Mathf.Abs(minY) < 0.0001f)
		{
			return;
		}

		glbRoot.Position += new Vector3(0f, -minY, 0f);
	}

	public static float GetSpawnOriginYOffset(string modelName, string modelsDirectory = DefaultModelsDirectory)
	{
		return TryGetModelBoundsHeight(modelName, modelsDirectory, out var height) ? height * 0.5f : 0f;
	}

	public static float GetEditorVisualExtraYOffset(string modelName, string modelsDirectory = DefaultModelsDirectory)
	{
		return TryGetModelBoundsHeight(modelName, modelsDirectory, out var height) ? height * 0.5f : 0f;
	}

	public static bool TryGetModelBoundsHeight(
		string modelName,
		string modelsDirectory,
		out float height)
	{
		height = 0f;
		if (string.IsNullOrWhiteSpace(modelName))
		{
			return false;
		}

		if (ModelHeightCache.TryGetValue(modelName, out var cached))
		{
			if (!cached.HasValue)
			{
				return false;
			}

			height = cached.Value;
			return height > 0f;
		}

		var glbPath = $"{modelsDirectory.TrimEnd('/')}/{modelName}.glb";
		if (!ResourceLoader.Exists(glbPath))
		{
			ModelHeightCache[modelName] = null;
			return false;
		}

		var packed = ResourceLoader.Load<PackedScene>(glbPath);
		if (packed is null)
		{
			ModelHeightCache[modelName] = null;
			return false;
		}

		var glbRoot = packed.Instantiate<Node3D>();
		try
		{
			ApplyGroundOffset(glbRoot);
			if (!TryGetCombinedMeshBoundsInRootSpace(glbRoot, out var combined) || combined.Size.Y <= 0f)
			{
				ModelHeightCache[modelName] = null;
				return false;
			}

			height = combined.Size.Y;
			ModelHeightCache[modelName] = height;
			return true;
		}
		finally
		{
			glbRoot.QueueFree();
		}
	}

	public static bool TryGetCombinedMeshBoundsInRootSpace(Node3D glbRoot, out Aabb combined)
	{
		combined = default;
		var aabbFound = false;

		foreach (var child in glbRoot.FindChildren("*", "MeshInstance3D", recursive: true, owned: false))
		{
			if (child is not MeshInstance3D meshInstance)
			{
				continue;
			}

			if (!TryGetMeshBoundsInRootSpace(meshInstance, glbRoot, out var transformed))
			{
				continue;
			}

			combined = aabbFound ? combined.Merge(transformed) : transformed;
			aabbFound = true;
		}

		return aabbFound;
	}

	private static bool TryGetMeshBoundsInRootSpace(MeshInstance3D meshInstance, Node3D root, out Aabb boundsInRootSpace)
	{
		boundsInRootSpace = default;
		var mesh = meshInstance.Mesh;
		if (mesh is null)
		{
			return false;
		}

		var localAabb = meshInstance.GetAabb();
		if (localAabb.Size == Vector3.Zero)
		{
			localAabb = mesh.GetAabb();
		}

		if (localAabb.Size == Vector3.Zero)
		{
			return false;
		}

		var toRoot = ComputeTransformRelativeToRoot(meshInstance, root);
		boundsInRootSpace = TransformAabb(toRoot, localAabb);
		return true;
	}

	public static Transform3D ComputeTransformRelativeToRoot(Node3D node, Node3D root)
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

	public static Aabb TransformAabb(Transform3D transform, Aabb aabb)
	{
		var position = aabb.Position;
		var size = aabb.Size;
		var corners = new[]
		{
			new Vector3(position.X, position.Y, position.Z),
			new Vector3(position.X + size.X, position.Y, position.Z),
			new Vector3(position.X, position.Y + size.Y, position.Z),
			new Vector3(position.X, position.Y, position.Z + size.Z),
			new Vector3(position.X + size.X, position.Y + size.Y, position.Z),
			new Vector3(position.X + size.X, position.Y, position.Z + size.Z),
			new Vector3(position.X, position.Y + size.Y, position.Z + size.Z),
			new Vector3(position.X + size.X, position.Y + size.Y, position.Z + size.Z),
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
}
