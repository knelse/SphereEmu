using System;
using Godot;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     Reference TerrainInstance/TerrainSampleAtInstance/TriangleAt and the three world queries
///     built on them. Positions are tile-local, so every query subtracts the instance origin, the
///     reference's TerrainSampleAtInstance does
/// </summary>
public sealed class MotionTerrainField
{
	/// <summary>
	///     One loaded tile: the shared resource plus the tile's world origin, which the reference
	///     derives from (row, column) through OriginRow/OriginColumn
	/// </summary>
	private sealed class TileInstance
	{
		public MotionTerrainResource Resource = null!;
		public float OriginX;
		public float OriginZ;
		public int TileX;
		public int TileZ;
	}

	// Keyed by the reference's TerrainTileKey, the two signed tile coords in one u64
	private readonly Dictionary<ulong, TileInstance> lookup = [];
	private readonly List<TileInstance> instances = [];
	private readonly HashSet<string> missing = [];

	public int LoadedCount => instances.Count;

	public void Clear ()
	{
		lookup.Clear ();
		instances.Clear ();
	}

	/// <summary>
	///     The reference's OriginRow/OriginColumn placement: a map grid cell (x, z) draws at
	///     OriginX = (x - 40) * TileSize, OriginZ = (39 - z) * TileSize
	/// </summary>
	public static (int TileX, int TileZ) TileForGridCell (int gridX, int gridZ) =>
		(gridX - MotionConfig.OriginRow, MotionConfig.OriginColumn - gridZ);

	public void Add (MotionTerrainResource resource, int tileX, int tileZ)
	{
		var key = TileKey (tileX, tileZ);
		if (lookup.ContainsKey (key))
		{
			return;
		}

		var instance = new TileInstance
		{
			Resource = resource,
			TileX = tileX,
			TileZ = tileZ,
			OriginX = tileX * MotionConfig.TileSize,
			OriginZ = tileZ * MotionConfig.TileSize,
		};
		lookup[key] = instance;
		instances.Add (instance);
	}

	public void NoteMissing (string stem) => missing.Add (stem);

	public bool HasMissing (string stem) => missing.Contains (stem);

	/// <summary>
	///     Reference TerrainTileKey
	/// </summary>
	private static ulong TileKey (int tileX, int tileZ) =>
		((ulong) (uint) tileX << 32) | (uint) tileZ;

	private static int TileCoord (float value) => (int) MathF.Floor (value / MotionConfig.TileSize);

	/// <summary>
	///     Reference TerrainTriangleAt: barycentric over the XZ projection, and the normal is flipped
	///     to point up, which in this Y-down frame is the -Y side
	/// </summary>
	private static bool TriangleAt (TileInstance instance, float localX, float localZ,
		int triangleOffset, out float height, out Vector3 normal)
	{
		height = 0f;
		normal = Vector3.Zero;
		var positions = instance.Resource.Positions;
		var indices = instance.Resource.Indices;
		if (triangleOffset + 2 >= indices.Length)
		{
			return false;
		}

		var a = positions[indices[triangleOffset]];
		var b = positions[indices[triangleOffset + 1]];
		var c = positions[indices[triangleOffset + 2]];
		var denominator = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
		if (MathF.Abs (denominator) <= 0.000001f)
		{
			return false;
		}

		var wa = ((b.Z - c.Z) * (localX - c.X) + (c.X - b.X) * (localZ - c.Z)) / denominator;
		var wb = ((c.Z - a.Z) * (localX - c.X) + (a.X - c.X) * (localZ - c.Z)) / denominator;
		var wc = 1f - wa - wb;
		if (wa < -0.001f || wb < -0.001f || wc < -0.001f)
		{
			return false;
		}

		height = wa * a.Y + wb * b.Y + wc * c.Y;
		var raw = MotionMath.NormalizeVector (
			MotionMath.Cross (MotionMath.Subtract (b, a), MotionMath.Subtract (c, a)));
		if (raw.Y < 0f)
		{
			raw = MotionMath.Scale (raw, -1f);
		}

		normal = raw;
		return true;
	}

