using Godot;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Shared GLB mesh bounds and feet-on-ground offset used by <see cref="WorldObject" /> and
///     <see cref="MonsterMultiMeshVisuals" />.
/// </summary>
internal static class GlbVisualGrounding
{
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
