using System;
using Godot;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     Reference StaticCollisionMesh, BuildStaticCollisionMesh and BuildCollisionBvhNode: the
///     model's local triangles, their order array, and the median-split BVH over them
/// </summary>
public sealed class MotionCollisionMesh
{
	public MotionTriangle[] Triangles = [];
	public int[] Order = [];
	public MotionBvhNode[] Nodes = [];

	// Node index while the pre-order split appends
	private int nextNode;

	public static MotionCollisionMesh? Build (Vector3[] vertices, List<int> indices)
	{
		if (indices.Count == 0)
		{
			return null;
		}

		var triangles = new List<MotionTriangle> (indices.Count / 3);
		for (var offset = 0; offset + 2 < indices.Count; offset += 3)
		{
			var ia = indices[offset];
			var ib = indices[offset + 1];
			var ic = indices[offset + 2];
			if (ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
			{
				continue;
			}

			var triangle = new MotionTriangle { A = vertices[ia], B = vertices[ib], C = vertices[ic] };
			var normal = MotionMath.Cross (
				MotionMath.Subtract (triangle.B, triangle.A), MotionMath.Subtract (triangle.C, triangle.A));
			if (MotionMath.Dot (normal, normal) <= 0.0000001f)
			{
				continue;
			}

			triangle.Center = MotionMath.Scale (
				MotionMath.Add (MotionMath.Add (triangle.A, triangle.B), triangle.C), 1f / 3f);
			triangle.Bounds = MotionBounds.Empty ();
			triangle.Bounds.Expand (triangle.A);
			triangle.Bounds.Expand (triangle.B);
			triangle.Bounds.Expand (triangle.C);
			triangles.Add (triangle);
		}

		if (triangles.Count == 0)
		{
			return null;
		}

		var mesh = new MotionCollisionMesh
		{
			Triangles = triangles.ToArray (),
			Order = new int[triangles.Count],
			// A median split of n leaves makes 2n-1 nodes
			Nodes = new MotionBvhNode[triangles.Count * 2],
		};
		for (var index = 0; index < mesh.Order.Length; ++index)
		{
			mesh.Order[index] = index;
		}

		mesh.BuildNode (0, mesh.Order.Length);
		Array.Resize (ref mesh.Nodes, mesh.nextNode);
		return mesh;
	}

	/// <summary>
	///     Reference BuildCollisionBvhNode: a leaf at 8 triangles, else split on the widest center
	///     axis at the median. The reference nth_elements, a full sort here partitions the same,
	///     and every query bounds-tests what a leaf hands it, so the result is the same
	/// </summary>
	private void BuildNode (int first, int count)
	{
		var nodeIndex = nextNode++;
		var node = new MotionBvhNode { Bounds = MotionBounds.Empty () };
		var centerBounds = MotionBounds.Empty ();
		for (var offset = 0; offset < count; ++offset)
		{
			var triangle = Triangles[Order[first + offset]];
			node.Bounds.Expand (triangle.Bounds.Min);
			node.Bounds.Expand (triangle.Bounds.Max);
			centerBounds.Expand (triangle.Center);
		}

		if (count <= 8)
		{
			node.First = (uint) first;
			node.Count = (uint) count;
			Nodes[nodeIndex] = node;
			return;
		}

		var extentX = centerBounds.Max.X - centerBounds.Min.X;
		var extentY = centerBounds.Max.Y - centerBounds.Min.Y;
		var extentZ = centerBounds.Max.Z - centerBounds.Min.Z;
		var axis = extentX >= extentY && extentX >= extentZ ? 0 : extentY >= extentZ ? 1 : 2;
		var leftCount = count / 2;

		// Order is partitioned so each half is contiguous, which the recursion relies on
		SortOrder (first, count, axis);
		Nodes[nodeIndex] = node;
		var leftIndex = nextNode;
		BuildNode (first, leftCount);
		var rightIndex = nextNode;
		BuildNode (first + leftCount, count - leftCount);
		Nodes[nodeIndex].Left = (uint) leftIndex;
		Nodes[nodeIndex].Right = (uint) rightIndex;
	}

	private void SortOrder (int first, int count, int axis)
	{
		var slice = new int[count];
		Array.Copy (Order, first, slice, 0, count);
		Array.Sort (slice, (left, right) => AxisValue (Triangles[left].Center, axis)
			.CompareTo (AxisValue (Triangles[right].Center, axis)));
		Array.Copy (slice, 0, Order, first, count);
	}

	private static float AxisValue (Vector3 value, int axis) => axis switch
	{
		0 => value.X,
		1 => value.Y,
		_ => value.Z,
	};
}
