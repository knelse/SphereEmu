using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Godot;
using SphServer.Shared.Logger;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     The client's static collision model: the bounds over every vertex and prepared triangle, a
///     BVH over the kind-2 prepared triangles or else the faces without the non-collision flag,
///     and the cylinder proxy a skinned model with no triangles uses
/// </summary>
public sealed class MotionModelResource
{
	public const int HeaderBytes = 0x102;
	public const int VertexStride = 0x20;
	public const int TriangleStride = 0x0a;
	public const int SurfaceStride = 0x0f;
	public const int CollisionTriangleStride = 0x50;
	public const int CollisionKindAxisAligned = 0;
	public const int CollisionKindOriented = 1;
	public const int CollisionKindTriangles = 2;
	public const int CollisionKindNone = 3;

	// Client fromExtents corner order, which the header oriented box also follows. Two triangles
	// per face of the box
	private static readonly int[] BoxIndices =
	[
		0, 1, 2, 0, 2, 3, 5, 6, 7, 5, 7, 4, 0, 1, 6, 0, 6, 5,
		3, 2, 7, 3, 7, 4, 0, 3, 4, 0, 4, 5, 1, 2, 7, 1, 7, 6,
	];

	public string Name = "";
	public MotionBounds Bounds = MotionBounds.Empty ();
	public bool IsSkinned;
	public float ProxyRadius;
	public MotionCollisionMesh? CollisionMesh;

	// A dynamic object (mob, NPC, chest, another player) collides as the client's contact query
	// has it: kind 0 the header box with no rotation, kind 1 the header oriented box, kind 2 the
	// prepared triangles, kind 3 nothing
	public int CollisionKind;
	public MotionCollisionMesh? DynamicMesh;
	public MotionBounds DynamicBounds = MotionBounds.Empty ();
	public bool DynamicAxisAligned => CollisionKind == CollisionKindAxisAligned;

	/// <summary>
	///     A kind-0 box with no model file behind it, the other players' character box
	/// </summary>
	public static MotionModelResource AxisAlignedBox (string name, Vector3 min, Vector3 max)
	{
		var resource = new MotionModelResource { Name = name, CollisionKind = CollisionKindAxisAligned };
		resource.SetDynamicBox (BoxCorners (min, max));
		return resource;
	}

	private static Vector3[] BoxCorners (Vector3 min, Vector3 max) =>
	[
		new (min.X, min.Y, min.Z), new (max.X, min.Y, min.Z), new (max.X, max.Y, min.Z),
		new (min.X, max.Y, min.Z), new (min.X, max.Y, max.Z), new (min.X, min.Y, max.Z),
		new (max.X, min.Y, max.Z), new (max.X, max.Y, max.Z),
	];

	private void SetDynamicBox (Vector3[] corners)
	{
		DynamicMesh = MotionCollisionMesh.Build (corners, [.. BoxIndices]);
		DynamicBounds = MotionBounds.Empty ();
		foreach (var corner in corners)
		{
			DynamicBounds.Expand (corner);
		}
	}

