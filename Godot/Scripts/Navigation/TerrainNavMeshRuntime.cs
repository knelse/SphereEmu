using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain.Fill;
using SphServer.Godot.Scripts.Terrain.WalkSurface;

namespace SphServer.Godot.Scripts.Navigation;

/// <summary>
///     Lazy, proximity-loading bridge from the baked per-tile <see cref="NavigationMesh" /> resources under
///     <see cref="NavMeshResourcesDirectory" /> (produced by <see cref="TerrainNavigationBaker" />) to a live
///     <see cref="NavigationServer3D" /> map, so spawn-slot baking (and, later, monster pathfinding) can query
///     real navmesh walkability instead of the walk-surface raster. Mirrors <c>WalkSurfaceCache</c>'s
///     lazy/static-loader shape so it fits the existing codebase conventions and stays cheap: regions are only
///     registered for tiles actually queried near a spawner/agent, not the whole ~5100-tile map at once.
///     <para>
///     Godot's <see cref="NavigationServer3D" /> only synchronizes region/map changes at the end of a physics
///     frame (Godot 4.4+ async nav updates) - querying a region immediately after registering it throws
///     ("query made before first map synchronization"). <see cref="EnsureTilesLoaded" /> only registers regions
///     (synchronous, cheap); callers that can await (editor tool buttons) must follow up with
///     <see cref="SyncAsync" /> once before querying newly-loaded tiles. Callers that cannot await (the
///     runtime respawn path, which holds a lock) call query methods directly - see
///     <see cref="IsReadyForQueries" /> for how that degrades safely instead of throwing.
///     </para>
/// </summary>
public static class TerrainNavMeshRuntime
{
    public const string NavMeshResourcesDirectory = "res://Godot/Terrain/GeneratedNavMeshes/";
    public const string MapBinPath = "res://Godot/Terrain/map.txt";
    public const float TileSizeWorld = 100f;

    // Must match TerrainNavigationBaker's bake params so query precision matches what was actually baked.
    public const float CellSize = 0.1f;
    public const float CellHeight = 0.1f;

    private const float HorizontalSnapToleranceMeters = 0.2f;

    // Empirically (see MapGetIterationId polling in SyncAsync), a freshly-created map's "active" flag and
    // its first region can each take more than one physics frame to actually land - one await isn't always
    // enough despite the docs suggesting it is. Poll instead of guessing a fixed frame count.
    private const int MaxSyncWaitFrames = 20;

    private static readonly ConcurrentDictionary<string, Rid> RegionsByTileKey = new();

    private static Rid _map;
    private static bool _mapCreated;
    private static bool _hasEverSynced;
    private static bool _pendingSync;

    private static bool _transformResolved;
    private static Transform3D _bakedToWorld = Transform3D.Identity;
    private static Vector3 _tileWorldOrigin;
    private static TerrainObjectsFill.TerrainTileGridIndex? _tileIndex;

    /// <summary>
    ///     False until at least one <see cref="SyncAsync" /> has completed. Query methods return "not walkable"
    ///     rather than calling into <see cref="NavigationServer3D" /> while this is false, since querying a
    ///     navigation map before its first-ever sync throws.
    /// </summary>
    public static bool IsReadyForQueries => _hasEverSynced;

    public static bool HasAnyTileFiles()
    {
        var absoluteDirectory = ProjectSettings.GlobalizePath(NavMeshResourcesDirectory);
        return Directory.Exists(absoluteDirectory) && Directory.GetFiles(absoluteDirectory, "*.res").Length > 0;
    }

    /// <summary>Frees every registered region and the navigation map. Call after a full nav rebake.</summary>
    public static void Invalidate()
    {
        foreach (var region in RegionsByTileKey.Values)
        {
            NavigationServer3D.FreeRid(region);
        }

        RegionsByTileKey.Clear();

        if (_mapCreated)
        {
            NavigationServer3D.FreeRid(_map);
            _map = default;
            _mapCreated = false;
        }

        _hasEverSynced = false;
        _pendingSync = false;
        _transformResolved = false;
        _tileIndex = null;
    }

