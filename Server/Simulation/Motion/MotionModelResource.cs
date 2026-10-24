using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Godot;
using SphServer.Shared.Logger;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     Reference StaticModelResource, collision only: the model bounds, the triangle BVH built from
///     the faces without the non-collision flag, and the cylinder proxy a skinned model uses
///     instead of triangles
/// </summary>
public sealed class MotionModelResource
{
	public const int HeaderBytes = 0x102;
	public const int VertexStride = 0x20;
	public const int TriangleStride = 0x0a;
	public const int SurfaceStride = 0x0f;

	public MotionBounds Bounds = MotionBounds.Empty ();
	public bool IsSkinned;
	public float ProxyRadius;
	public MotionCollisionMesh? CollisionMesh;

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

		if (extraMode == 2)
		{
			offset = SectionEnd (offset, extraRecordCount, 0x0c);
			offset = SectionEnd (offset, extraBlockCount, 0x50);
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

		var resource = new MotionModelResource { Bounds = bounds };

		// Reference WillSkin: SkinWeightCount, Objects (strip_groups_0x27) and TransformKeys
		// (skin_records_0x1c) all present. A skinned model gets no triangle collision at all, only
		// the proxy, so its rest pose never reaches the solver
		var willSkin = skinWeightCount != 0 && stripGroupCount != 0 && skinRecordCount != 0;
		resource.IsSkinned = willSkin;
		if (willSkin)
		{
			resource.ProxyRadius = MotionConfig.SkinnedProxyRadius (
				bounds.Max.X - bounds.Min.X, bounds.Max.Z - bounds.Min.Z);
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

		resource.CollisionMesh = MotionCollisionMesh.Build (vertices, collision);
		return resource;
	}

	private static int SectionEnd (int offset, int count, int stride) => count < 0 ? -1 : offset + count * stride;
}