	private static Vector3 ReadPoint (byte[] data, int at) =>
		new (
			BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (at, 4)),
			BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (at + 4, 4)),
			BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (at + 8, 4)));

	/// <summary>
	///     Reference ParseInfo: the header counts, then the section table walked in the same order.
	///     The reference throws when the computed size is not the file size, kept as a skip, so a
	///     model that does not parse blocks nothing rather than something guessed
	/// </summary>
	public static MotionModelResource? Build (string mdlPath)
	{
		var name = Path.GetFileName (mdlPath);
		byte[]? data;
		try
		{
			data = File.ReadAllBytes (mdlPath);
		}
		catch (Exception exception)
		{
			SphLogger.Info ($"MDL read failed {name}: {exception.Message}");
			return null;
		}

		if (data.Length < HeaderBytes
			|| data[0] != (byte) 'M' || data[1] != (byte) 'D' || data[2] != (byte) 'L' || data[3] != (byte) '!')
		{
			SphLogger.Info ($"Bad MDL file: {name}");
			return null;
		}

		var vertexCount = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (0x04, 2));
		var indexCount = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (0x06, 2));
		var surfaceCount = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (0x08, 2));
		var materialCount = data[0x0a];
		var materialNamesSize = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (0x0b, 2));
		var stripGroupCount = data[0x0d];
		var smallBlockSize = data[0x0e];
		var skinRecordCount = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (0x10, 2));
		var skinIndexCount = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (0x12, 2));
		var skinWeightCount = data[0x14];
		var animationFlag = data[0x17];
		var animationTableCount = BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (0x18, 2));
		var extraMode = BinaryPrimitives.ReadInt32LittleEndian (data.AsSpan (0x1a, 4));
		var extraRecordCount = BinaryPrimitives.ReadInt32LittleEndian (data.AsSpan (0xfa, 4));
		var extraBlockCount = BinaryPrimitives.ReadInt32LittleEndian (data.AsSpan (0xfe, 4));

		if (HeaderBytes + materialNamesSize > data.Length || materialNamesSize < materialCount)
		{
			SphLogger.Info ($"MDL material names out of range: {name}");
			return null;
		}

		var offset = HeaderBytes + materialNamesSize;
		var verticesOffset = offset;
		offset = SectionEnd (offset, vertexCount, VertexStride);
		var trianglesOffset = offset;
		offset = SectionEnd (offset, indexCount, TriangleStride);
		var surfacesOffset = offset;
		offset = SectionEnd (offset, surfaceCount, SurfaceStride);
		offset = SectionEnd (offset, stripGroupCount, 0x27);
		offset = SectionEnd (offset, smallBlockSize, 1);
		if (skinWeightCount != 0)
		{
			offset = SectionEnd (offset, skinRecordCount, 0x1c);
			offset = SectionEnd (offset, skinIndexCount, 0x03);
			offset = SectionEnd (offset, skinWeightCount, 0x02);
		}
		else
		{
			offset = SectionEnd (offset, stripGroupCount, 0x18);
		}

		if (animationFlag == 1)
		{
			offset = SectionEnd (offset, animationTableCount, 0x04);
			offset = SectionEnd (offset, indexCount, 0x06);
		}

		// The client rejects a collision kind past None (3), so such a model is never placed there
		if (extraMode < 0 || extraMode > CollisionKindNone)
		{
			SphLogger.Info ($"MDL collision kind invalid: {name} {extraMode}");
			return null;
		}

		var collisionTrianglesOffset = -1;
		if (extraMode == CollisionKindTriangles)
		{
			offset = SectionEnd (offset, extraRecordCount, 0x0c);
			collisionTrianglesOffset = offset;
			offset = SectionEnd (offset, extraBlockCount, CollisionTriangleStride);
		}

		if (offset < 0 || offset != data.Length)
		{
			SphLogger.Info ($"MDL section size mismatch: {name} {offset} of {data.Length}");
			return null;
		}

		var vertices = new Vector3[vertexCount];
		var bounds = MotionBounds.Empty ();
		for (var index = 0; index < vertexCount; ++index)
		{
			var at = verticesOffset + index * VertexStride;
			var position = new Vector3 (
				BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (at, 4)),
				BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (at + 4, 4)),
				BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (at + 8, 4)));
			vertices[index] = position;
			bounds.Expand (position);
		}

		// Client ModelResources kind 2: 80-byte records, the three corners at +24, +36 and +48
		var hasCollisionTriangles = collisionTrianglesOffset >= 0 && extraBlockCount > 0;
		var collisionVertices = new Vector3[hasCollisionTriangles ? extraBlockCount * 3 : 0];
		var collisionDirections = new byte[hasCollisionTriangles ? extraBlockCount : 0];
		var collisionMaterials = new byte[collisionDirections.Length];
		for (var index = 0; index < collisionDirections.Length; ++index)
		{
			var flags = BinaryPrimitives.ReadUInt32LittleEndian (
				data.AsSpan (collisionTrianglesOffset + index * CollisionTriangleStride + 76, 4));
			collisionDirections[index] = (byte) ((flags >> 8) & 0xFF);
			collisionMaterials[index] = (byte) (flags & 0xFF);
		}

		for (var index = 0; index < collisionVertices.Length; ++index)
		{
			var at = collisionTrianglesOffset + index / 3 * CollisionTriangleStride + 24 + index % 3 * 12;
			collisionVertices[index] = new Vector3 (
				BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (at, 4)),
				BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (at + 4, 4)),
				BinaryPrimitives.ReadSingleLittleEndian (data.AsSpan (at + 8, 4)));
			bounds.Expand (collisionVertices[index]);
		}

		var resource = new MotionModelResource { Name = name, Bounds = bounds, CollisionKind = extraMode };
		// Client ModelResources header: min at 30, max at 42, the eight oriented corners from 54
		if (extraMode == CollisionKindAxisAligned)
		{
			resource.SetDynamicBox (BoxCorners (ReadPoint (data, 30), ReadPoint (data, 42)));
		}
		else if (extraMode == CollisionKindOriented)
		{
			var corners = new Vector3[8];
			for (var corner = 0; corner < corners.Length; ++corner)
			{
				corners[corner] = ReadPoint (data, 54 + corner * 12);
			}

			resource.SetDynamicBox (corners);
		}

		// The client's skinned test: bones, keyframes and animation frames all present. Those
		// models still keep their bind-pose triangles; the proxy is only the fallback when the
		// mesh has no collision triangles
		var willSkin = skinWeightCount != 0 && stripGroupCount != 0 && skinRecordCount != 0
			&& skinIndexCount != 0;

		// Client ReferenceField: a kind-2 model collides its prepared triangles, every one, and
		// only the other kinds fall back to the faces without the non-collision flag
		if (hasCollisionTriangles)
		{
			var prepared = new List<int> (collisionVertices.Length);
			for (var index = 0; index < collisionVertices.Length; ++index)
			{
				prepared.Add (index);
			}

			resource.CollisionMesh = MotionCollisionMesh.Build (collisionVertices, prepared, collisionDirections,
				collisionMaterials);
			resource.DynamicMesh = resource.CollisionMesh;
			resource.DynamicBounds = MotionBounds.Empty ();
			foreach (var vertex in collisionVertices)
			{
				resource.DynamicBounds.Expand (vertex);
			}

			if (resource.CollisionMesh is null && willSkin)
			{
				resource.IsSkinned = true;
				resource.ProxyRadius = MotionConfig.SkinnedProxyRadius (
					bounds.Max.X - bounds.Min.X, bounds.Max.Z - bounds.Min.Z);
			}

			return resource;
		}

		var collision = new List<int> ();
		for (var surface = 0; surface < surfaceCount; ++surface)
		{
			var at = surfacesOffset + surface * SurfaceStride;
			var firstTriangle = BinaryPrimitives.ReadInt16LittleEndian (data.AsSpan (at + 2, 2));
			var triangleCount = BinaryPrimitives.ReadInt16LittleEndian (data.AsSpan (at + 4, 2));
			var firstVertex = BinaryPrimitives.ReadInt16LittleEndian (data.AsSpan (at + 6, 2));
			var surfaceVertexCount = BinaryPrimitives.ReadInt16LittleEndian (data.AsSpan (at + 8, 2));
			if (firstTriangle < 0 || triangleCount < 0 || firstVertex < 0 || surfaceVertexCount < 0
				|| firstTriangle > indexCount || triangleCount > indexCount - firstTriangle
				|| firstVertex > vertexCount || surfaceVertexCount > vertexCount - firstVertex)
			{
				SphLogger.Info ($"MDL surface range invalid: {name}");
				return null;
			}

			for (var entry = 0; entry < triangleCount; ++entry)
			{
				var triangleAt = trianglesOffset + (firstTriangle + entry) * TriangleStride;
				if ((BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (triangleAt + 6, 2))
						& MotionConfig.NonCollisionTriangleFlag) != 0)
				{
					continue;
				}

				collision.Add (firstVertex + BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (triangleAt, 2)));
				collision.Add (firstVertex + BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (triangleAt + 2, 2)));
				collision.Add (firstVertex + BinaryPrimitives.ReadUInt16LittleEndian (data.AsSpan (triangleAt + 4, 2)));
			}
		}

		// Build drops degenerate triangles, the same filter the client applies before it picks the
		// proxy, so the fallback keys on the built mesh and not on the raw index count
		resource.CollisionMesh = MotionCollisionMesh.Build (vertices, collision);
		if (resource.CollisionMesh is null && willSkin)
		{
			resource.IsSkinned = true;
			resource.ProxyRadius = MotionConfig.SkinnedProxyRadius (
				bounds.Max.X - bounds.Min.X, bounds.Max.Z - bounds.Min.Z);
		}

		return resource;
	}

	private static int SectionEnd (int offset, int count, int stride) => count < 0 ? -1 : offset + count * stride;
}
