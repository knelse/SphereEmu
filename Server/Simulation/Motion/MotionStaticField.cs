using System;
using System.Collections.Generic;
using Godot;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     Reference static collision: the placed models, their 8-unit cell grid, the BVH triangle
///     query, CapsuleOverlapsStatic and StaticFloorHeightAt. Local triangles stay local, the
///     instance's world matrix maps them at query time, which is what the reference does
/// </summary>
public sealed class MotionStaticField
{
	private struct Instance
	{
		public MotionModelResource Resource;
		public MotionCollisionMesh? Mesh;
		public MotionMatrix World;
		public MotionMatrix InverseWorld;
		public MotionBounds Bounds;
		public float CenterX;
		public float CenterZ;
		public float Radius;
		public bool Capsule;
	}

	// Reference StaticCollisionCells, StaticCollisionInstances and the large-instance overflow list
	private readonly Dictionary<long, List<int>> cells = [];
	private readonly List<Instance> instances = [];
	private readonly List<Instance> dynamicInstances = [];
	private readonly List<int> largeInstances = [];
	private readonly HashSet<string> missingModels = new (StringComparer.OrdinalIgnoreCase);

	// Reference StaticCollisionVisitMarks/Generation, so one query visits an instance once
	private int[] visitMarks = [];
	private int visitGeneration;

	private readonly List<int> instanceScratch = [];
	private readonly List<int> triangleScratch = [];

	public int LoadedCount => instances.Count;
	public int LoadedCellCount => cells.Count;

	// The model behind the deepest hit of the last CapsuleOverlapsStatic, for the motion trace
	public string LastOverlapModel { get; private set; } = "";

	public bool HasMissingModel (string modelName) => missingModels.Contains (modelName);

	public void NoteMissingModel (string modelName) => missingModels.Add (modelName);

	/// <summary>
	///     Reference static collision build. A model under 0.10 in every dimension blocks nothing, a
	///     skinned one is a cylinder at its origin, and an instance spanning over 256 cells goes to
	///     the always-scanned list instead of the grid
	/// </summary>
	public void Add (MotionModelResource resource, in MotionMatrix world)
	{
		var bounds = boundsOf (resource, world);
		if (!bounds.IsValid)
		{
			return;
		}

		var width = bounds.Max.X - bounds.Min.X;
		var depth = bounds.Max.Z - bounds.Min.Z;
		var height = bounds.Max.Y - bounds.Min.Y;
		if (Math.Max (width, Math.Max (depth, height)) < 0.10f)
		{
			return;
		}

		var instance = new Instance
		{
			Resource = resource,
			Mesh = resource.CollisionMesh,
			World = world,
			Bounds = bounds,
			Capsule = resource.IsSkinned,
		};

		if (instance.Capsule)
		{
			instance.CenterX = world.M41;
			instance.CenterZ = world.M43;
			instance.Radius = MotionConfig.SkinnedProxyRadius (width, depth);
			instance.Bounds.Min.X = instance.CenterX - instance.Radius;
			instance.Bounds.Max.X = instance.CenterX + instance.Radius;
			instance.Bounds.Min.Z = instance.CenterZ - instance.Radius;
			instance.Bounds.Max.Z = instance.CenterZ + instance.Radius;
		}
		else if (resource.CollisionMesh is null
			|| !MotionMatrix.TryInvert (world, out instance.InverseWorld))
		{
			return;
		}

		var instanceIndex = instances.Count;
		instances.Add (instance);

		var inflate = MotionConfig.PlayerCollisionRadius + MotionConfig.CollisionSkin;
		var minCellX = CellCoord (instance.Bounds.Min.X - inflate);
		var maxCellX = CellCoord (instance.Bounds.Max.X + inflate);
		var minCellZ = CellCoord (instance.Bounds.Min.Z - inflate);
		var maxCellZ = CellCoord (instance.Bounds.Max.Z + inflate);
		var cellCount = (long) (maxCellX - minCellX + 1) * (maxCellZ - minCellZ + 1);
		if (cellCount > 256)
		{
			largeInstances.Add (instanceIndex);
			return;
		}

		for (var cellZ = minCellZ; cellZ <= maxCellZ; ++cellZ)
		{
			for (var cellX = minCellX; cellX <= maxCellX; ++cellX)
			{
				if (!cells.TryGetValue (CellKey (cellX, cellZ), out var list))
				{
					list = [];
					cells[CellKey (cellX, cellZ)] = list;
				}

				list.Add (instanceIndex);
			}
		}
	}

