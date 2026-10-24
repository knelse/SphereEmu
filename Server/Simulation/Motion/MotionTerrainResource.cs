using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Godot;
using SphServer.Shared.Logger;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     Reference TerrainCpuResource: one LND tile's raw triangles plus the 32x32 surface grid the
///     support probe walks. Tile-local, the tile origin applied by the instance
/// </summary>
public sealed class MotionTerrainResource
{
	public const int HeaderBytes = 0x68c0;
	public const int VertexBytes = 40;
	public const int TriangleBytes = 28;
	public const int SurfaceGridResolution = 32;

	public Vector3[] Positions = [];
	public uint[] Indices = [];

	/// <summary>
	///     The reference vector-of-vectors SurfaceCells as CSR: 32*32+1 cell start offsets into
	///     SurfaceCellTriangles, so a cell is the range between two neighbours
	/// </summary>
	public int[] SurfaceCellStarts = [];
	public uint[] SurfaceCellTriangles = [];

	public MotionBounds Bounds = MotionBounds.Empty ();

	public static MotionTerrainResource? Build (string lndPath)
	{
		var data = ReadAllBytes (lndPath);
		if (data is null || data.Length < HeaderBytes)
		{
			SphLogger.Info ($"LND header too short: {Path.GetFileName (lndPath)}");
			return null;
		}

		var vertexCount = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (4, 2));
		if (vertexCount == 0)
		{
			SphLogger.Info ($"LND vertex count invalid: {Path.GetFileName (lndPath)}");
			return null;
		}

		var trianglesOffset = HeaderBytes + vertexCount * VertexBytes;
		if (trianglesOffset > data.Length || (data.Length - trianglesOffset) % TriangleBytes != 0)
		{
			SphLogger.Info ($"LND triangle table invalid: {Path.GetFileName (lndPath)}");
			return null;
		}

		var triangleCount = (data.Length - trianglesOffset) / TriangleBytes;
		if (triangleCount == 0)
		{
			SphLogger.Info ($"LND has no triangles: {Path.GetFileName (lndPath)}");
			return null;
		}

		var resource = new MotionTerrainResource ();
		resource.Positions = new Vector3[vertexCount];
		for (var index = 0; index < vertexCount; ++index)
		{
			var position = ReadVector3 (data, HeaderBytes + index * VertexBytes);
			resource.Positions[index] = position;
			resource.Bounds.Expand (position);
		}

		resource.Indices = new uint[triangleCount * 3];
		for (var index = 0; index < triangleCount; ++index)
		{
			var offset = trianglesOffset + index * TriangleBytes;
			resource.Indices[index * 3] = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (offset, 2));
			resource.Indices[index * 3 + 1] = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (offset + 2, 2));
			resource.Indices[index * 3 + 2] = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (offset + 4, 2));
		}

		resource.BuildSurfaceGrid (MotionConfig.TileSize);
		return resource;
	}

	/// <summary>
	///     Reference BuildTerrainSurfaceGrid: a triangle lands in every cell its XZ bounds touch
	/// </summary>
	private void BuildSurfaceGrid (float tileSize)
	{
		var cellCount = SurfaceGridResolution * SurfaceGridResolution;
		var cells = new List<uint>[cellCount];
		for (var index = 0; index < cellCount; ++index)
		{
			cells[index] = [];
		}

		for (uint triangle = 0; triangle + 2 < Indices.Length; triangle += 3)
		{
			var a = Positions[Indices[triangle]];
			var b = Positions[Indices[triangle + 1]];
			var c = Positions[Indices[triangle + 2]];
			var minCellX = SurfaceCell (MathF.Min (a.X, MathF.Min (b.X, c.X)), tileSize);
			var maxCellX = SurfaceCell (MathF.Max (a.X, MathF.Max (b.X, c.X)), tileSize);
			var minCellZ = SurfaceCell (MathF.Min (a.Z, MathF.Min (b.Z, c.Z)), tileSize);
			var maxCellZ = SurfaceCell (MathF.Max (a.Z, MathF.Max (b.Z, c.Z)), tileSize);
			for (var cellZ = minCellZ; cellZ <= maxCellZ; ++cellZ)
			{
				for (var cellX = minCellX; cellX <= maxCellX; ++cellX)
				{
					cells[cellZ * SurfaceGridResolution + cellX].Add (triangle);
				}
			}
		}

		SurfaceCellStarts = new int[cellCount + 1];
		var total = 0;
		for (var index = 0; index < cellCount; ++index)
		{
			SurfaceCellStarts[index] = total;
			total += cells[index].Count;
		}

		SurfaceCellStarts[cellCount] = total;
		SurfaceCellTriangles = new uint[total];
		var write = 0;
		for (var index = 0; index < cellCount; ++index)
		{
			foreach (var triangle in cells[index])
			{
				SurfaceCellTriangles[write++] = triangle;
			}
		}
	}

	/// <summary>
	///     Triangle offsets of one surface cell. The reference reads the cell for the local point
	///     and walks it; a miss there falls back to every triangle in the tile
	/// </summary>
	public ArraySegment<uint> CellTriangles (int cellIndex) =>
		new (SurfaceCellTriangles, SurfaceCellStarts[cellIndex],
			SurfaceCellStarts[cellIndex + 1] - SurfaceCellStarts[cellIndex]);

	private static int SurfaceCell (float value, float tileSize)
	{
		var scaled = value / tileSize * SurfaceGridResolution;
		return (int) Math.Clamp (MathF.Floor (scaled), 0f, SurfaceGridResolution - 1f);
	}

	internal static int SurfaceCellIndex (float local, float tileSize) =>
		SurfaceCell (local, tileSize);

	private static Vector3 ReadVector3 (byte[] data, int offset) =>
		new (
			BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (offset, 4)),
			BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (offset + 4, 4)),
			BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (offset + 8, 4)));

	private static byte[]? ReadAllBytes (string path)
	{
		try
		{
			return File.ReadAllBytes (path);
		}
		catch (Exception exception)
		{
			SphLogger.Info ($"LND read failed {path}: {exception.Message}");
			return null;
		}
	}
}
