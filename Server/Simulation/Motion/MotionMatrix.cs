using System;
using Godot;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     The reference D3DMATRIX layout: row-major _11.._44, translation in _41.._43. Kept as its own
///     type so TransformPoint, InvertAffineMatrix and PlacementMatrix read like the reference
/// </summary>
public struct MotionMatrix
{
	public float M11;
	public float M12;
	public float M13;
	public float M14;
	public float M21;
	public float M22;
	public float M23;
	public float M24;
	public float M31;
	public float M32;
	public float M33;
	public float M34;
	public float M41;
	public float M42;
	public float M43;
	public float M44;

	public static MotionMatrix Identity () => new ()
	{
		M11 = 1f,
		M22 = 1f,
		M33 = 1f,
		M44 = 1f,
	};

	/// <summary>
	///     The client's own placement matrix, recompile Math.cpp fromEuler, which is the matrix its
	///     renderer draws the model with (Collision.cpp:183). The reference composes
	///     RotationY(-rot.Y) * RotationX(rot.X) * RotationZ(rot.Z) from swapped axis floats, and over
	///     223427 placements 35541 rotate on pitch/roll, where the two orders diverge (worst
	///     Frobenius 3.39), so a server collision box would sit rotated off the drawn mesh. The
	///     yaw-only 91.4% agree either way; the angles here are the .mbd floats in file order
	/// </summary>
	public static MotionMatrix Placement (Vector3 position, Vector3 angles)
	{
		var (sa, ca) = (MathF.Sin (angles.X), MathF.Cos (angles.X));
		var (sb, cb) = (MathF.Sin (angles.Y), MathF.Cos (angles.Y));
		var (sc, cc) = (MathF.Sin (angles.Z), MathF.Cos (angles.Z));
		return new MotionMatrix
		{
			M11 = cc * ca - sb * sa * sc,
			M12 = -sc * cb,
			M13 = -sa * cc - sb * ca * sc,
			M21 = sc * ca + cc * sb * sa,
			M22 = cc * cb,
			M23 = cc * sb * ca - sc * sa,
			M31 = cb * sa,
			M32 = -sb,
			M33 = cb * ca,
			M41 = position.X,
			M42 = position.Y,
			M43 = position.Z,
			M44 = 1f,
		};
	}

	/// <summary>
	///     Reference RotationXMatrix
	/// </summary>
	public static MotionMatrix RotationX (float radians)
	{
		var matrix = Identity ();
		var c = MathF.Cos (radians);
		var s = MathF.Sin (radians);
		matrix.M22 = c;
		matrix.M23 = s;
		matrix.M32 = -s;
		matrix.M33 = c;
		return matrix;
	}

	/// <summary>
	///     Reference RotationYMatrix
	/// </summary>
	public static MotionMatrix RotationY (float radians)
	{
		var matrix = Identity ();
		var c = MathF.Cos (radians);
		var s = MathF.Sin (radians);
		matrix.M11 = c;
		matrix.M13 = -s;
		matrix.M31 = s;
		matrix.M33 = c;
		return matrix;
	}

	/// <summary>
	///     Reference RotationZMatrix
	/// </summary>
	public static MotionMatrix RotationZ (float radians)
	{
		var matrix = Identity ();
		var c = MathF.Cos (radians);
		var s = MathF.Sin (radians);
		matrix.M11 = c;
		matrix.M12 = s;
		matrix.M21 = -s;
		matrix.M22 = c;
		return matrix;
	}