	/// <summary>
	///     The dynamic objects around the player being stepped, replaced each step. They take the
	///     resource's DynamicMesh, the client contact query's shape, not the static collision mesh
	/// </summary>
	public void ClearDynamic () => dynamicInstances.Clear ();

	public void AddDynamic (MotionModelResource resource, in MotionMatrix world)
	{
		if (resource.DynamicMesh is not { } mesh || !MotionMatrix.TryInvert (world, out var inverse))
		{
			return;
		}

		dynamicInstances.Add (new Instance
		{
			Resource = resource,
			Mesh = mesh,
			World = world,
			InverseWorld = inverse,
			Bounds = resource.DynamicBounds.Transform (world),
		});
	}

	// Dynamic instances ride the same scratch as statics under a complemented index
	private Instance InstanceAt (int index) => index >= 0 ? instances[index] : dynamicInstances[~index];

	public void Clear ()
	{
		cells.Clear ();
		instances.Clear ();
		dynamicInstances.Clear ();
		largeInstances.Clear ();
		visitMarks = [];
		visitGeneration = 0;
	}

	/// <summary>
	///     The model bounds in the placement frame, the reference's TransformBounds of the resource
	///     bounds by the placement world matrix
	/// </summary>
	private static MotionBounds boundsOf (MotionModelResource resource, in MotionMatrix world) =>
		resource.Bounds.Transform (world);

	/// <summary>
	///     Reference StaticRenderCellCoord, a floor divide so negative coords land in the right cell
	/// </summary>
	private static int CellCoord (float value)
	{
		var scaled = value / MotionConfig.StaticCollisionCellSize;
		var floor = (int) MathF.Floor (scaled);
		return floor;
	}

	private static long CellKey (int cellX, int cellZ) => ((long) cellX << 32) | (uint) cellZ;

	/// <summary>
	///     Reference QueryStaticCollisionInstances
	/// </summary>
	private void QueryInstances (MotionBounds area)
	{
		instanceScratch.Clear ();
		if (!area.IsValid)
		{
			return;
		}

		for (var index = 0; index < dynamicInstances.Count; ++index)
		{
			if (dynamicInstances[index].Bounds.Intersects (area))
			{
				instanceScratch.Add (~index);
			}
		}

		if (instances.Count == 0)
		{
			return;
		}

		++visitGeneration;
		if (visitGeneration == 0)
		{
			visitMarks = new int[instances.Count];
			visitGeneration = 1;
		}

		if (visitMarks.Length < instances.Count)
		{
			Array.Resize (ref visitMarks, instances.Count);
		}

		var minCellX = CellCoord (area.Min.X);
		var maxCellX = CellCoord (area.Max.X);
		var minCellZ = CellCoord (area.Min.Z);
		var maxCellZ = CellCoord (area.Max.Z);

		void Visit (int instanceIndex)
		{
			if ((uint) instanceIndex >= instances.Count || visitMarks[instanceIndex] == visitGeneration)
			{
				return;
			}

			visitMarks[instanceIndex] = visitGeneration;
			if (instances[instanceIndex].Bounds.Intersects (area))
			{
				instanceScratch.Add (instanceIndex);
			}
		}

		for (var cellZ = minCellZ; cellZ <= maxCellZ; ++cellZ)
		{
			for (var cellX = minCellX; cellX <= maxCellX; ++cellX)
			{
				if (!cells.TryGetValue (CellKey (cellX, cellZ), out var list))
				{
					continue;
				}

				foreach (var instanceIndex in list)
				{
					Visit (instanceIndex);
				}
			}
		}

		foreach (var instanceIndex in largeInstances)
		{
			Visit (instanceIndex);
		}
	}

