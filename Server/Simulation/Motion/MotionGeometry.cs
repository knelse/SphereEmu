using System;
using Godot;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     Reference FBox3 plus the box tests the static queries use
/// </summary>
public struct MotionBounds
{
	public Vector3 Min;
	public Vector3 Max;

	public static MotionBounds Empty () =>
		new (
			new Vector3 (float.MaxValue, float.MaxValue, float.MaxValue),
			new Vector3 (float.MinValue, float.MinValue, float.MinValue));

	public MotionBounds (Vector3 min, Vector3 max)
	{
		Min = min;
		Max = max;
	}

	public readonly bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;

	public readonly bool Contains (Vector3 point) =>
		point.X >= Min.X && point.X <= Max.X
		&& point.Y >= Min.Y && point.Y <= Max.Y
		&& point.Z >= Min.Z && point.Z <= Max.Z;

	public readonly bool Intersects (in MotionBounds other) =>
		IsValid && other.IsValid
		&& Min.X <= other.Max.X && Max.X >= other.Min.X
		&& Min.Y <= other.Max.Y && Max.Y >= other.Min.Y
		&& Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

	public void Expand (Vector3 point)
	{
		Min = new Vector3 (
			MathF.Min (Min.X, point.X),
			MathF.Min (Min.Y, point.Y),
			MathF.Min (Min.Z, point.Z));
		Max = new Vector3 (
			MathF.Max (Max.X, point.X),
			MathF.Max (Max.Y, point.Y),
			MathF.Max (Max.Z, point.Z));
	}

	public readonly float CenterAlong (int axis) => axis switch
	{
		0 => (Min.X + Max.X) * 0.5f,
		1 => (Min.Y + Max.Y) * 0.5f,
		_ => (Min.Z + Max.Z) * 0.5f,
	};

	public readonly float ExtentAlong (int axis) => axis switch
	{
		0 => (Max.X - Min.X) * 0.5f,
		1 => (Max.Y - Min.Y) * 0.5f,
		_ => (Max.Z - Min.Z) * 0.5f,
	};

	/// <summary>
	///     Reference TransformBounds: the transformed center plus the abs-of-basis extent sum
	/// </summary>
	public readonly MotionBounds Transform (in MotionMatrix matrix)
	{
		var center = MotionMath.TransformPoint (
			new Vector3 (CenterAlong (0), CenterAlong (1), CenterAlong (2)), matrix);
		var worldExtentX = MathF.Abs (matrix.M11) * ExtentAlong (0)
			+ MathF.Abs (matrix.M21) * ExtentAlong (1)
			+ MathF.Abs (matrix.M31) * ExtentAlong (2);
		var worldExtentY = MathF.Abs (matrix.M12) * ExtentAlong (0)
			+ MathF.Abs (matrix.M22) * ExtentAlong (1)
			+ MathF.Abs (matrix.M32) * ExtentAlong (2);
		var worldExtentZ = MathF.Abs (matrix.M13) * ExtentAlong (0)
			+ MathF.Abs (matrix.M23) * ExtentAlong (1)
			+ MathF.Abs (matrix.M33) * ExtentAlong (2);
		return new MotionBounds (
			new Vector3 (center.X - worldExtentX, center.Y - worldExtentY, center.Z - worldExtentZ),
			new Vector3 (center.X + worldExtentX, center.Y + worldExtentY, center.Z + worldExtentZ));
	}
}

/// <summary>
///     Reference StaticCollisionTriangle: the three local vertices, their center, their bounds
/// </summary>
public struct MotionTriangle
{
	public Vector3 A;
	public Vector3 B;
	public Vector3 C;
	public Vector3 Center;
	public MotionBounds Bounds;

	// A kind-2 prepared triangle's surface direction, (collision_flags >> 8) & 255: the old client's
	// support_direction, which picks the slide friction. Faces and terrain carry 0
	public byte Direction;

	// A kind-2 prepared triangle's material, collision_flags & 255. Non-zero (a tree canopy, a bush)
	// blocks nothing and only slows the body, as the old client's contact query has it
	public byte Material;
}

/// <summary>
///     Reference StaticCollisionBvhNode. A node with Count != 0 is a leaf over TriangleOrder
/// </summary>
public struct MotionBvhNode
{
	public MotionBounds Bounds;
	public uint First;
	public uint Count;
	public uint Left;
	public uint Right;

