using System;
using System.Runtime.CompilerServices;
using Godot;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     The reference vector helpers, triangle tests and box tests, in float so the server and the
///     client integrator produce the same numbers
/// </summary>
public static class MotionMath
{
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public static float Dot (Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public static Vector3 Cross (Vector3 a, Vector3 b) =>
		new (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public static Vector3 Subtract (Vector3 left, Vector3 right) =>
		new (left.X - right.X, left.Y - right.Y, left.Z - right.Z);

	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public static Vector3 Add (Vector3 left, Vector3 right) =>
		new (left.X + right.X, left.Y + right.Y, left.Z + right.Z);

	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public static Vector3 Scale (Vector3 value, float factor) =>
		new (value.X * factor, value.Y * factor, value.Z * factor);

	/// <summary>
	///     Reference NormalizeVector: a degenerate vector becomes +Y, which the support code reads
	///     as flat floor
	/// </summary>
	public static Vector3 NormalizeVector (Vector3 value)
	{
		var length = MathF.Sqrt (Dot (value, value));
		if (length <= 0.00001f)
		{
			return new Vector3 (0f, 1f, 0f);
		}

		return new Vector3 (value.X / length, value.Y / length, value.Z / length);
	}

	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public static Vector3 TransformPoint (Vector3 point, in MotionMatrix matrix) =>
		new (
			point.X * matrix.M11 + point.Y * matrix.M21 + point.Z * matrix.M31 + matrix.M41,
			point.X * matrix.M12 + point.Y * matrix.M22 + point.Z * matrix.M32 + matrix.M42,
			point.X * matrix.M13 + point.Y * matrix.M23 + point.Z * matrix.M33 + matrix.M43);

	/// <summary>
	///     Reference AxisValue, the BVH split-axis reader
	/// </summary>
	[MethodImpl (MethodImplOptions.AggressiveInlining)]
	public static float AxisValue (Vector3 value, int axis) => axis switch
	{
		0 => value.X,
		1 => value.Y,
		_ => value.Z,
	};

	/// <summary>
	///     Reference BoundsIntersect
	/// </summary>
	public static bool BoundsIntersect (in MotionBounds a, in MotionBounds b) =>
		a.IsValid && b.IsValid
		&& a.Min.X <= b.Max.X && a.Max.X >= b.Min.X
		&& a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y
		&& a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
}