	/// <summary>
	///     Reference QueryStaticCollisionTriangles: the area goes to local space through the inverse
	///     placement matrix, then the BVH descends with a fixed stack
	/// </summary>
	private void QueryTriangles (in Instance instance, in MotionBounds area)
	{
		triangleScratch.Clear ();
		var mesh = instance.Mesh;
		if (mesh is null || mesh.Nodes.Length == 0)
		{
			return;
		}

		var localArea = area.Transform (instance.InverseWorld);
		Span<uint> stack = stackalloc uint[64];
		var stackSize = 1;
		stack[0] = 0;
		while (stackSize != 0)
		{
			var nodeIndex = stack[--stackSize];
			if (nodeIndex >= mesh.Nodes.Length)
			{
				continue;
			}

			ref var node = ref mesh.Nodes[nodeIndex];
			if (!node.Bounds.Intersects (localArea))
			{
				continue;
			}

			if (node.IsLeaf)
			{
				for (var offset = 0u; offset < node.Count; ++offset)
				{
					// The leaf indexes the order array, the reference resolves it to the triangle
					var triangleIndex = mesh.Order[(int) node.First + (int) offset];
					if (mesh.Triangles[triangleIndex].Bounds.Intersects (localArea))
					{
						triangleScratch.Add (triangleIndex);
					}
				}

				continue;
			}

			if (stackSize + 2 <= stack.Length)
			{
				stack[stackSize++] = node.Left;
				stack[stackSize++] = node.Right;
			}
		}
	}

