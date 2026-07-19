using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Navigation;

/// <summary>
///     Lazy, proximity-loading bridge from baked <see cref="NavigationMesh" /> resources to a live
///     <see cref="NavigationServer3D" /> map:
///     outdoor tiles under <see cref="NavMeshResourcesDirectory" /> (from
///     <c>Tools/bake_and_export_single_nav.gd</c>) and indoor clusters under
///     <see cref="IndoorNavMeshResourcesDirectory" /> (from
///     <c>Tools/export_all_indoor_clusters.ps1 -WriteNavRes</c>).
///     Regions load only near the queried spawner/agent.
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
    public const string IndoorNavMeshResourcesDirectory = "res://Godot/Terrain/GeneratedIndoorNavMeshes/";
    public const string MapBinPath = "res://Godot/Terrain/map.txt";
    public const float TileSizeWorld = 100f;

    // Must match TerrainNavigationBaker's bake params so query precision matches what was actually baked.
    public const float CellSize = 0.1f;
    public const float CellHeight = 0.1f;

    private const float HorizontalSnapToleranceMeters = 0.2f;

    // Skip the Y-refine second closest-point when the first snap is already this tight horizontally.
    private const float SkipYRefineHorizontalToleranceMeters = HorizontalSnapToleranceMeters * 0.5f;

    // Upper bound while polling for MapGetIterationId to advance. Editor physics can be slow/irregular;
    // readiness is detected from the iteration id + a probe query, not from hitting this cap.
    private const int MaxSyncWaitFrames = 60;

    public enum DiscQueryMode
    {
        /// <summary>8-point disc + Y refine (runtime / single-spawner bake).</summary>
        Full,

        /// <summary>4-point cardinal disc; skips Y refine when first snap is already tight (batch bake).</summary>
        BakeFast,
    }

    private static readonly ConcurrentDictionary<string, Rid> RegionsByTileKey = new();

    private static readonly JsonSerializerOptions IndoorIndexJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static Rid _map;
    private static bool _mapCreated;
    private static bool _hasEverSynced;
    private static bool _pendingSync;

    private static bool _transformResolved;
    private static Transform3D _bakedToWorld = Transform3D.Identity;
    private static Vector3 _tileWorldOrigin;
    private static TerrainObjectsFill.TerrainTileGridIndex? _tileIndex;
    private static List<IndoorClusterEntry>? _indoorIndex;
    private static bool _indoorIndexAttempted;

    /// <summary>
    ///     TerrainObjects local → TerrainGrid / nav-mesh local (same as bake_and_export_single_nav.gd
    ///     <c>Ry(-90°)+OBJECT_ORIGIN_SHIFT</c>). MonsterSpawners live in TerrainObjects/SOURCE_BASIS space;
    ///     indoor <see cref="IndoorNavMeshResourcesDirectory" /> verts are in this nav-local frame.
    /// </summary>
    private static Transform3D _objectsToGridLocal = Transform3D.Identity;
    private static bool _objectsToGridResolved;

    /// <summary>
    ///     False until at least one <see cref="SyncAsync" /> has completed. Query methods return "not walkable"
    ///     rather than calling into <see cref="NavigationServer3D" /> while this is false, since querying a
    ///     navigation map before its first-ever sync throws.
    /// </summary>
    public static bool IsReadyForQueries => _hasEverSynced;

    /// <summary>Number of navmesh tile regions currently registered on the bake map.</summary>
    public static int LoadedRegionCount => RegionsByTileKey.Count;

    public static bool HasAnyTileFiles()
    {
        return HasResFiles(NavMeshResourcesDirectory) || HasResFiles(IndoorNavMeshResourcesDirectory);
    }

    private static bool HasResFiles(string resDirectory)
    {
        var absoluteDirectory = ProjectSettings.GlobalizePath(resDirectory);
        return Directory.Exists(absoluteDirectory) && Directory.GetFiles(absoluteDirectory, "*.res").Length > 0;
    }

    /// <summary>Frees every registered region and the navigation map. Call after a full nav rebake.</summary>
    public static void Invalidate()
    {
        UnloadAllRegions();

        if (_mapCreated)
        {
            NavigationServer3D.FreeRid(_map);
            _map = default;
            _mapCreated = false;
        }

        _transformResolved = false;
        _tileIndex = null;
        _indoorIndex = null;
        _indoorIndexAttempted = false;
        _objectsToGridResolved = false;
        _objectsToGridLocal = Transform3D.Identity;
    }

    /// <summary>
    ///     Frees registered nav regions but keeps the map + terrain transform. Used between spatial bake
    ///     batches so the whole world is not resident in NavigationServer at once.
    /// </summary>
    public static void UnloadAllRegions()
    {
        foreach (var region in RegionsByTileKey.Values)
        {
            NavigationServer3D.FreeRid(region);
        }

        RegionsByTileKey.Clear();
        _hasEverSynced = false;
        _pendingSync = false;
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
                if (LoadAndRegisterNavRes(tileKey, $"{NavMeshResourcesDirectory}{tileKey}.res"))
                {
                    newlyLoaded = true;
                }
            }
        }

        // Indoor cluster index / .res verts are in objects→grid (nav-local) space. Spawn bake passes
        // MonsterSpawner GlobalPosition (SOURCE_BASIS / TerrainObjects-local), which is NOT nav-local —
        // convert before proximity test. Outdoor tile lookup above keeps using TerrainGrid.inv * pos
        // (unchanged; outdoor terrain nav already lines up with spawner space).
        var indoorLocalCenter = SpawnerSpaceToNavLocal(worldCenter);
        if (LoadNearbyIndoorClusters(indoorLocalCenter, radiusMeters))
        {
            newlyLoaded = true;
        }

        if (newlyLoaded)
        {
            _pendingSync = true;
        }

        return newlyLoaded;
    }

    /// <summary>
    ///     Waits until newly-registered regions (from <see cref="EnsureTilesLoaded" />) are actually queryable.
    ///     No-op if nothing new was registered since the last successful sync. Only call from a context that
    ///     can await - never from inside a <c>lock</c> (use <see cref="TrySyncImmediate" /> there instead).
    /// </summary>
    /// <remarks>
    ///     Godot only applies region/map changes at physics-frame sync (or via
    ///     <see cref="NavigationServer3D.MapForceUpdate" />). A fixed frame wait is not enough: async map
    ///     iterations can lag, and the editor may barely tick physics - both used to make spawn-slot baking
    ///     fail intermittently on valid spawners (retry later succeeded once the map had quietly finished
    ///     syncing). We poll <see cref="NavigationServer3D.MapGetIterationId" /> and a probe query instead.
    /// </remarks>
    /// <param name="force">
    ///     When true, re-flush/probe even if we already marked ourselves synced. Used by bake retries after a
    ///     false NotWalkable failure so we do not early-out while the map is still settling.
    /// </param>
    public static async Task SyncAsync(SceneTree tree, bool force = false)
    {
        if (!_mapCreated)
        {
            return;
        }

        if (!force && !_pendingSync && _hasEverSynced)
        {
            return;
        }

        var iterationBefore = NavigationServer3D.MapGetIterationId(_map);
        // Only require an iteration bump when new regions were registered; a forced re-probe of an already
        // synced map may not change the id.
        var requireIterationAdvance = _pendingSync;

        // MapForceUpdate alone leaves iteration_id=1 and MapGetClosestPoint returns Vector3.Zero until the
        // server has completed a real sync pass. Headless needs one PhysicsFrame; the editor @tool path
        // must not await PhysicsFrame (it can stop emitting and hang BakeAll / the spawner plugin).
        TryForceMapUpdate();
        if (TryMarkSynced(iterationBefore, requireIterationAdvance)
            || TryMarkSynced(iterationBefore, requireIterationAdvance: false))
        {
            return;
        }

        if (MonsterSpawnSlotHeadlessBake.IsActive || AlchemyMaterialSpawnSlotHeadlessBake.IsActive)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            TryForceMapUpdate();
            if (TryMarkSynced(iterationBefore, requireIterationAdvance: false))
            {
                return;
            }
        }

        for (var i = 0; i < MaxSyncWaitFrames; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            TryForceMapUpdate();
            if (TryMarkSynced(iterationBefore, requireIterationAdvance: false))
            {
                return;
            }
        }

        TryForceMapUpdate();
        if (!TryMarkSynced(iterationBefore, requireIterationAdvance: false))
        {
            GD.PushWarning(
                "TerrainNavMeshRuntime: navigation map did not become queryable after sync wait; "
                + "spawn-slot bake may report false NotWalkable failures.");
        }
    }

    /// <summary>
    ///     Best-effort synchronous sync for callers that cannot await (e.g. locked runtime respawn). Uses
    ///     <see cref="NavigationServer3D.MapForceUpdate" />. Returns true when the map is queryable afterward.
    /// </summary>
    public static bool TrySyncImmediate(bool force = false)
    {
        if (!_mapCreated)
        {
            return false;
        }

        if (!force && !_pendingSync && _hasEverSynced)
        {
            return true;
        }

        var iterationBefore = NavigationServer3D.MapGetIterationId(_map);
        var requireIterationAdvance = _pendingSync;
        TryForceMapUpdate();
        return TryMarkSynced(iterationBefore, requireIterationAdvance)
               || TryMarkSynced(iterationBefore, requireIterationAdvance: false);
    }

    private static void TryForceMapUpdate()
    {
        if (!_mapCreated)
        {
            return;
        }

        try
        {
            NavigationServer3D.MapForceUpdate(_map);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"TerrainNavMeshRuntime: MapForceUpdate failed ({ex.Message}); waiting on frames instead.");
        }
    }

    private static bool TryMarkSynced(uint iterationBefore, bool requireIterationAdvance)
    {
        if (!_mapCreated)
        {
            return false;
        }

        try
        {
            var iteration = NavigationServer3D.MapGetIterationId(_map);
            if (iteration == 0)
            {
                return false;
            }

            // New regions require a sync that advances the iteration past what we observed before waiting.
            if (requireIterationAdvance && iterationBefore != 0 && iteration == iterationBefore)
            {
                return false;
            }

            // Confirms queries no longer throw "before first map synchronization".
            // IMPORTANT: before a real sync pass Godot returns Vector3.Zero without throwing — that must
            // not count as ready (indoor WrongLevel then reports nav Y=1 after SOURCE_BASIS remap).
            var probe = NavigationServer3D.MapGetClosestPoint(_map, Vector3.Zero);
            if (RegionsByTileKey.Count > 0 && probe == Vector3.Zero && iteration < 2)
            {
                return false;
            }

            _pendingSync = false;
            _hasEverSynced = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     True when <paramref name="worldPos" /> (and, implicitly, the small ring of points
    ///     <paramref name="radiusMeters" /> around it used to approximate a mob-body footprint) sits on the
    ///     loaded navmesh. <paramref name="refinedCenter" /> is <paramref name="worldPos" /> snapped to the
    ///     navmesh's actual surface height, so callers get an accurate ground Y as a side effect of validation.
    /// </summary>
    public static bool IsDiscWalkable(Vector3 worldPos, float radiusMeters, out Vector3 refinedCenter)
        => IsDiscWalkable(worldPos, radiusMeters, DiscQueryMode.Full, out refinedCenter);

    public static bool IsDiscWalkable(
        Vector3 worldPos,
        float radiusMeters,
        DiscQueryMode mode,
        out Vector3 refinedCenter)
    {
        refinedCenter = worldPos;

        // Center always allows Y-refine (slot height comes from this snap); ring points in BakeFast skip it.
        if (!IsPointOnNavMesh(worldPos, out var snappedCenter, refineY: true))
        {
            return false;
        }

        refinedCenter = snappedCenter;

        var ring = mode == DiscQueryMode.BakeFast
            ? NavMobBodyDisk.CardinalOffsets
            : NavMobBodyDisk.Offsets;
        var refineRingY = mode == DiscQueryMode.Full;
        foreach (var (offsetX, offsetZ) in ring)
        {
            // Probe ring at the center's snapped ground Y. Using the caller's coarse Y (often a floating
            // spawner marker) with BakeFast's refineY:false reject makes whole discs look unwalkable
            // even when the XZ footprint is on nav.
            var ringPoint = new Vector3(
                worldPos.X + offsetX * radiusMeters,
                snappedCenter.Y,
                worldPos.Z + offsetZ * radiusMeters);
            if (!IsPointOnNavMesh(ringPoint, out _, refineRingY))
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
        => IsPointOnNavMesh(worldPos, out snapped, refineY: true);

    public static bool IsPointOnNavMesh(Vector3 worldPos, out Vector3 snapped, bool refineY)
    {
        snapped = worldPos;

        if (!TryClosestPoint(worldPos, out var closest))
        {
            return false;
        }

        var dx = closest.X - worldPos.X;
        var dz = closest.Z - worldPos.Z;
        var horizontalDistSq = dx * dx + dz * dz;
        var alreadyTight = horizontalDistSq
                           <= SkipYRefineHorizontalToleranceMeters * SkipYRefineHorizontalToleranceMeters;

        // worldPos.Y is only ever a coarse guess. MapGetClosestPoint is a full 3D nearest-neighbor search,
        // so a bad Y can lock onto an unrelated polygon. Re-probe once using the first pass's Y — unless the
        // first snap is already horizontally tight (common on flat terrain / bake-fast path).
        if (refineY && !alreadyTight)
        {
            var refinedProbe = new Vector3(worldPos.X, closest.Y, worldPos.Z);
            if (TryClosestPoint(refinedProbe, out var refined))
            {
                closest = refined;
                dx = closest.X - worldPos.X;
                dz = closest.Z - worldPos.Z;
                horizontalDistSq = dx * dx + dz * dz;
            }
        }

        snapped = closest;

        // Only horizontal (XZ) containment judges "is this actually on the mesh here".
        return horizontalDistSq <= HorizontalSnapToleranceMeters * HorizontalSnapToleranceMeters;
    }

    /// <summary>
    ///     Raw closest-point query. Returns false (rather than throwing) before the map's first sync.
    ///     <paramref name="worldPos" /> is spawner/SOURCE_BASIS space (same as <c>MonsterSpawner.GlobalPosition</c>);
    ///     indoor-depth probes are mapped into NavigationServer world via TerrainObjects→grid.
    /// </summary>
    public static bool TryClosestPoint(Vector3 worldPos, out Vector3 closest)
    {
        closest = worldPos;

        if (!_mapCreated || !_hasEverSynced)
        {
            return false;
        }

        try
        {
            var indoor = IndoorAreaCriteria.IsIndoorDepth(worldPos.Y);
            var queryPos = indoor ? SpawnerSpaceToNavWorld(worldPos) : worldPos;
            var snapped = NavigationServer3D.MapGetClosestPoint(_map, queryPos);
            // Pre-sync / empty-map sentinel: do not remap Zero into spawner space (becomes ~Y=1 indoor).
            if (snapped == Vector3.Zero && queryPos.LengthSquared() > 1f)
            {
                return false;
            }

            closest = indoor ? NavWorldToSpawnerSpace(snapped) : snapped;
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
    ///     Outdoor-only: cast straight down from <paramref name="worldPos" /> up to
    ///     <paramref name="maxDropMeters" /> and return the first navmesh hit under the same XZ.
    ///     Used when a spawner marker floats above terrain so bake can recenter on walkable ground.
    /// </summary>
    public static bool TryFindNavMeshBelow(Vector3 worldPos, float maxDropMeters, out Vector3 onNav)
    {
        onNav = worldPos;

        if (!_mapCreated || !_hasEverSynced || maxDropMeters <= 0f)
        {
            return false;
        }

        // Indoor probes use a different frame; drop-to-ground is an outdoor bake convenience only.
        if (IndoorAreaCriteria.IsIndoorDepth(worldPos.Y))
        {
            return false;
        }

        try
        {
            var end = worldPos + new Vector3(0f, -maxDropMeters, 0f);
            var hit = NavigationServer3D.MapGetClosestPointToSegment(_map, worldPos, end);
            if (hit == Vector3.Zero && worldPos.LengthSquared() > 1f)
            {
                return false;
            }

            var dx = hit.X - worldPos.X;
            var dz = hit.Z - worldPos.Z;
            if (dx * dx + dz * dz
                > HorizontalSnapToleranceMeters * HorizontalSnapToleranceMeters)
            {
                return false;
            }

            var drop = worldPos.Y - hit.Y;
            if (drop < 0f || drop > maxDropMeters)
            {
                return false;
            }

            // Re-query at the spawner's XZ with the hit Y so we keep the radius center under the marker.
            var probe = new Vector3(worldPos.X, hit.Y, worldPos.Z);
            if (!IsPointOnNavMesh(probe, out onNav, refineY: true))
            {
                return false;
            }

            drop = worldPos.Y - onNav.Y;
            return drop >= 0f && drop <= maxDropMeters;
        }
        catch (Exception ex)
        {
            GD.PushWarning(
                $"TerrainNavMeshRuntime: downward nav query failed ({ex.Message}); treating as no ground.");
            return false;
        }
    }

    private static Vector3 SpawnerSpaceToNavLocal(Vector3 spawnerSpace)
        => _objectsToGridLocal * spawnerSpace;

    private static Vector3 SpawnerSpaceToNavWorld(Vector3 spawnerSpace)
        => _bakedToWorld * SpawnerSpaceToNavLocal(spawnerSpace);

    private static Vector3 NavWorldToSpawnerSpace(Vector3 navWorld)
        => _objectsToGridLocal.AffineInverse() * (_bakedToWorld.AffineInverse() * navWorld);

    /// <summary>
    ///     Recast path between spawner-space endpoints. Indoor-depth points are mapped through
    ///     TerrainObjects→grid (same as <see cref="TryClosestPoint" />); waypoints are returned in
    ///     spawner space.
    /// </summary>
    public static Vector3[] FindPath(Vector3 start, Vector3 end, bool optimize = true)
    {
        if (!_mapCreated || !_hasEverSynced)
        {
            return [];
        }

        try
        {
            var startIndoor = IndoorAreaCriteria.IsIndoorDepth(start.Y);
            var endIndoor = IndoorAreaCriteria.IsIndoorDepth(end.Y);
            var navStart = startIndoor ? SpawnerSpaceToNavWorld(start) : start;
            var navEnd = endIndoor ? SpawnerSpaceToNavWorld(end) : end;
            var path = NavigationServer3D.MapGetPath(_map, navStart, navEnd, optimize);
            if (path.Length == 0)
            {
                return path;
            }

            // Indoor cluster verts live in nav-world; remap when either endpoint was indoor-depth.
            if (startIndoor || endIndoor)
            {
                for (var i = 0; i < path.Length; i++)
                {
                    path[i] = NavWorldToSpawnerSpace(path[i]);
                }
            }

            return path;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"TerrainNavMeshRuntime: FindPath failed ({ex.Message}).");
            return [];
        }
    }

    private static bool LoadNearbyIndoorClusters(Vector3 localCenter, float radiusMeters)
    {
        EnsureIndoorIndexLoaded();
        if (_indoorIndex is null || _indoorIndex.Count == 0)
        {
            return false;
        }

        var newlyLoaded = false;
        var radiusSq = radiusMeters * radiusMeters;
        foreach (var entry in _indoorIndex)
        {
            if (!ClusterIntersectsQuery(entry, localCenter, radiusMeters, radiusSq))
            {
                continue;
            }

            var key = $"indoor_cluster_{entry.Id}";
            var path = string.IsNullOrWhiteSpace(entry.Path)
                ? $"{IndoorNavMeshResourcesDirectory}cluster_{entry.Id}.res"
                : entry.Path;
            if (LoadAndRegisterNavRes(key, path))
            {
                newlyLoaded = true;
            }
        }

        return newlyLoaded;
    }

    private static bool ClusterIntersectsQuery(
        IndoorClusterEntry entry,
        Vector3 localCenter,
        float radiusMeters,
        float radiusSq)
    {
        if (entry.HasAabb)
        {
            // Expand AABB by query radius (XZ + Y) so a nearby probe still pulls the cluster in.
            var min = entry.AabbMin - new Vector3(radiusMeters, radiusMeters, radiusMeters);
            var max = entry.AabbMax + new Vector3(radiusMeters, radiusMeters, radiusMeters);
            return localCenter.X >= min.X && localCenter.X <= max.X
                   && localCenter.Y >= min.Y && localCenter.Y <= max.Y
                   && localCenter.Z >= min.Z && localCenter.Z <= max.Z;
        }

        var dx = entry.CenterNav.X - localCenter.X;
        var dy = entry.CenterNav.Y - localCenter.Y;
        var dz = entry.CenterNav.Z - localCenter.Z;
        var reach = entry.Radius + radiusMeters;
        return dx * dx + dy * dy + dz * dz <= reach * reach || dx * dx + dz * dz <= radiusSq;
    }

    private static void EnsureIndoorIndexLoaded()
    {
        if (_indoorIndexAttempted)
        {
            return;
        }

        _indoorIndexAttempted = true;
        _indoorIndex = new List<IndoorClusterEntry>();

        var indexPath = ProjectSettings.GlobalizePath($"{IndoorNavMeshResourcesDirectory}index.json");
        if (File.Exists(indexPath))
        {
            try
            {
                var json = File.ReadAllText(indexPath);
                if (json.Length > 0 && json[0] == '\uFEFF')
                {
                    json = json[1..];
                }

                var file = JsonSerializer.Deserialize<IndoorIndexFile>(json, IndoorIndexJsonOptions);
                if (file?.Clusters != null)
                {
                    foreach (var c in file.Clusters)
                    {
                        if (TryParseIndoorEntry(c, out var entry))
                        {
                            _indoorIndex.Add(entry);
                        }
                    }
                }

                return;
            }
            catch (Exception ex)
            {
                GD.PushWarning($"TerrainNavMeshRuntime: failed to read indoor nav index ({ex.Message}); scanning sidecars.");
            }
        }

        var dir = ProjectSettings.GlobalizePath(IndoorNavMeshResourcesDirectory);
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var sidecar in Directory.GetFiles(dir, "cluster_*.nav.json"))
        {
            try
            {
                var json = File.ReadAllText(sidecar);
                var c = JsonSerializer.Deserialize<IndoorClusterJson>(json, IndoorIndexJsonOptions);
                if (c != null && TryParseIndoorEntry(c, out var entry))
                {
                    _indoorIndex.Add(entry);
                }
            }
            catch (Exception ex)
            {
                GD.PushWarning($"TerrainNavMeshRuntime: bad indoor nav sidecar {Path.GetFileName(sidecar)} ({ex.Message})");
            }
        }
    }

    private static bool TryParseIndoorEntry(IndoorClusterJson c, out IndoorClusterEntry entry)
    {
        entry = default;
        if (c.Id < 0)
        {
            return false;
        }

        var center = Vec3FromJson(c.CenterNav);
        var hasAabb = false;
        var aabbMin = Vector3.Zero;
        var aabbMax = Vector3.Zero;
        if (c.AabbNav?.Min != null && c.AabbNav.Max != null)
        {
            aabbMin = Vec3FromJson(c.AabbNav.Min);
            aabbMax = Vec3FromJson(c.AabbNav.Max);
            hasAabb = true;
        }

        entry = new IndoorClusterEntry(
            c.Id,
            string.IsNullOrWhiteSpace(c.Path) ? "" : c.Path.Trim(),
            center,
            c.Radius > 0 ? c.Radius : 40f,
            hasAabb,
            aabbMin,
            aabbMax);
        return true;
    }

    private static Vector3 Vec3FromJson(JsonVec3? v)
    {
        if (v is null)
        {
            return Vector3.Zero;
        }

        return new Vector3(v.X, v.Y, v.Z);
    }

    private static bool LoadAndRegisterNavRes(string regionKey, string resourcePath)
    {
        if (RegionsByTileKey.ContainsKey(regionKey))
        {
            return false;
        }

        if (!ResourceLoader.Exists(resourcePath))
        {
            return false;
        }

        var navMesh = ResourceLoader.Load<NavigationMesh>(resourcePath, cacheMode: ResourceLoader.CacheMode.Ignore);
        if (navMesh is null)
        {
            return false;
        }

        var region = NavigationServer3D.RegionCreate();
        NavigationServer3D.RegionSetMap(region, _map);
        NavigationServer3D.RegionSetEnabled(region, true);
        NavigationServer3D.RegionSetNavigationMesh(region, navMesh);
        NavigationServer3D.RegionSetTransform(region, _bakedToWorld);

        return RegionsByTileKey.TryAdd(regionKey, region);
    }

    private readonly record struct IndoorClusterEntry(
        int Id,
        string Path,
        Vector3 CenterNav,
        float Radius,
        bool HasAabb,
        Vector3 AabbMin,
        Vector3 AabbMax);

    private sealed class IndoorIndexFile
    {
        [JsonPropertyName("clusters")]
        public List<IndoorClusterJson>? Clusters { get; set; }
    }

    private sealed class IndoorClusterJson
    {
        [JsonPropertyName("id")]
        public int Id { get; set; } = -1;

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("radius")]
        public float Radius { get; set; }

        [JsonPropertyName("center_nav")]
        public JsonVec3? CenterNav { get; set; }

        [JsonPropertyName("aabb_nav")]
        public JsonAabb? AabbNav { get; set; }
    }

    private sealed class JsonAabb
    {
        [JsonPropertyName("min")]
        public JsonVec3? Min { get; set; }

        [JsonPropertyName("max")]
        public JsonVec3? Max { get; set; }
    }

    private sealed class JsonVec3
    {
        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("z")]
        public float Z { get; set; }
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
            var terrain = TryResolveTerrainGridMap(contextNode);
            if (terrain is null || terrain.GetParent() is not Node3D terrainGridNode)
            {
                return false;
            }

            _tileWorldOrigin = terrain.Position;
            _bakedToWorld = terrainGridNode.GlobalTransform;
            ResolveObjectsToGridLocal(terrainGridNode);

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
            // Deterministic physics-frame sync: async iterations made "wait N frames" flaky for bake tools.
            NavigationServer3D.MapSetUseAsyncIterations(_map, false);
            NavigationServer3D.MapSetActive(_map, true);
            _mapCreated = true;
        }

        return true;
    }

    private static void ResolveObjectsToGridLocal(Node3D terrainGridNode)
    {
        if (_objectsToGridResolved)
        {
            return;
        }

        _objectsToGridResolved = true;
        var terrainScene = terrainGridNode.GetParent();
        var terrainObjects = terrainScene?.GetNodeOrNull<Node3D>("TerrainObjects");
        if (terrainObjects is not null)
        {
            // Same formula as TerrainObjectsFill.GetObjectsToGridLocalTransform (sibling local xforms).
            _objectsToGridLocal = terrainGridNode.Transform.AffineInverse() * terrainObjects.Transform;
            return;
        }

        // Headless / missing scene graph: match bake_and_export_single_nav.gd defaults.
        _objectsToGridLocal = new Transform3D(
            Basis.FromEuler(new Vector3(0f, Mathf.DegToRad(-90f), 0f)),
            new Vector3(4000f, 0f, 4000f));
        GD.PushWarning(
            "TerrainNavMeshRuntime: TerrainObjects not found; using Ry(-90°)+(4000,0,4000) for indoor nav space.");
    }

    private static GridMap? TryResolveTerrainGridMap(Node3D contextNode)
    {
        var tree = contextNode.GetTree();
        if (tree is null)
        {
            return null;
        }

        foreach (var node in tree.Root.FindChildren("*", nameof(GridMap), recursive: true, owned: false))
        {
            if (node is GridMap gridMap && node.Name == TerrainGridFill.TerrainNodeName)
            {
                return gridMap;
            }
        }

        return null;
    }
}