	/// <summary>
	///     Reference MultiplyMatrix, row-major
	/// </summary>
	public static MotionMatrix Multiply (in MotionMatrix left, in MotionMatrix right)
	{
		var outMatrix = new MotionMatrix ();
		var l = new[]
		{
			left.M11, left.M12, left.M13, left.M14,
			left.M21, left.M22, left.M23, left.M24,
			left.M31, left.M32, left.M33, left.M34,
			left.M41, left.M42, left.M43, left.M44,
		};
		var r = new[]
		{
			right.M11, right.M12, right.M13, right.M14,
			right.M21, right.M22, right.M23, right.M24,
			right.M31, right.M32, right.M33, right.M34,
			right.M41, right.M42, right.M43, right.M44,
		};
		var o = new float[16];
		for (var row = 0; row < 4; ++row)
		{
			for (var column = 0; column < 4; ++column)
			{
				o[row * 4 + column] = l[row * 4] * r[column]
					+ l[row * 4 + 1] * r[4 + column]
					+ l[row * 4 + 2] * r[8 + column]
					+ l[row * 4 + 3] * r[12 + column];
			}
		}

		outMatrix.M11 = o[0];
		outMatrix.M12 = o[1];
		outMatrix.M13 = o[2];
		outMatrix.M14 = o[3];
		outMatrix.M21 = o[4];
		outMatrix.M22 = o[5];
		outMatrix.M23 = o[6];
		outMatrix.M24 = o[7];
		outMatrix.M31 = o[8];
		outMatrix.M32 = o[9];
		outMatrix.M33 = o[10];
		outMatrix.M34 = o[11];
		outMatrix.M41 = o[12];
		outMatrix.M42 = o[13];
		outMatrix.M43 = o[14];
		outMatrix.M44 = o[15];
		return outMatrix;
	}

	/// <summary>
	///     Reference InvertAffineMatrix, the 3x3 inverse plus the negated translation
	/// </summary>
	public static bool TryInvert (in MotionMatrix matrix, out MotionMatrix inverse)
	{
		inverse = new MotionMatrix ();
		var determinant = matrix.M11 * (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32)
			- matrix.M12 * (matrix.M21 * matrix.M33 - matrix.M23 * matrix.M31)
			+ matrix.M13 * (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31);
		if (MathF.Abs (determinant) <= 0.000001f)
		{
			return false;
		}

		var invDet = 1f / determinant;
		inverse.M11 = (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32) * invDet;
		inverse.M12 = (matrix.M13 * matrix.M32 - matrix.M12 * matrix.M33) * invDet;
		inverse.M13 = (matrix.M12 * matrix.M23 - matrix.M13 * matrix.M22) * invDet;
		inverse.M21 = (matrix.M23 * matrix.M31 - matrix.M21 * matrix.M33) * invDet;
		inverse.M22 = (matrix.M11 * matrix.M33 - matrix.M13 * matrix.M31) * invDet;
		inverse.M23 = (matrix.M13 * matrix.M21 - matrix.M11 * matrix.M23) * invDet;
		inverse.M31 = (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31) * invDet;
		inverse.M32 = (matrix.M12 * matrix.M31 - matrix.M11 * matrix.M32) * invDet;
		inverse.M33 = (matrix.M11 * matrix.M22 - matrix.M12 * matrix.M21) * invDet;
		inverse.M41 = -(matrix.M41 * inverse.M11 + matrix.M42 * inverse.M21 + matrix.M43 * inverse.M31);
		inverse.M42 = -(matrix.M41 * inverse.M12 + matrix.M42 * inverse.M22 + matrix.M43 * inverse.M32);
		inverse.M43 = -(matrix.M41 * inverse.M13 + matrix.M42 * inverse.M23 + matrix.M43 * inverse.M33);
		inverse.M44 = 1f;
		return true;
	}

	/// <summary>
	///     Godot Basis rows are the same three basis vectors the reference stores in _11.._33, so a
	///     placement reaches the solver frame through this and nothing else
	/// </summary>
	public static MotionMatrix FromBasis (in Transform3D transform)
	{
		var basis = transform.Basis;
		return new MotionMatrix
		{
			M11 = basis.X.X,
			M12 = basis.Y.X,
			M13 = basis.Z.X,
			M14 = 0f,
			M21 = basis.X.Y,
			M22 = basis.Y.Y,
			M23 = basis.Z.Y,
			M24 = 0f,
			M31 = basis.X.Z,
			M32 = basis.Y.Z,
			M33 = basis.Z.Z,
			M34 = 0f,
			M41 = transform.Origin.X,
			M42 = transform.Origin.Y,
			M43 = transform.Origin.Z,
			M44 = 1f,
		};
	}
}