	/// <summary>
	///     Reference CapsuleOverlapsStatic. The body is a vertical capsule of PlayerCollisionRadius
	///     from the feet up PlayerCollisionHeight, and IgnoreSupportingFloor skips the face the body
	///     is standing on so the floor it rests on is not a block
	/// </summary>
	public bool CapsuleOverlapsStatic (float x, float feetY, float z, out MotionCollisionHit hit,
		bool ignoreSupportingFloor = true)
	{
		hit = default;
		LastOverlapModel = "";
		var skin = Math.Clamp (MotionConfig.CollisionSkin, 0f, MotionConfig.PlayerCollisionRadius * 0.5f);
		var radius = Math.Max (0.05f, MotionConfig.PlayerCollisionRadius - skin);
		var topY = feetY - MotionConfig.PlayerCollisionHeight;
		var segmentTopY = topY + radius;
		var segmentBottomY = feetY - radius;
		if (segmentTopY > segmentBottomY)
		{
			var middle = (topY + feetY) * 0.5f;
			segmentTopY = middle;
			segmentBottomY = middle;
		}

		var segmentStart = new Vector3 (x, segmentTopY, z);
		var segmentEnd = new Vector3 (x, segmentBottomY, z);
		var area = new MotionBounds (
			new Vector3 (x - radius, topY, z - radius),
			new Vector3 (x + radius, feetY, z + radius));
		QueryInstances (area);

		var overlap = false;
		var best = default (MotionCollisionHit);
		foreach (var instanceIndex in instanceScratch)
		{
			var instance = InstanceAt (instanceIndex);
			if (instance.Capsule)
			{
				if (feetY < instance.Bounds.Min.Y || topY > instance.Bounds.Max.Y)
				{
					continue;
				}

				var deltaX = x - instance.CenterX;
				var deltaZ = z - instance.CenterZ;
				var limit = radius + instance.Radius;
				var distanceSquared = deltaX * deltaX + deltaZ * deltaZ;
				if (distanceSquared >= limit * limit)
				{
					continue;
				}

				var distance = MathF.Sqrt (Math.Max (distanceSquared, 0.0000001f));
				var penetration = limit - distance;
				if (!overlap || penetration > best.Penetration)
				{
					best.Normal = new Vector3 (deltaX / distance, 0f, deltaZ / distance);
					best.Penetration = penetration;
					LastOverlapModel = instance.Resource.Name;
				}

				overlap = true;
				continue;
			}

			QueryTriangles (instance, area);
			var mesh = instance.Mesh!;
			foreach (var triangleIndex in triangleScratch)
			{
				var local = mesh.Triangles[triangleIndex];
				if (local.Material != 0)
				{
					continue;
				}

				var a = MotionMath.TransformPoint (local.A, instance.World);
				var b = MotionMath.TransformPoint (local.B, instance.World);
				var c = MotionMath.TransformPoint (local.C, instance.World);
				var triangleNormal = MotionMath.Cross (
					MotionMath.Subtract (b, a), MotionMath.Subtract (c, a));
				var normalLengthSquared = MotionMath.Dot (triangleNormal, triangleNormal);
				if (normalLengthSquared <= 0.0000001f)
				{
					continue;
				}

				triangleNormal = MotionMath.Scale (triangleNormal, 1f / MathF.Sqrt (normalLengthSquared));
				var walkable = MathF.Abs (triangleNormal.Y) >= MotionConfig.CollisionFloorNormalThreshold;
				var supportingRegionTopY = feetY - radius - skin;
				var supportsCenter = false;
				if (ignoreSupportingFloor && walkable && MathF.Abs (triangleNormal.Y) > 0.0001f
					&& MotionGeometry.PointInTriangleXz (x, z, a, b, c))
				{
					var surfaceY = a.Y
						- (triangleNormal.X * (x - a.X) + triangleNormal.Z * (z - a.Z)) / triangleNormal.Y;
					supportsCenter = surfaceY >= feetY - skin * 2f && surfaceY <= feetY + skin * 2f;
				}

				var distanceSquared = MotionGeometry.SegmentTriangleDistanceSquared (
					segmentStart, segmentEnd, a, b, c, out var segmentPoint, out var trianglePoint);
				if (distanceSquared >= radius * radius)
				{
					continue;
				}

				var contactDistance = MathF.Sqrt (Math.Max (distanceSquared, 0f));
				var normal = MotionMath.Subtract (segmentPoint, trianglePoint);
				if (contactDistance > 0.00001f)
				{
					normal = MotionMath.Scale (normal, 1f / contactDistance);
				}
				else
				{
					var center = new Vector3 (x, (topY + feetY) * 0.5f, z);
					if (MotionMath.Dot (triangleNormal, MotionMath.Subtract (center, a)) < 0f)
					{
						triangleNormal = MotionMath.Scale (triangleNormal, -1f);
					}

					normal = triangleNormal;
				}

				var lowerSupportContact = trianglePoint.Y >= supportingRegionTopY
					&& trianglePoint.Y <= feetY + skin * 2f && normal.Y <= -0.05f;
				if (ignoreSupportingFloor && walkable && (supportsCenter || lowerSupportContact))
				{
					continue;
				}

				var trianglePenetration = radius - contactDistance;
				if (!overlap || trianglePenetration > best.Penetration)
				{
					best.Normal = normal;
					best.Penetration = trianglePenetration;
					LastOverlapModel = instance.Resource.Name;
				}

				overlap = true;
			}
		}

		hit = best;
		return overlap;
	}

	/// <summary>
	///     Reference StaticFloorHeightAt: the lowest surface Y (highest surface) whose XZ triangle
	///     covers the point, its plane evaluated at the point, inside the Y window
	/// </summary>
	/// <summary>
	///     The old client's Material query: the lowest material among the material triangles the
	///     body touches, NoSurfaceMaterial when it touches none. Movement scales by material / 6
	/// </summary>
	public int SurfaceMaterialAt (float x, float feetY, float z)
	{
		var skin = Math.Clamp (MotionConfig.CollisionSkin, 0f, MotionConfig.PlayerCollisionRadius * 0.5f);
		var radius = Math.Max (0.05f, MotionConfig.PlayerCollisionRadius - skin);
		var topY = feetY - MotionConfig.PlayerCollisionHeight;
		var segmentStart = new Vector3 (x, Math.Min (topY + radius, (topY + feetY) * 0.5f), z);
		var segmentEnd = new Vector3 (x, Math.Max (feetY - radius, (topY + feetY) * 0.5f), z);
		var area = new MotionBounds (new Vector3 (x - radius, topY, z - radius), new Vector3 (x + radius, feetY, z + radius));
		QueryInstances (area);

		var material = MotionConfig.NoSurfaceMaterial;
		foreach (var instanceIndex in instanceScratch)
		{
			var instance = InstanceAt (instanceIndex);
			if (instance.Capsule)
			{
				continue;
			}

			QueryTriangles (instance, area);
			var mesh = instance.Mesh!;
			foreach (var triangleIndex in triangleScratch)
			{
				var local = mesh.Triangles[triangleIndex];
				if (local.Material == 0 || local.Material >= material)
				{
					continue;
				}

				var a = MotionMath.TransformPoint (local.A, instance.World);
				var b = MotionMath.TransformPoint (local.B, instance.World);
				var c = MotionMath.TransformPoint (local.C, instance.World);
				if (MotionGeometry.SegmentTriangleDistanceSquared (segmentStart, segmentEnd, a, b, c, out _, out _)
					< radius * radius)
				{
					material = local.Material;
				}
			}
		}

		return material;
	}