    /// <summary>
    ///     Registers (but does not sync) every navmesh tile whose baked geometry falls within
    ///     <paramref name="radiusMeters" /> of <paramref name="worldCenter" />. Returns true when at least one
    ///     new region was registered this call (meaning callers that can await should follow up with
    ///     <see cref="SyncAsync" /> before trusting query results for that area).
    /// </summary>
    public static bool EnsureTilesLoaded(Node3D contextNode, Vector3 worldCenter, float radiusMeters)
    {
        if (!EnsureMapAndTransform(contextNode) || _tileIndex is null)
        {
            return false;
        }

        var localCenter = _bakedToWorld.AffineInverse() * worldCenter;
        var minGx = Mathf.FloorToInt((localCenter.X - _tileWorldOrigin.X - radiusMeters) / TileSizeWorld);
        var maxGx = Mathf.FloorToInt((localCenter.X - _tileWorldOrigin.X + radiusMeters) / TileSizeWorld);
        var minGz = Mathf.FloorToInt((localCenter.Z - _tileWorldOrigin.Z - radiusMeters) / TileSizeWorld);
        var maxGz = Mathf.FloorToInt((localCenter.Z - _tileWorldOrigin.Z + radiusMeters) / TileSizeWorld);

        var newlyLoaded = false;
        for (var gz = minGz; gz <= maxGz; gz++)
        {
            for (var gx = minGx; gx <= maxGx; gx++)
            {
                var tileCenterLocal = _tileWorldOrigin + new Vector3(
                    (gx + 0.5f) * TileSizeWorld,
                    0f,
                    (gz + 0.5f) * TileSizeWorld);

                if (!_tileIndex.TryGetTile(tileCenterLocal, out var masterName, out var occurrence))
                {
                    continue;
                }

                var tileKey = TerrainObjectsFill.TerrainTileGridIndex.BuildTileGroupKey(masterName, occurrence);
                if (LoadAndRegisterTile(tileKey))
                {
                    newlyLoaded = true;
                }
            }
        }

        if (newlyLoaded)
        {
            _pendingSync = true;
        }

        return newlyLoaded;
    }

