using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using SphServer.Godot.Scripts.Util;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Godot.Scripts.Terrain.Fill;

namespace SphServer.Godot.Scripts.Navigation;

/// <summary>
/// NavigationServer3D throws if queried before the first physics-frame sync
/// </summary>
public static class TerrainNavMeshRuntime
{
    // Trailing slash so callers can concatenate the tile file name
    public static string NavMeshResourcesDirectory => TerrainBakePaths.GeneratedNavMeshesDir.TrimEnd ('/') + "/";
    public static string IndoorNavMeshResourcesDirectory =>
        TerrainBakePaths.GeneratedIndoorNavMeshesDir.TrimEnd ('/') + "/";
    public const string MapBinPath = "res://Godot/Terrain/map.txt";
    public const float TileSizeWorld = 100f;

    // Same cell size as TerrainNavigationBaker, or query precision drifts from the bake
    public const float CellSize = 0.1f;
    public const float CellHeight = 0.1f;

    private const float HorizontalSnapToleranceMeters = 0.2f;

    // First snap already this tight in XZ skips the second closest-point
    private const float SkipYRefineHorizontalToleranceMeters = HorizontalSnapToleranceMeters * 0.5f;

    // Poll cap only. Readiness is MapGetIterationId plus a probe, because editor physics is
    // irregular
    private const int MaxSyncWaitFrames = 60;

    public enum DiscQueryMode
    {
        /// <summary>
        /// 8-point disc plus Y refine (runtime / single-spawner bake)
        /// </summary>
        Full,

        /// <summary>
        /// 4-point cardinal disc; skips Y refine when the first snap is already tight (batch bake)
        /// </summary>
        BakeFast,
    }

    private static readonly ConcurrentDictionary<string, Rid> RegionsByTileKey = new ();

    private static readonly JsonSerializerOptions IndoorIndexJsonOptions = new ()
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
    /// TerrainObjects local to nav-local is Ry(-90 deg) + OBJECT_ORIGIN_SHIFT; spawners stay
    /// SOURCE_BASIS, indoor verts are nav-local
    /// </summary>
    private static Transform3D _objectsToGridLocal = Transform3D.Identity;
    private static bool _objectsToGridResolved;

    /// <summary>
    /// False until the first sync; queries return not-walkable instead of throwing before that
    /// </summary>
    public static bool IsReadyForQueries => _hasEverSynced;

    public static int LoadedRegionCount => RegionsByTileKey.Count;

    public static bool HasAnyTileFiles ()
    {
        return HasResFiles (NavMeshResourcesDirectory) || HasResFiles (IndoorNavMeshResourcesDirectory);
    }

    private static bool HasResFiles (string directory) =>
        ResPathIO.DirectoryHasFileSuffix (directory, ".res");