	public bool StaticFloorHeightAt (float x, float z, float minY, float maxY, out float outY,
		out Vector3 outNormal) =>
		StaticFloorHeightAt (x, z, minY, maxY, out outY, out outNormal, out _);

	/// <summary>
	///     outDirection is the floor triangle's surface direction, the slide friction index
	/// </summary>
	public bool StaticFloorHeightAt (float x, float z, float minY, float maxY, out float outY,
		out Vector3 outNormal, out int outDirection)
	{
		outY = maxY;
		outNormal = new Vector3 (0f, 1f, 0f);
		outDirection = 0;
		var bestDirection = 0;
		const float probeRadius = 0.02f;
		var area = new MotionBounds (
			new Vector3 (x - probeRadius, minY - MotionConfig.CollisionSkin, z - probeRadius),
			new Vector3 (x + probeRadius, maxY + MotionConfig.CollisionSkin, z + probeRadius));
		QueryInstances (area);

		var found = false;
		var bestY = maxY;
		var bestNormal = new Vector3 (0f, 1f, 0f);
		foreach (var instanceIndex in instanceScratch)
		{
			var instance = InstanceAt (instanceIndex);
			if (instance.Capsule)
			{
				continue;
			}

			QueryTriangles (instance, area);
			var mesh = instance.Mesh!;
			foreach (var triangleIndex in triangleScratch)
			{
				var local = mesh.Triangles[triangleIndex];
				if (local.Material != 0)
				{
					continue;
				}

				var a = MotionMath.TransformPoint (local.A, instance.World);
				var b = MotionMath.TransformPoint (local.B, instance.World);
				var c = MotionMath.TransformPoint (local.C, instance.World);
				var normal = MotionMath.Cross (MotionMath.Subtract (b, a), MotionMath.Subtract (c, a));
				if (MotionMath.Dot (normal, normal) <= 0.0000001f)
				{
					continue;
				}

				normal = MotionMath.Scale (normal, 1f / MathF.Sqrt (MotionMath.Dot (normal, normal)));
				if (normal.Y < 0f)
				{
					normal = MotionMath.Scale (normal, -1f);
				}

				if (normal.Y < MotionConfig.CollisionFloorNormalThreshold
					|| !MotionGeometry.PointInTriangleXz (x, z, a, b, c))
				{
					continue;
				}

				var surfaceY = a.Y - (normal.X * (x - a.X) + normal.Z * (z - a.Z)) / normal.Y;
				if (surfaceY < minY || surfaceY > maxY || (found && surfaceY >= bestY))
				{
					continue;
				}

				found = true;
				bestY = surfaceY;
				bestNormal = normal;
				bestDirection = local.Direction;
			}
		}

		if (!found)
		{
			return false;
		}

		outY = bestY;
		outNormal = bestNormal;
		outDirection = bestDirection;
		return true;
	}
}

/// <summary>
///     Reference FGameWorldCollisionHit: the push direction and how deep the body is
/// </summary>
public struct MotionCollisionHit
{
	public Vector3 Normal;
	public float Penetration;
}