	public readonly bool IsLeaf => Count != 0;
}

/// <summary>
///     Reference SegmentIntersectsTriangle, SegmentSegmentDistanceSquared, ClosestPointOnTriangle,
///     SegmentTriangleDistanceSquared and PointTriangleDistanceSquared
/// </summary>
public static class MotionGeometry
{
	/// <summary>
	///     Reference PointInTriangleXz, the 2D test the floor probe uses
	/// </summary>
	public static bool PointInTriangleXz (float px, float pz, Vector3 a, Vector3 b, Vector3 c)
	{
		var d1 = (px - b.X) * (a.Z - b.Z) - (a.X - b.X) * (pz - b.Z);
		var d2 = (px - c.X) * (b.Z - c.Z) - (b.X - c.X) * (pz - c.Z);
		var d3 = (px - a.X) * (c.Z - a.Z) - (c.X - a.X) * (pz - a.Z);
		var negative = d1 < -0.0001f || d2 < -0.0001f || d3 < -0.0001f;
		var positive = d1 > 0.0001f || d2 > 0.0001f || d3 > 0.0001f;
		return !(negative && positive);
	}

	/// <summary>
	///     Reference ClosestPointOnTriangle, the barycentric region walk
	/// </summary>
	public static Vector3 ClosestPointOnTriangle (Vector3 point, Vector3 a, Vector3 b, Vector3 c)
	{
		var ab = MotionMath.Subtract (b, a);
		var ac = MotionMath.Subtract (c, a);
		var ap = MotionMath.Subtract (point, a);
		var d1 = MotionMath.Dot (ab, ap);
		var d2 = MotionMath.Dot (ac, ap);
		if (d1 <= 0f && d2 <= 0f)
		{
			return a;
		}

		var bp = MotionMath.Subtract (point, b);
		var d3 = MotionMath.Dot (ab, bp);
		var d4 = MotionMath.Dot (ac, bp);
		if (d3 >= 0f && d4 <= d3)
		{
			return b;
		}

		var vc = d1 * d4 - d3 * d2;
		if (vc <= 0f && d1 >= 0f && d3 <= 0f)
		{
			return MotionMath.Add (a, MotionMath.Scale (ab, d1 / (d1 - d3)));
		}

		var cp = MotionMath.Subtract (point, c);
		var d5 = MotionMath.Dot (ab, cp);
		var d6 = MotionMath.Dot (ac, cp);
		if (d6 >= 0f && d5 <= d6)
		{
			return c;
		}

		var vb = d5 * d2 - d1 * d6;
		if (vb <= 0f && d2 >= 0f && d6 <= 0f)
		{
			return MotionMath.Add (a, MotionMath.Scale (ac, d2 / (d2 - d6)));
		}

		var va = d3 * d6 - d5 * d4;
		if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
		{
			return MotionMath.Add (b,
				MotionMath.Scale (MotionMath.Subtract (c, b), (d4 - d3) / ((d4 - d3) + (d5 - d6))));
		}

		var denominator = 1f / (va + vb + vc);
		return MotionMath.Add (a,
			MotionMath.Add (MotionMath.Scale (ab, vb * denominator), MotionMath.Scale (ac, vc * denominator)));
	}

	/// <summary>
	///     Reference PointTriangleDistanceSquared
	/// </summary>
	public static float PointTriangleDistanceSquared (Vector3 point, Vector3 a, Vector3 b, Vector3 c)
	{
		var closest = ClosestPointOnTriangle (point, a, b, c);
		var delta = MotionMath.Subtract (point, closest);
		return MotionMath.Dot (delta, delta);
	}

	/// <summary>
	///     Reference SegmentIntersectsTriangle, the Mobler-Moller test
	/// </summary>
	public static bool SegmentIntersectsTriangle (Vector3 start, Vector3 end, Vector3 a, Vector3 b,
		Vector3 c, out Vector3 intersection)
	{
		intersection = Vector3.Zero;
		var direction = MotionMath.Subtract (end, start);
		var edge1 = MotionMath.Subtract (b, a);
		var edge2 = MotionMath.Subtract (c, a);
		var p = MotionMath.Cross (direction, edge2);
		var determinant = MotionMath.Dot (edge1, p);
		if (MathF.Abs (determinant) <= 0.0000001f)
		{
			return false;
		}

		var invDet = 1f / determinant;
		var t = MotionMath.Subtract (start, a);
		var u = MotionMath.Dot (t, p) * invDet;
		if (u < 0f || u > 1f)
		{
			return false;
		}

		var q = MotionMath.Cross (t, edge1);
		var v = MotionMath.Dot (direction, q) * invDet;
		if (v < 0f || u + v > 1f)
		{
			return false;
		}

		var segmentT = MotionMath.Dot (edge2, q) * invDet;
		if (segmentT < 0f || segmentT > 1f)
		{
			return false;
		}

		intersection = MotionMath.Add (start, MotionMath.Scale (direction, segmentT));
		return true;
	}