    /// <summary>
    /// Drops the map too, so the next full rebake starts empty
    /// </summary>
    public static void Invalidate ()
    {
        UnloadAllRegions ();

        if (_mapCreated)
        {
            NavigationServer3D.FreeRid (_map);
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
    /// Drops regions only, so a spatial bake batch does not hold the whole world in
    /// NavigationServer
    /// </summary>
    public static void UnloadAllRegions ()
    {
        foreach (var region in RegionsByTileKey.Values)
        {
            NavigationServer3D.FreeRid (region);
        }

        RegionsByTileKey.Clear ();
        _hasEverSynced = false;
        _pendingSync = false;
    }

    /// <summary>
    /// True when a new region was registered. Queries on that area need a sync first
    /// </summary>
    public static bool EnsureTilesLoaded (Node3D contextNode, Vector3 worldCenter, float radiusMeters)
    {
        if (!EnsureMapAndTransform (contextNode) || _tileIndex is null)
        {
            return false;
        }

        var localCenter = _bakedToWorld.AffineInverse () * worldCenter;
        var minGx = Mathf.FloorToInt ((localCenter.X - _tileWorldOrigin.X - radiusMeters) / TileSizeWorld);
        var maxGx = Mathf.FloorToInt ((localCenter.X - _tileWorldOrigin.X + radiusMeters) / TileSizeWorld);
        var minGz = Mathf.FloorToInt ((localCenter.Z - _tileWorldOrigin.Z - radiusMeters) / TileSizeWorld);
        var maxGz = Mathf.FloorToInt ((localCenter.Z - _tileWorldOrigin.Z + radiusMeters) / TileSizeWorld);

        var newlyLoaded = false;
        for (var gz = minGz; gz <= maxGz; gz++)
        {
            for (var gx = minGx; gx <= maxGx; gx++)
            {
                var tileCenterLocal = _tileWorldOrigin + new Vector3 (
                    (gx + 0.5f) * TileSizeWorld,
                    0f,
                    (gz + 0.5f) * TileSizeWorld);

                if (!_tileIndex.TryGetTile (tileCenterLocal, out var masterName, out var occurrence))
                {
                    continue;
                }

                var tileKey = TerrainObjectsFill.TerrainTileGridIndex.BuildTileGroupKey (masterName, occurrence);
                if (LoadAndRegisterNavRes (tileKey, TerrainBakePaths.NavMeshRes (tileKey)))
                {
                    newlyLoaded = true;
                }
            }
        }

        // Indoor cluster verts are nav-local; spawn bake passes SOURCE_BASIS GlobalPosition.
        // Outdoor tiles already line up with spawner space
        var indoorLocalCenter = SpawnerSpaceToNavLocal (worldCenter);
        if (LoadNearbyIndoorClusters (indoorLocalCenter, radiusMeters))
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
    /// force re-probes a map already marked synced. A lock cannot await this
    /// </summary>
    public static async Task SyncAsync (SceneTree tree, bool force = false)
    {
        if (!_mapCreated)
        {
            return;
        }

        if (!force && !_pendingSync && _hasEverSynced)
        {
            return;
        }

        var iterationBefore = NavigationServer3D.MapGetIterationId (_map);
        // A forced re-probe of an already-synced map may not bump MapGetIterationId
        var requireIterationAdvance = _pendingSync;

        // Editor @tool must not await PhysicsFrame: it can stop emitting and hang BakeAll. Headless
        // needs one PhysicsFrame
        if (TryMarkSynced (iterationBefore, requireIterationAdvance)
            || TryMarkSynced (iterationBefore, requireIterationAdvance: false))
        {
            return;
        }

        if (MonsterSpawnSlotHeadlessBake.IsActive || AlchemyMaterialSpawnSlotHeadlessBake.IsActive)
        {
            await tree.ToSignal (tree, SceneTree.SignalName.PhysicsFrame);
            if (TryMarkSynced (iterationBefore, requireIterationAdvance: false))
            {
                return;
            }
        }

        for (var i = 0; i < MaxSyncWaitFrames; i++)
        {
            await tree.ToSignal (tree, SceneTree.SignalName.ProcessFrame);
            if (TryMarkSynced (iterationBefore, requireIterationAdvance: false))
            {
                return;
            }
        }

        if (!TryMarkSynced (iterationBefore, requireIterationAdvance: false))
        {
            GD.PushWarning (
                "TerrainNavMeshRuntime: navigation map did not become queryable after sync wait; "
                + "spawn-slot bake may report false NotWalkable failures.");
        }
    }

    /// <summary>
    /// True only when the map is already queryable. A locked caller retries next frame
    /// </summary>
    public static bool TrySyncImmediate (bool force = false)
    {
        if (!_mapCreated)
        {
            return false;
        }

        if (!force && !_pendingSync && _hasEverSynced)
        {
            return true;
        }

        var iterationBefore = NavigationServer3D.MapGetIterationId (_map);
        var requireIterationAdvance = _pendingSync;
        return TryMarkSynced (iterationBefore, requireIterationAdvance)
               || TryMarkSynced (iterationBefore, requireIterationAdvance: false);
    }

    private static bool TryMarkSynced (uint iterationBefore, bool requireIterationAdvance)
    {
        if (!_mapCreated)
        {
            return false;
        }

        try
        {
            var iteration = NavigationServer3D.MapGetIterationId (_map);
            if (iteration == 0)
            {
                return false;
            }

            // New regions need a sync that advances the iteration past the id seen before the wait
            if (requireIterationAdvance && iterationBefore != 0 && iteration == iterationBefore)
            {
                return false;
            }

            // Before a real sync, MapGetClosestPoint returns Vector3.Zero without throwing.
            // Remapping that Zero is indoor nav Y=1 (WrongLevel)
            var probe = NavigationServer3D.MapGetClosestPoint (_map, Vector3.Zero);
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
    /// radiusMeters is a mob-body ring around worldPos. refinedCenter is worldPos snapped to
    /// navmesh ground Y
    /// </summary>
    public static bool IsDiscWalkable (Vector3 worldPos, float radiusMeters, out Vector3 refinedCenter)
        => IsDiscWalkable (worldPos, radiusMeters, DiscQueryMode.Full, out refinedCenter);

    public static bool IsDiscWalkable (
        Vector3 worldPos,
        float radiusMeters,
        DiscQueryMode mode,
        out Vector3 refinedCenter)
    {
        refinedCenter = worldPos;

        // Center always allows Y-refine (slot height comes from this snap); ring points in BakeFast
        // skip it
        if (!IsPointOnNavMesh (worldPos, out var snappedCenter, refineY: true))
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
            // Ring probes use the center's snapped ground Y. A floating marker Y with BakeFast
            // refineY:false rejects a disc that is on nav
            var ringPoint = new Vector3 (
                worldPos.X + offsetX * radiusMeters,
                snappedCenter.Y,
                worldPos.Z + offsetZ * radiusMeters);
            if (!IsPointOnNavMesh (ringPoint, out _, refineRingY))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// snapped includes navmesh ground Y. worldPos.Y is not the height check
    /// </summary>
    public static bool IsPointOnNavMesh (Vector3 worldPos, out Vector3 snapped)
        => IsPointOnNavMesh (worldPos, out snapped, refineY: true);

    public static bool IsPointOnNavMesh (Vector3 worldPos, out Vector3 snapped, bool refineY)
    {
        snapped = worldPos;

        if (!TryClosestPoint (worldPos, out var closest))
        {
            return false;
        }

        var dx = closest.X - worldPos.X;
        var dz = closest.Z - worldPos.Z;
        var horizontalDistSq = dx * dx + dz * dz;
        var alreadyTight = horizontalDistSq
                           <= SkipYRefineHorizontalToleranceMeters * SkipYRefineHorizontalToleranceMeters;

        // worldPos.Y is a coarse guess. MapGetClosestPoint is 3D nearest-neighbor, so a bad Y locks
        // onto another polygon unless the first snap is already tight in XZ
        if (refineY && !alreadyTight)
        {
            var refinedProbe = new Vector3 (worldPos.X, closest.Y, worldPos.Z);
            if (TryClosestPoint (refinedProbe, out var refined))
            {
                closest = refined;
                dx = closest.X - worldPos.X;
                dz = closest.Z - worldPos.Z;
                horizontalDistSq = dx * dx + dz * dz;
            }
        }

        snapped = closest;

        // Only XZ containment decides whether the point is on the mesh here
        return horizontalDistSq <= HorizontalSnapToleranceMeters * HorizontalSnapToleranceMeters;
    }

    /// <summary>
    /// False before the first sync, instead of throwing. worldPos is SOURCE_BASIS; indoor probes
    /// map through TerrainObjects to grid
    /// </summary>
    public static bool TryClosestPoint (Vector3 worldPos, out Vector3 closest)
    {
        closest = worldPos;

        if (!_mapCreated || !_hasEverSynced)
        {
            return false;
        }

        try
        {
            var indoor = IndoorAreaCriteria.IsIndoorDepth (worldPos.Y);
            var queryPos = indoor ? SpawnerSpaceToNavWorld (worldPos) : worldPos;
            var snapped = NavigationServer3D.MapGetClosestPoint (_map, queryPos);
            // Pre-sync sentinel is Vector3.Zero. Remapping it into spawner space becomes indoor Y
            // about 1
            if (snapped == Vector3.Zero && queryPos.LengthSquared () > 1f)
            {
                return false;
            }

            closest = indoor ? NavWorldToSpawnerSpace (snapped) : snapped;
            return true;
        }
        catch (Exception ex)
        {
            // NavigationServer3D still throws if a lock-held caller races the first sync. That path
            // returns not-on-navmesh
            GD.PushWarning ($"TerrainNavMeshRuntime: closest-point query failed ({ex.Message}); treating as unwalkable.");
            return false;
        }
    }

    /// <summary>
    /// Outdoor only. A marker floating above terrain recenters on the first nav hit under the same
    /// XZ, within maxDropMeters
    /// </summary>
    public static bool TryFindNavMeshBelow (Vector3 worldPos, float maxDropMeters, out Vector3 onNav)
    {
        onNav = worldPos;

        if (!_mapCreated || !_hasEverSynced || maxDropMeters <= 0f)
        {
            return false;
        }

        // Indoor probes use a different frame; drop-to-ground is outdoor bake only
        if (IndoorAreaCriteria.IsIndoorDepth (worldPos.Y))
        {
            return false;
        }

        try
        {
            var end = worldPos + new Vector3 (0f, -maxDropMeters, 0f);
            var hit = NavigationServer3D.MapGetClosestPointToSegment (_map, worldPos, end);
            if (hit == Vector3.Zero && worldPos.LengthSquared () > 1f)
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

            // Re-query at the spawner's XZ with the hit Y so the radius center stays under the
            // marker
            var probe = new Vector3 (worldPos.X, hit.Y, worldPos.Z);
            if (!IsPointOnNavMesh (probe, out onNav, refineY: true))
            {
                return false;
            }

            drop = worldPos.Y - onNav.Y;
            return drop >= 0f && drop <= maxDropMeters;
        }
        catch (Exception ex)
        {
            GD.PushWarning (
                $"TerrainNavMeshRuntime: downward nav query failed ({ex.Message}); treating as no ground.");
            return false;
        }
    }

    private static Vector3 SpawnerSpaceToNavLocal (Vector3 spawnerSpace)
        => _objectsToGridLocal * spawnerSpace;

    private static Vector3 SpawnerSpaceToNavWorld (Vector3 spawnerSpace)
        => _bakedToWorld * SpawnerSpaceToNavLocal (spawnerSpace);

    private static Vector3 NavWorldToSpawnerSpace (Vector3 navWorld)
        => _objectsToGridLocal.AffineInverse () * (_bakedToWorld.AffineInverse () * navWorld);

    /// <summary>
    /// Indoor-depth endpoints map through TerrainObjects to grid. Waypoints come back in spawner
    /// space
    /// </summary>
    public static Vector3[] FindPath (Vector3 start, Vector3 end, bool optimize = true)
    {
        if (!_mapCreated || !_hasEverSynced)
        {
            return [];
        }

        try
        {
            var startIndoor = IndoorAreaCriteria.IsIndoorDepth (start.Y);
            var endIndoor = IndoorAreaCriteria.IsIndoorDepth (end.Y);
            var navStart = startIndoor ? SpawnerSpaceToNavWorld (start) : start;
            var navEnd = endIndoor ? SpawnerSpaceToNavWorld (end) : end;
            var path = NavigationServer3D.MapGetPath (_map, navStart, navEnd, optimize);
            if (path.Length == 0)
            {
                return path;
            }

            // Indoor cluster verts are nav-world; remap when either endpoint was indoor-depth
            if (startIndoor || endIndoor)
            {
                for (var i = 0; i < path.Length; i++)
                {
                    path[i] = NavWorldToSpawnerSpace (path[i]);
                }
            }

            return path;
        }
        catch (Exception ex)
        {
            GD.PushWarning ($"TerrainNavMeshRuntime: FindPath failed ({ex.Message}).");
            return [];
        }
    }

    private static bool LoadNearbyIndoorClusters (Vector3 localCenter, float radiusMeters)
    {
        EnsureIndoorIndexLoaded ();
        if (_indoorIndex is null || _indoorIndex.Count == 0)
        {
            return false;
        }

        var newlyLoaded = false;
        var radiusSq = radiusMeters * radiusMeters;
        foreach (var entry in _indoorIndex)
        {
            if (!ClusterIntersectsQuery (entry, localCenter, radiusMeters, radiusSq))
            {
                continue;
            }

            var key = $"indoor_cluster_{entry.Id}";
            var path = string.IsNullOrWhiteSpace (entry.Path)
                ? TerrainBakePaths.IndoorClusterRes (entry.Id)
                : entry.Path;
            if (LoadAndRegisterNavRes (key, path))
            {
                newlyLoaded = true;
            }
        }

        return newlyLoaded;
    }

    private static bool ClusterIntersectsQuery (
        IndoorClusterEntry entry,
        Vector3 localCenter,
        float radiusMeters,
        float radiusSq)
    {
        if (entry.HasAabb)
        {
            // AABB grows by the query radius in XZ and Y, or a nearby probe misses the cluster
            var min = entry.AabbMin - new Vector3 (radiusMeters, radiusMeters, radiusMeters);
            var max = entry.AabbMax + new Vector3 (radiusMeters, radiusMeters, radiusMeters);
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

    private static void EnsureIndoorIndexLoaded ()
    {
        if (_indoorIndexAttempted)
        {
            return;
        }

        _indoorIndexAttempted = true;
        _indoorIndex = new List<IndoorClusterEntry> ();

        var indexPath = TerrainBakePaths.IndoorIndexJson;
        if (ResPathIO.TryReadAllText (indexPath, out var json))
        {
            try
            {
                var file = JsonSerializer.Deserialize<IndoorIndexFile> (json, IndoorIndexJsonOptions);
                if (file?.Clusters != null)
                {
                    foreach (var c in file.Clusters)
                    {
                        if (TryParseIndoorEntry (c, out var entry))
                        {
                            _indoorIndex.Add (entry);
                        }
                    }
                }

                return;
            }
            catch (Exception ex)
            {
                GD.PushWarning ($"TerrainNavMeshRuntime: failed to read indoor nav index ({ex.Message}); scanning sidecars.");
            }
        }

        var dir = TerrainBakePaths.GeneratedIndoorNavMeshesDir;
        foreach (var name in ResPathIO.EnumerateFileNames (dir, ".nav.json"))
        {
            if (!name.StartsWith ("cluster_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sidecarPath = ResPathIO.IsVirtualPath (dir)
                ? ResPathIO.JoinVirtual (dir.TrimEnd ('/'), name)
                : Path.Combine (dir.Replace ('/', Path.DirectorySeparatorChar), name);
            try
            {
                if (!ResPathIO.TryReadAllText (sidecarPath, out var sidecarJson))
                {
                    continue;
                }

                var c = JsonSerializer.Deserialize<IndoorClusterJson> (sidecarJson, IndoorIndexJsonOptions);
                if (c != null && TryParseIndoorEntry (c, out var entry))
                {
                    _indoorIndex.Add (entry);
                }
            }
            catch (Exception ex)
            {
                GD.PushWarning ($"TerrainNavMeshRuntime: bad indoor nav sidecar {name} ({ex.Message})");
            }
        }
    }

    private static bool TryParseIndoorEntry (IndoorClusterJson c, out IndoorClusterEntry entry)
    {
        entry = default;
        if (c.Id < 0)
        {
            return false;
        }

        var center = Vec3FromJson (c.CenterNav);
        var hasAabb = false;
        var aabbMin = Vector3.Zero;
        var aabbMax = Vector3.Zero;
        if (c.AabbNav?.Min != null && c.AabbNav.Max != null)
        {
            aabbMin = Vec3FromJson (c.AabbNav.Min);
            aabbMax = Vec3FromJson (c.AabbNav.Max);
            hasAabb = true;
        }

        var path = string.IsNullOrWhiteSpace (c.Path) ? "" : c.Path.Trim ();
        if (string.IsNullOrEmpty (path) || path.StartsWith ("res://", StringComparison.Ordinal)
            || path.Contains ("GeneratedIndoorNavMeshes", StringComparison.OrdinalIgnoreCase))
        {
            path = TerrainBakePaths.IndoorClusterRes (c.Id);
        }

        entry = new IndoorClusterEntry (
            c.Id,
            path,
            center,
            c.Radius > 0 ? c.Radius : 40f,
            hasAabb,
            aabbMin,
            aabbMax);
        return true;
    }

    private static Vector3 Vec3FromJson (JsonVec3? v)
    {
        if (v is null)
        {
            return Vector3.Zero;
        }

        return new Vector3 (v.X, v.Y, v.Z);
    }

    private static bool LoadAndRegisterNavRes (string regionKey, string resourcePath)
    {
        if (RegionsByTileKey.ContainsKey (regionKey))
        {
            return false;
        }

        if (!ResourceLoader.Exists (resourcePath))
        {
            return false;
        }

        var navMesh = ResourceLoader.Load<NavigationMesh> (resourcePath, cacheMode: ResourceLoader.CacheMode.Ignore);
        if (navMesh is null)
        {
            return false;
        }

        var region = NavigationServer3D.RegionCreate ();
        NavigationServer3D.RegionSetMap (region, _map);
        NavigationServer3D.RegionSetEnabled (region, true);
        NavigationServer3D.RegionSetNavigationMesh (region, navMesh);
        NavigationServer3D.RegionSetTransform (region, _bakedToWorld);

        return RegionsByTileKey.TryAdd (regionKey, region);
    }

    private readonly record struct IndoorClusterEntry (
        int Id,
        string Path,
        Vector3 CenterNav,
        float Radius,
        bool HasAabb,
        Vector3 AabbMin,
        Vector3 AabbMax);

    private sealed class IndoorIndexFile
    {
        [JsonPropertyName ("clusters")]
        public List<IndoorClusterJson>? Clusters { get; set; }
    }

    private sealed class IndoorClusterJson
    {
        [JsonPropertyName ("id")]
        public int Id { get; set; } = -1;

        [JsonPropertyName ("path")]
        public string? Path { get; set; }

        [JsonPropertyName ("radius")]
        public float Radius { get; set; }

        [JsonPropertyName ("center_nav")]
        public JsonVec3? CenterNav { get; set; }

        [JsonPropertyName ("aabb_nav")]
        public JsonAabb? AabbNav { get; set; }
    }

    private sealed class JsonAabb
    {
        [JsonPropertyName ("min")]
        public JsonVec3? Min { get; set; }

        [JsonPropertyName ("max")]
        public JsonVec3? Max { get; set; }
    }

    private sealed class JsonVec3
    {
        [JsonPropertyName ("x")]
        public float X { get; set; }

        [JsonPropertyName ("y")]
        public float Y { get; set; }

        [JsonPropertyName ("z")]
        public float Z { get; set; }
    }

    /// <summary>
    /// Baked verts live in the Terrain GridMap frame. GlobalTransform is safe here: this never runs
    /// on an orphan instantiate
    /// </summary>
    private static bool EnsureMapAndTransform (Node3D contextNode)
    {
        if (_transformResolved && _mapCreated)
        {
            return true;
        }

        if (!_transformResolved)
        {
            var terrain = TryResolveTerrainGridMap (contextNode);
            if (terrain is null || terrain.GetParent () is not Node3D terrainGridNode)
            {
                return false;
            }

            _tileWorldOrigin = terrain.Position;
            _bakedToWorld = terrainGridNode.GlobalTransform;
            ResolveObjectsToGridLocal (terrainGridNode);

            _tileIndex = TerrainObjectsFill.TerrainTileGridIndex.TryBuild (MapBinPath, TileSizeWorld, _tileWorldOrigin);
            if (_tileIndex is null)
            {
                return false;
            }

            _transformResolved = true;
        }

        if (!_mapCreated)
        {
            _map = NavigationServer3D.MapCreate ();
            NavigationServer3D.MapSetUp (_map, Vector3.Up);
            NavigationServer3D.MapSetCellSize (_map, CellSize);
            NavigationServer3D.MapSetCellHeight (_map, CellHeight);
            // Async nav iterations made a fixed frame wait flaky for bake tools
            NavigationServer3D.MapSetUseAsyncIterations (_map, false);
            NavigationServer3D.MapSetActive (_map, true);
            _mapCreated = true;
        }

        return true;
    }

    private static void ResolveObjectsToGridLocal (Node3D terrainGridNode)
    {
        if (_objectsToGridResolved)
        {
            return;
        }

        _objectsToGridResolved = true;
        var terrainScene = terrainGridNode.GetParent ();
        var terrainObjects = terrainScene?.GetNodeOrNull<Node3D> ("TerrainObjects");
        if (terrainObjects is not null)
        {
            // Same formula as TerrainObjectsFill.GetObjectsToGridLocalTransform (sibling local
            // transforms)
            _objectsToGridLocal = terrainGridNode.Transform.AffineInverse () * terrainObjects.Transform;
            return;
        }

        // Headless or a missing scene graph uses bake_and_export_single_nav.gd defaults
        _objectsToGridLocal = new Transform3D (
            Basis.FromEuler (new Vector3 (0f, Mathf.DegToRad (-90f), 0f)),
            new Vector3 (4000f, 0f, 4000f));
        GD.PushWarning (
            "TerrainNavMeshRuntime: TerrainObjects not found; using Ry(-90°)+(4000,0,4000) for indoor nav space.");
    }

    private static GridMap? TryResolveTerrainGridMap (Node3D contextNode)
    {
        var tree = contextNode.GetTree ();
        if (tree is null)
        {
            return null;
        }

        foreach (var node in tree.Root.FindChildren ("*", nameof (GridMap), recursive: true, owned: false))
        {
            if (node is GridMap gridMap && node.Name == TerrainGridFill.TerrainNodeName)
            {
                return gridMap;
            }
        }

        return null;
    }
}
