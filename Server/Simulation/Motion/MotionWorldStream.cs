using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using SphServer.Shared.Logger;

namespace SphServer.Server.Simulation.Motion;

/// <summary>
///     Streams the solver's geometry from raw .lnd tiles and .mdl placements. The reference's
///     radii: terrain VisibleRadius tiles, statics StaticObjectRadius plus a tile. Caches per
///     asset, so a model parses once for all of its placements
/// </summary>
public partial class MotionWorldStream : Node
{
	private const int TerrainRadiusTiles = MotionConfig.VisibleRadius;

	// A guard tile beyond the load radius, the reference's GuardRadius = VisibleRadius + 1, so a
	// player crossing a tile border never steps into unloaded collision
	private const int GuardTiles = MotionConfig.VisibleRadius + 1;

	private readonly MotionAssetSource assets = new ();
	private readonly MotionTerrainField terrain = new ();
	private readonly MotionStaticField statics = new ();
	private readonly Dictionary<string, MotionTerrainResource?> terrainCache = [];
	private readonly Dictionary<string, MotionModelResource?> modelCache = [];
	private readonly HashSet<(int Gx, int Gz)> loadedTerrain = [];
	private readonly HashSet<(int Gx, int Gz)> loadedStatics = [];

	// Placements grouped by tile cell once per MBD file, so a cell query is a dictionary hit
	private readonly Dictionary<(int Gx, int Gz), List<MotionPlacement>> placementsByCell = [];
	private bool placementsBuilt;

	private int terrainLoadFailures;
	private int staticLoadFailures;

	public MotionTerrainField Terrain => terrain;
	public MotionStaticField Statics => statics;
	public int MapCellCount => assets.MapCellCount;
	public int LoadedTerrainCells => loadedTerrain.Count;
	public int LoadedStaticCells => loadedStatics.Count;
	public int LoadedTileCount => terrain.LoadedCount;
	public int LoadedInstanceCount => statics.LoadedCount;

	public override void _Ready ()
	{
		if (!assets.LoadMap ())
		{
			return;
		}

		BuildPlacementIndex ();
	}

	/// <summary>
	///     One pass over sst*.mbd: every blocking placement lands in the tile cell of its position,
	///     which is floor(position / TileSize) in the game frame
	/// </summary>
	private void BuildPlacementIndex ()
	{
		if (placementsBuilt)
		{
			return;
		}

		placementsBuilt = true;
		var directory = MotionAssetSource.Absolute (Path.Combine (MotionAssetSource.ModelRoot, "params"));
		if (!Directory.Exists (directory))
		{
			SphLogger.Info ($"MotionWorldStream: no placement dir {directory}");
			return;
		}

		var total = 0;
		foreach (var file in MotionAssetSource.StaticGroupFiles (directory))
		{
			foreach (var placement in MotionAssetSource.ParsePlacements (file))
			{
				var key = ((int) Math.Floor (placement.Position.X / MotionConfig.TileSize),
					(int) Math.Floor (placement.Position.Z / MotionConfig.TileSize));
				if (!placementsByCell.TryGetValue (key, out var list))
				{
					list = [];
					placementsByCell[key] = list;
				}

				list.Add (placement);
				total++;
			}
		}

		SphLogger.Info ($"MotionWorldStream: {total} placements in {placementsByCell.Count} cells");
	}

	/// <summary>
	///     Load the collision a player at this pose needs: tiles within TerrainRadiusTiles, statics
	///     within StaticObjectRadius. The guard ring is what the reference's streaming guard preloads
	/// </summary>
	public void EnsureAroundWorldPosition (Vector3 gamePosition)
	{
		if (!placementsBuilt && assets.MapCellCount == 0)
		{
			return;
		}

		var center = TileCoord (gamePosition);
		for (var z = center.Z - GuardTiles; z <= center.Z + GuardTiles; ++z)
		{
			for (var x = center.X - GuardTiles; x <= center.X + GuardTiles; ++x)
			{
				EnsureTerrainCell (x, z);
			}
		}

		var staticRadius = (int) MathF.Ceiling (
			(MotionConfig.StaticObjectRadius + MotionConfig.TileSize) / MotionConfig.TileSize);
		for (var z = center.Z - staticRadius; z <= center.Z + staticRadius; ++z)
		{
			for (var x = center.X - staticRadius; x <= center.X + staticRadius; ++x)
			{
				EnsureStaticCell (x, z);
			}
		}
	}