    /// <summary>
    ///     Awaits enough physics frames for newly-registered regions (from <see cref="EnsureTilesLoaded" />) to
    ///     become queryable. No-op (returns immediately) if nothing new was registered since the last sync.
    ///     Only call from a context that can await - never from inside a <c>lock</c> (see class docs).
    /// </summary>
    /// <remarks>
    ///     Godot's docs say one physics frame after registering map/region changes is enough, but that's not
    ///     reliable in practice here: a newly-created map's "active" flag and its first region can each take
    ///     more than one physics frame to actually land (confirmed empirically via
    ///     <c>NavigationServer3D.MapGetIterationId</c> - it can stay unchanged for 2+ frames, and even once it
    ///     first ticks the region isn't necessarily queryable yet). We just wait a fixed, generous frame count
    ///     instead of trying to detect "done" from iteration id.
    /// </remarks>
    public static async Task SyncAsync(SceneTree tree)
    {
        if (!_pendingSync && _hasEverSynced)
        {
            return;
        }

        for (var i = 0; i < MaxSyncWaitFrames; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        _pendingSync = false;
        _hasEverSynced = true;
    }

    /// <summary>
    ///     True when <paramref name="worldPos" /> (and, implicitly, the small ring of points
    ///     <paramref name="radiusMeters" /> around it used to approximate a mob-body footprint) sits on the
    ///     loaded navmesh. <paramref name="refinedCenter" /> is <paramref name="worldPos" /> snapped to the
    ///     navmesh's actual surface height, so callers get an accurate ground Y as a side effect of validation.
    /// </summary>
    public static bool IsDiscWalkable(Vector3 worldPos, float radiusMeters, out Vector3 refinedCenter)
    {
        refinedCenter = worldPos;

        if (!IsPointOnNavMesh(worldPos, out var snappedCenter))
        {
            return false;
        }

        refinedCenter = snappedCenter;

        foreach (var (offsetX, offsetZ) in WalkSurfaceMobBodyDisk.Offsets)
        {
            var ringPoint = new Vector3(
                worldPos.X + offsetX * radiusMeters,
                worldPos.Y,
                worldPos.Z + offsetZ * radiusMeters);
            if (!IsPointOnNavMesh(ringPoint, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     True when <paramref name="worldPos" /> is within snap tolerance of the navmesh surface.
    ///     <paramref name="snapped" /> is the closest point on the navmesh (accurate ground Y included).
    /// </summary>
    public static bool IsPointOnNavMesh(Vector3 worldPos, out Vector3 snapped)
    {
        snapped = worldPos;

        if (!TryClosestPoint(worldPos, out var closest))
        {
            return false;
        }

        // worldPos.Y is only ever a coarse guess (walk-surface atlas sample, or a spawner's own possibly
        // stale placement height). MapGetClosestPoint is a full 3D nearest-neighbor search, so a bad Y can
        // lock onto some unrelated, similarly-elevated polygon far away in XZ instead of the correct one
        // directly below/above the query point. Re-probe once using the first pass's returned Y - real
        // terrain height varies gradually relative to tile size, so this reliably converges to the right
        // polygon even when the original Y guess was off by several meters.
        var refinedProbe = new Vector3(worldPos.X, closest.Y, worldPos.Z);
        if (TryClosestPoint(refinedProbe, out var refined))
        {
            closest = refined;
        }

        snapped = closest;

        // Only horizontal (XZ) containment judges "is this actually on the mesh here" - Y is whatever the
        // navmesh says, not something we independently know to compare against (this map's outdoor terrain
        // is single-layer, so there's no multi-height ambiguity horizontal-only matching could confuse).
        var dx = closest.X - worldPos.X;
        var dz = closest.Z - worldPos.Z;
        var horizontalDistSq = dx * dx + dz * dz;
        return horizontalDistSq <= HorizontalSnapToleranceMeters * HorizontalSnapToleranceMeters;
    }

    /// <summary>Raw closest-point query. Returns false (rather than throwing) before the map's first sync.</summary>
    public static bool TryClosestPoint(Vector3 worldPos, out Vector3 closest)
    {
        closest = worldPos;

        if (!_mapCreated || !_hasEverSynced)
        {
            return false;
        }

        try
        {
            closest = NavigationServer3D.MapGetClosestPoint(_map, worldPos);
            return true;
        }
        catch (Exception ex)
        {
            // Defense in depth: NavigationServer3D throws if queried before its first-ever sync. IsReadyForQueries
            // should already prevent that, but callers on the non-awaiting (lock-guarded) runtime path can't wait
            // out a race here, so degrade to "not on navmesh" instead of crashing gameplay.
            GD.PushWarning($"TerrainNavMeshRuntime: closest-point query failed ({ex.Message}); treating as unwalkable.");
            return false;
        }
    }

    /// <summary>
    ///     Reserved for the future pathfinding migration (replacing <c>OutdoorPathQuery</c>'s custom A* over
    ///     the walk-surface grid) - not yet called from anywhere.
    /// </summary>
    public static Vector3[] FindPath(Vector3 start, Vector3 end, bool optimize = true)
    {
        if (!_mapCreated || !_hasEverSynced)
        {
            return [];
        }

        return NavigationServer3D.MapGetPath(_map, start, end, optimize);
    }

    private static bool LoadAndRegisterTile(string tileGroupKey)
    {
        if (RegionsByTileKey.ContainsKey(tileGroupKey))
        {
            return false;
        }

        var path = $"{NavMeshResourcesDirectory}{tileGroupKey}.res";
        if (!ResourceLoader.Exists(path))
        {
            return false;
        }

        var navMesh = ResourceLoader.Load<NavigationMesh>(path, cacheMode: ResourceLoader.CacheMode.Ignore);
        if (navMesh is null)
        {
            return false;
        }

        var region = NavigationServer3D.RegionCreate();
        NavigationServer3D.RegionSetMap(region, _map);
        NavigationServer3D.RegionSetEnabled(region, true);
        NavigationServer3D.RegionSetNavigationMesh(region, navMesh);
        NavigationServer3D.RegionSetTransform(region, _bakedToWorld);

        return RegionsByTileKey.TryAdd(tileGroupKey, region);
    }

    /// <summary>
    ///     Resolves the "Terrain" GridMap + its parent's transform (the frame baked navmesh vertices live in -
    ///     see <see cref="TerrainNavigationBaker" />'s <c>gridWorldOrigin</c> doc) and creates the shared
    ///     navigation map, once. Uses <see cref="Node3D.GlobalTransform" /> (unlike the headless bake tools,
    ///     which avoid it) because this service only ever runs inside a real, live scene tree - either the
    ///     editor's open scene or the actually-running game - never the orphan-instantiate context the
    ///     standalone export/verification tools use.
    /// </summary>
    private static bool EnsureMapAndTransform(Node3D contextNode)
    {
        if (_transformResolved && _mapCreated)
        {
            return true;
        }

        if (!_transformResolved)
        {
            var terrain = MonsterSpawnGroundQuery.TryResolveTerrainGridMap(contextNode);
            if (terrain is null || terrain.GetParent() is not Node3D terrainGridNode)
            {
                return false;
            }

            _tileWorldOrigin = terrain.Position;
            _bakedToWorld = terrainGridNode.GlobalTransform;

            _tileIndex = TerrainObjectsFill.TerrainTileGridIndex.TryBuild(MapBinPath, TileSizeWorld, _tileWorldOrigin);
            if (_tileIndex is null)
            {
                return false;
            }

            _transformResolved = true;
        }

        if (!_mapCreated)
        {
            _map = NavigationServer3D.MapCreate();
            NavigationServer3D.MapSetUp(_map, Vector3.Up);
            NavigationServer3D.MapSetCellSize(_map, CellSize);
            NavigationServer3D.MapSetCellHeight(_map, CellHeight);
            NavigationServer3D.MapSetActive(_map, true);
            _mapCreated = true;
        }

        return true;
    }
}