	/// <summary>
	///     Reference TerrainSampleAtInstance. ClosestToReference picks the surface nearest ReferenceY,
	///     and the fallback walk (TerrainSurfaceAt) picks the highest, which in this Y-down frame is
	///     the lowest Y
	/// </summary>
	private static bool SampleAtInstance (TileInstance instance, float worldX, float worldZ,
		float referenceY, bool closestToReference, out float outHeight, out Vector3 outNormal)
	{
		outHeight = referenceY;
		outNormal = Vector3.Zero;
		var localX = worldX - instance.OriginX;
		var localZ = worldZ - instance.OriginZ;
		if (localX < -0.01f || localX > MotionConfig.TileSize + 0.01f
			|| localZ < -0.01f || localZ > MotionConfig.TileSize + 0.01f)
		{
			return false;
		}

		var bestHeight = referenceY;
		var bestDistance = float.MaxValue;
		var bestNormal = Vector3.Zero;
		var found = false;

		void SampleTriangle (uint triangleOffset)
		{
			if (!TriangleAt (instance, localX, localZ, (int) triangleOffset, out var height, out var normal))
			{
				return;
			}

			var distance = closestToReference ? MathF.Abs (height - referenceY) : -height;
			var closer = distance < bestDistance - 0.0001f;
			var equalUpperSurface = closestToReference
				&& MathF.Abs (distance - bestDistance) <= 0.0001f && height < bestHeight;
			if (!found || closer || equalUpperSurface)
			{
				bestDistance = distance;
				bestHeight = height;
				bestNormal = normal;
				found = true;
			}
		}

		var cellX = MotionTerrainResource.SurfaceCellIndex (localX, MotionConfig.TileSize);
		var cellZ = MotionTerrainResource.SurfaceCellIndex (localZ, MotionConfig.TileSize);
		if (instance.Resource.SurfaceCellStarts.Length > 0)
		{
			foreach (var triangle in instance.Resource.CellTriangles (
					cellZ * MotionTerrainResource.SurfaceGridResolution + cellX))
			{
				SampleTriangle (triangle);
			}
		}
		else
		{
			for (uint offset = 0; offset + 2 < instance.Resource.Indices.Length; offset += 3)
			{
				SampleTriangle (offset);
			}
		}

		if (!found)
		{
			return false;
		}

		outHeight = bestHeight;
		outNormal = bestNormal;
		return true;
	}

	/// <summary>
	///     Reference TerrainHeightAt: the tile lookup first, then every loaded tile
	/// </summary>
	public bool HeightAt (float worldX, float worldZ, float referenceY, out float outHeight)
	{
		outHeight = referenceY;
		var tileX = TileCoord (worldX);
		var tileZ = TileCoord (worldZ);
		if (lookup.TryGetValue (TileKey (tileX, tileZ), out var direct)
			&& SampleAtInstance (direct, worldX, worldZ, referenceY, true, out outHeight, out _))
		{
			return true;
		}

		var bestHeight = referenceY;
		var bestDistance = float.MaxValue;
		var found = false;
		foreach (var instance in instances)
		{
			if (!SampleAtInstance (instance, worldX, worldZ, referenceY, true, out var height, out _))
			{
				continue;
			}

			var distance = MathF.Abs (height - referenceY);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				bestHeight = height;
				found = true;
			}
		}

		if (!found)
		{
			return false;
		}