	private static (int X, int Z) TileCoord (Vector3 gamePosition) =>
		((int) Math.Floor (gamePosition.X / MotionConfig.TileSize),
			(int) Math.Floor (gamePosition.Z / MotionConfig.TileSize));

	/// <summary>
	///     One tile: its stem from the map, its .lnd parsed once per stem, placed at the reference's
	///     origin. An empty cell is marked loaded so it is never probed again
	/// </summary>
	private void EnsureTerrainCell (int tileX, int tileZ)
	{
		if (loadedTerrain.Contains ((tileX, tileZ)))
		{
			return;
		}

		loadedTerrain.Add ((tileX, tileZ));

		// The map grid is addressed through OriginRow/OriginColumn, so invert it for the stem
		var (gridX, gridZ) = GridCellForTile (tileX, tileZ);
		if (!assets.TryGetStem (gridX, gridZ, out var stem))
		{
			return;
		}

		if (!terrainCache.TryGetValue (stem, out var resource))
		{
			var path = assets.FindLnd (stem);
			resource = path is null ? null : MotionTerrainResource.Build (path);
			terrainCache[stem] = resource;
			if (resource is null && ++terrainLoadFailures <= 8)
			{
				SphLogger.Info ($"MotionWorldStream: no terrain for {stem}");
			}
		}

		if (resource is not null)
		{
			terrain.Add (resource, tileX, tileZ);
		}
	}

	/// <summary>
	///     The map cell whose record the renderer picks for a tile. LoadMapGrid keys a record by
	///     row*80+column with row = tileX+OriginRow and column = OriginColumn-tileZ, and the client's
	///     bindTerrainCell uses the same pair, so the grid X is the column and the grid Z the row.
	///     Swapping them puts the terrain 500 m off the rendered ground, median over sampled tiles
	/// </summary>
	private static (int Gx, int Gz) GridCellForTile (int tileX, int tileZ) =>
		(MotionConfig.OriginColumn - tileZ, tileX + MotionConfig.OriginRow);

	/// <summary>
	///     One cell's placements. A model's mesh is parsed once per stem and shared by every
	///     placement of it, which is the reference's StaticModelResource reuse
	/// </summary>
	private void EnsureStaticCell (int cellX, int cellZ)
	{
		if (loadedStatics.Contains ((cellX, cellZ)))
		{
			return;
		}

		loadedStatics.Add ((cellX, cellZ));
		if (!placementsByCell.TryGetValue ((cellX, cellZ), out var placements))
		{
			return;
		}

		foreach (var placement in placements)
		{
			if (!modelCache.TryGetValue (placement.ModelName, out var resource))
			{
				var path = assets.FindMdl (placement.ModelName);
				resource = path is null ? null : MotionModelResource.Build (path);
				modelCache[placement.ModelName] = resource;
				if (resource is null && ++staticLoadFailures <= 8)
				{
					SphLogger.Info ($"MotionWorldStream: no model for {placement.ModelName}");
				}
			}

			if (resource is not null)
			{
				statics.Add (resource,
					MotionMatrix.Placement (placement.Position, placement.Rotation));
			}
		}
	}

	/// <summary>
	///     A model by its client name, parsed once and shared with the placements that use it
	/// </summary>
	public MotionModelResource? Model (string modelName)
	{
		var key = modelName.ToLowerInvariant ();
		if (!modelCache.TryGetValue (key, out var resource))
		{
			var path = assets.FindMdl (key);
			resource = path is null ? null : MotionModelResource.Build (path);
			modelCache[key] = resource;
		}

		return resource;
	}

	public void UnloadAll ()
	{
		terrain.Clear ();
		statics.Clear ();
		loadedTerrain.Clear ();
		loadedStatics.Clear ();
	}
}
