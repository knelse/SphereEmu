using System;
using System.Collections.Generic;
using System.IO;
using SphServer.Shared.Logger;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     The client's landscape\contours.bin and its Contours::typeAt: typed XZ polygons in the game
///     frame. The jump reads the surface type under the feet from it
/// </summary>
public sealed class MotionContours
{
	// The client's jumpSurface range and the surface whose jump height is restricted to x2
	public const int JumpSurfaceFirst = 1000;
	public const int JumpSurfaceLast = 1100;
	public const int ReducedJumpSurface = 1037;

	private const int MaxVertices = 64;
	private const int RecordBytes = 24 + 4 * MaxVertices * 4;

	private sealed class Contour
	{
		public int Type;
		public float MinX;
		public float MaxX;
		public float MinZ;
		public float MaxZ;
		public float[] X = [];
		public float[] Z = [];
	}

	private readonly List<Contour> records = [];

	private static MotionContours? shared;

	public static MotionContours Shared => shared ??= Load ();

	private static MotionContours Load ()
	{
		var contours = new MotionContours ();
		var path = MotionAssetSource.Absolute (Path.Combine (MotionAssetSource.LandscapeRoot, "contours.bin"));
		if (!File.Exists (path))
		{
			SphLogger.Info ($"MotionContours: missing {path}");
			return contours;
		}

		var bytes = File.ReadAllBytes (path);
		var count = bytes.Length >= 4 ? BitConverter.ToInt32 (bytes, 0) : -1;
		if (count < 0 || count > (bytes.Length - 4) / RecordBytes)
		{
			SphLogger.Info ($"MotionContours: truncated {path}");
			return contours;
		}

		for (var index = 0; index < count; ++index)
		{
			var at = 4 + index * RecordBytes;
			var vertexCount = BitConverter.ToInt32 (bytes, at + 4);
			if (vertexCount < 0 || vertexCount > MaxVertices)
			{
				SphLogger.Info ($"MotionContours: invalid vertex count in {path}");
				contours.records.Clear ();
				return contours;
			}

			var contour = new Contour
			{
				Type = BitConverter.ToInt32 (bytes, at),
				MinX = BitConverter.ToSingle (bytes, at + 8),
				MaxX = BitConverter.ToSingle (bytes, at + 12),
				MinZ = BitConverter.ToSingle (bytes, at + 16),
				MaxZ = BitConverter.ToSingle (bytes, at + 20),
				X = new float[vertexCount],
				Z = new float[vertexCount],
			};
			// Columns of 64: x, z, then the two neighbour columns the server does not need
			for (var vertex = 0; vertex < vertexCount; ++vertex)
			{
				contour.X[vertex] = BitConverter.ToSingle (bytes, at + 24 + vertex * 4);
				contour.Z[vertex] = BitConverter.ToSingle (bytes, at + 24 + MaxVertices * 4 + vertex * 4);
			}

			contours.records.Add (contour);
		}

		SphLogger.Info ($"MotionContours: {contours.records.Count} contours");
		return contours;
	}

	/// <summary>
	///     Contours::typeAt: the records from the first of type >= first up to the first past last,
	///     the last one containing the point wins, -1 when none does
	/// </summary>
	public int TypeAt (float x, float z, int first, int last)
	{
		var begin = records.FindIndex (contour => contour.Type >= first);
		if (begin < 0 || records[begin].Type > last)
		{
			return -1;
		}

		var end = records.FindIndex (begin, contour => contour.Type > last);
		if (end < 0)
		{
			end = records.Count;
		}

		for (var index = end - 1; index >= begin; --index)
		{
			if (Contains (records[index], x, z))
			{
				return records[index].Type;
			}
		}

		return -1;
	}

	// Contour::contains: crossings of the edges below the point, with the same float roundings
	private static bool Contains (Contour contour, float pointX, float pointZ)
	{
		if (pointX < contour.MinX || pointX > contour.MaxX || pointZ < contour.MinZ || pointZ > contour.MaxZ)
		{
			return false;
		}

		var crossings = 0u;
		var count = contour.X.Length;
		for (var current = 0; current < count; ++current)
		{
			var left = current;
			var right = current == 0 ? count - 1 : current - 1;
			if (contour.X[left] == contour.X[right])
			{
				continue;
			}

			if (contour.X[right] < contour.X[left])
			{
				(left, right) = (right, left);
			}

			double leftX = contour.X[left];
			double rightX = contour.X[right];
			if (leftX >= pointX || rightX < pointX)
			{
				continue;
			}

			double rightZ = contour.Z[right];
			var roundedSlope = (float) ((rightZ - contour.Z[left]) / (rightX - leftX));
			double slope = roundedSlope;
			var intercept = (float) (contour.Z[left] - leftX * slope);
			var edgeZ = (float) (slope * pointX + intercept);
			if (edgeZ > pointZ)
			{
				++crossings;
			}
		}

		return (crossings & 1u) != 0;
	}
}