		outHeight = bestHeight;
		return true;
	}

	/// <summary>
	///     Reference TerrainSurfaceAt, the walk (grass, water) query: the highest surface, the lowest
	///     Y in this frame
	/// </summary>
	public bool SurfaceAt (float worldX, float worldZ, out float outHeight, out Vector3 outNormal)
	{
		outHeight = 0f;
		outNormal = Vector3.Zero;
		var tileX = TileCoord (worldX);
		var tileZ = TileCoord (worldZ);
		if (lookup.TryGetValue (TileKey (tileX, tileZ), out var direct)
			&& SampleAtInstance (direct, worldX, worldZ, 0f, false, out outHeight, out outNormal))
		{
			return true;
		}

		var bestHeight = float.MaxValue;
		var bestNormal = Vector3.Zero;
		var found = false;
		foreach (var instance in instances)
		{
			if (!SampleAtInstance (instance, worldX, worldZ, 0f, false, out var height, out var normal))
			{
				continue;
			}

			if (!found || height < bestHeight)
			{
				bestHeight = height;
				bestNormal = normal;
				found = true;
			}
		}

		if (!found)
		{
			return false;
		}

		outHeight = bestHeight;
		outNormal = bestNormal;
		return true;
	}

	/// <summary>
	///     Reference TerrainSurfaceNearAt, the query the solver runs. The seam probe reaches the
	///     neighbouring tile within CollisionSkin, the plane projection keeps a neighbour's sample
	///     honest, and the final probe of four is the reference's own fallback
	/// </summary>
	public bool SurfaceNearAt (float worldX, float worldZ, float referenceY,
		out float outHeight, out Vector3 outNormal)
	{
		outHeight = referenceY;
		outNormal = Vector3.Zero;
		if (referenceY > MotionConfig.DungeonDepth)
		{
			return false;
		}

		var bestHeight = referenceY;
		var bestDistance = float.MaxValue;
		var bestNormal = Vector3.Zero;
		var found = false;

		bool Consider (TileInstance instance, float sampleX, float sampleZ)
		{
			if (!SampleAtInstance (instance, sampleX, sampleZ, referenceY, true, out var height, out var normal))
			{
				return false;
			}

			var offsetX = sampleX - worldX;
			var offsetZ = sampleZ - worldZ;
			if (offsetX != 0f || offsetZ != 0f)
			{
				if (MathF.Abs (normal.Y) <= 0.05f)
				{
					return false;
				}

				var projected = height + (normal.X * offsetX + normal.Z * offsetZ) / normal.Y;
				if (MathF.Abs (projected - height) > MathF.Max (MotionConfig.CollisionSkin * 2f, 0.05f))
				{
					return false;
				}

				height = projected;
			}

			var distance = MathF.Abs (height - referenceY);
			if (found && distance > bestDistance + 0.0001f)
			{
				return true;
			}

			if (found && MathF.Abs (distance - bestDistance) <= 0.0001f && height >= bestHeight)
			{
				return true;
			}

			bestHeight = height;
			bestDistance = distance;
			bestNormal = normal;
			found = true;
			return true;
		}

		bool SampleTile (int offsetXTile, int offsetZTile, float sampleX, float sampleZ)
		{
			var tileX = TileCoord (sampleX) + offsetXTile;
			var tileZ = TileCoord (sampleZ) + offsetZTile;
			return lookup.TryGetValue (TileKey (tileX, tileZ), out var instance)
				&& Consider (instance, sampleX, sampleZ);
		}

		void Sample (float sampleX, float sampleZ)
		{
			var tileX = TileCoord (sampleX);
			var tileZ = TileCoord (sampleZ);
			var primaryFound = SampleTile (0, 0, sampleX, sampleZ);
			var localX = sampleX - tileX * MotionConfig.TileSize;
			var localZ = sampleZ - tileZ * MotionConfig.TileSize;
			var seam = MathF.Max (MotionConfig.CollisionSkin, 0.01f);
			var minOffsetX = !primaryFound || localX <= seam ? -1 : 0;
			var maxOffsetX = !primaryFound || localX >= MotionConfig.TileSize - seam ? 1 : 0;
			var minOffsetZ = !primaryFound || localZ <= seam ? -1 : 0;
			var maxOffsetZ = !primaryFound || localZ >= MotionConfig.TileSize - seam ? 1 : 0;
			for (var offsetZ = minOffsetZ; offsetZ <= maxOffsetZ; ++offsetZ)
			{
				for (var offsetX = minOffsetX; offsetX <= maxOffsetX; ++offsetX)
				{
					if (offsetX != 0 || offsetZ != 0)
					{
						SampleTile (offsetX, offsetZ, sampleX, sampleZ);
					}
				}
			}
		}

		Sample (worldX, worldZ);
		if (!found)
		{
			var probe = Math.Clamp (MotionConfig.CollisionSkin * 0.25f, 0.002f, 0.01f);
			Sample (worldX + probe, worldZ);
			Sample (worldX - probe, worldZ);
			Sample (worldX, worldZ + probe);
			Sample (worldX, worldZ - probe);
		}

		if (!found)
		{
			foreach (var instance in instances)
			{
				Consider (instance, worldX, worldZ);
			}
		}

		if (!found)
		{
			return false;
		}

		outHeight = bestHeight;
		outNormal = bestNormal;
		return true;
	}
}