	/// <summary>
	///     Reference SegmentSegmentDistanceSquared, the closest pair on two segments
	/// </summary>
	public static float SegmentSegmentDistanceSquared (Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2,
		out Vector3 outA, out Vector3 outB)
	{
		var d1 = MotionMath.Subtract (q1, p1);
		var d2 = MotionMath.Subtract (q2, p2);
		var r = MotionMath.Subtract (p1, p2);
		var a = MotionMath.Dot (d1, d1);
		var e = MotionMath.Dot (d2, d2);
		var f = MotionMath.Dot (d2, r);
		if (a <= 0.0000001f && e <= 0.0000001f)
		{
			outA = p1;
			outB = p2;
			return MotionMath.Dot (r, r);
		}

		var s = 0f;
		var t = 0f;
		if (a <= 0.0000001f)
		{
			t = Math.Clamp (f / e, 0f, 1f);
		}
		else
		{
			var cc = MotionMath.Dot (d1, r);
			if (e <= 0.0000001f)
			{
				s = Math.Clamp (-cc / a, 0f, 1f);
			}
			else
			{
				var bb = MotionMath.Dot (d1, d2);
				var denominator = a * e - bb * bb;
				if (denominator > 0.0000001f)
				{
					s = Math.Clamp ((bb * f - cc * e) / denominator, 0f, 1f);
				}

				t = (bb * s + f) / e;
				if (t < 0f)
				{
					t = 0f;
					s = Math.Clamp (-cc / a, 0f, 1f);
				}
				else if (t > 1f)
				{
					t = 1f;
					s = Math.Clamp ((bb - cc) / a, 0f, 1f);
				}
			}
		}

		outA = MotionMath.Add (p1, MotionMath.Scale (d1, s));
		outB = MotionMath.Add (p2, MotionMath.Scale (d2, t));
		var delta = MotionMath.Subtract (outA, outB);
		return MotionMath.Dot (delta, delta);
	}

	/// <summary>
	///     Reference SegmentTriangleDistanceSquared: an intersection is 0, else the best of the two
	///     endpoint-to-triangle distances and the three segment-to-edge distances
	/// </summary>
	public static float SegmentTriangleDistanceSquared (Vector3 start, Vector3 end, Vector3 a,
		Vector3 b, Vector3 c, out Vector3 segmentPoint, out Vector3 trianglePoint)
	{
		if (SegmentIntersectsTriangle (start, end, a, b, c, out var intersection))
		{
			segmentPoint = intersection;
			trianglePoint = intersection;
			return 0f;
		}

		segmentPoint = start;
		trianglePoint = ClosestPointOnTriangle (start, a, b, c);
		var first = MotionMath.Subtract (segmentPoint, trianglePoint);
		var best = MotionMath.Dot (first, first);

		var endTriangle = ClosestPointOnTriangle (end, a, b, c);
		var endDelta = MotionMath.Subtract (end, endTriangle);
		var endDistance = MotionMath.Dot (endDelta, endDelta);
		if (endDistance < best)
		{
			best = endDistance;
			segmentPoint = end;
			trianglePoint = endTriangle;
		}

		Span<(Vector3, Vector3)> edges = stackalloc (Vector3, Vector3)[3];
		edges[0] = (a, b);
		edges[1] = (b, c);
		edges[2] = (c, a);
		for (var index = 0; index < 3; ++index)
		{
			var edge = edges[index];
			var distance = SegmentSegmentDistanceSquared (start, end, edge.Item1, edge.Item2,
				out var edgeSegment, out var edgeTriangle);
			if (distance < best)
			{
				best = distance;
				segmentPoint = edgeSegment;
				trianglePoint = edgeTriangle;
			}
		}

		return best;
	}
}