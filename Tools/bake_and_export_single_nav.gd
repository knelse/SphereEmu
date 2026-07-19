extends SceneTree

const NavGlbMerge = preload("res://Tools/nav_glb_merge.gd")

# Bakes nav region(s) with object obstructions, then exports side-by-side GLB(s).
# Bake step runs concurrently via NavigationServer3D.bake_from_source_geometry_data_async
# (default: one job per CPU core). Does NOT call TerrainNavigationBaker / RebuildTerrainObjects.
#
# Usage:
#   godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- --tile Cliffn_rd05_00_04 --out D:/1
#   godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- --count 50 --out D:/1
#   godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- --count 50 --jobs 8 --out D:/1
#   godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- --all --out D:/1
#   godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- --skip 200 --count 1000 --out D:/1
#   godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- --master fill_00 --bake-only
#   godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- --missing --bake-only
#   # Towns: ALWAYS bake as a 2x2 combined block (never 1x1). Prefer Tools/bake_town_nav.ps1.
#   godot ... -- --tile Town4_00_00 --tile Town4_01_00 --tile Town4_10_00 --tile Town4_11_00 --combined --combined-name Town4
#
# Town bake policy (preview + production .res from --combined):
#   - Town_ph00: NAV_EXPERIMENT_BUILDING_FILL=1 → current fill + fabricated roof caps
#   - All other towns: baseline carve (no fill). Default allowlist is Town_ph00 only.
#   - Combined bake writes four GeneratedNavMeshes/{tile}.res clipped from the continuous mesh.

const NAV_DIR := "res://Godot/Terrain/GeneratedNavMeshes/"
const TILES_DIR := "res://Godot/Terrain/Tiles/"
const OBJECT_DATA_DIR := "res://Godot/Terrain/ObjectDataJson/"
const MODELS_DIR := "res://Godot/Models/"
const MAP_PATH := "res://Godot/Terrain/map.txt"
const GRID_WIDTH := 80
const RECORD_SIZE := 22
const TILE_SIZE := 100.0
# Matches the live "Terrain" GridMap's actual saved Position in terrain_scene.scn, which is
# (0,0,0), not the TerrainGridFill.TerrainWorldOrigin code default of (-4000,0,-4000) — see
# export_world_nav_glb.gd for the on-disk verification. Newly baked tiles must use the same
# origin as the ~3850 already-baked GeneratedNavMeshes/*.res files or they won't line up.
const TERRAIN_ORIGIN := Vector3.ZERO
const SOURCE_BASIS := Basis(Vector3.RIGHT, Vector3.DOWN, Vector3.FORWARD)
const SIDE_GAP := 140.0
## Overridable via NAV_EXPERIMENT_OBST_CELL for resolution-tuning experiments. This is coarser than
## the bake's own CELL_SIZE=0.1 by default - each small clutter object's carved footprint rounds up
## to whole 0.25m cells, and in a dense cluster (boxes/baskets/lights/tree trunks a meter or two
## apart) those rounded-up footprints can touch/merge and seal off real walkable gaps between them
## that a finer carve grid would leave open. See the Town4 s_bld23 courtyard fragmentation
## investigation.
static func _obstruction_cell_size() -> float:
	var env := OS.get_environment("NAV_EXPERIMENT_OBST_CELL")
	if env != "":
		return float(env)
	return 0.25
const SKIP_MASTERS := ["fill_empt_00"]
# TerrainObjects (Ry(-90°) about world origin) and TerrainGrid (its own unrelated translation) are
# independently-offset siblings under TerrainScene; converting a raw object placement into the same
# frame ground tiles/nav use here is Ry(-90°) then this shift — verified against the live scene's
# actual TerrainGrid/TerrainObjects transforms (TerrainGrid.transform.affine_inverse() *
# TerrainObjects.transform == translate(4000.025,0,4000.2) + Ry(-90°)). See export_world_nav_glb.gd.
const OBJECT_ORIGIN_SHIFT := Vector3(4000.0, 0.0, 4000.0)
var _objects_basis := Basis.from_euler(Vector3(0.0, deg_to_rad(-90.0), 0.0))

# Bake params (match TerrainNavigationBaker defaults)
const CELL_SIZE := 0.1
const CELL_HEIGHT := 0.1
const AGENT_RADIUS := 0.25
const AGENT_HEIGHT := 1.8
const AGENT_MAX_CLIMB := 0.3
# Vertical tolerance for the island-reconnect bridge (reattaching Recast tessellation seams on a
# continuous sloped surface, e.g. a decorative embankment ramp). This is deliberately DECOUPLED from
# AGENT_MAX_CLIMB: the agent step-height (0.3m) governs Recast's own walkable filter, but adjacent
# component seams on a 40-50 deg ramp differ in Y by up to ~1m, so a 0.3m reconnect tolerance would
# sever the ramp and prune its upper reach. Overridable via NAV_EXPERIMENT_BRIDGE_CLIMB.
const BRIDGE_MAX_CLIMB := 1.0
const AGENT_MAX_SLOPE_DEFAULT := 70.0
## Preview GLB only: skip object meshes whose pivot is more than this far below sampled ground.
const PREVIEW_OBJECT_MAX_DEPTH_BELOW_GROUND := 500.0

## Overridable via NAV_EXPERIMENT_SLOPE_DEG for threshold-tuning experiments; keeps the walkable-tri
## classification (WALKABLE_SLOPE_MAX_DEG) and Recast's own agent_max_slope filter in lockstep so we
## don't feed faces as "walkable" that Recast's own voxelizer would then reject anyway.
static func _agent_max_slope_deg() -> float:
	var env := OS.get_environment("NAV_EXPERIMENT_SLOPE_DEG")
	if env != "":
		return float(env)
	return AGENT_MAX_SLOPE_DEFAULT

## Recast's own walkable step-height filter (nav.agent_max_climb). Overridable via NAV_EXPERIMENT_CLIMB
## for tuning: too small and stepped ramps/stairs fragment (adjacent treads whose riser exceeds this
## don't connect); too large and short walls become steppable.
static func _agent_max_climb() -> float:
	var env := OS.get_environment("NAV_EXPERIMENT_CLIMB")
	if env != "":
		return float(env)
	return AGENT_MAX_CLIMB

var _mesh_parts_cache: Dictionary = {}
var _objects_by_cell: Dictionary = {} # Vector2i -> Array of placements

var _cells_by_key: Dictionary = {}
var _key_by_gxgz: Dictionary = {} # Vector2i(gx,gz) -> tile_key, for lazy cross-tile neighbor loading
var _keys: PackedStringArray = []
# Cross-tile reachability (NAV_EXPERIMENT_XTILE_REACH=1): defer prune/export until all requested tiles
# are baked, then resolve elevated seam-islands (roofs vs cross-tile ramparts) by lazily loading only
# the neighbors an ambiguous island actually borders. See _finalize_xtile.
var _xtile := false
var _xtile_cache: Dictionary = {} # tile_key -> {nav, tile_min, tile_max, ground_grid, ground_fallback, master, gx, gz, placements, requested}
var _xtile_analysis: Dictionary = {} # tile_key -> classify result (lazy)
var _xtile_requested: Array = [] # tile_keys the user asked to export
var _out_dir := "D:/1"
var _jobs_max := 1
var _next_index := 0
var _inflight := 0
var _ok := 0
var _fail := 0
var _t0_ms := 0
var _finished := false
var _do_export_glb := true
var _skip_arg := 0
# --combined: bake every requested --tile into ONE continuous navmesh (single source geometry, single
# Recast bake) and optionally export one GLB. With --write-res (or --bake-only), also splits the mesh
# into per-tile production .res files. Seams between the cells are baked as one mesh so there is
# nothing to stitch, and orphan-island pruning uses the whole 2x2 block's outer edge as the only
# boundary. Towns MUST use this path — never bake town cells 1x1.
var _combined := false
var _combined_name := "combined"
var _combined_write_res := false

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var tile_key := ""
	var tile_keys := PackedStringArray()
	var count := 0
	var skip := 0
	var bake_all := false
	var export_glb := true
	var master_filter := ""
	var bake_missing := false
	_jobs_max = OS.get_processor_count()
	_out_dir = "D:/1"
	var i := 0
	while i < args.size():
		if args[i] == "--tile" and i + 1 < args.size():
			tile_key = args[i + 1]
			tile_keys.append(args[i + 1]) # accumulate so multiple --tile flags bake together (needed for xtile)
			i += 2
		elif args[i] == "--count" and i + 1 < args.size():
			count = int(args[i + 1])
			i += 2
		elif args[i] == "--skip" and i + 1 < args.size():
			skip = maxi(0, int(args[i + 1]))
			i += 2
		elif args[i] == "--all":
			bake_all = true
			i += 1
		elif args[i] == "--master" and i + 1 < args.size():
			master_filter = args[i + 1].to_lower()
			i += 2
		elif args[i] == "--missing":
			bake_missing = true
			i += 1
		elif args[i] == "--bake-only":
			export_glb = false
			i += 1
		elif args[i] == "--combined":
			_combined = true
			i += 1
		elif args[i] == "--combined-name" and i + 1 < args.size():
			_combined_name = args[i + 1]
			i += 2
		elif args[i] == "--write-res":
			_combined_write_res = true
			i += 1
		elif args[i] == "--jobs" and i + 1 < args.size():
			_jobs_max = maxi(1, int(args[i + 1]))
			i += 2
		elif args[i] == "--out" and i + 1 < args.size():
			_out_dir = args[i + 1]
			i += 2
		else:
			i += 1

	DirAccess.make_dir_recursive_absolute(_out_dir)

	_cells_by_key = _build_cell_lookup()
	for k in _cells_by_key.keys():
		var c: Dictionary = _cells_by_key[k]
		_key_by_gxgz[Vector2i(int(c["gx"]), int(c["gz"]))] = k
	_xtile = OS.get_environment("NAV_EXPERIMENT_XTILE_REACH") == "1"
	if not tile_keys.is_empty():
		_keys = tile_keys
	elif bake_missing:
		var key_list: Array = _cells_by_key.keys()
		key_list.sort()
		_keys = PackedStringArray()
		for ki in key_list:
			var k: String = ki
			if not ResourceLoader.exists(NAV_DIR + k + ".res"):
				_keys.append(k)
		print("Found %d cell(s) with no existing .res" % _keys.size())
		if _keys.is_empty():
			print("Nothing missing.")
			quit(0)
			return
	elif not master_filter.is_empty():
		var key_list: Array = _cells_by_key.keys()
		key_list.sort()
		_keys = PackedStringArray()
		for ki in key_list:
			var k: String = ki
			var cell: Dictionary = _cells_by_key[k]
			if str(cell["master"]).to_lower() == master_filter:
				_keys.append(k)
		if _keys.is_empty():
			push_error("No cells found for master=", master_filter)
			quit(1)
			return
	elif bake_all or count > 0:
		var key_list: Array = _cells_by_key.keys()
		key_list.sort()
		_keys = PackedStringArray()
		var end_i := key_list.size() if bake_all else mini(skip + count, key_list.size())
		var start_i := 0 if bake_all else skip
		for ki in range(start_i, end_i):
			_keys.append(str(key_list[ki]))
		if _keys.is_empty():
			push_error("No regions in range skip=%d count=%d all=%s (total=%d)" % [
				skip, count, bake_all, key_list.size(),
			])
			quit(1)
			return
	else:
		_keys = PackedStringArray(["Cliffn_rd05_00_04"])

	_do_export_glb = export_glb
	_skip_arg = skip
	# --bake-only implies writing production .res from a combined bake (otherwise combined would
	# bake and discard, which is useless for towns that must ship as four tile .res files).
	if _combined and not _do_export_glb:
		_combined_write_res = true

	print("Indexing object placements...")
	var t_idx := Time.get_ticks_msec()
	_objects_by_cell = _build_objects_by_cell()
	print("Indexed %d cells with objects in %.1fs" % [
		_objects_by_cell.size(),
		(Time.get_ticks_msec() - t_idx) / 1000.0,
	])

	print("Baking%s %d region(s) (skip=%d) with %d concurrent bake job(s) → %s" % [
		"+export" if _do_export_glb else " only",
		_keys.size(),
		_skip_arg if tile_key.is_empty() else 0,
		_jobs_max,
		_out_dir if _do_export_glb else NAV_DIR,
	])
	_t0_ms = Time.get_ticks_msec()
	if _combined:
		_run_combined_bake()
		print("Done (combined): in %.1fs" % ((Time.get_ticks_msec() - _t0_ms) / 1000.0))
		quit(0 if _fail == 0 else 1)
		return
	_pump_jobs()
	# Keep MainLoop alive until async bakes finish (_process).

func _process(_delta: float) -> bool:
	if _finished:
		return false
	if _next_index >= _keys.size() and _inflight == 0:
		_finished = true
		if _xtile:
			print("All requested tiles baked; resolving cross-tile reachability...")
			_finalize_xtile()
		print("Done: ok=%d fail=%d in %.1fs (jobs=%d)" % [
			_ok, _fail, (Time.get_ticks_msec() - _t0_ms) / 1000.0, _jobs_max,
		])
		quit(0 if _fail == 0 else 1)
	return false

func _pump_jobs() -> void:
	while _inflight < _jobs_max and _next_index < _keys.size():
		var index := _next_index
		_next_index += 1
		var key: String = _keys[index]
		if not _cells_by_key.has(key):
			push_error("No map cell for ", key)
			_fail += 1
			continue
		var cell: Dictionary = _cells_by_key[key]
		print("[%d/%d] start %s cell=(%d,%d)" % [
			index + 1, _keys.size(), key, cell["gx"], cell["gz"],
		])
		if not _start_bake_job(key, cell["master"], cell["gx"], cell["gz"], index):
			_fail += 1

func _start_bake_job(tile_key: String, master: String, gx: int, gz: int, index: int) -> bool:
	var prepared := _prepare_bake(tile_key, master, gx, gz)
	if prepared.is_empty():
		return false

	var nav: NavigationMesh = prepared["nav"]
	var source: NavigationMeshSourceGeometryData3D = prepared["source"]
	var job := {
		"tile_key": tile_key,
		"master": master,
		"gx": gx,
		"gz": gz,
		"index": index,
		"nav": nav,
		"source": source,
		"placements": prepared["placements"],
		"obst_count": prepared["obst_count"],
		"trunk_count": prepared["trunk_count"],
		"undercroft_count": prepared["undercroft_count"],
		"obstruction_cells": prepared["obstruction_cells"],
		"ground_grid": prepared["ground_grid"],
		"ground_fallback": prepared["ground_fallback"],
		"walkable_faces": prepared["walkable_faces"],
		"portal_aabbs": prepared.get("portal_aabbs", []),
		"gate_seam_aabbs": prepared.get("gate_seam_aabbs", []),
		"t0": Time.get_ticks_msec(),
	}
	_inflight += 1
	NavigationServer3D.bake_from_source_geometry_data_async(
		nav,
		source,
		_on_bake_finished.bind(job)
	)
	return true

func _on_bake_finished(job: Dictionary) -> void:
	var tile_key: String = job["tile_key"]
	var nav: NavigationMesh = job["nav"]
	var polys_before := nav.get_polygon_count()
	if _xtile:
		# Defer prune/export: stash the raw bake so _finalize_xtile can resolve elevated seam-islands
		# against neighbors once every requested tile is in.
		var wp: Vector3 = TERRAIN_ORIGIN + Vector3(int(job["gx"]) * TILE_SIZE, 0.0, int(job["gz"]) * TILE_SIZE)
		_xtile_cache[tile_key] = {
			"nav": nav,
			"tile_min": Vector2(wp.x, wp.z),
			"tile_max": Vector2(wp.x, wp.z) + Vector2(TILE_SIZE, TILE_SIZE),
			"ground_grid": job["ground_grid"],
			"ground_fallback": job["ground_fallback"],
			"obstruction_cells": job.get("obstruction_cells", {}),
			"master": job["master"],
			"gx": int(job["gx"]),
			"gz": int(job["gz"]),
			"placements": job["placements"],
			"walkable_faces": job["walkable_faces"],
		}
		if not _xtile_requested.has(tile_key):
			_xtile_requested.append(tile_key)
		print("  [%d/%d] baked %s (deferred, polys=%d)" % [int(job["index"]) + 1, _keys.size(), tile_key, polys_before])
		_inflight -= 1
		_pump_jobs()
		return
	if OS.get_environment("NAV_EXPERIMENT_PRUNE_ISLANDS") == "1":
		var world_pos: Vector3 = TERRAIN_ORIGIN + Vector3(int(job["gx"]) * TILE_SIZE, 0.0, int(job["gz"]) * TILE_SIZE)
		var tile_min := Vector2(world_pos.x, world_pos.z)
		var tile_max := tile_min + Vector2(TILE_SIZE, TILE_SIZE)
		var obstruction_cells: Dictionary = job.get("obstruction_cells", {})
		var ground_grid: Dictionary = job.get("ground_grid", {})
		var ground_fallback: float = job.get("ground_fallback", 0.0)
		nav = _prune_orphan_interior_islands(
			nav, tile_min, tile_max, obstruction_cells, ground_grid, ground_fallback,
			_prune_lenient_for_tile(tile_key), job.get("portal_aabbs", []),
			job.get("gate_seam_aabbs", [])
		)
		job["nav"] = nav
	var elapsed := (Time.get_ticks_msec() - int(job["t0"])) / 1000.0
	print("  [%d/%d] baked %s in %.2fs polys=%d(raw=%d) obst=%d trunks=%d undercroft=%d placements=%d" % [
		int(job["index"]) + 1,
		_keys.size(),
		tile_key,
		elapsed,
		nav.get_polygon_count(),
		polys_before,
		job["obst_count"],
		job["trunk_count"],
		job["undercroft_count"],
		(job["placements"] as Array).size(),
	])

	var success := false
	if nav.get_polygon_count() == 0:
		push_error("Empty bake for ", tile_key)
	else:
		var nav_path := NAV_DIR + tile_key + ".res"
		var err := ResourceSaver.save(nav, nav_path)
		if err != OK:
			push_error("Failed to save ", nav_path, " err=", err)
		elif not _do_export_glb:
			success = true
		else:
			nav = load(nav_path)
			success = _export_one_glb(
				tile_key,
				job["master"],
				job["gx"],
				job["gz"],
				nav,
				job["placements"],
				_out_dir,
				job["walkable_faces"],
				job.get("ground_grid", {}),
				float(job.get("ground_fallback", 0.0))
			)

	if success:
		_ok += 1
	else:
		_fail += 1

	_inflight -= 1
	_pump_jobs()

## Combined bake: merge every requested tile's source geometry into one NavigationMeshSourceGeometryData3D,
## run a single synchronous Recast bake over the whole block, prune orphan islands against the block's
## outer edge, then export a single GLB. No xtile stitching - it's one continuous mesh.
func _run_combined_bake() -> void:
	var combined_source := NavigationMeshSourceGeometryData3D.new()
	var combined_obst: Dictionary = {}
	var combined_walk := PackedVector3Array()
	var combined_grid: Dictionary = {}
	var combined_portals: Array = []
	var combined_gate_seams: Array = []
	var nav_template: NavigationMesh = null
	var tiles_data: Array = []
	var ground_fallback := 0.0
	var min_pos := Vector2(INF, INF)
	var max_pos := Vector2(-INF, -INF)
	for ki in range(_keys.size()):
		var key: String = _keys[ki]
		if not _cells_by_key.has(key):
			push_error("No map cell for ", key)
			_fail += 1
			continue
		var cell: Dictionary = _cells_by_key[key]
		var gx := int(cell["gx"])
		var gz := int(cell["gz"])
		print("[%d/%d] prepare %s cell=(%d,%d)" % [ki + 1, _keys.size(), key, gx, gz])
		var prepared := _prepare_bake(key, cell["master"], gx, gz)
		if prepared.is_empty():
			_fail += 1
			continue
		combined_source.merge(prepared["source"])
		var ob: Dictionary = prepared["obstruction_cells"]
		for k in ob.keys():
			combined_obst[k] = ob[k]
		combined_walk.append_array(prepared["walkable_faces"])
		# Ground grids are keyed by global world cell, so union them (summing s/n on seam overlaps).
		var gg: Dictionary = prepared["ground_grid"]
		for k in gg.keys():
			if combined_grid.has(k):
				var e: Dictionary = combined_grid[k]
				var o: Dictionary = gg[k]
				e["s"] = float(e["s"]) + float(o["s"])
				e["n"] = int(e["n"]) + int(o["n"])
			else:
				combined_grid[k] = (gg[k] as Dictionary).duplicate()
		for pa in prepared.get("portal_aabbs", []):
			combined_portals.append(pa)
		for ga in prepared.get("gate_seam_aabbs", []):
			combined_gate_seams.append(ga)
		nav_template = prepared["nav"]
		ground_fallback = prepared["ground_fallback"]
		tiles_data.append({
			"tile_key": key,
			"master": cell["master"],
			"gx": gx,
			"gz": gz,
			"placements": prepared["placements"],
		})
		var wp := TERRAIN_ORIGIN + Vector3(gx * TILE_SIZE, 0.0, gz * TILE_SIZE)
		min_pos = Vector2(minf(min_pos.x, wp.x), minf(min_pos.y, wp.z))
		max_pos = Vector2(maxf(max_pos.x, wp.x + TILE_SIZE), maxf(max_pos.y, wp.z + TILE_SIZE))

	if nav_template == null:
		push_error("Combined bake: no tiles prepared")
		_fail += 1
		return

	var nav := nav_template
	print("Combined: baking %d tile(s) as one navmesh (region %s..%s)..." % [tiles_data.size(), min_pos, max_pos])
	var t_bake := Time.get_ticks_msec()
	NavigationServer3D.bake_from_source_geometry_data(nav, combined_source)
	var polys_raw := nav.get_polygon_count()
	print("Combined: baked polys=%d in %.2fs" % [polys_raw, (Time.get_ticks_msec() - t_bake) / 1000.0])

	if OS.get_environment("NAV_EXPERIMENT_PRUNE_ISLANDS") == "1":
		var lenient := _prune_lenient_for_keys(_keys)
		nav = _prune_orphan_interior_islands(
			nav, min_pos, max_pos, combined_obst, combined_grid, ground_fallback, lenient,
			combined_portals, combined_gate_seams
		)
		var nv_chk := nav.get_vertices()
		var portal_poly_n := 0
		for pi_chk in range(nav.get_polygon_count()):
			var poly_chk: PackedInt32Array = nav.get_polygon(pi_chk)
			if poly_chk.size() < 3:
				continue
			var c_chk := Vector3.ZERO
			for vi_chk in poly_chk:
				c_chk += nv_chk[vi_chk]
			c_chk /= float(poly_chk.size())
			# SW hrz21 throat in world XZ
			if absf(c_chk.x - 5891.0) < 6.0 and absf(c_chk.z - 2499.5) < 2.0 \
					and c_chk.y > -401.0 and c_chk.y < -397.0:
				portal_poly_n += 1
		print(
			"Combined: after prune polys=%d verts=%d portal_sw_polys=%d (lenient_castle=%s)"
			% [nav.get_polygon_count(), nv_chk.size(), portal_poly_n, lenient]
		)

	if nav.get_polygon_count() == 0:
		push_error("Combined bake produced empty navmesh")
		_fail += 1
		return

	# Split the continuous 2x2 (or N-tile) bake into per-tile production .res files when requested.
	# Towns MUST be baked combined so prune/seams see the whole block; runtime still loads one .res
	# per cell. Preview-only combined runs (GLB without --write-res) skip this so they don't clobber
	# GeneratedNavMeshes.
	var saved := 0
	if _combined_write_res:
		for td in tiles_data:
			var tkey: String = td["tile_key"]
			var tgx: int = td["gx"]
			var tgz: int = td["gz"]
			var tmin := Vector2(TERRAIN_ORIGIN.x + tgx * TILE_SIZE, TERRAIN_ORIGIN.z + tgz * TILE_SIZE)
			var tmax := tmin + Vector2(TILE_SIZE, TILE_SIZE)
			var tile_nav := _split_navmesh_to_tile(nav, tmin, tmax)
			if tile_nav.get_polygon_count() == 0:
				push_warning("Combined bake: empty clip for ", tkey, " (bounds ", tmin, "..", tmax, ")")
				continue
			var nav_path := NAV_DIR + tkey + ".res"
			var err := ResourceSaver.save(tile_nav, nav_path)
			if err != OK:
				push_error("Failed to save ", nav_path, " err=", err)
				_fail += 1
			else:
				saved += 1
				print("  saved %s (%d polys)" % [nav_path, tile_nav.get_polygon_count()])
		print("Combined: wrote %d/%d per-tile .res under %s" % [saved, tiles_data.size(), NAV_DIR])

	if not _do_export_glb:
		if saved > 0:
			_ok += 1
		else:
			_fail += 1
		return

	if _export_combined_glb(
		_combined_name, tiles_data, nav, combined_walk, combined_grid, ground_fallback
	):
		_ok += 1
	else:
		_fail += 1

## Renders every tile's terrain + objects and the single combined navmesh into one GLB, all in a shared
## frame centred on the whole block so terrain and nav line up.
func _export_combined_glb(
	out_name: String,
	tiles_data: Array,
	nav: NavigationMesh,
	walkable_faces: PackedVector3Array,
	ground_grid: Dictionary,
	ground_fallback: float = 0.0
) -> bool:
	# Frame centre = centre of the union of every tile mesh (full extents, not just centres) so the
	# whole block is centred like a single tile export is, and so we can size the side-by-side gap to
	# the actual block width instead of the single-tile SIDE_GAP (which is too small for a 2x2 block
	# and lets the two halves overlap).
	var block := AABB()
	var have_box := false
	for td in tiles_data:
		var tm := _load_tile_mesh(td["master"])
		if tm == null:
			continue
		var wp := TERRAIN_ORIGIN + Vector3(int(td["gx"]) * TILE_SIZE, 0.0, int(td["gz"]) * TILE_SIZE)
		var gx3 := Transform3D(_tile_mesh_basis(), wp)
		var aabb := tm.get_aabb()
		for ei in range(8):
			var pt := gx3 * aabb.get_endpoint(ei)
			if not have_box:
				block = AABB(pt, Vector3.ZERO)
				have_box = true
			else:
				block = block.expand(pt)
	var frame_center := block.position + block.size * 0.5
	# Separate the terrain and nav halves along X by the block width plus a ~1/4-width gap, so they
	# sit fully clear of each other instead of overlaying.
	var sep := maxf(block.size.x, SIDE_GAP) * 1.25

	var root := Node3D.new()
	root.name = out_name
	var terrain_root := Node3D.new()
	terrain_root.name = "Terrain"
	root.add_child(terrain_root)
	var nav_root := Node3D.new()
	nav_root.name = "NavMesh"
	nav_root.position = Vector3(sep * 0.5, 0.0, 0.0)
	root.add_child(nav_root)

	var terrain_offset := -frame_center + Vector3(-sep * 0.5, 0.0, 0.0)
	var merge := NavGlbMerge.new_tile_state()
	var skipped_deep := 0

	for td in tiles_data:
		var tile_mesh := _load_tile_mesh(td["master"])
		if tile_mesh == null:
			push_error("Missing tile mesh for ", td["master"])
			continue
		var wp := TERRAIN_ORIGIN + Vector3(int(td["gx"]) * TILE_SIZE, 0.0, int(td["gz"]) * TILE_SIZE)
		var ground_xform := Transform3D(_tile_mesh_basis(), wp)
		NavGlbMerge.append_mesh(
			merge["ground"],
			tile_mesh,
			NavGlbMerge.apply_translation(ground_xform, terrain_offset)
		)
		for placement in (td["placements"] as Array):
			if _preview_skip_deep_buried_object(placement, ground_grid, ground_fallback):
				skipped_deep += 1
				continue
			var object_name: String = placement["object_name"]
			var category: String = placement["category"]
			var world_xform: Transform3D = placement["transform"]
			world_xform = NavGlbMerge.apply_translation(world_xform, terrain_offset)
			var parts: Array = _load_object_parts(object_name)
			if parts.is_empty():
				continue
			var cat: String = category if category in merge["object"] else "other"
			for part in parts:
				NavGlbMerge.append_mesh(merge["object"][cat], part["mesh"], world_xform * part["local"])
	if skipped_deep > 0:
		print("    preview: skipped ", skipped_deep, " object(s) >",
			PREVIEW_OBJECT_MAX_DEPTH_BELOW_GROUND, "m below ground on ", out_name)

	var obj_walk_grid := _build_obj_walk_grid(walkable_faces, OBJ_COLOR_CELL)
	var is_obj := func(centroid: Vector3) -> bool:
		return _nav_over_object(obj_walk_grid, OBJ_COLOR_CELL, centroid)
	NavGlbMerge.append_nav_split(merge["nav"], merge["nav_obj"], nav, frame_center, is_obj)
	NavGlbMerge.finalize_tile_meshes(terrain_root, nav_root, merge)

	get_root().add_child(root)
	var out_path := _out_dir.path_join(out_name + ".glb")
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_scene(root, state)
	if err != OK:
		push_error("GLTF append failed err=", err)
		root.queue_free()
		return false
	err = doc.write_to_filesystem(state, out_path)
	root.queue_free()
	if err != OK:
		push_error("GLTF write failed err=", err)
		return false
	print("Exported (combined) ", out_path)
	return true

func _prepare_bake(tile_key: String, master: String, gx: int, gz: int) -> Dictionary:
	var world_pos := TERRAIN_ORIGIN + Vector3(gx * TILE_SIZE, 0.0, gz * TILE_SIZE)
	var tile_mesh := _load_tile_mesh(master)
	if tile_mesh == null:
		push_error("Missing tile mesh for ", master)
		return {}

	var ground_xform := Transform3D(_tile_mesh_basis(), world_pos)
	var ground_faces := _mesh_faces_world(tile_mesh, ground_xform)
	# Spatial grid over the ground verts so per-vertex terrain-height sampling (used by the
	# below-terrain cull, called for every object triangle vertex) is ~O(1) instead of O(all verts).
	var ground_grid := _build_ground_height_grid(ground_faces, GROUND_GRID_CELL)

	var cell_key := Vector2i(gx, gz)
	# Duplicated: we append spilled-in neighbour objects below and must not mutate the shared cache.
	var placements: Array = (_objects_by_cell.get(cell_key, []) as Array).duplicate()

	# Objects are bucketed to a single cell by their pivot, so a building straddling a tile seam is
	# only carved in its owner tile. In the neighbour tile the spilled-in half is uncarved: its walls
	# are missing and its interior reads as open, boundary-touching, ground-level nav that the
	# classifier keeps walkable (the "walkable interior where a building reaches into the adjacent
	# cell" artifact). Pull in any neighbour-cell object whose mesh AABB actually crosses into this
	# tile so its walls carve here too and the interior seals. See Town4_00_00 boundary buildings.
	var tmin_x := world_pos.x
	var tmax_x := world_pos.x + TILE_SIZE
	var tmin_z := world_pos.z
	var tmax_z := world_pos.z + TILE_SIZE
	const SPILL_PIVOT_MARGIN := 60.0
	var spill_count := 0
	var spill_enabled := OS.get_environment("NAV_EXPERIMENT_SPILL") != "0"
	for dz in range(-1, 2):
		if not spill_enabled:
			break
		for dx in range(-1, 2):
			if dx == 0 and dz == 0:
				continue
			for np in _objects_by_cell.get(Vector2i(gx + dx, gz + dz), []):
				var po: Vector3 = np["transform"].origin
				if po.x < tmin_x - SPILL_PIVOT_MARGIN or po.x > tmax_x + SPILL_PIVOT_MARGIN \
				or po.z < tmin_z - SPILL_PIVOT_MARGIN or po.z > tmax_z + SPILL_PIVOT_MARGIN:
					continue
				if _skip_obstruction(np["object_name"]):
					continue
				var nparts := _load_object_parts(np["object_name"])
				if nparts.is_empty():
					continue
				var na := _parts_world_aabb(nparts, np["transform"])
				if na.position.x > tmax_x or na.position.x + na.size.x < tmin_x \
				or na.position.z > tmax_z or na.position.z + na.size.z < tmin_z:
					continue
				# Duplicate + tag so the carve loop takes only this object's WALL carve (to seal the
				# straddling interior) and skips its walkable faces - those belong to the neighbour
				# tile's nav and would otherwise grow duplicate mesh out past the shared seam.
				var sp: Dictionary = np.duplicate()
				sp["_spill"] = true
				placements.append(sp)
				spill_count += 1
	if spill_count > 0 and OS.get_environment("DIAG_XTILE") == "1":
		print("    spill: pulled ", spill_count, " neighbour object(s) into ", tile_key)

	var ground_aabb := tile_mesh.get_aabb()
	var surface_y := (ground_xform * ground_aabb.position).y + (ground_xform.basis * ground_aabb.size).y * 0.5
	var diag_height_obj: String = OS.get_environment("DIAG_HEIGHT_CHECK")
	if diag_height_obj != "":
		for placement in placements:
			var pname: String = placement["object_name"]
			if pname == diag_height_obj:
				var porigin: Vector3 = placement["transform"].origin
				var real_y := _sample_surface_y(ground_faces, Vector2(porigin.x, porigin.z), surface_y)
				print("HEIGHT_CHECK ", pname, " xz=(", porigin.x, ",", porigin.z, ") flat_surface_y=", surface_y, " real_local_ground_y=", real_y, " gap=", real_y - surface_y)
	var diag_region: String = OS.get_environment("DIAG_LIST_REGION")
	if diag_region != "":
		var parts_r := diag_region.split(",")
		var rminx := float(parts_r[0]); var rmaxx := float(parts_r[1])
		var rminz := float(parts_r[2]); var rmaxz := float(parts_r[3])
		print("REGION diag tile=", tile_key, " flat_surface_y=%.2f" % surface_y)
		for placement in placements:
			var porigin2: Vector3 = placement["transform"].origin
			var lx := porigin2.x - world_pos.x
			var lz := porigin2.z - world_pos.z
			if lx >= rminx and lx <= rmaxx and lz >= rminz and lz <= rmaxz:
				var ground_y := _sample_surface_y(ground_faces, Vector2(porigin2.x, porigin2.z), surface_y)
				# Real (JSON-Y-preserving) mesh vertical extent vs the extent the bake actually
				# uses after snapping the pivot to ground. Reveals "iceberg" objects whose real
				# placement sits mostly below terrain (only the top pokes out).
				var real_parts: Array = _load_object_parts(placement["object_name"])
				var real_min_y := INF
				var real_max_y := -INF
				for rp in real_parts:
					var rmesh: Mesh = rp["mesh"]
					var rx: Transform3D = placement["transform"] * rp["local"]
					var rfaces := _mesh_faces_world(rmesh, rx)
					for fv in rfaces:
						real_min_y = minf(real_min_y, fv.y)
						real_max_y = maxf(real_max_y, fv.y)
				print("REGION_OBJ ", placement["object_name"], " local=(%.1f,%.1f)" % [lx, lz],
					" json_world_y=%.2f flat_surf=%.2f sampled_ground=%.2f" % [porigin2.y, surface_y, ground_y],
					" real_mesh_y=[%.2f,%.2f]" % [real_min_y, real_max_y],
					" above_ground=%.2f below_ground=%.2f" % [maxf(real_max_y - ground_y, 0.0), maxf(ground_y - real_min_y, 0.0)])

	var source := NavigationMeshSourceGeometryData3D.new()

	# Every carved obstruction's XZ/Y footprint, in the same grid used for the wall carve itself.
	# The post-bake gap-bridging pass (_prune_orphan_interior_islands) checks this so it only
	# bridges hairline Recast tessellation seams, never a gap that a real (if thin) wall occupies.
	var tile_obstruction_cells: Dictionary = {}

	# World-space walkable object faces (ADDFACES=2 near-horizontal object geometry fed to the bake as
	# real walkable source). Used only for GLB colouring: nav triangles sitting over these are drawn
	# blue ("walkable object surface") vs green for bare terrain nav.
	var tile_walkable_faces := PackedVector3Array()

	var obst_count := 0
	var trunk_count := 0
	var undercroft_count := 0

	# Cc_* terrain GLBs are ground heightfields (no steep wall tris). Still slope-split + buried
	# filter them when ADDFACES=2 so any near-flat shelf in the tile mesh is cleaned; real castle
	# walls come from cc*/castle* object placements (full-height carve + deck-cluster filter).
	# Disable with NAV_EXPERIMENT_CASTLE_TERRAIN=0.
	# When NAV_EXPERIMENT_ENTRANCE_ARCH_KITS is set, defer wall carve until after arch portals so
	# the throat is portal_protect'd (otherwise tile-mesh walls reseal entrance strips).
	var castle_terrain := _is_castle_terrain_tile(master) or _is_castle_terrain_tile(tile_key)
	var deferred_castle_terrain_wall := PackedVector3Array()
	var deferred_castle_terrain_walk := PackedVector3Array()
	var deferred_castle_carve_h := 1.0e6
	var deferred_castle_walk_before := 0
	if castle_terrain and OS.get_environment("NAV_EXPERIMENT_ADDFACES") == "2" \
			and OS.get_environment("NAV_EXPERIMENT_CASTLE_TERRAIN") != "0":
		var split := _split_walkable_wall_faces(ground_faces)
		var terrain_walk: PackedVector3Array = split["walkable"]
		var terrain_wall: PackedVector3Array = split["wall"]
		var walk_before := int(terrain_walk.size() / 3)
		terrain_walk = _filter_castle_kit_buried_walkable(terrain_walk)
		source.add_faces(terrain_walk, Transform3D.IDENTITY)
		var carve_h_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_CARVE_HEIGHT")
		var carve_h := float(carve_h_env) if carve_h_env != "" else 1.0e6
		var defer_wall := not OS.get_environment("NAV_EXPERIMENT_ENTRANCE_ARCH_KITS").is_empty()
		if terrain_wall.size() > 0 and not defer_wall:
			obst_count += _add_projected_mesh_footprint_from_world_faces(
				source, terrain_wall, carve_h, 0.0, tile_obstruction_cells, terrain_walk
			)
		elif terrain_wall.size() > 0 and defer_wall:
			deferred_castle_terrain_wall = terrain_wall
			deferred_castle_terrain_walk = terrain_walk
			deferred_castle_carve_h = carve_h
			deferred_castle_walk_before = walk_before
		print(
			"castle_terrain: ", tile_key,
			" walk_tris ", walk_before, " -> ", int(terrain_walk.size() / 3),
			" wall_tris ", int(terrain_wall.size() / 3),
			" wall_cells ", obst_count,
			" defer_wall=", defer_wall
		)
	elif OS.get_environment("NAV_EXPERIMENT_BRIDGE_RIBBON_ONLY") != "1":
		source.add_faces(ground_faces, Transform3D.IDENTITY)
	# Global building-fill accumulators (NAV_EXPERIMENT_BUILDING_FILL): every object's roofed cells
	# and wall footprint cells across the whole tile, so the enclosure flood sees real gates/archways
	# in any object's walls (a courtyard reached through a wide gate stays walkable), while narrow
	# door/window gaps get sealed. Filled per-object below; consumed once after the loop.
	#
	# Policy (locked after Town_ph00 / Town4 review): when BUILDING_FILL is on, ONLY Town_ph00 gets
	# the current fill + fabricated-roof path. Every other town stays on the baseline carve (no fill).
	# Override with NAV_EXPERIMENT_FILL_INCLUDE (comma substrings → allowlist) or
	# NAV_EXPERIMENT_FILL_EXCLUDE (comma substrings → denylist). Include wins if both are set.
	var fill_enabled := _building_fill_enabled_for_tile(tile_key)
	var fill_roofed: Dictionary = {} # Vector2i -> lowest overhead Y
	var fill_walls: Dictionary = {}  # Vector2i -> true
	var fill_walk: Dictionary = {}   # Vector2i -> lowest walkable-surface Y (caps the carve so
	                                 # elevated walkables - ramparts/decks/upper floors - survive)
	# Castle main-gate leaves (castle1_gts / cc103). Usually placed as a pair; after the object loop
	# we cluster them and carve a through-wall seam so indoor/outdoor nav don't share edges (closed
	# by default; a runtime NavigationLink will reconnect when the gate opens).
	var gate_leaves: Array = []
	# ALWAYS_CARVE is deferred until after every object's walkable faces (ramps/ramparts/decks) are
	# on the source. Carving mid-loop stamped near-ground obstruction through ramp add_faces and
	# wiped the terrain landing under toes. Flush runs once at the end with walk-protect.
	var pending_always_carve: Array = [] # [{base_faces, ground_y}, ...]
	# Arch-gateway hole cells (always-carve grid). Neighbors' dilate/seal must not stamp these,
	# or courtyard arches (hrz21) stay sealed. Hole-only — no through-pad (pad + bridge faces made
	# messy gateway walkables and skipped outer-wall ground stamps).
	var portal_protect_cells: Dictionary = {} # Vector2i -> true
	var portal_aabbs: Array = [] # [{min_x,max_x,min_z,max_z}, ...] world XZ of arch openings
	# Outer curtain-wall arches (field↔bailey): skip portal strip/protect and force solid carve.
	var outer_arch_origins: Dictionary = {} # Vector2i quantized xz -> true
	var portal_cell := _always_carve_obst_cell_size()
	# portal_aabbs is always defined (empty when arch portals are off) for prune consumers.
	if OS.get_environment("NAV_EXPERIMENT_ADDFACES") == "2" \
			and OS.get_environment("NAV_EXPERIMENT_ARCH_PORTAL") != "0":
		var portal_kits := 0
		var portal_faces_total := 0
		var portal_outer_skipped := 0
		# Castle mass centroid from non-arch wall/building kits — used to tell curtain (outer)
		# arches from internal bailey passages.
		var mass_sx := 0.0
		var mass_sz := 0.0
		var mass_n := 0
		for pl_m in placements:
			var mn: String = pl_m["object_name"]
			if _is_arch_gateway_terrain_object(mn) or _skip_obstruction(mn):
				continue
			if not (mn.begins_with("hrz") or mn.begins_with("cc") or mn.begins_with("castle")
					or mn.begins_with("Town") or mn.begins_with("tow")):
				continue
			var mo: Vector3 = (pl_m["transform"] as Transform3D).origin
			mass_sx += mo.x
			mass_sz += mo.z
			mass_n += 1
		var mass_c := Vector2(
			mass_sx / float(mass_n) if mass_n > 0 else 0.0,
			mass_sz / float(mass_n) if mass_n > 0 else 0.0
		)
		for placement in placements:
			var pname: String = placement["object_name"]
			if not _is_arch_gateway_terrain_object(pname):
				continue
			if _skip_obstruction(pname):
				continue
			var pparts: Array = _load_object_parts(pname)
			if pparts.is_empty():
				continue
			var pxform: Transform3D = placement["transform"]
			var pground := _sample_ground_height(
				ground_grid, GROUND_GRID_CELL, Vector2(pxform.origin.x, pxform.origin.z), surface_y
			)
			var pfaces := PackedVector3Array()
			for part in pparts:
				var pmesh: Mesh = part["mesh"]
				var lx: Transform3D = pxform * part["local"]
				pfaces.append_array(_mesh_faces_world(pmesh, lx))
			if OS.get_environment("NAV_EXPERIMENT_AUTHORED_Y") == "1":
				pfaces = _cull_faces_below_terrain(pfaces, ground_grid, GROUND_GRID_CELL, surface_y)
			var split := _split_walkable_wall_faces(pfaces)
			var pwall: PackedVector3Array = split["wall"]
			if pwall.is_empty():
				continue
			var occupied := _rasterize_faces_xz_cells(pwall, portal_cell)
			if occupied.is_empty():
				continue
			# Through-axis from wall *thickness* (not AABB aspect). North hrz21 kits have deep
			# courtyard returns so AABB is tall in Z; aspect-ratio then flips through-axis and
			# portal stitches look east-west instead of through the arch.
			var through_z := _arch_wall_through_z(occupied)
			var wall_band := _arch_wall_thickness_band(occupied, through_z)
			# Prefer detected arch-hole columns; fall back to a centre corridor through the wall.
			var openings := _arch_portal_opening_cells(occupied, portal_cell, through_z)
			if openings.size() > 500:
				openings = {}
			if openings.is_empty():
				openings = _arch_portal_center_corridor_cells(occupied, portal_cell, through_z)
			if openings.is_empty():
				continue
			# Keep hole cells inside the wall thickness band (drop deep recess / wing junk).
			if int(wall_band["lo"]) <= int(wall_band["hi"]):
				var clipped: Dictionary = {}
				for ck_c in openings.keys():
					var cc: Vector2i = ck_c
					var along := cc.y if through_z else cc.x
					if along < int(wall_band["lo"]) or along > int(wall_band["hi"]):
						continue
					clipped[cc] = true
				if not clipped.is_empty():
					openings = clipped
			# North hrz21 recesses make openings 6–8m deep. Clamp through-depth to ~2.25m at the
			# widest gateway slice, but keep the full lateral width of the opening (clamp cells
			# alone collapse to a narrow column and miss bailey floors).
			var throat := _arch_portal_clamp_throat(openings, through_z, portal_cell, 2.25)
			if throat.is_empty():
				throat = openings
			var lat_min_c := 999999
			var lat_max_c := -999999
			var thr_min_c := 999999
			var thr_max_c := -999999
			for ck_h in openings.keys():
				var ckh: Vector2i = ck_h
				var lat := ckh.x if through_z else ckh.y
				lat_min_c = mini(lat_min_c, lat)
				lat_max_c = maxi(lat_max_c, lat)
			for ck_t in throat.keys():
				var ckt0: Vector2i = ck_t
				var along0 := ckt0.y if through_z else ckt0.x
				thr_min_c = mini(thr_min_c, along0)
				thr_max_c = maxi(thr_max_c, along0)
			var hole_min_x: float
			var hole_max_x: float
			var hole_min_z: float
			var hole_max_z: float
			if through_z:
				hole_min_x = float(lat_min_c) * portal_cell
				hole_max_x = float(lat_max_c + 1) * portal_cell
				hole_min_z = float(thr_min_c) * portal_cell
				hole_max_z = float(thr_max_c + 1) * portal_cell
			else:
				hole_min_x = float(thr_min_c) * portal_cell
				hole_max_x = float(thr_max_c + 1) * portal_cell
				hole_min_z = float(lat_min_c) * portal_cell
				hole_max_z = float(lat_max_c + 1) * portal_cell
			# Curtain-wall (outer) arches open field↔bailey. Portal strips + protect cells keep that
			# throat walkable and fuse outdoor/interior into one region (Cc_2_hr_occ00). Skip them;
			# ALWAYS_CARVE seals the hole. Internal bailey arches stay open via strip/protect.
			var hole_c := Vector2((hole_min_x + hole_max_x) * 0.5, (hole_min_z + hole_max_z) * 0.5)
			var side_reach := 4.0
			var side_a: Vector2
			var side_b: Vector2
			if through_z:
				side_a = Vector2(hole_c.x, hole_min_z - side_reach)
				side_b = Vector2(hole_c.x, hole_max_z + side_reach)
			else:
				side_a = Vector2(hole_min_x - side_reach, hole_c.y)
				side_b = Vector2(hole_max_x + side_reach, hole_c.y)
			var is_outer_arch := false
			if mass_n >= 4:
				var d_hole := hole_c.distance_to(mass_c)
				var d_a := side_a.distance_to(mass_c)
				var d_b := side_b.distance_to(mass_c)
				is_outer_arch = maxf(d_a, d_b) > d_hole + 1.25 and minf(d_a, d_b) < d_hole + 0.75
			# Castle entrance arches (cc95) still need a ground strip through the throat even when
			# they sit on the outer curtain — skipping them left Cc_1_00_05's east arch blocked
			# (green outdoor stopped at the mouth; only elevated blue remained inside).
			if is_outer_arch and not _is_castle_entrance_arch_object(pname):
				portal_outer_skipped += 1
				var okey := Vector2i(roundi(pxform.origin.x), roundi(pxform.origin.z))
				outer_arch_origins[okey] = true
				if OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
					print(
						"    arch_portal OUTER skip ", pname,
						" hole=(%.1f..%.1f, %.1f..%.1f)" % [hole_min_x, hole_max_x, hole_min_z, hole_max_z]
					)
				continue
			# One continuous ground strip through the throat (+ short through-pad).
			var pad_env := OS.get_environment("NAV_EXPERIMENT_ARCH_PORTAL_PAD")
			var pad_m := float(pad_env) if pad_env != "" else 2.5
			# Entrance arches: modest through-pad only. A 4 m pad + dense protect-cell faces
			# spilled blue ground islands under masonry (Cc_1_00_05).
			if _is_castle_entrance_arch_object(pname) and pad_env == "":
				pad_m = 1.5
			var strip_min_x := hole_min_x
			var strip_max_x := hole_max_x
			var strip_min_z := hole_min_z
			var strip_max_z := hole_max_z
			if through_z:
				strip_min_z -= pad_m
				strip_max_z += pad_m
			else:
				strip_min_x -= pad_m
				strip_max_x += pad_m
			# Entrance arch: walkable strip is a narrow corridor through the mouth (not the full
			# hole width). Protect is a thin buffer around that strip only — never feed the
			# whole protect AABB as walkable faces (that painted ground-level blue leaks).
			# Override width with NAV_EXPERIMENT_ENTRANCE_CORRIDOR_WIDTH (default 0.5).
			# With AGENT_RADIUS=0.25, 0.5 m is knife-edge; use >=1.0 if the strip vanishes.
			var is_entrance_arch := _is_castle_entrance_arch_object(pname)
			var entrance_corridor_w := 0.5
			var ecw_env := OS.get_environment("NAV_EXPERIMENT_ENTRANCE_CORRIDOR_WIDTH")
			if ecw_env != "":
				entrance_corridor_w = float(ecw_env)
			if is_entrance_arch:
				if through_z:
					var mid_x := (hole_min_x + hole_max_x) * 0.5
					strip_min_x = mid_x - entrance_corridor_w * 0.5
					strip_max_x = mid_x + entrance_corridor_w * 0.5
				else:
					var mid_z := (hole_min_z + hole_max_z) * 0.5
					strip_min_z = mid_z - entrance_corridor_w * 0.5
					strip_max_z = mid_z + entrance_corridor_w * 0.5
			# Protect = throat cells. Entrance: strip + small buffer. Others: hole-only.
			var protect_min_x := hole_min_x
			var protect_max_x := hole_max_x
			var protect_min_z := hole_min_z
			var protect_max_z := hole_max_z
			if is_entrance_arch:
				protect_min_x = strip_min_x
				protect_max_x = strip_max_x
				protect_min_z = strip_min_z
				protect_max_z = strip_max_z
				# Thin buffer only — a wide moat + full-hole corridor wiped entire pier
				# footprints (Cc_1_00_06: two ALWAYS_CARVE jobs carved=0).
				var lat_pad := 0.2
				if through_z:
					protect_min_x -= lat_pad
					protect_max_x += lat_pad
				else:
					protect_min_z -= lat_pad
					protect_max_z += lat_pad
			var protect_cells := _portal_cells_from_aabb(
				protect_min_x, protect_max_x, protect_min_z, protect_max_z, portal_cell
			)
			if is_entrance_arch:
				protect_cells = _dilate_occupied_cells(protect_cells, portal_cell, portal_cell * 2.0)
			for ck in protect_cells.keys():
				portal_protect_cells[ck] = true
			# Prefer the lowest terrain under the portal (north bailey is ~3m below mean court).
			var bridge_y := pground + 0.08
			var t_min := INF
			var mid_hx := (hole_min_x + hole_max_x) * 0.5
			var mid_hz := (hole_min_z + hole_max_z) * 0.5
			var samples := [
				Vector2(mid_hx, mid_hz),
				Vector2(mid_hx, hole_min_z - pad_m * 0.5),
				Vector2(mid_hx, hole_max_z + pad_m * 0.5),
				Vector2(hole_min_x + 0.5, mid_hz),
				Vector2(hole_max_x - 0.5, mid_hz),
			]
			# Entrance: also sample further into both approaches so a raised sill / keep floor
			# under the hole does not lift the strip above bailey grade (Cc_1_00_05).
			if is_entrance_arch:
				var reach := maxf(pad_m + 2.0, 4.0)
				if through_z:
					samples.append(Vector2(mid_hx, hole_min_z - reach))
					samples.append(Vector2(mid_hx, hole_max_z + reach))
				else:
					samples.append(Vector2(hole_min_x - reach, mid_hz))
					samples.append(Vector2(hole_max_x + reach, mid_hz))
			for sp in samples:
				var ty := _sample_ground_height(ground_grid, GROUND_GRID_CELL, sp, pground)
				if ty < INF:
					t_min = minf(t_min, ty)
			if t_min < INF:
				bridge_y = t_min + 0.08
			# Keep-grade arches (Cc_1_00_06 cc22): terrain samples sit ~2m below the walkable
			# deck through the throat. Prefer kit walkable sill/deck tris in the hole, else
			# NAV_EXPERIMENT_ENTRANCE_BRIDGE_LIFT / DROP (tile-local).
			if is_entrance_arch:
				var pwalk: PackedVector3Array = split["walkable"]
				var deck_sum := 0.0
				var deck_n := 0
				var iw := 0
				while iw + 2 < pwalk.size():
					var wa: Vector3 = pwalk[iw]
					var wb: Vector3 = pwalk[iw + 1]
					var wc: Vector3 = pwalk[iw + 2]
					iw += 3
					var wcx := (wa.x + wb.x + wc.x) / 3.0
					var wcz := (wa.z + wb.z + wc.z) / 3.0
					if wcx < hole_min_x - 1.0 or wcx > hole_max_x + 1.0 \
							or wcz < hole_min_z - 1.0 or wcz > hole_max_z + 1.0:
						continue
					var wy := (wa.y + wb.y + wc.y) / 3.0
					# Deck / sill above terrain, not rampart crowns.
					if wy < bridge_y + 0.35 or wy > bridge_y + 4.5:
						continue
					deck_sum += wy
					deck_n += 1
				if deck_n > 0:
					bridge_y = deck_sum / float(deck_n) + 0.08
				var lift_env := OS.get_environment("NAV_EXPERIMENT_ENTRANCE_BRIDGE_LIFT")
				if lift_env != "":
					bridge_y += float(lift_env)
				var drop_env := OS.get_environment("NAV_EXPERIMENT_ENTRANCE_BRIDGE_DROP")
				if drop_env != "":
					bridge_y -= float(drop_env)
			if hole_min_x < hole_max_x:
				portal_aabbs.append({
					"min_x": protect_min_x, "max_x": protect_max_x,
					"min_z": protect_min_z, "max_z": protect_max_z,
					"strip_min_x": strip_min_x, "strip_max_x": strip_max_x,
					"strip_min_z": strip_min_z, "strip_max_z": strip_max_z,
					"through_z": through_z,
					"entrance": is_entrance_arch,
					"bridge_y": bridge_y,
				})
			# Walkable strip = corridor AABB only (dense protect-cell carpet removed).
			var bridge := _portal_walkable_strip_aabb(
				strip_min_x, strip_max_x, strip_min_z, strip_max_z, bridge_y
			)
			if is_entrance_arch:
				var strip_cells := _portal_cells_from_aabb(
					strip_min_x, strip_max_x, strip_min_z, strip_max_z, portal_cell
				)
				bridge.append_array(
					_portal_walkable_faces_from_cells(strip_cells, portal_cell, bridge_y)
				)
			if bridge.size() > 0 and not placement.get("_spill", false):
				source.add_faces(bridge, Transform3D.IDENTITY)
				tile_walkable_faces.append_array(bridge)
				portal_faces_total += int(bridge.size() / 3)
			if OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
				print(
					"    arch_portal kit ", pname,
					" throat_cells=", protect_cells.size(),
					" through_z=", through_z,
					" hole=(%.1f..%.1f, %.1f..%.1f)" % [hole_min_x, hole_max_x, hole_min_z, hole_max_z],
					" bridge_y=%.2f" % bridge_y,
					" strip_tris=", int(bridge.size() / 3)
				)
			portal_kits += 1
		if portal_kits > 0 or portal_outer_skipped > 0 \
				or OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
			print(
				"    arch_portal: ", portal_kits, " kit(s), ",
				portal_protect_cells.size(), " protect cell(s), ",
				portal_faces_total, " strip tri(s), outer_skipped=", portal_outer_skipped,
				" on ", tile_key
			)
	# Deferred castle tile-mesh wall carve (after portals) so entrance throats stay open.
	if deferred_castle_terrain_wall.size() > 0:
		var dwall_skipped := {"n": 0}
		var dwall_cells := _add_projected_mesh_footprint_from_world_faces(
			source, deferred_castle_terrain_wall, deferred_castle_carve_h, 0.0,
			tile_obstruction_cells, deferred_castle_terrain_walk,
			portal_protect_cells, portal_cell, dwall_skipped, portal_aabbs
		)
		obst_count += dwall_cells
		print(
			"castle_terrain deferred wall: ", tile_key,
			" walk_tris ", deferred_castle_walk_before, " -> ",
			int(deferred_castle_terrain_walk.size() / 3),
			" wall_tris ", int(deferred_castle_terrain_wall.size() / 3),
			" wall_cells ", dwall_cells,
			" portal_skipped ", int(dwall_skipped["n"])
		)
	# Bridges first: bowl-shaped ribbon of quads following authored deck Y. Pull planks from the
	# 3×3 neighbourhood (not just this tile's pivots) so a span that crosses a tile seam still
	# gets one continuous ribbon in every overlapping bake — otherwise Recast leaves climb gaps
	# at mid-span (~8m sag vs agent_max_climb 0.3).
	var bridge_planks: Array = _collect_bridge_planks_for_tile(gx, gz, placements)
	if not bridge_planks.is_empty():
		# Pass ground grid so end approaches can step onto cliff rims (ribbon alone stops at
		# the last plank pivot and leaves a climb/erosion gap to terrain nav).
		var ribbon := _bridge_bowl_quad_strip(bridge_planks, ground_grid, surface_y)
		if ribbon.size() > 0:
			source.add_faces(ribbon, Transform3D.IDENTITY)
			tile_walkable_faces.append_array(ribbon)
			obst_count += 1
			if OS.get_environment("DIAG_BRIDGE") == "1":
				print(
					"    bridge_bowl: ", bridge_planks.size(), " plank(s), ",
					int(ribbon.size() / 3), " tris on ", tile_key
				)
				var rb := {}
				var ri := 0
				while ri + 2 < ribbon.size():
					var rc := (ribbon[ri] + ribbon[ri + 1] + ribbon[ri + 2]) / 3.0
					var rg: Vector3 = Basis.from_euler(Vector3(0, deg_to_rad(-90), 0)).inverse() * (rc - Vector3(4000, 0, 4000))
					var rbin := int(floor(rg.z / 10.0)) * 10
					if not rb.has(rbin):
						rb[rbin] = {"n": 0, "ymin": rg.y, "ymax": rg.y}
					rb[rbin]["n"] = int(rb[rbin]["n"]) + 1
					rb[rbin]["ymin"] = minf(float(rb[rbin]["ymin"]), rg.y)
					rb[rbin]["ymax"] = maxf(float(rb[rbin]["ymax"]), rg.y)
					ri += 3
				var rkeys: Array = rb.keys()
				rkeys.sort()
				for rbk in rkeys:
					print(
						"      ribbon_src z~", rbk,
						" tris=", rb[rbk]["n"],
						" y=[%.1f,%.1f]" % [float(rb[rbk]["ymin"]), float(rb[rbk]["ymax"])]
					)
				for bp in bridge_planks:
					var bppos: Vector3 = bp["pos"]
					var bpg: Vector3 = Basis.from_euler(Vector3(0, deg_to_rad(-90), 0)).inverse() * (bppos - Vector3(4000, 0, 4000))
					print("      plank godot=", bpg, " nav=", bppos, " hw=", bp["half_width"])

	var ribbon_only := OS.get_environment("NAV_EXPERIMENT_BRIDGE_RIBBON_ONLY") == "1"
	for placement in placements:
		if ribbon_only:
			break
		var object_name: String = placement["object_name"]
		if _skip_obstruction(object_name):
			continue
		# Already contributed walkable ribbon above; skip mesh carve entirely.
		if _is_bridge_deck_object(object_name):
			continue
		var parts: Array = _load_object_parts(object_name)
		if parts.is_empty():
			continue
		var world_xform: Transform3D = placement["transform"]
		var authored_y := OS.get_environment("NAV_EXPERIMENT_AUTHORED_Y") == "1"
		# Real terrain height under the object's XZ; drives the near-ground carve band so it always
		# tracks the actual ground, not the object's (possibly deep-below / high-above) pivot.
		var obj_ground_y := _sample_ground_height(ground_grid, GROUND_GRID_CELL, Vector2(world_xform.origin.x, world_xform.origin.z), surface_y)
		if authored_y:
			# Keep the object's real authored JSON Y instead of snapping its pivot to the ground.
			# The authored Y is meaningful: ~77% of objects are placed exactly on the terrain (so
			# this is a no-op for them), while "iceberg" retaining walls (s_dcr*) are intentionally
			# sunk 8-11m with only their top poking out, bridge decks sit above a ravine, and
			# flags/lights are up on poles/walls. Snapping every pivot to ground vertically
			# relocated all of those - lifting the whole submerged wall above ground, sinking bridge
			# decks into the ravine floor, etc. See the Town4_00_00 "iceberg" investigation.
			pass
		elif OS.get_environment("NAV_EXPERIMENT_REALHEIGHT") == "1":
			world_xform.origin.y = obj_ground_y
		else:
			world_xform.origin.y = surface_y
		if _is_castle_gate_object(object_name):
			gate_leaves.append({
				"transform": world_xform,
				"ground_y": obj_ground_y,
				"parts": parts,
				"name": object_name,
			})
			continue
		# Top of the "near-ground" band used by the footprint carve. With authored Y the pivot can be
		# far from the surface, so anchor the band to the sampled ground; in the snap modes the pivot
		# already sits at ~ground, so anchoring to the pivot preserves the prior baseline exactly.
		var carve_band_base := obj_ground_y if authored_y else world_xform.origin.y
		var carve_band_top := carve_band_base + NEAR_GROUND_CARVE_MAX_HEIGHT
		var do_always_carve := _is_always_carve_terrain_object(object_name)
		if _is_tree_object(object_name):
			var trunk_aabb := _resolve_tree_trunk_aabb(parts, world_xform)
			if trunk_aabb.size.y < 0.05:
				continue
			_add_projected_aabb(source, trunk_aabb, tile_obstruction_cells)
			trunk_count += 1
			obst_count += 1
		elif OS.get_environment("NAV_EXPERIMENT_ADDFACES") == "2":
			# EXPERIMENTAL v2: split by slope. Near-horizontal tris (stairs/ramps/floors/decks) are
			# real walkable source geometry (add_faces); near-vertical tris (walls) stay on the
			# proven near-ground footprint obstruction carve from _add_projected_mesh_footprint -
			# v1 (feeding everything through add_faces) showed thin wall planes don't reliably
			# rasterize as blocking volume in recast's voxelizer, so walls stopped blocking at all.
			var all_faces := PackedVector3Array()
			for part in parts:
				var mesh: Mesh = part["mesh"]
				var xform: Transform3D = world_xform * part["local"]
				all_faces.append_array(_mesh_faces_world(mesh, xform))
			# Preprocess: cull everything below the terrain surface so only the object's real
			# above-ground silhouette drives nav. An "iceberg" mass (e.g. the s_dcr* embankments/
			# retaining berms, authored 7-11m into the ground with only a ridge/slope poking out)
			# then contributes exactly what's visible above ground - its buried bulk no longer
			# overrides the walkable terrain street beneath it and then gets pruned into a hole.
			# See the Town4_00_00 s_dcr5 "diagonal street blocked" investigation.
			if authored_y:
				all_faces = _cull_faces_below_terrain(all_faces, ground_grid, GROUND_GRID_CELL, surface_y)
			var walkable_faces := PackedVector3Array()
			var wall_faces := PackedVector3Array()
			var i := 0
			while i + 2 < all_faces.size():
				var a: Vector3 = all_faces[i]
				var b: Vector3 = all_faces[i + 1]
				var c: Vector3 = all_faces[i + 2]
				i += 3
				var normal := (b - a).cross(c - a)
				if normal.length_squared() < 0.000001:
					continue
				normal = normal.normalized()
				var slope_deg := rad_to_deg(acos(clampf(absf(normal.y), 0.0, 1.0)))
				if slope_deg <= _agent_max_slope_deg():
					walkable_faces.append(a)
					walkable_faces.append(b)
					walkable_faces.append(c)
				else:
					wall_faces.append(a)
					wall_faces.append(b)
					wall_faces.append(c)
			# Opt-in only: never strip walkway add_faces by default (that erased ramparts).
			if _is_castle_kit_object(object_name) and walkable_faces.size() > 0 \
					and OS.get_environment("NAV_EXPERIMENT_CASTLE_DECK_CLUSTER") == "1":
				walkable_faces = _filter_castle_kit_deck_clusters(walkable_faces)
			# Authored non-walkable roofs/tops: never feed near-horizontal tris as walkable source.
			# Walls still carve; ramparts come from other kit pieces (cc24/25/30/… decks).
			if walkable_faces.size() > 0 and _is_never_walkable_roof_object(object_name):
				walkable_faces = PackedVector3Array()
			# Solid rocks: carve-only. Near-horizontal rock faces would otherwise become walkable
			# source and then walk-protect the ALWAYS_CARVE stamp (green ground survives under the
			# boulder; blue ring is the rock's own tops).
			if walkable_faces.size() > 0 and _is_solid_rock_terrain_object(object_name):
				walkable_faces = PackedVector3Array()
			if walkable_faces.size() > 0 and not placement.get("_spill", false):
				source.add_faces(walkable_faces, Transform3D.IDENTITY)
				tile_walkable_faces.append_array(walkable_faces)
				obst_count += 1
			var inflate := TOWER_WALL_INFLATE if _is_tower_object(object_name) else 0.0
			# Approach ramps (cc55/cc56): no near-ground wall carve and no ALWAYS_CARVE. Their steep
			# tris are ramp sides/underside; carving them digs a trench that deletes the toe landing
			# and blocks the ground↔ramp weld. Other kits keep the heal3 wall carve.
			var is_approach_ramp := _is_approach_ramp_object(object_name)
			# Large keep shells: ALWAYS_CARVE dilate/fill was sealing walkable undercrofts
			# (Cc_1_hr_occ02). Prefer the mid-loop steep-face footprint for those; thin wall strips
			# and arch kits stay on the end-of-pass ALWAYS_CARVE path.
			var always_carve_here := do_always_carve
			if always_carve_here and wall_faces.size() >= 9 \
					and not _is_arch_gateway_terrain_object(object_name) \
					and not _is_solid_rock_terrain_object(object_name) \
					and not _is_curtain_arch_wall_object(object_name):
				var footprint_cells := _count_projected_xz_cells(wall_faces, _always_carve_obst_cell_size())
				var footprint_area := float(footprint_cells) * _always_carve_obst_cell_size() * _always_carve_obst_cell_size()
				var max_ac_area_env := OS.get_environment("NAV_EXPERIMENT_ALWAYS_CARVE_MAX_AREA")
				var max_ac_area := float(max_ac_area_env) if max_ac_area_env != "" else 25.0
				if footprint_area > max_ac_area:
					always_carve_here = false
			if wall_faces.size() > 0 and not is_approach_ramp and not always_carve_here \
					and OS.get_environment("NAV_EXPERIMENT_WALL_FOOTPRINT") != "0":
				# Wall footprint always stamps real wall faces (pillars/outer walls). Portal protect
				# is skipped for ordinary kits by default — oversized courtyard protect left green
				# under outer ring walls. When ENTRANCE_ARCH_KITS is set, protect is a narrow
				# corridor: honor it for every kit so neighbour walls cannot reseal the strip
				# (Cc_1_00_06 keep arch). Curtain cc22 always honors protect (Cc_1_00_05).
				var wall_portal_protect: Dictionary = {}
				var wall_portal_cell := 0.0
				var honor_portal := not portal_protect_cells.is_empty() and (
					_is_curtain_arch_wall_object(object_name)
					or _is_arch_gateway_terrain_object(object_name)
					or not OS.get_environment("NAV_EXPERIMENT_ENTRANCE_ARCH_KITS").is_empty()
				)
				if honor_portal:
					wall_portal_protect = portal_protect_cells
					wall_portal_cell = portal_cell
				var cells := _add_projected_mesh_footprint_from_world_faces(
					source, wall_faces, carve_band_top, inflate, tile_obstruction_cells,
					walkable_faces, wall_portal_protect, wall_portal_cell, {},
					portal_aabbs if honor_portal else []
				)
				obst_count += cells
			# Allowlist: queue base footprint for a single end-of-pass carve (after all walkables).
			# Arch gateways stay on ALWAYS_CARVE with preserve_openings (no dilate).
			if always_carve_here and all_faces.size() > 0 and not is_approach_ramp:
				var solid_rock := _is_solid_rock_terrain_object(object_name)
				# Curtain wall with an arch cut (cc22): solid-carve pier-wing faces outside the
				# sibling portal's lateral column so land under masonry clears while the throat
				# stays open (Cc_1_00_05).
				var curtain_arch_wall := _is_curtain_arch_wall_object(object_name)
				var base_faces: PackedVector3Array
				var curtain_pier_solid := false
				if solid_rock:
					# Rocks are 6–11m across with hollow mesh bottoms; bottom-band silhouette + 5m
					# seal leaves the interior walkable. Full mesh XZ + solid AABB fill.
					base_faces = all_faces
				elif curtain_arch_wall and all_faces.size() >= 9:
					# Pier wings outside the portal lateral column (entrance cc22 or sibling of
					# cc95). Never skip the whole footprint — oversized protect + opening_aware
					# left carved=0 on Cc_1_00_06. Throat stays open via portal_protect erase.
					var pier_faces := _filter_faces_outside_arch_lateral_column(
						all_faces, portal_aabbs, 0.2
					)
					base_faces = _filter_faces_near_ground_y(
						pier_faces, obj_ground_y, ALWAYS_CARVE_GROUND_BAND
					)
					if base_faces.size() < 9:
						base_faces = _filter_faces_near_mesh_bottom(
							pier_faces, ALWAYS_CARVE_BASE_BAND
						)
					curtain_pier_solid = base_faces.size() >= 9
					if not curtain_pier_solid:
						base_faces = pier_faces if pier_faces.size() >= 9 else all_faces
						curtain_pier_solid = base_faces.size() >= 9 and portal_aabbs.size() > 0
				else:
					# Buildings/walls: project STEEP faces (any height) as the XZ shell.
					# hrz08 has almost no tris below ~4m (only 8 skirt tris), so a ground/mesh-min
					# band under-stamps; upper wall planes still define the solid footprint.
					# Near-horizontal floors/soffits are excluded — arch/door voids have no wall
					# tris, so openings stay clear (threshold slabs are walkable add_faces and
					# walk-protect if they overlap). Do NOT AABB-solid-fill (that plugs arches).
					base_faces = wall_faces
					if base_faces.is_empty():
						base_faces = _filter_faces_near_ground_y(
							all_faces, obj_ground_y, ALWAYS_CARVE_GROUND_BAND
						)
					if base_faces.is_empty():
						base_faces = _filter_faces_near_mesh_bottom(all_faces, ALWAYS_CARVE_BASE_BAND)
				if base_faces.size() > 0:
					pending_always_carve.append({
						"name": object_name,
						"base_faces": base_faces,
						"ground_y": obj_ground_y,
						# Rocks: AABB solid. cc22 pier wings: occupied+dilate+component AABB.
						"solid_fill": solid_rock,
						"solid_occupied_fill": curtain_pier_solid,
						"opening_aware_fill": (not solid_rock) and (not curtain_pier_solid),
						# Arch/gate kits: no wall-dilate / tight seal only — dilate+2.25m seal
						# plugs ~3m courtyard arches (Cc_2_hr_occ00 hrz21), leaving only ramp links.
						# Outer curtain arches must NOT preserve openings (field↔bailey seal).
						# Entrance arches (cc95) keep preserve_openings so the portal strip throat
						# is not sealed by dilate.
						"preserve_openings": (not solid_rock) and (not curtain_pier_solid) \
							and _is_arch_gateway_terrain_object(object_name) \
							and (
								_is_castle_entrance_arch_object(object_name)
								or not outer_arch_origins.has(Vector2i(
									roundi(world_xform.origin.x), roundi(world_xform.origin.z)
								))
							),
					})
			# Accumulate this object's roofed cells (near-horizontal cover well above ground) and wall
			# footprint into the tile-global grids for the enclosure carve after the loop.
			if fill_enabled and not placement.get("_spill", false):
				_accumulate_roofed_cells(all_faces, ground_grid, surface_y, fill_roofed)
				_accumulate_wall_cells(wall_faces, fill_walls)
				_accumulate_walk_cells(walkable_faces, fill_walk)
			# Safety net: a tower with no modeled floor/stair geometry of its own (walkable_faces
			# empty) relies entirely on the ground tile underneath for its interior "floor" - if its
			# wall mesh has no real door gap at ground level, that leaves an unreachable walkable
			# island inside the ring. Force-block it, same as before. Towers that DO model a real
			# floor/stair (walkable_faces non-empty) skip this so a real archway can stay reachable.
			if _is_tower_object(object_name) and walkable_faces.size() == 0:
				var outer := _parts_world_aabb(parts, world_xform)
				var inner := _inset_aabb_xz(outer, TOWER_INTERIOR_INSET)
				if inner.size.x > 0.4 and inner.size.z > 0.4 and inner.size.y > 0.05:
					_add_projected_aabb(source, inner, tile_obstruction_cells)
					obst_count += 1
		else:
			# Near-ground mesh tris (not a single full-object AABB) for everything else: a curved/
			# L-shaped/diagonal object's bounding box can enclose a lot of genuinely walkable space
			# around it (see the Town4 "snow01b"/courtyard investigation) - tracing the real
			# near-ground silhouette keeps the carve hugging the actual footprint instead. Towers
			# additionally get XZ inflate (thin wall planes) plus a solid interior fill so the
			# hollow shell doesn't leave an orphan walkable island inside.
			var inflate := TOWER_WALL_INFLATE if _is_tower_object(object_name) else 0.0
			var cells := _add_projected_mesh_footprint(source, parts, world_xform, inflate, tile_obstruction_cells, carve_band_top)
			if cells > 0:
				obst_count += cells
				if _is_undercroft_object(object_name):
					undercroft_count += 1
			if _is_tower_object(object_name):
				var outer := _parts_world_aabb(parts, world_xform)
				var inner := _inset_aabb_xz(outer, TOWER_INTERIOR_INSET)
				if inner.size.x > 0.4 and inner.size.z > 0.4 and inner.size.y > 0.05:
					_add_projected_aabb(source, inner, tile_obstruction_cells)
					obst_count += 1

	# Global enclosure carve: seal building interiors that are only reachable through narrow
	# door/window gaps, while leaving open courtyards, reachable roofs, and wide covered passages
	# (gates/archways/streets under a rampart or bridge) walkable. Runs once over the whole tile so
	# the flood sees every object's real openings. See the Town_ph00 transparent-house investigation.
	if fill_enabled and not fill_roofed.is_empty():
		var fab_roof_faces := PackedVector3Array()
		obst_count += _carve_enclosed_interiors(source, fill_roofed, fill_walls, fill_walk, ground_grid, surface_y, tile_obstruction_cells, fab_roof_faces)
		# The fabricated flat roof caps are real walkable geometry: feed them to the bake and tag them
		# as object-walkable so they colour blue and the prune knows to keep them.
		if fab_roof_faces.size() > 0:
			source.add_faces(fab_roof_faces, Transform3D.IDENTITY)
			tile_walkable_faces.append_array(fab_roof_faces)

	# ALWAYS_CARVE flush: all walkable add_faces (incl. fabricated roofs) are known, so the stamp
	# can skip / cap under ramps, decks, and ramparts instead of punching through them.
	# Tile-local escape hatch: NAV_EXPERIMENT_ALWAYS_CARVE=0 (used while opening Cc_1_00_06).
	if not pending_always_carve.is_empty() \
			and OS.get_environment("NAV_EXPERIMENT_ALWAYS_CARVE") != "0":
		var always_cells := 0
		for job_v in pending_always_carve:
			var job: Dictionary = job_v
			var carved_n := _carve_always_carve_base_footprint(
				source,
				job["base_faces"],
				float(job["ground_y"]),
				tile_obstruction_cells,
				tile_walkable_faces,
				bool(job.get("solid_fill", false)),
				bool(job.get("opening_aware_fill", false)),
				bool(job.get("preserve_openings", false)),
				portal_protect_cells,
				bool(job.get("solid_occupied_fill", false)),
				portal_aabbs
			)
			always_cells += carved_n
			if OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
				print(
					"    always_carve ", str(job.get("name", "?")),
					" carved=", carved_n,
					" preserve_openings=", bool(job.get("preserve_openings", false)),
					" solid_occupied=", bool(job.get("solid_occupied_fill", false)),
					" opening_aware=", bool(job.get("opening_aware_fill", false)),
					" solid_fill=", bool(job.get("solid_fill", false))
				)
		obst_count += always_cells
		if OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
			print("    always_carve: flushed ", pending_always_carve.size(), " object(s), ", always_cells, " cell(s) on ", tile_key)

	# Re-stamp entrance portal strips AFTER all carves. Early strip faces are often voxel-eaten by
	# neighbouring wall pads; a post-carve pass keeps the keep-grade corridor (Cc_1_00_06).
	if not portal_aabbs.is_empty() and not OS.get_environment("NAV_EXPERIMENT_ENTRANCE_ARCH_KITS").is_empty():
		var restamp := 0
		for pa_v in portal_aabbs:
			var pa: Dictionary = pa_v
			if not bool(pa.get("entrance", false)):
				continue
			var by := float(pa.get("bridge_y", 0.0))
			var smin_x := float(pa.get("strip_min_x", pa["min_x"]))
			var smax_x := float(pa.get("strip_max_x", pa["max_x"]))
			var smin_z := float(pa.get("strip_min_z", pa["min_z"]))
			var smax_z := float(pa.get("strip_max_z", pa["max_z"]))
			var strip := _portal_walkable_strip_aabb(smin_x, smax_x, smin_z, smax_z, by)
			var scells := _portal_cells_from_aabb(smin_x, smax_x, smin_z, smax_z, portal_cell)
			strip.append_array(_portal_walkable_faces_from_cells(scells, portal_cell, by))
			if strip.size() > 0:
				source.add_faces(strip, Transform3D.IDENTITY)
				tile_walkable_faces.append_array(strip)
				restamp += int(strip.size() / 3)
		if restamp > 0 and OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
			print("    entrance_strip restamp tris=", restamp, " on ", tile_key)

	# Drop LoS obstruction records that landed in arch portals (wall-footprint grid ≠ always-carve
	# grid, so a neighbour stamp can still be recorded even when the always-carve cell was skipped).
	if not portal_protect_cells.is_empty():
		var obst_cell_lo := _obstruction_cell_size()
		var drop_keys: Array = []
		for ok in tile_obstruction_cells.keys():
			var ock: Vector2i = ok
			var ox := (float(ock.x) + 0.5) * obst_cell_lo
			var oz := (float(ock.y) + 0.5) * obst_cell_lo
			if portal_protect_cells.has(Vector2i(floori(ox / portal_cell), floori(oz / portal_cell))):
				drop_keys.append(ock)
		for dk in drop_keys:
			tile_obstruction_cells.erase(dk)

	# Castle gate seam: always carve (unless explicitly disabled). Must run after other object
	# geometry so the doorway cut is present in the source Recast sees.
	var gate_seam_aabbs: Array = [] # [{min_x,max_x,min_z,max_z}, ...] — prune must not weld across these
	if OS.get_environment("NAV_EXPERIMENT_GATE_SEAM") != "0" and not gate_leaves.is_empty():
		var seams := _carve_castle_gate_seams(source, gate_leaves, tile_obstruction_cells, gate_seam_aabbs)
		obst_count += seams
		if seams > 0:
			print("    gate_seam: carved ", seams, " seam(s) from ", gate_leaves.size(), " leaf/leaves on ", tile_key)

	var nav := NavigationMesh.new()
	nav.cell_size = CELL_SIZE
	nav.cell_height = CELL_HEIGHT
	nav.agent_radius = AGENT_RADIUS
	nav.agent_height = AGENT_HEIGHT
	nav.agent_max_climb = _agent_max_climb()
	nav.agent_max_slope = _agent_max_slope_deg()
	# region_min_size is squared and interpreted in *voxel* units by Recast (Godot's own docs: "a
	# value of 8 will set the number of cells to 64"), not world meters. At our very fine
	# CELL_SIZE=0.1 (matching TerrainNavigationBaker.cs), the long-standing default of 4.0 only
	# filters out (4*4)*0.1*0.1 = 0.16 sqm - practically nothing - so dense clutter (boxes/baskets/
	# lights/tree trunks) fragments a real, walkable courtyard floor into dozens of tiny <3 sqm
	# noise islands that then get flagged as "orphan interior" and pruned. NAV_EXPERIMENT_REGION_MIN
	# lets us test a corrected value; see the Town4 s_bld23 courtyard investigation.
	var region_min_env := OS.get_environment("NAV_EXPERIMENT_REGION_MIN")
	nav.region_min_size = float(region_min_env) if region_min_env != "" else 4.0
	# Same voxel-squared unit gotcha as region_min_size. region_merge_size controls how large an
	# adjacent region can be and still get absorbed into a bigger neighbor during watershed growing
	# (this is what actually heals small-but-touching fragments, vs region_min_size which only
	# deletes non-viable ones outright). Default 20.0 -> only (20*20)*0.01 = 4 sqm at our cell size.
	var region_merge_env := OS.get_environment("NAV_EXPERIMENT_REGION_MERGE")
	nav.region_merge_size = float(region_merge_env) if region_merge_env != "" else 20.0
	nav.edge_max_length = 12.0
	nav.edge_max_error = 1.3
	nav.detail_sample_distance = 6.0
	nav.filter_ledge_spans = false
	# Floating suspension decks often sit under cliff lips / rope geometry; low-height filtering
	# drops the sagging mid-span even when the bowl ribbon is present in source.
	nav.filter_walkable_low_height_spans = OS.get_environment("NAV_EXPERIMENT_LOW_HEIGHT") != "0"

	return {
		"nav": nav,
		"source": source,
		"placements": placements,
		"obst_count": obst_count,
		"trunk_count": trunk_count,
		"undercroft_count": undercroft_count,
		"obstruction_cells": tile_obstruction_cells,
		"ground_grid": ground_grid,
		"ground_fallback": surface_y,
		"walkable_faces": tile_walkable_faces,
		"portal_aabbs": portal_aabbs,
		"gate_seam_aabbs": gate_seam_aabbs,
	}

func _find_root(parent: PackedInt32Array, x: int) -> int:
	var r := x
	while parent[r] != r:
		r = parent[r]
	return r

func _find_root_dict(parent: Dictionary, x: int) -> int:
	var r := x
	while int(parent[r]) != r:
		parent[r] = parent[parent[r]]
		r = int(parent[r])
	return r

## Recast's own agent_max_climb/agent_max_slope-aware voxelization already decides which elevated
## floors are truly reachable (via a real staircase/ramp, however it was modeled) vs floating in
## isolation with no climbable path - the region-growing step keeps those as separate regions, but
## Godot's bake just emits every region above region_min_size, connected or not. This drops any
## fully-interior connected component with no path to the tile's main network - i.e. an enclosed
## room/floor with no real climbable connection to the outdoor network. See the Town4
## s_bld23/s_bld22c "enclosed interior stays walkable" investigation.
##
## A component is kept if it touches the tile's outer boundary (within EDGE_MARGIN) even if it's
## not connected to what looks like the "main" network from this single tile alone - baking one
## tile in isolation can't see a real second plaza/street network that only joins up through a
## neighboring tile (or a bridge the source data doesn't stitch perfectly), so discarding those by
## size/rank alone was wrong (confirmed against Town4_01_00/Town4_10_00, which have a second large
## legitimate region separated by tile framing, not a real orphan island). Only components fully
## inside the tile (can never connect to a neighbor) and disconnected from every edge-touching
## component are true orphans and get dropped, regardless of their size.
## Pure XZ/Y distance alone can't tell "hairline Recast tessellation seam, nothing is actually
## there" apart from "genuine thin wall, whose carved obstruction footprint happens to leave a
## similarly small navmesh gap on either side" - both look identical to the gap-bridging distance
## check. This walks the straight segment between the two candidate bridge points and rejects the
## bridge if any real carved-obstruction cell (from _prepare_bake's tile_obstruction_cells, i.e.
## actual object geometry, not a rasterization artifact) lies on it at a height that would plausibly
## block a walking agent there. See the "insides of buildings became walkable" regression where the
## pure-distance bridge connected real (thin) interior walls' carve gaps to the outdoor network.
func _bridge_segment_clear(obstruction_cells: Dictionary, a: Vector3, b: Vector3) -> bool:
	if obstruction_cells.is_empty():
		return true
	var obst_cell := _obstruction_cell_size()
	var dist := Vector2(a.x - b.x, a.z - b.z).length()
	var steps: int = maxi(2, ceili(dist / (obst_cell * 0.5)))
	for s in range(steps + 1):
		var t := float(s) / float(steps)
		var px := lerpf(a.x, b.x, t)
		var pz := lerpf(a.z, b.z, t)
		# The walkable surface height at this point along the bridge (both endpoints are on nav).
		var path_y := lerpf(a.y, b.y, t)
		var key := Vector2i(floori(px / obst_cell), floori(pz / obst_cell))
		if obstruction_cells.has(key):
			var entry: Dictionary = obstruction_cells[key]
			var emin: float = entry["min_y"]
			var emax: float = entry["max_y"]
			# Only a wall that rises MORE than one step above the walking surface (and whose base is
			# at/below that surface, so it isn't a harmless overhang) is a real barrier. A carve that
			# tops out at/below the path - e.g. the retaining wall UNDER the s_dcr5 embankment ramp,
			# which crests right at the ramp deck - must not veto the bridge, otherwise a continuous
			# ramp gets severed and its upper reach pruned. Genuine building/tower walls still top out
			# metres above the ground path, so interiors and roofs stay blocked as before.
			if emax > path_y + AGENT_MAX_CLIMB and emin <= path_y + AGENT_MAX_CLIMB:
				if OS.get_environment("DIAG_BRIDGE_POINTS") == "1":
					print("  BRIDGE_BLOCKED_DETAIL key=", key, " obst_y=[%.2f,%.2f]" % [emin, emax],
						" seg_y=[%.2f,%.2f] path_y=%.2f sample=(%.2f,%.2f)" % [a.y, b.y, path_y, px, pz])
				return false
	return true

## Ground↔ramp LoS: same as the ordinary bridge clear, but ignore near-ground obstruction stamps
## (ALWAYS_CARVE / ramp-side wall carve capped at ~ground+2m). Those sit under the slope and were
## vetoing toe welds even when blue ramp nav and green terrain are only a metre apart.
func _bridge_segment_clear_ramp(obstruction_cells: Dictionary, a: Vector3, b: Vector3) -> bool:
	if obstruction_cells.is_empty():
		return true
	var obst_cell := _obstruction_cell_size()
	var path_lo := minf(a.y, b.y)
	var ignore_top := path_lo + NEAR_GROUND_CARVE_MAX_HEIGHT + 0.75
	var dist := Vector2(a.x - b.x, a.z - b.z).length()
	var steps: int = maxi(2, ceili(dist / (obst_cell * 0.5)))
	for s in range(steps + 1):
		var t := float(s) / float(steps)
		var px := lerpf(a.x, b.x, t)
		var pz := lerpf(a.z, b.z, t)
		var path_y := lerpf(a.y, b.y, t)
		var key := Vector2i(floori(px / obst_cell), floori(pz / obst_cell))
		if not obstruction_cells.has(key):
			continue
		var entry: Dictionary = obstruction_cells[key]
		var emin: float = entry["min_y"]
		var emax: float = entry["max_y"]
		if emax <= ignore_top:
			continue
		if emax > path_y + AGENT_MAX_CLIMB and emin <= path_y + AGENT_MAX_CLIMB:
			return false
	return true

## Castle rampart walkway LoS: ignore wall-body carve that only barely punches through the deck
## (that was severing merlon gaps into separate islands). Still block tall obstacles that rise well
## above BOTH walkway endpoints (real merlons / towers), so we don't weld through solid blockers.
func _bridge_segment_clear_walkway(obstruction_cells: Dictionary, a: Vector3, b: Vector3) -> bool:
	if obstruction_cells.is_empty():
		return true
	var obst_cell := _obstruction_cell_size()
	var merlon_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_MERLON_CLEAR")
	var merlon_clear := float(merlon_env) if merlon_env != "" else 1.5
	var walk_top := maxf(a.y, b.y)
	var dist := Vector2(a.x - b.x, a.z - b.z).length()
	var steps: int = maxi(2, ceili(dist / (obst_cell * 0.5)))
	for s in range(steps + 1):
		var t := float(s) / float(steps)
		var px := lerpf(a.x, b.x, t)
		var pz := lerpf(a.z, b.z, t)
		var key := Vector2i(floori(px / obst_cell), floori(pz / obst_cell))
		if not obstruction_cells.has(key):
			continue
		var entry: Dictionary = obstruction_cells[key]
		var emin: float = entry["min_y"]
		var emax: float = entry["max_y"]
		if emax > walk_top + merlon_clear and emin <= walk_top + AGENT_MAX_CLIMB:
			return false
	return true

const XTILE_EDGE_MARGIN := 0.5
const XTILE_SEAM_XZ_TOL := 0.75    # neighbor seam verts within this XZ distance are the same walkway
const XTILE_MAX_DEPTH := 8         # transitive rampart-chain hops before giving up

## Splits a baked tile's polygons into connected components and classifies each as reachable (open
## outdoor ground, or an interior area bridged to it), ambiguous (an elevated island that only touches
## the tile edge - a roof or a cross-tile rampart, undecidable in isolation) or interior (a sealed
## room, always dropped). Also records each root's tile-edge vertices + which edges it touches so the
## cross-tile resolver can seam-join against neighbors. Mirrors the seed/bridge logic of
## _prune_orphan_interior_islands but returns structure instead of a pruned mesh.
func _classify_components(nav: NavigationMesh, tile_min: Vector2, tile_max: Vector2, obstruction_cells: Dictionary, ground_grid: Dictionary, ground_fallback: float) -> Dictionary:
	var poly_count := nav.get_polygon_count()
	var verts := nav.get_vertices()
	var polys: Array = []
	for p in range(poly_count):
		polys.append(nav.get_polygon(p))

	var parent := PackedInt32Array()
	parent.resize(poly_count)
	for i in range(poly_count):
		parent[i] = i
	var edge_to_poly: Dictionary = {}
	for p in range(poly_count):
		var poly: PackedInt32Array = polys[p]
		var n := poly.size()
		for i in range(n):
			var a: int = poly[i]
			var b: int = poly[(i + 1) % n]
			var key := Vector2i(mini(a, b), maxi(a, b))
			if edge_to_poly.has(key):
				var ra := _find_root(parent, p)
				var rb := _find_root(parent, edge_to_poly[key])
				if ra != rb:
					parent[ra] = rb
			else:
				edge_to_poly[key] = p

	var gsc_env := OS.get_environment("NAV_EXPERIMENT_GROUND_CLEARANCE")
	var ground_clearance := float(gsc_env) if gsc_env != "" else 2.0

	var polys_by_root: Dictionary = {}
	var reachable: Dictionary = {}      # ground-seeded (open outdoor at edge)
	var edge_seeded: Dictionary = {}    # reachable *solely* because a vertex sat on a tile edge at ground height
	var touches_boundary: Dictionary = {}
	var boundary_pts: Dictionary = {}   # root -> Array[Vector3]
	var edges_mask: Dictionary = {}     # root -> int bitmask 1:-x 2:+x 4:-z 8:+z
	for p in range(poly_count):
		var r := _find_root(parent, p)
		if not polys_by_root.has(r):
			polys_by_root[r] = []
			boundary_pts[r] = []
			edges_mask[r] = 0
		(polys_by_root[r] as Array).append(p)
		for vi in polys[p]:
			var v: Vector3 = verts[vi]
			var on_nx: bool = v.x <= tile_min.x + XTILE_EDGE_MARGIN
			var on_px: bool = v.x >= tile_max.x - XTILE_EDGE_MARGIN
			var on_nz: bool = v.z <= tile_min.y + XTILE_EDGE_MARGIN
			var on_pz: bool = v.z >= tile_max.y - XTILE_EDGE_MARGIN
			if on_nx or on_px or on_nz or on_pz:
				touches_boundary[r] = true
				(boundary_pts[r] as Array).append(v)
				var m: int = edges_mask[r]
				if on_nx: m |= 1
				if on_px: m |= 2
				if on_nz: m |= 4
				if on_pz: m |= 8
				edges_mask[r] = m
				if not reachable.get(r, false):
					var terr := _sample_ground_height(ground_grid, GROUND_GRID_CELL, Vector2(v.x, v.z), ground_fallback)
					if v.y - terr <= ground_clearance:
						reachable[r] = true
						edge_seeded[r] = true

	# In-tile gap bridging: fold interior areas separated from the reachable ground network only by a
	# hairline Recast seam (e.g. the s_bld23 courtyard through its gate) into reachable. Bridges only
	# across <=0.5m/one-climb gaps with a clear line of sight through carved obstructions.
	var bridge_dist_env := OS.get_environment("NAV_EXPERIMENT_BRIDGE_GAP")
	var bridge_max_dist := float(bridge_dist_env) if bridge_dist_env != "" else 0.5
	var bridge_climb_env := OS.get_environment("NAV_EXPERIMENT_BRIDGE_CLIMB")
	var bridge_max_climb := float(bridge_climb_env) if bridge_climb_env != "" else BRIDGE_MAX_CLIMB
	if bridge_max_dist > 0.0:
		var bridge_cell := 1.0
		var grid: Dictionary = {}
		for r in polys_by_root.keys():
			if reachable.get(r, false):
				for p in (polys_by_root[r] as Array):
					for vi in polys[p]:
						var v: Vector3 = verts[vi]
						var gk := Vector2i(floori(v.x / bridge_cell), floori(v.z / bridge_cell))
						if not grid.has(gk):
							grid[gk] = []
						(grid[gk] as Array).append(vi)
		var changed := true
		while changed:
			changed = false
			for r in polys_by_root.keys():
				if reachable.get(r, false):
					continue
				var cands: Array = []
				for p in (polys_by_root[r] as Array):
					for vi in polys[p]:
						var v: Vector3 = verts[vi]
						var gk := Vector2i(floori(v.x / bridge_cell), floori(v.z / bridge_cell))
						for dx in range(-1, 2):
							for dz in range(-1, 2):
								var nk := Vector2i(gk.x + dx, gk.y + dz)
								if not grid.has(nk):
									continue
								for ovi in (grid[nk] as Array):
									var ov: Vector3 = verts[ovi]
									var d := Vector2(v.x - ov.x, v.z - ov.z).length()
									if d <= bridge_max_dist and absf(v.y - ov.y) <= bridge_max_climb:
										cands.append({"d": d, "v": v, "ov": ov})
				cands.sort_custom(func(x, y): return float(x["d"]) < float(y["d"]))
				for cand in cands:
					if not _bridge_segment_clear(obstruction_cells, cand["v"], cand["ov"]):
						continue
					reachable[r] = true
					for p in (polys_by_root[r] as Array):
						for vi in polys[p]:
							var v: Vector3 = verts[vi]
							var gk := Vector2i(floori(v.x / bridge_cell), floori(v.z / bridge_cell))
							if not grid.has(gk):
								grid[gk] = []
							(grid[gk] as Array).append(vi)
					changed = true
					break

	# Straddling-interior demotion: a building whose footprint crosses a tile seam leaves its interior
	# as a SEPARATE nav component that reaches open air only at the tile edge (its walls seal every
	# other side). The edge-seed rule above then marks it reachable and it stays walkable. Real open
	# terrain, by contrast, is one big component that laps against 3-4 edges. So: any component that is
	# reachable *only* because it clipped an edge (edge_seeded), yet touches <=2 edges and is small,
	# is demoted to ambiguous. The cross-tile resolver then keeps it only if it seam-joins ground on
	# the neighbour side (a genuine through-street/alley) and drops it otherwise (a sealed interior -
	# its neighbour half is carved solid thanks to the spill pass). Bridged-in areas (courtyards
	# reached through a gate) are reachable but NOT edge_seeded, so they are never demoted.
	if OS.get_environment("NAV_EXPERIMENT_EDGE_INTERIOR") != "0":
		var area_env := OS.get_environment("NAV_EXPERIMENT_EDGE_INTERIOR_AREA")
		var big_area := float(area_env) if area_env != "" else 400.0
		var diag_ei := OS.get_environment("DIAG_XTILE") == "1"
		for r in polys_by_root.keys():
			if not (reachable.get(r, false) and edge_seeded.get(r, false)):
				continue
			var m: int = edges_mask.get(r, 0)
			var edge_count := (m & 1) + ((m >> 1) & 1) + ((m >> 2) & 1) + ((m >> 3) & 1)
			if edge_count >= 3:
				continue # laps 3+ edges -> unmistakably open terrain, keep as a ground seed
			var area := 0.0
			for p in (polys_by_root[r] as Array):
				var poly: PackedInt32Array = polys[p]
				for ti in range(1, poly.size() - 1):
					var v0: Vector3 = verts[poly[0]]
					var v1: Vector3 = verts[poly[ti]]
					var v2: Vector3 = verts[poly[ti + 1]]
					area += absf((v1.x - v0.x) * (v2.z - v0.z) - (v2.x - v0.x) * (v1.z - v0.z)) * 0.5
			if area >= big_area:
				continue # large enough to be genuine terrain, keep
			reachable[r] = false # demote -> becomes ambiguous below, resolved cross-tile
			if diag_ei:
				print("    edge-interior demote root=", r, " edges=", edge_count, " area=%.1f sqm" % area)

	var ambiguous: Dictionary = {}
	for r in polys_by_root.keys():
		if not reachable.get(r, false) and touches_boundary.get(r, false):
			ambiguous[r] = true

	return {
		"verts": verts,
		"polys": polys,
		"polys_by_root": polys_by_root,
		"reachable": reachable,
		"ambiguous": ambiguous,
		"boundary_pts": boundary_pts,
		"edges": edges_mask,
	}

## Synchronously bakes a neighbor tile (only used by the cross-tile resolver to pull in a neighbor an
## ambiguous seam-island borders) and caches the raw result. Requested tiles are already cached from
## the async pass, so this only ever fires for genuine off-set neighbors.
func _bake_tile_sync(tile_key: String) -> Dictionary:
	if _xtile_cache.has(tile_key):
		return _xtile_cache[tile_key]
	if not _cells_by_key.has(tile_key):
		return {}
	var cell: Dictionary = _cells_by_key[tile_key]
	var gx := int(cell["gx"]); var gz := int(cell["gz"])
	var prepared := _prepare_bake(tile_key, cell["master"], gx, gz)
	if prepared.is_empty():
		return {}
	var nav: NavigationMesh = prepared["nav"]
	NavigationServer3D.bake_from_source_geometry_data(nav, prepared["source"])
	var world_pos: Vector3 = TERRAIN_ORIGIN + Vector3(gx * TILE_SIZE, 0.0, gz * TILE_SIZE)
	var entry := {
		"nav": nav,
		"tile_min": Vector2(world_pos.x, world_pos.z),
		"tile_max": Vector2(world_pos.x, world_pos.z) + Vector2(TILE_SIZE, TILE_SIZE),
		"ground_grid": prepared["ground_grid"],
		"ground_fallback": prepared["ground_fallback"],
		"obstruction_cells": prepared["obstruction_cells"],
		"master": cell["master"],
		"gx": gx,
		"gz": gz,
		"placements": prepared["placements"],
		"walkable_faces": prepared["walkable_faces"],
	}
	_xtile_cache[tile_key] = entry
	print("    xtile: baked neighbor ", tile_key, " for reachability")
	return entry

func _ensure_analyzed(tile_key: String) -> Dictionary:
	if _xtile_analysis.has(tile_key):
		return _xtile_analysis[tile_key]
	var entry := _bake_tile_sync(tile_key)
	if entry.is_empty():
		return {}
	var res := _classify_components(entry["nav"], entry["tile_min"], entry["tile_max"], entry["obstruction_cells"], entry["ground_grid"], entry["ground_fallback"])
	_xtile_analysis[tile_key] = res
	return res

## Which (dgx,dgz) neighbor tiles a component's edge vertices border, including the diagonal when a
## vertex sits in a corner.
func _root_neighbor_offsets(boundary_pts: Array, tile_min: Vector2, tile_max: Vector2) -> Array:
	var offs := {}
	for v in boundary_pts:
		var ox := 0
		var oz := 0
		if v.x <= tile_min.x + XTILE_EDGE_MARGIN:
			ox = -1
		elif v.x >= tile_max.x - XTILE_EDGE_MARGIN:
			ox = 1
		if v.z <= tile_min.y + XTILE_EDGE_MARGIN:
			oz = -1
		elif v.z >= tile_max.y - XTILE_EDGE_MARGIN:
			oz = 1
		if ox != 0:
			offs[Vector2i(ox, 0)] = true
		if oz != 0:
			offs[Vector2i(0, oz)] = true
		if ox != 0 and oz != 0:
			offs[Vector2i(ox, oz)] = true
	return offs.keys()

func _seam_match(pts_a: Array, pts_b: Array) -> bool:
	for a in pts_a:
		for b in pts_b:
			if absf(a.x - b.x) <= XTILE_SEAM_XZ_TOL and absf(a.z - b.z) <= XTILE_SEAM_XZ_TOL \
				and absf(a.y - b.y) <= AGENT_MAX_CLIMB \
				and Vector2(a.x - b.x, a.z - b.z).length() <= XTILE_SEAM_XZ_TOL:
				return true
	return false

func _build_pruned_nav(an: Dictionary, kept_roots: Dictionary) -> NavigationMesh:
	var pruned := NavigationMesh.new()
	pruned.vertices = an["verts"]
	var polys: Array = an["polys"]
	var polys_by_root: Dictionary = an["polys_by_root"]
	for r in kept_roots.keys():
		for p in (polys_by_root[r] as Array):
			pruned.add_polygon(polys[p])
	return pruned

## After every requested tile has baked (async), resolve the fate of each tile's ambiguous elevated
## seam-islands by growing a seam-link graph across only the neighbors those islands actually touch,
## seeding reachability from open ground, then keep/prune and export each requested tile.
func _finalize_xtile() -> void:
	var adj: Dictionary = {}          # nodeid -> Array[nodeid]
	var node_tile: Dictionary = {}
	var node_root: Dictionary = {}
	var ground_seeds: Array = []
	var expand: Array = []
	var enqueued: Dictionary = {}

	for tk in _xtile_requested:
		var an := _ensure_analyzed(tk)
		if an.is_empty():
			continue
		for r in (an["ambiguous"] as Dictionary).keys():
			var nid := "%s#%d" % [tk, r]
			node_tile[nid] = tk
			node_root[nid] = r
			if not enqueued.has(nid):
				enqueued[nid] = true
				expand.append(nid)

	var diag_xtile := OS.get_environment("DIAG_XTILE") == "1"
	var hops := 0
	while not expand.is_empty():
		var nid: String = expand.pop_back()
		var tk: String = node_tile[nid]
		var r: int = node_root[nid]
		var an: Dictionary = _xtile_analysis[tk]
		var entry: Dictionary = _xtile_cache[tk]
		var apts: Array = an["boundary_pts"][r]
		var offs := _root_neighbor_offsets(apts, entry["tile_min"], entry["tile_max"])
		if diag_xtile:
			var bmn := Vector3(INF, INF, INF)
			var bmx := Vector3(-INF, -INF, -INF)
			for p in (an["polys_by_root"][r] as Array):
				for vi in an["polys"][p]:
					var v: Vector3 = an["verts"][vi]
					bmn = bmn.min(v); bmx = bmx.max(v)
			print("  XTILE island ", nid, " world_bbox=(", bmn, ")-(", bmx, ") offs=", offs, " bnd_pts=", apts.size())
		var worldedge := false
		for off in offs:
			var nkey := Vector2i(int(entry["gx"]) + off.x, int(entry["gz"]) + off.y)
			if not _key_by_gxgz.has(nkey):
				worldedge = true
				continue
			var nk: String = _key_by_gxgz[nkey]
			var nan := _ensure_analyzed(nk)
			if nan.is_empty():
				continue
			var diag_matches := 0
			for r2 in (nan["polys_by_root"] as Dictionary).keys():
				var r2_reach: bool = nan["reachable"].get(r2, false)
				var r2_amb: bool = nan["ambiguous"].get(r2, false)
				if not (r2_reach or r2_amb):
					continue
				if not _seam_match(apts, nan["boundary_pts"][r2]):
					continue
				diag_matches += 1
				var rid := "%s#%d" % [nk, r2]
				node_tile[rid] = nk
				node_root[rid] = r2
				if not adj.has(nid):
					adj[nid] = []
				if not adj.has(rid):
					adj[rid] = []
				(adj[nid] as Array).append(rid)
				(adj[rid] as Array).append(nid)
				if r2_reach:
					ground_seeds.append(rid)
				elif r2_amb and not enqueued.has(rid):
					enqueued[rid] = true
					if hops < XTILE_MAX_DEPTH * 64:
						expand.append(rid)
			if diag_xtile:
				var best_d := INF
				var best_dy := INF
				var best_cls := "?"
				for r2 in (nan["polys_by_root"] as Dictionary).keys():
					var cls := "interior"
					if nan["reachable"].get(r2, false): cls = "ground"
					elif nan["ambiguous"].get(r2, false): cls = "ambig"
					for a in apts:
						for b in (nan["boundary_pts"][r2] as Array):
							var dd := Vector2(a.x - b.x, a.z - b.z).length()
							if dd < best_d:
								best_d = dd; best_dy = absf(a.y - b.y); best_cls = cls
				print("    -> neighbor ", nk, " off=", off, " seam_matches=", diag_matches,
					" closest_any: dist=%.2f dy=%.2f cls=%s" % [best_d, best_dy, best_cls])
		if worldedge:
			ground_seeds.append(nid)
		hops += 1

	var node_reach: Dictionary = {}
	var q: Array = ground_seeds.duplicate()
	for s in q:
		node_reach[s] = true
	while not q.is_empty():
		var cur: String = q.pop_back()
		for nb in adj.get(cur, []):
			if not node_reach.get(nb, false):
				node_reach[nb] = true
				q.append(nb)

	for tk in _xtile_requested:
		if not _xtile_analysis.has(tk):
			_fail += 1
			continue
		var an: Dictionary = _xtile_analysis[tk]
		var raw_polys := 0
		for r in (an["polys_by_root"] as Dictionary).keys():
			raw_polys += (an["polys_by_root"][r] as Array).size()
		var kept_roots: Dictionary = {}
		var rescued := 0
		for r in (an["reachable"] as Dictionary).keys():
			if an["reachable"][r]:
				kept_roots[r] = true
		for r in (an["ambiguous"] as Dictionary).keys():
			var nid := "%s#%d" % [tk, r]
			if node_reach.get(nid, false):
				kept_roots[r] = true
				rescued += 1
		var pruned := _build_pruned_nav(an, kept_roots)
		var dropped_amb := (an["ambiguous"] as Dictionary).size() - rescued
		print("    xtile ", tk, ": kept ", pruned.get_polygon_count(), "/", raw_polys,
			" polys; rescued ", rescued, " cross-tile island(s), dropped ", dropped_amb, " roof/interior island(s)")
		if not _finalize_export(tk, pruned):
			_fail += 1
		else:
			_ok += 1

func _finalize_export(tile_key: String, nav: NavigationMesh) -> bool:
	if nav.get_polygon_count() == 0:
		push_error("Empty bake for ", tile_key)
		return false
	var entry: Dictionary = _xtile_cache[tile_key]
	var nav_path := NAV_DIR + tile_key + ".res"
	var err := ResourceSaver.save(nav, nav_path)
	if err != OK:
		push_error("Failed to save ", nav_path, " err=", err)
		return false
	if not _do_export_glb:
		return true
	var loaded: NavigationMesh = load(nav_path)
	return _export_one_glb(
		tile_key,
		entry["master"],
		int(entry["gx"]),
		int(entry["gz"]),
		loaded,
		entry["placements"],
		_out_dir,
		entry.get("walkable_faces", PackedVector3Array()),
		entry.get("ground_grid", {}),
		float(entry.get("ground_fallback", 0.0))
	)

## Castle / cc tiles only: keep intentional enclosed walk space that the strict town prune would
## drop (courtyards that never touch the 2x2 outer edge, rampart fragments sealed behind walls).
## Towns must NOT use this — sealed house interiors would come back.
func _prune_lenient_for_tile(tile_key: String) -> bool:
	var k := tile_key.to_lower()
	return k.begins_with("cc_") or k.begins_with("castle_")

func _prune_lenient_for_keys(keys: PackedStringArray) -> bool:
	for k in keys:
		if _prune_lenient_for_tile(str(k)):
			return true
	return false

func _prune_orphan_interior_islands(nav: NavigationMesh, tile_min: Vector2, tile_max: Vector2, obstruction_cells: Dictionary = {}, ground_grid: Dictionary = {}, ground_fallback: float = 0.0, lenient_castle: bool = false, portal_aabbs: Array = [], gate_seam_aabbs: Array = []) -> NavigationMesh:
	var poly_count := nav.get_polygon_count()
	if poly_count == 0:
		return nav
	const EDGE_MARGIN := 0.5
	# Roof/rampart-cap pruning: a component is only seeded as reachable via the tile boundary if the
	# boundary polygon sits near terrain height there (i.e. it's the real outdoor ground continuing
	# off-tile), not an elevated cap that merely clips the tile edge. Everything above ground still
	# stays reachable if it connects down via a ramp/stair (shared nav edges keep it in the ground
	# root) - only genuinely disconnected elevated islands (roofs, tower caps) lose their free pass.
	var ground_seed := OS.get_environment("NAV_EXPERIMENT_GROUND_SEED") == "1"
	var gsc_env := OS.get_environment("NAV_EXPERIMENT_GROUND_CLEARANCE")
	var ground_clearance := float(gsc_env) if gsc_env != "" else 2.0
	var verts := nav.get_vertices()
	var polys: Array = []
	for p in range(poly_count):
		polys.append(nav.get_polygon(p))

	var parent := PackedInt32Array()
	parent.resize(poly_count)
	for i in range(poly_count):
		parent[i] = i

	var edge_to_poly: Dictionary = {} # Vector2i(min_idx, max_idx) -> first polygon index seen
	for p in range(poly_count):
		var poly: PackedInt32Array = polys[p]
		var n := poly.size()
		for i in range(n):
			var a: int = poly[i]
			var b: int = poly[(i + 1) % n]
			var key := Vector2i(mini(a, b), maxi(a, b))
			if edge_to_poly.has(key):
				var other: int = edge_to_poly[key]
				var ra := _find_root(parent, p)
				var rb := _find_root(parent, other)
				if ra != rb:
					parent[ra] = rb
			else:
				edge_to_poly[key] = p

	var area_by_root: Dictionary = {}
	var polys_by_root: Dictionary = {}
	var touches_boundary_root: Dictionary = {}
	for p in range(poly_count):
		var r := _find_root(parent, p)
		var poly: PackedInt32Array = polys[p]
		var area := 0.0
		for j in range(1, poly.size() - 1):
			var a: Vector3 = verts[poly[0]]
			var b: Vector3 = verts[poly[j]]
			var c: Vector3 = verts[poly[j + 1]]
			area += 0.5 * (b - a).cross(c - a).length()
		area_by_root[r] = float(area_by_root.get(r, 0.0)) + area
		if not polys_by_root.has(r):
			polys_by_root[r] = []
		(polys_by_root[r] as Array).append(p)
		if not touches_boundary_root.get(r, false):
			for vi in poly:
				var v: Vector3 = verts[vi]
				if v.x <= tile_min.x + EDGE_MARGIN or v.x >= tile_max.x - EDGE_MARGIN \
					or v.z <= tile_min.y + EDGE_MARGIN or v.z >= tile_max.y - EDGE_MARGIN:
					if ground_seed:
						var terr_y := _sample_ground_height(ground_grid, GROUND_GRID_CELL, Vector2(v.x, v.z), ground_fallback)
						if v.y - terr_y > ground_clearance:
							continue
					touches_boundary_root[r] = true
					break

	# Snapshot true tile-edge roots before bridging/courtyard marks expand touches_boundary_root.
	# Used to veto outdoor↔bailey welds without treating bridged bailey fragments as "exterior".
	var tile_edge_roots: Dictionary = {}
	for r_te in touches_boundary_root.keys():
		if touches_boundary_root[r_te]:
			tile_edge_roots[r_te] = true

	# Force-keep fabricated building-roof caps (see _fabricated_roof_cells): they are intentional
	# uniform walkable tops over sealed buildings and are typically unreachable islands, so the normal
	# orphan prune would drop them. Keep any component with a polygon sitting on a fabricated-roof cell
	# at roughly the fabricated height.
	if not _fabricated_roof_cells.is_empty():
		var fob_cell := _obstruction_cell_size()
		for r in polys_by_root.keys():
			if touches_boundary_root.get(r, false):
				continue
			var keep_roof := false
			for p in (polys_by_root[r] as Array):
				var poly2: PackedInt32Array = polys[p]
				var cx := 0.0; var cz := 0.0; var cy := 0.0
				for vi in poly2:
					var vv: Vector3 = verts[vi]
					cx += vv.x; cz += vv.z; cy += vv.y
				var inv := 1.0 / float(poly2.size())
				var fkey := Vector2i(floori((cx * inv) / fob_cell), floori((cz * inv) / fob_cell))
				if _fabricated_roof_cells.has(fkey) and absf((cy * inv) - float(_fabricated_roof_cells[fkey])) <= 1.0:
					keep_roof = true
					break
			if keep_roof:
				touches_boundary_root[r] = true

	# Open (perimeter) edges, keyed by vertex, for the geometric weld below. A bridge that merely
	# relabels an orphan component as "reachable" keeps its polygons in the output but leaves them
	# geometrically disconnected - fine for pruning decisions, but the runtime nav map still treats
	# it as an island, so a stepped ramp whose treads each land in a separate component never links
	# to the terrain (Town4 central ramps). To fix that we also stitch two triangles across every
	# accepted gap, attaching to a real border edge on each side so Godot's nav map connects them.
	var weld_enabled := OS.get_environment("NAV_EXPERIMENT_WELD") != "0"
	var edge_use: Dictionary = {}
	for p in range(poly_count):
		var poly_e: PackedInt32Array = polys[p]
		var ne := poly_e.size()
		for i in range(ne):
			var ea: int = poly_e[i]
			var eb: int = poly_e[(i + 1) % ne]
			var ek := Vector2i(mini(ea, eb), maxi(ea, eb))
			edge_use[ek] = int(edge_use.get(ek, 0)) + 1
	var open_nbr: Dictionary = {} # vi -> Array[int] other endpoints reachable via an open border edge
	var any_nbr: Dictionary = {} # vi -> Array[int] any edge neighbour (open or shared)
	for ek in edge_use.keys():
		var ka: int = (ek as Vector2i).x
		var kb: int = (ek as Vector2i).y
		if not any_nbr.has(ka):
			any_nbr[ka] = []
		(any_nbr[ka] as Array).append(kb)
		if not any_nbr.has(kb):
			any_nbr[kb] = []
		(any_nbr[kb] as Array).append(ka)
		if int(edge_use[ek]) == 1:
			if not open_nbr.has(ka):
				open_nbr[ka] = []
			(open_nbr[ka] as Array).append(kb)
			if not open_nbr.has(kb):
				open_nbr[kb] = []
			(open_nbr[kb] as Array).append(ka)
	var weld_requests: Array = [] # {vi, ovi} vertex-index pairs to stitch across accepted gaps

	# Bridge tiny gaps: a hairline seam left by Recast's watershed/contour step at a narrow
	# chokepoint (two regions that grew right up against each other but don't share a polygon
	# edge) looks identical to a genuinely sealed room by pure connectivity, but isn't one. If an
	# orphan component's closest approach to an already-reachable component is within
	# NAV_EXPERIMENT_BRIDGE_GAP meters horizontally (default 0.5m) and AGENT_MAX_CLIMB vertically,
	# treat it as reachable rather than dropping it. Runs to a fixed point so multi-hop bridging
	# chains (orphan A bridges to orphan B bridges to main) work. See the Town4 s_bld23 courtyard
	# investigation: a 0.22m, same-height gap with no object at the pinch point.
	var bridge_dist_env := OS.get_environment("NAV_EXPERIMENT_BRIDGE_GAP")
	var bridge_max_dist := float(bridge_dist_env) if bridge_dist_env != "" else 0.5
	var bridged_components := 0
	if bridge_max_dist > 0.0:
		var bridge_climb_env := OS.get_environment("NAV_EXPERIMENT_BRIDGE_CLIMB")
		var bridge_max_climb := float(bridge_climb_env) if bridge_climb_env != "" else BRIDGE_MAX_CLIMB
		var bridge_cell := 1.0
		var grid: Dictionary = {} # Vector2i cell -> Array[int] vertex indices from reachable roots
		for r in polys_by_root.keys():
			if touches_boundary_root.get(r, false):
				for p in (polys_by_root[r] as Array):
					for vi in polys[p]:
						var v: Vector3 = verts[vi]
						var key := Vector2i(floori(v.x / bridge_cell), floori(v.z / bridge_cell))
						if not grid.has(key):
							grid[key] = []
						(grid[key] as Array).append(vi)
		var diag_bridge_points := OS.get_environment("DIAG_BRIDGE_POINTS") == "1"
		var changed := true
		while changed:
			changed = false
			for r in polys_by_root.keys():
				if touches_boundary_root.get(r, false):
					continue
				# Collect every candidate pair within threshold (not just the closest), since the
				# closest one might be blocked by real wall geometry while a slightly farther one
				# has a genuinely clear line of sight.
				var candidates: Array = []
				for p in (polys_by_root[r] as Array):
					for vi in polys[p]:
						var v: Vector3 = verts[vi]
						var key := Vector2i(floori(v.x / bridge_cell), floori(v.z / bridge_cell))
						for dx in range(-1, 2):
							for dz in range(-1, 2):
								var nkey := Vector2i(key.x + dx, key.y + dz)
								if not grid.has(nkey):
									continue
								for ovi in (grid[nkey] as Array):
									var ov: Vector3 = verts[ovi]
									var d := Vector2(v.x - ov.x, v.z - ov.z).length()
									var climb := absf(v.y - ov.y)
									if d <= bridge_max_dist and climb <= bridge_max_climb:
										candidates.append({"d": d, "climb": climb, "v": v, "ov": ov, "vi": vi, "ovi": ovi})
				candidates.sort_custom(func(x, y): return float(x["d"]) < float(y["d"]))
				for cand in candidates:
					var v2: Vector3 = cand["v"]
					var ov2: Vector3 = cand["ov"]
					if _segment_hits_xz_aabbs(v2, ov2, gate_seam_aabbs, 0.5):
						continue
					if not _bridge_segment_clear(obstruction_cells, v2, ov2):
						if diag_bridge_points:
							print("BRIDGE_BLOCKED root=", r, " dist=%.3f climb=%.3f pt=" % [float(cand["d"]), float(cand["climb"])], v2, " -> real wall in the way")
						continue
					touches_boundary_root[r] = true
					bridged_components += 1
					if weld_enabled:
						weld_requests.append({"vi": int(cand["vi"]), "ovi": int(cand["ovi"])})
					if diag_bridge_points:
						print("BRIDGE root=", r, " area=%.1f" % float(area_by_root[r]),
							" dist=%.3f climb=%.3f pt=" % [float(cand["d"]), float(cand["climb"])], v2)
					for p in (polys_by_root[r] as Array):
						for vi in polys[p]:
							var v: Vector3 = verts[vi]
							var key := Vector2i(floori(v.x / bridge_cell), floori(v.z / bridge_cell))
							if not grid.has(key):
								grid[key] = []
							(grid[key] as Array).append(vi)
					changed = true
					break

	if OS.get_environment("DIAG_VERTEX_DUP") == "1":
		var pos_to_indices: Dictionary = {}
		for vi in range(verts.size()):
			var v: Vector3 = verts[vi]
			var key := Vector3i(roundi(v.x * 100.0), roundi(v.y * 100.0), roundi(v.z * 100.0))
			if not pos_to_indices.has(key):
				pos_to_indices[key] = []
			(pos_to_indices[key] as Array).append(vi)
		var dup_positions := 0
		var dup_vertex_count := 0
		for key in pos_to_indices.keys():
			var idxs: Array = pos_to_indices[key]
			if idxs.size() > 1:
				dup_positions += 1
				dup_vertex_count += idxs.size()
		print("VERTEX_DUP: total_verts=", verts.size(), " unique_positions=", pos_to_indices.size(),
			" positions_with_duplicates=", dup_positions, " total_duplicate_vertex_refs=", dup_vertex_count)

	var diag_region: String = OS.get_environment("DIAG_COMPONENT_REGION")
	if diag_region != "":
		var pr := diag_region.split(",")
		var rminx := float(pr[0]); var rmaxx := float(pr[1])
		var rminz := float(pr[2]); var rmaxz := float(pr[3])
		var seen_roots: Dictionary = {}
		for r in area_by_root.keys():
			var polys_r: Array = polys_by_root[r]
			var hit := false
			var comp_min := Vector2(INF, INF)
			var comp_max := Vector2(-INF, -INF)
			var y_min := INF
			var y_max := -INF
			for p in polys_r:
				var poly: PackedInt32Array = polys[p]
				for vi in poly:
					var v: Vector3 = verts[vi]
					comp_min.x = minf(comp_min.x, v.x); comp_min.y = minf(comp_min.y, v.z)
					comp_max.x = maxf(comp_max.x, v.x); comp_max.y = maxf(comp_max.y, v.z)
					y_min = minf(y_min, v.y); y_max = maxf(y_max, v.y)
					if v.x >= rminx and v.x <= rmaxx and v.z >= rminz and v.z <= rmaxz:
						hit = true
			if hit and not seen_roots.has(r):
				seen_roots[r] = true
				print("COMPONENT root=", r, " polys=", polys_r.size(), " area=%.1f" % float(area_by_root[r]),
					" boundary_touch=", touches_boundary_root.get(r, false),
					" bbox=(", comp_min, ")-(", comp_max, ")",
					" y=[%.2f,%.2f]" % [y_min, y_max])

	if OS.get_environment("DIAG_DROPPED_SIZES") == "1":
		var sizes: Array = []
		for r in area_by_root.keys():
			if not touches_boundary_root.get(r, false):
				sizes.append(float(area_by_root[r]))
		sizes.sort()
		sizes.reverse()
		print("DROPPED_SIZES (largest 30 of ", sizes.size(), "): ", sizes.slice(0, 30))

	var diag_gap_root: String = OS.get_environment("DIAG_GAP_TO_MAIN")
	if diag_gap_root != "":
		var target_root := int(diag_gap_root)
		var main_root := -1
		var main_size := -1
		for r in polys_by_root.keys():
			if touches_boundary_root.get(r, false) and (polys_by_root[r] as Array).size() > main_size:
				main_size = (polys_by_root[r] as Array).size()
				main_root = r
		if polys_by_root.has(target_root) and main_root != -1:
			var target_verts_set: Dictionary = {}
			for p in polys_by_root[target_root]:
				for vi in polys[p]:
					target_verts_set[vi] = true
			var main_verts_set: Dictionary = {}
			for p in polys_by_root[main_root]:
				for vi in polys[p]:
					main_verts_set[vi] = true
			var best_d := INF
			var best_pair := Vector2i(-1, -1)
			for tvi in target_verts_set.keys():
				var tv: Vector3 = verts[tvi]
				for mvi in main_verts_set.keys():
					var mv: Vector3 = verts[mvi]
					var d := Vector2(tv.x - mv.x, tv.z - mv.z).length()
					if d < best_d:
						best_d = d
						best_pair = Vector2i(tvi, mvi)
			if best_pair.x != -1:
				var tv2: Vector3 = verts[best_pair.x]
				var mv2: Vector3 = verts[best_pair.y]
				print("GAP_TO_MAIN target_root=", target_root, " main_root=", main_root,
					" min_xz_dist=%.3f" % best_d, " target_pt=", tv2, " main_pt=", mv2)

	# Castle/cc lenient keep (Towns never take this path):
	# 1) Keep large bailey/courtyard interiors (near-grade).
	# 2) Bridge ramps/ramparts into that network with a wider gap/climb; elevated↔elevated uses a
	#    walkway LoS that ignores wall-body carve punching the deck (so agents can walk the circuit).
	# 3) Refuse elevated orphans whose centroid sits inside the courtyard footprint — those are
	#    building roofs, not ramparts on the outer wall ring.
	var castle_courtyard_kept := 0
	var castle_bridged := 0
	var castle_roofs_skipped := 0
	var castle_roofs_stripped := 0
	var drop_polys: Dictionary = {} # poly index -> true (castle poly-level carve)
	# Used after the lenient block to reject stitch tris that re-create stripped roofs.
	var castle_roof_court_y := 0.0
	var castle_roof_hard_elev := 5.0
	var castle_filter_roof_welds := false
	# Exterior↔bailey weld seal (set inside lenient block when a courtyard bbox exists).
	var seal_court_valid := false
	var seal_court_min := Vector2()
	var seal_court_max := Vector2()
	var seal_court_y := 0.0
	var seal_ramp_lo := 2.0
	var seal_exterior_grade_vi: Dictionary = {} # vi -> true (tile-boundary near-grade, not courtyard)
	var seal_interior_grade_vi: Dictionary = {} # vi -> true (courtyard-root near-grade)
	var seal_courtyard_roots: Dictionary = {} # root -> true (persists for weld tagging)
	var weld_gate_blocked := 0
	var weld_ext_int_blocked := 0
	if lenient_castle:
		var keep_sqm_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_KEEP_SQM")
		var keep_sqm := float(keep_sqm_env) if keep_sqm_env != "" else 80.0
		var court_clear_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_COURTYARD_CLEARANCE")
		var court_clear := float(court_clear_env) if court_clear_env != "" else 10.0
		var court_min := Vector2(INF, INF)
		var court_max := Vector2(-INF, -INF)
		var court_y_sum := 0.0
		var court_y_n := 0
		var courtyard_roots: Dictionary = {}
		for r in area_by_root.keys():
			if touches_boundary_root.get(r, false):
				continue
			var area_r: float = float(area_by_root[r])
			if area_r < keep_sqm:
				continue
			var sy := 0.0
			var sx := 0.0
			var sz := 0.0
			var nvert := 0
			for p in (polys_by_root[r] as Array):
				for vi in polys[p]:
					var vv: Vector3 = verts[vi]
					sx += vv.x; sy += vv.y; sz += vv.z
					nvert += 1
			if nvert == 0:
				continue
			var invn := 1.0 / float(nvert)
			var terr := _sample_ground_height(ground_grid, GROUND_GRID_CELL, Vector2(sx * invn, sz * invn), ground_fallback)
			if (sy * invn) - terr > court_clear:
				continue
			touches_boundary_root[r] = true
			courtyard_roots[r] = true
			seal_courtyard_roots[r] = true
			castle_courtyard_kept += 1
			for p in (polys_by_root[r] as Array):
				for vi in polys[p]:
					var vv2: Vector3 = verts[vi]
					court_min.x = minf(court_min.x, vv2.x); court_min.y = minf(court_min.y, vv2.z)
					court_max.x = maxf(court_max.x, vv2.x); court_max.y = maxf(court_max.y, vv2.z)
					court_y_sum += vv2.y
					court_y_n += 1

		# Inset from the bailey bbox: only the INNER footprint is treated as "roof zone". Too small an
		# inset falsely flags wall-ring ramparts (centroids near the bailey edge) as roofs and skips
		# bridging them; too large lets interior roofs through. 12m leaves ~wall-thickness+walkway on
		# large baileys; on small cc_1-style courts a fixed 12m can invalidate the zone entirely, so
		# clamp by a fraction of the courtyard span (default 22%).
		var inset_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_ROOF_INSET")
		var roof_inset := float(inset_env) if inset_env != "" else 12.0
		var ramp_lo_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_RAMPART_LO")
		var ramp_lo := float(ramp_lo_env) if ramp_lo_env != "" else 2.0
		var ramp_hi_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_RAMPART_HI")
		var ramp_hi := float(ramp_hi_env) if ramp_hi_env != "" else 14.0
		var court_y := (court_y_sum / float(court_y_n)) if court_y_n > 0 else 0.0
		if court_y_n > 0 and court_min.x < court_max.x and court_min.y < court_max.y:
			seal_court_valid = true
			seal_court_min = court_min
			seal_court_max = court_max
			seal_court_y = court_y
			seal_ramp_lo = ramp_lo
		var court_w := court_max.x - court_min.x
		var court_d := court_max.y - court_min.y
		var court_span := minf(court_w, court_d)
		var frac_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_ROOF_INSET_FRAC")
		var inset_frac := float(frac_env) if frac_env != "" else 0.22
		var roof_inset_eff := roof_inset
		if court_span > 1.0:
			roof_inset_eff = minf(roof_inset, court_span * inset_frac)
			# Keep a non-empty interior even on very small baileys.
			roof_inset_eff = minf(roof_inset_eff, court_span * 0.45)
		var roof_min := court_min + Vector2(roof_inset_eff, roof_inset_eff)
		var roof_max := court_max - Vector2(roof_inset_eff, roof_inset_eff)
		var roof_bbox_valid := roof_min.x < roof_max.x and roof_min.y < roof_max.y

		var c_gap_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_BRIDGE_GAP")
		var c_gap := float(c_gap_env) if c_gap_env != "" else 4.0
		var c_climb_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_BRIDGE_CLIMB")
		var c_climb := float(c_climb_env) if c_climb_env != "" else 3.5
		# Circuit welds (rampart↔rampart): tighter than courtyard↔ramp links so roofs a couple metres
		# above the walkway don't attach, but wide enough to heal fragmented rampart/gate-deck gaps.
		var circ_gap_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_CIRCUIT_GAP")
		# 5.0 covers typical gatehouse / corner seams on cc_1 (was 3.5; Cc_1_00_01 needed ~4.3).
		var circ_gap := float(circ_gap_env) if circ_gap_env != "" else 5.0
		var circ_climb_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_CIRCUIT_CLIMB")
		var circ_climb := float(circ_climb_env) if circ_climb_env != "" else 1.5
		if c_gap > 0.0 and castle_courtyard_kept > 0:
			var c_cell := 1.0
			var c_grid: Dictionary = {}
			for r in polys_by_root.keys():
				if not touches_boundary_root.get(r, false):
					continue
				for p in (polys_by_root[r] as Array):
					for vi in polys[p]:
						var v: Vector3 = verts[vi]
						var key := Vector2i(floori(v.x / c_cell), floori(v.z / c_cell))
						if not c_grid.has(key):
							c_grid[key] = []
						(c_grid[key] as Array).append(vi)
			var c_changed := true
			while c_changed:
				c_changed = false
				for r in polys_by_root.keys():
					if touches_boundary_root.get(r, false):
						continue
					# Orphan mean Y + footprint — reject interior roofs before bridging.
					var osy := 0.0; var onv := 0; var oinside := 0
					for p in (polys_by_root[r] as Array):
						for vi in polys[p]:
							var ovv: Vector3 = verts[vi]
							osy += ovv.y; onv += 1
							if roof_bbox_valid and ovv.x >= roof_min.x and ovv.x <= roof_max.x \
								and ovv.z >= roof_min.y and ovv.z <= roof_max.y:
								oinside += 1
					if onv == 0:
						continue
					var ocy := osy / float(onv)
					var elev_above_court := ocy - court_y
					if elev_above_court >= ramp_lo:
						if elev_above_court > ramp_hi:
							castle_roofs_skipped += 1
							continue
						if float(oinside) / float(onv) >= 0.6:
							castle_roofs_skipped += 1
							continue
					var candidates: Array = []
					for p in (polys_by_root[r] as Array):
						for vi in polys[p]:
							var v: Vector3 = verts[vi]
							var key := Vector2i(floori(v.x / c_cell), floori(v.z / c_cell))
							var rad := ceili(c_gap / c_cell) + 1
							for dx in range(-rad, rad + 1):
								for dz in range(-rad, rad + 1):
									var nkey := Vector2i(key.x + dx, key.y + dz)
									if not c_grid.has(nkey):
										continue
									for ovi in (c_grid[nkey] as Array):
										var ov: Vector3 = verts[ovi]
										var d := Vector2(v.x - ov.x, v.z - ov.z).length()
										var climb := absf(v.y - ov.y)
										if d <= c_gap and climb <= c_climb:
											candidates.append({"d": d, "v": v, "ov": ov, "vi": vi, "ovi": ovi})
					candidates.sort_custom(func(x, y): return float(x["d"]) < float(y["d"]))
					for cand in candidates:
						var v2: Vector3 = cand["v"]
						var ov2: Vector3 = cand["ov"]
						if _segment_hits_xz_aabbs(v2, ov2, gate_seam_aabbs, 0.5):
							continue
						if _weld_is_exterior_interior_grade(
							v2, ov2, court_min, court_max, court_y, ramp_lo
						):
							continue
						var both_elev := (v2.y - court_y) >= ramp_lo and (ov2.y - court_y) >= ramp_lo
						# Elevated↔elevated orphan links use the tight circuit climb so a roof deck
						# a few metres above a rampart cannot attach here either.
						if both_elev and absf(v2.y - ov2.y) > circ_climb:
							continue
						# Elevated↔elevated: walkway LoS (merlon-aware).
						# Ground↔orphan: only use ramp LoS when the link actually climbs (approach
						# ramp toe). Flat near-grade links keep strict clear — permissive ramp LoS
						# here was welding through the castle gate and shredding the gate deck.
						var clear: bool
						if both_elev:
							clear = _bridge_segment_clear_walkway(obstruction_cells, v2, ov2)
						elif absf(v2.y - ov2.y) > 1.0:
							clear = _bridge_segment_clear_ramp(obstruction_cells, v2, ov2)
						else:
							clear = _bridge_segment_clear(obstruction_cells, v2, ov2)
						if not clear:
							continue
						touches_boundary_root[r] = true
						castle_bridged += 1
						bridged_components += 1
						if weld_enabled:
							weld_requests.append({"vi": int(cand["vi"]), "ovi": int(cand["ovi"])})
						for p in (polys_by_root[r] as Array):
							for vi in polys[p]:
								var v: Vector3 = verts[vi]
								var key := Vector2i(floori(v.x / c_cell), floori(v.z / c_cell))
								if not c_grid.has(key):
									c_grid[key] = []
								(c_grid[key] as Array).append(vi)
						c_changed = true
						break

			# Unify fragmented bailey floors. Courtyard-keep marks each large near-grade island
			# reachable on its own; the orphan→reachable bridge above then skips them, so passages
			# that only leave a Recast hairline (or a gap filled by fat wall-carve LoS) never get
			# stitch tris — green/blue courtyard pieces stay disjoint (Cc_1_hr_occ02).
			# Stay inside the keep-derived court bbox (no pad): padding pulled the exterior-touching
			# main root in and welded outer ground through the gate (weld4 regression).
			# Only weld among courtyard_kept roots — never the true tile-boundary exterior root.
			# Including exterior here (via touches_boundary) let ≤0.5m LoS-skip stitches cross the
			# closed gate seam and merge indoor/outdoor into one region (Cc_1_00_00).
			var court_floor_welded := 0
			if castle_courtyard_kept > 0 and court_y_n > 0:
				var floor_roots: Array = []
				for r in courtyard_roots.keys():
					var fsy := 0.0
					var f_in_court := 0
					for p in (polys_by_root[r] as Array):
						for vi in polys[p]:
							var fv: Vector3 = verts[vi]
							if fv.x >= court_min.x and fv.x <= court_max.x \
									and fv.z >= court_min.y and fv.z <= court_max.y:
								fsy += fv.y
								f_in_court += 1
					if f_in_court == 0:
						continue
					# Near-grade only (mean of verts inside the bailey bbox — not the whole
					# component, or mixed roof/ramp verts skew the mean).
					if (fsy / float(f_in_court)) - court_y >= ramp_lo:
						continue
					floor_roots.append(r)
				if floor_roots.size() > 1:
					var fr_parent: Dictionary = {}
					for r0 in floor_roots:
						fr_parent[r0] = r0
					var f_grid: Dictionary = {} # cell -> Array[{vi, root}]
					for r1 in floor_roots:
						for p in (polys_by_root[r1] as Array):
							for vi in polys[p]:
								var fv2: Vector3 = verts[vi]
								# Strict bailey footprint — no pad into exterior/gate lips.
								if fv2.x < court_min.x or fv2.x > court_max.x \
										or fv2.z < court_min.y or fv2.z > court_max.y:
									continue
								if fv2.y - court_y >= ramp_lo:
									continue
								var fk := Vector2i(floori(fv2.x / c_cell), floori(fv2.z / c_cell))
								if not f_grid.has(fk):
									f_grid[fk] = []
								(f_grid[fk] as Array).append({"vi": vi, "root": r1})
					var f_changed := true
					while f_changed:
						f_changed = false
						for r2 in floor_roots:
							var ra2: Variant = r2
							while fr_parent[ra2] != ra2:
								fr_parent[ra2] = fr_parent[fr_parent[ra2]]
								ra2 = fr_parent[ra2]
							var candidates2: Array = []
							for p in (polys_by_root[r2] as Array):
								for vi in polys[p]:
									var v3: Vector3 = verts[vi]
									if v3.x < court_min.x or v3.x > court_max.x \
											or v3.z < court_min.y or v3.z > court_max.y:
										continue
									if v3.y - court_y >= ramp_lo:
										continue
									var key3 := Vector2i(floori(v3.x / c_cell), floori(v3.z / c_cell))
									var rad3 := ceili(c_gap / c_cell) + 1
									for dx in range(-rad3, rad3 + 1):
										for dz in range(-rad3, rad3 + 1):
											var nkey3 := Vector2i(key3.x + dx, key3.y + dz)
											if not f_grid.has(nkey3):
												continue
											for ent in (f_grid[nkey3] as Array):
												var oroot: Variant = ent["root"]
												var rb2: Variant = oroot
												while fr_parent[rb2] != rb2:
													fr_parent[rb2] = fr_parent[fr_parent[rb2]]
													rb2 = fr_parent[rb2]
												if rb2 == ra2:
													continue
												var ovi3: int = int(ent["vi"])
												var ov3: Vector3 = verts[ovi3]
												var d3 := Vector2(v3.x - ov3.x, v3.z - ov3.z).length()
												var climb3 := absf(v3.y - ov3.y)
												if d3 <= c_gap and climb3 <= c_climb:
													candidates2.append({
														"d": d3, "v": v3, "ov": ov3,
														"vi": vi, "ovi": ovi3, "oroot": oroot
													})
							candidates2.sort_custom(func(x, y): return float(x["d"]) < float(y["d"]))
							for cand2 in candidates2:
								# Skip LoS for courtyard floor welds. Near-ground wall/ALWAYS_CARVE
								# stamps fill arch doorways and veto ramp-LoS even when both bailey
								# floors are open courtyards (hrz21 gaps ~3m). Exterior is never a
								# courtyard_root, so this cannot stitch through the closed main gate.
								var ru: Variant = r2
								while fr_parent[ru] != ru:
									fr_parent[ru] = fr_parent[fr_parent[ru]]
									ru = fr_parent[ru]
								var rv: Variant = cand2["oroot"]
								while fr_parent[rv] != rv:
									fr_parent[rv] = fr_parent[fr_parent[rv]]
									rv = fr_parent[rv]
								if ru != rv:
									fr_parent[rv] = ru
								court_floor_welded += 1
								if weld_enabled:
									# Tag as court_floor so stitch uses neighbour-fan (same as short
									# circuit gaps) — pick_open often finds no unused border edge on
									# these hairline bailey seams and silently drops the weld.
									weld_requests.append({
										"vi": int(cand2["vi"]),
										"ovi": int(cand2["ovi"]),
										"court_floor": true
									})
								f_changed = true
								break
					if court_floor_welded > 0 or OS.get_environment("DIAG_BRIDGE_POINTS") == "1":
						print("    prune: castle/cc courtyard floor welds=", court_floor_welded,
							" floor_roots=", floor_roots.size())

			# Grade-island welds: connect near-grade courtyard floor pieces that share a prune
			# root via ramparts (so floor_roots above is size 1) but are edge-disconnected at
			# ground — open hrz21 arches leave ~3m Recast gaps while the circuit still links
			# over the walls. Exterior boundary verts stay excluded via the court bbox.
			var grade_floor_welded := 0
			if castle_courtyard_kept > 0 and court_y_n > 0 and c_gap > 0.0:
				var grade_polys: Array = [] # poly indices near-grade inside court
				for pi_g in range(polys.size()):
					var idxs_g: PackedInt32Array = polys[pi_g]
					if idxs_g.size() < 3:
						continue
					var gsy := 0.0
					var gnv := 0
					var gin := 0
					for vi_g in idxs_g:
						var gv: Vector3 = verts[vi_g]
						if gv.x < court_min.x or gv.x > court_max.x \
								or gv.z < court_min.y or gv.z > court_max.y:
							continue
						gin += 1
						if gv.y - court_y < ramp_lo:
							gsy += gv.y
							gnv += 1
					if gin == 0 or gnv == 0:
						continue
					if (gsy / float(gnv)) - court_y >= ramp_lo:
						continue
					grade_polys.append(pi_g)
				if grade_polys.size() > 1:
					# Edge adjacency among grade polys only.
					var g_edge: Dictionary = {} # edge key -> Array[poly]
					for pi_e in grade_polys:
						var idxs_e: PackedInt32Array = polys[pi_e]
						for ei in range(idxs_e.size()):
							var a_e: int = idxs_e[ei]
							var b_e: int = idxs_e[(ei + 1) % idxs_e.size()]
							var ek := Vector2i(mini(a_e, b_e), maxi(a_e, b_e))
							if not g_edge.has(ek):
								g_edge[ek] = []
							(g_edge[ek] as Array).append(pi_e)
					var g_adj: Dictionary = {} # poly -> Array[poly]
					for pi_a in grade_polys:
						g_adj[pi_a] = []
					for ek2 in g_edge.keys():
						var plist: Array = g_edge[ek2]
						if plist.size() < 2:
							continue
						for ia in range(plist.size()):
							for ib in range(ia + 1, plist.size()):
								var pa: int = int(plist[ia])
								var pb: int = int(plist[ib])
								(g_adj[pa] as Array).append(pb)
								(g_adj[pb] as Array).append(pa)
					var g_island: Dictionary = {} # poly -> island id
					var island_id := 0
					for pi_s in grade_polys:
						if g_island.has(pi_s):
							continue
						var gq: Array = [pi_s]
						g_island[pi_s] = island_id
						var qi_s := 0
						while qi_s < gq.size():
							var cur_s: int = int(gq[qi_s])
							qi_s += 1
							for nb_s in (g_adj[cur_s] as Array):
								var nb_i: int = int(nb_s)
								if g_island.has(nb_i):
									continue
								g_island[nb_i] = island_id
								gq.append(nb_i)
						island_id += 1
					if island_id > 1:
						var gi_parent: Dictionary = {}
						for iid in range(island_id):
							gi_parent[iid] = iid
						var g_cell := 1.0
						var g_grid: Dictionary = {} # cell -> Array[{vi, island}]
						for pi_v in grade_polys:
							var iid_v: int = int(g_island[pi_v])
							for vi_v in polys[pi_v]:
								var vv: Vector3 = verts[vi_v]
								if vv.x < court_min.x or vv.x > court_max.x \
										or vv.z < court_min.y or vv.z > court_max.y:
									continue
								if vv.y - court_y >= ramp_lo:
									continue
								var gk := Vector2i(floori(vv.x / g_cell), floori(vv.z / g_cell))
								if not g_grid.has(gk):
									g_grid[gk] = []
								(g_grid[gk] as Array).append({"vi": vi_v, "island": iid_v})
						var g_changed := true
						while g_changed:
							g_changed = false
							for iid_a in range(island_id):
								var ra_g: Variant = iid_a
								while gi_parent[ra_g] != ra_g:
									gi_parent[ra_g] = gi_parent[gi_parent[ra_g]]
									ra_g = gi_parent[ra_g]
								var cands_g: Array = []
								for pi_c in grade_polys:
									if int(g_island[pi_c]) != iid_a:
										continue
									for vi_c in polys[pi_c]:
										var vc: Vector3 = verts[vi_c]
										if vc.x < court_min.x or vc.x > court_max.x \
												or vc.z < court_min.y or vc.z > court_max.y:
											continue
										if vc.y - court_y >= ramp_lo:
											continue
										var key_c := Vector2i(floori(vc.x / g_cell), floori(vc.z / g_cell))
										var rad_g := ceili(c_gap / g_cell) + 1
										for dx_g in range(-rad_g, rad_g + 1):
											for dz_g in range(-rad_g, rad_g + 1):
												var nk_g := Vector2i(key_c.x + dx_g, key_c.y + dz_g)
												if not g_grid.has(nk_g):
													continue
												for ent_g in (g_grid[nk_g] as Array):
													var oid: int = int(ent_g["island"])
													var rb_g: Variant = oid
													while gi_parent[rb_g] != rb_g:
														gi_parent[rb_g] = gi_parent[gi_parent[rb_g]]
														rb_g = gi_parent[rb_g]
													if rb_g == ra_g:
														continue
													var ovi_g: int = int(ent_g["vi"])
													var ov_g: Vector3 = verts[ovi_g]
													var d_g := Vector2(vc.x - ov_g.x, vc.z - ov_g.z).length()
													var climb_g := absf(vc.y - ov_g.y)
													if d_g > c_gap or climb_g > c_climb:
														continue
													if _segment_hits_xz_aabbs(vc, ov_g, gate_seam_aabbs, 0.5):
														continue
													# Prefer stitching through known arch portals. Also allow
													# clear LoS deep inside the roof-inset bailey so keep
													# undercroft / courtyard floor fragments (pillar gaps,
													# Recast seams) can unify — without welding around the
													# outer curtain / closed main gate (court lip).
													var mx_g := (vc.x + ov_g.x) * 0.5
													var mz_g := (vc.z + ov_g.z) * 0.5
													var through_portal := not portal_aabbs.is_empty() \
														and _point_in_arch_portal_aabbs(
															portal_aabbs, mx_g, mz_g, 0.75
														)
													var deep_bailey := roof_bbox_valid \
														and mx_g >= roof_min.x and mx_g <= roof_max.x \
														and mz_g >= roof_min.y and mz_g <= roof_max.y
													if through_portal:
														pass
													elif deep_bailey:
														# Skip LoS inside the roof-inset bailey: wall /
														# ALWAYS_CARVE cells still mark arch throats and
														# undercroft pillar gaps as blocked even when the
														# floor is open (same rationale as court_floor).
														pass
													elif portal_aabbs.is_empty() and _bridge_segment_clear(
														obstruction_cells, vc, ov_g
													):
														pass
													else:
														continue
													cands_g.append({
														"d": d_g, "vi": vi_c, "ovi": ovi_g,
														"oroot": oid
													})
								cands_g.sort_custom(func(x, y): return float(x["d"]) < float(y["d"]))
								for cand_g in cands_g:
									var ru_g: Variant = iid_a
									while gi_parent[ru_g] != ru_g:
										gi_parent[ru_g] = gi_parent[gi_parent[ru_g]]
										ru_g = gi_parent[ru_g]
									var rv_g: Variant = cand_g["oroot"]
									while gi_parent[rv_g] != rv_g:
										gi_parent[rv_g] = gi_parent[gi_parent[rv_g]]
										rv_g = gi_parent[rv_g]
									if ru_g != rv_g:
										gi_parent[rv_g] = ru_g
									grade_floor_welded += 1
									if weld_enabled:
										weld_requests.append({
											"vi": int(cand_g["vi"]),
											"ovi": int(cand_g["ovi"]),
											"court_floor": true
										})
									g_changed = true
									break
						if grade_floor_welded > 0 or OS.get_environment("DIAG_BRIDGE_POINTS") == "1":
							print(
								"    prune: castle/cc grade-island floor welds=", grade_floor_welded,
								" islands=", island_id, " grade_polys=", grade_polys.size()
							)

			# Forced arch-portal stitches: grade islands may already be one component via a path
			# around the wall ends, so the island-union pass never fires through hrz21 throats.
			# For each portal AABB, stitch the nearest near-grade verts on opposite sides of the
			# wall (through-axis) even when they share an island.
			var portal_forced := 0
			if castle_courtyard_kept > 0 and court_y_n > 0 and not portal_aabbs.is_empty():
				for pa_f in portal_aabbs:
					var pd: Dictionary = pa_f
					var pmin_x := float(pd["min_x"])
					var pmax_x := float(pd["max_x"])
					var pmin_z := float(pd["min_z"])
					var pmax_z := float(pd["max_z"])
					# Prefer kit-recorded through-axis; AABB aspect is wrong on deep recess kits.
					var through_z := bool(pd["through_z"]) if pd.has("through_z") \
						else ((pmax_z - pmin_z) <= (pmax_x - pmin_x))
					# Keep-grade entrance arches (Cc_1_00_06): stitch deck lips near bridge_y,
					# not buried terrain scraps ~2m below the throat.
					var is_entrance_portal := bool(pd.get("entrance", false))
					var portal_bridge_y := float(pd["bridge_y"]) if pd.has("bridge_y") else court_y
					var elev_allow := 4.0 if is_entrance_portal else minf(ramp_lo, 1.5)
					# Courtyard floors just outside the hole lips (±reach), not wall mid scraps.
					# Lateral pad: throat clamp can leave a sub-metre-wide hole while floors sit
					# under the wider gateway opening.
					var reach := 3.5
					var lat_pad := 2.0
					var side_a: Array = [] # {vi, v}
					var side_b: Array = []
					for pi_f in range(polys.size()):
						for vi_f in polys[pi_f]:
							var vf: Vector3 = verts[vi_f]
							# Strict near-grade — elevated scraps at hole lips create mid-air quads.
							# Entrance keep arches: allow deck band up to elev_allow above court.
							if vf.y - court_y >= elev_allow:
								continue
							if is_entrance_portal and absf(vf.y - portal_bridge_y) > 1.75:
								continue
							# Bailey-only: outer-wall arches have a field-side lip outside the
							# courtyard bbox. Stitching those reopens outdoor↔bailey on castles
							# where Recast already fused both into one prune root (Cc_2_hr).
							if vf.x < court_min.x or vf.x > court_max.x \
									or vf.z < court_min.y or vf.z > court_max.y:
								continue
							if through_z:
								if vf.x < pmin_x - lat_pad or vf.x > pmax_x + lat_pad:
									continue
								if vf.z >= pmin_z - reach and vf.z <= pmin_z + 0.35:
									side_a.append({"vi": vi_f, "v": vf})
								elif vf.z >= pmax_z - 0.35 and vf.z <= pmax_z + reach:
									side_b.append({"vi": vi_f, "v": vf})
							else:
								if vf.z < pmin_z - lat_pad or vf.z > pmax_z + lat_pad:
									continue
								if vf.x >= pmin_x - reach and vf.x <= pmin_x + 0.35:
									side_a.append({"vi": vi_f, "v": vf})
								elif vf.x >= pmax_x - 0.35 and vf.x <= pmax_x + reach:
									side_b.append({"vi": vi_f, "v": vf})
					if side_a.is_empty() or side_b.is_empty():
						if OS.get_environment("DIAG_ALWAYS_CARVE") == "1" \
								or OS.get_environment("DIAG_BRIDGE_POINTS") == "1":
							print(
								"    portal_forced empty sides aabb xz=(%.1f..%.1f, %.1f..%.1f) through_z=%s sides=%d/%d"
								% [pmin_x, pmax_x, pmin_z, pmax_z, through_z, side_a.size(), side_b.size()]
							)
						continue
					var portal_gap := maxf(c_gap, 8.0)
					# Prefer flat through-pairs. Reject only elevated scraps above court_y — north
					# bailey lips sit ~3.4m below mean court_y (sunken), so abs(elev) rejects the
					# real floor.
					var best_d := INF
					var best_vi := -1
					var best_ovi := -1
					var best_score := INF
					var near_dd := INF
					var near_dy := INF
					var near_ya := 0.0
					var near_yb := 0.0
					var pair_elev_max := elev_allow
					for ea in side_a:
						var va: Vector3 = ea["v"]
						for eb in side_b:
							var vb: Vector3 = eb["v"]
							var dd := Vector2(va.x - vb.x, va.z - vb.z).length()
							var dy := absf(va.y - vb.y)
							if dd < near_dd:
								near_dd = dd
								near_dy = dy
								near_ya = va.y
								near_yb = vb.y
							if dd > portal_gap or dy > 2.0:
								continue
							# Either lip above allowed band ⇒ mid-air scrap, skip.
							if va.y - court_y > pair_elev_max or vb.y - court_y > pair_elev_max:
								continue
							var lateral := absf(va.x - vb.x) if through_z else absf(va.z - vb.z)
							# Prefer near-mean court when available, but don't reject sunken floors.
							var sunk := maxf(court_y - va.y, court_y - vb.y)
							var score := dd + dy * 4.0 + lateral * 2.0 + maxf(0.0, sunk - 1.0) * 0.5
							if is_entrance_portal:
								score += absf(va.y - portal_bridge_y) + absf(vb.y - portal_bridge_y)
							if dd < 1.5:
								score += 10.0
							if score < best_score:
								best_score = score
								best_d = dd
								best_vi = int(ea["vi"])
								best_ovi = int(eb["vi"])
					if best_vi < 0:
						if OS.get_environment("DIAG_ALWAYS_CARVE") == "1" \
								or OS.get_environment("DIAG_BRIDGE_POINTS") == "1":
							print(
								"    portal_forced miss aabb xz=(%.1f..%.1f, %.1f..%.1f) through_z=%s sides=%d/%d near_dd=%.2f dy=%.2f ya=%.2f yb=%.2f court_y=%.2f"
								% [pmin_x, pmax_x, pmin_z, pmax_z, through_z, side_a.size(), side_b.size(),
									near_dd, near_dy, near_ya, near_yb, court_y]
							)
						continue
					portal_forced += 1
					if OS.get_environment("DIAG_BRIDGE_POINTS") == "1" \
							or OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
						var va_f: Vector3 = verts[best_vi]
						var vb_f: Vector3 = verts[best_ovi]
						print(
							"    portal_forced d=%.2f through_z=%s a=(%.1f,%.2f,%.1f) b=(%.1f,%.2f,%.1f)"
							% [best_d, through_z, va_f.x, va_f.y, va_f.z, vb_f.x, vb_f.y, vb_f.z]
						)
					if weld_enabled:
						weld_requests.append({
							"vi": best_vi,
							"ovi": best_ovi,
							"court_floor": true,
							"portal_forced": true
						})
				if portal_forced > 0 or OS.get_environment("DIAG_BRIDGE_POINTS") == "1" \
						or OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
					print(
						"    prune: castle/cc portal forced stitches=", portal_forced,
						" portals=", portal_aabbs.size()
					)

			# Third pass: weld elevated rampart polys to EACH OTHER at the SAME deck height.
			# Per-POLY (not per-root): on small cc_1 castles, ramps often union bailey floor +
			# wall walk into one prune root, so a root-mean Y stays near grade and the old
			# elev_kept filter left band_ys empty → this entire heal was skipped (Cc_1_00_01).
			var circuit_welds := 0
			var deck_band_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_DECK_BAND")
			# Slightly wider than the old 1.25 so gatehouse / corner fragments at near-deck height
			# still join the circuit (roofs remain excluded via roof-zone + above-deck strip).
			var deck_band := float(deck_band_env) if deck_band_env != "" else 2.0
			var elev_poly_ys: Array = []
			var elev_poly_candidates: Array = [] # {pi, ctr_y, inside_frac}
			for r in area_by_root.keys():
				if not touches_boundary_root.get(r, false):
					continue
				for p in (polys_by_root[r] as Array):
					var pi0: int = p
					var idxs0: PackedInt32Array = polys[pi0]
					if idxs0.size() < 3:
						continue
					var ctr0 := Vector3.ZERO
					var inside0 := 0
					for vi0 in idxs0:
						var vv0: Vector3 = verts[vi0]
						ctr0 += vv0
						if roof_bbox_valid and vv0.x >= roof_min.x and vv0.x <= roof_max.x \
							and vv0.z >= roof_min.y and vv0.z <= roof_max.y:
							inside0 += 1
					ctr0 /= float(idxs0.size())
					var elev0 := ctr0.y - court_y
					if elev0 < ramp_lo or elev0 > ramp_hi:
						continue
					var ifrac0 := float(inside0) / float(idxs0.size())
					# Do not exclude by roof-zone here: circuit heal must see every elevated poly so
					# wall-walk fragments that sit inside the bailey bbox still get stitched. Deep
					# interior roofs are removed afterward by the poly roof strip / component strip.
					elev_poly_ys.append(ctr0.y)
					elev_poly_candidates.append({"pi": pi0, "y": ctr0.y, "ifrac": ifrac0})
			# Dominant elevated walkway height (median of kept elevated polys outside roof zone).
			elev_poly_ys.sort()
			var deck_y := float(elev_poly_ys[elev_poly_ys.size() / 2]) if not elev_poly_ys.is_empty() else court_y
			# Heal gaps on the elevated walkway itself. Include ALL kept elevated polys in the
			# rampart band (not only ±deck_band around the median): mid-height ledges/ramps on
			# cc_1 sit 3-5m below the dominant wall-top mode and were previously left out of the
			# heal, so they never received circuit welds. Cross-height links stay gated by
			# circ_climb; deck_y/deck_band remain for logging + roof strip.
			var deck_polys: Array = []
			for cand_p in elev_poly_candidates:
				deck_polys.append(int(cand_p["pi"]))
			var dparent: Dictionary = {}
			for pi2 in deck_polys:
				dparent[pi2] = pi2
			var edge_deck: Dictionary = {}
			for pi3 in deck_polys:
				var poly_d: PackedInt32Array = polys[pi3]
				var n_d := poly_d.size()
				for ei in range(n_d):
					var ea: int = poly_d[ei]
					var eb: int = poly_d[(ei + 1) % n_d]
					var ek := Vector2i(mini(ea, eb), maxi(ea, eb))
					if edge_deck.has(ek):
						var other_p: int = edge_deck[ek]
						var ra := _find_root_dict(dparent, pi3)
						var rb := _find_root_dict(dparent, other_p)
						if ra != rb:
							dparent[ra] = rb
					else:
						edge_deck[ek] = pi3
			var e_grid: Dictionary = {}
			var vert_droot: Dictionary = {}
			for pi4 in deck_polys:
				var dr := _find_root_dict(dparent, pi4)
				for vi4 in polys[pi4]:
					vert_droot[vi4] = dr
					var v4: Vector3 = verts[vi4]
					var key4 := Vector2i(floori(v4.x / c_cell), floori(v4.z / c_cell))
					if not e_grid.has(key4):
						e_grid[key4] = []
					(e_grid[key4] as Array).append(vi4)
			# Collect cross-island candidates first (sorted by distance), then accept up to a few
			# vert pairs per elevated-root pair. Avoids (a) root-pair lock before geometry succeeds
			# and (b) exploding into thousands of stitches between the same islands.
			var cross_cands: Array = [] # {d, vi, ovi, dr, odr}
			var linked_verts: Dictionary = {} # hole-fill midpoint cells
			var rad2 := ceili(circ_gap / c_cell) + 1
			for pi5 in deck_polys:
				var dr5 := _find_root_dict(dparent, pi5)
				for vi5 in polys[pi5]:
					var v5: Vector3 = verts[vi5]
					var key5 := Vector2i(floori(v5.x / c_cell), floori(v5.z / c_cell))
					for dx in range(-rad2, rad2 + 1):
						for dz in range(-rad2, rad2 + 1):
							var nkey := Vector2i(key5.x + dx, key5.y + dz)
							if not e_grid.has(nkey):
								continue
							for ovi in (e_grid[nkey] as Array):
								if int(ovi) <= vi5:
									continue # each unordered pair once
								var ov5: Vector3 = verts[ovi]
								var d5 := Vector2(v5.x - ov5.x, v5.z - ov5.z).length()
								var dy5 := absf(v5.y - ov5.y)
								if d5 > circ_gap or dy5 > circ_climb:
									continue
								var odr := _find_root_dict(dparent, int(vert_droot[ovi]))
								dr5 = _find_root_dict(dparent, pi5)
								var cross := odr != dr5
								if cross:
									# Elevated↔elevated: skip LoS for gaps within circ_gap. Wall-carve
									# stamps between merlons were rejecting real 3-5m rampart seams
									# (Cc_1_00_01: 4.3m split between the two largest wall-walk islands).
									# Climb is already gated by circ_climb.
									cross_cands.append({
										"d": d5, "vi": vi5, "ovi": int(ovi), "dr": dr5, "odr": odr,
									})
									continue
								# Local hole fill: same island, short gap, midpoint empty.
								if d5 > 2.5 or dy5 > 0.75:
									continue
								if edge_deck.has(Vector2i(mini(vi5, int(ovi)), maxi(vi5, int(ovi)))):
									continue
								var hole := false
								if d5 < 0.35:
									hole = true
								else:
									var mid := Vector3((v5.x + ov5.x) * 0.5, (v5.y + ov5.y) * 0.5, (v5.z + ov5.z) * 0.5)
									var mkey := Vector2i(floori(mid.x / c_cell), floori(mid.z / c_cell))
									var mid_clear := true
									for mdx in range(-1, 2):
										for mdz in range(-1, 2):
											var mk2 := Vector2i(mkey.x + mdx, mkey.y + mdz)
											if not e_grid.has(mk2):
												continue
											for mvi in (e_grid[mk2] as Array):
												if mvi == vi5 or mvi == ovi:
													continue
												var mv: Vector3 = verts[mvi]
												if Vector2(mv.x - mid.x, mv.z - mid.z).length() < 0.45 \
														and absf(mv.y - mid.y) <= 0.75:
													mid_clear = false
													break
											if not mid_clear:
												break
										if not mid_clear:
											break
									hole = mid_clear
								if not hole:
									continue
								var mpair := Vector2i(floori((v5.x + ov5.x) * 0.5 / c_cell),
									floori((v5.z + ov5.z) * 0.5 / c_cell))
								if linked_verts.has(mpair):
									continue
								linked_verts[mpair] = true
								circuit_welds += 1
								if weld_enabled:
									weld_requests.append({"vi": vi5, "ovi": int(ovi), "circuit": true})
			cross_cands.sort_custom(func(a, b): return float(a["d"]) < float(b["d"]))
			if OS.get_environment("DIAG_CIRCUIT") == "1":
				var bb_min := Vector3(INF, INF, INF)
				var bb_max := Vector3(-INF, -INF, -INF)
				var island_n: Dictionary = {}
				for pi_d in deck_polys:
					var rd := _find_root_dict(dparent, pi_d)
					island_n[rd] = int(island_n.get(rd, 0)) + 1
					for vi_d in polys[pi_d]:
						var pv: Vector3 = verts[vi_d]
						bb_min = bb_min.min(pv)
						bb_max = bb_max.max(pv)
				print("    DIAG_CIRCUIT cross_cands=", cross_cands.size(),
					" deck_polys=", deck_polys.size(),
					" edge_islands=", island_n.size(),
					" bbox=", bb_min, "..", bb_max)
				# Small elevated islands by poly count
				var smalls: Array = []
				for rd2 in island_n.keys():
					if int(island_n[rd2]) <= 40:
						var sy := 0.0; var sn := 0; var sx := 0.0; var sz := 0.0
						for pi_s in deck_polys:
							if _find_root_dict(dparent, pi_s) != rd2:
								continue
							for vi_s in polys[pi_s]:
								var ps: Vector3 = verts[vi_s]
								sx += ps.x; sy += ps.y; sz += ps.z; sn += 1
						if sn > 0:
							smalls.append({"n": int(island_n[rd2]), "c": Vector3(sx / sn, sy / sn, sz / sn)})
				smalls.sort_custom(func(a, b): return int(a["n"]) > int(b["n"]))
				for s in smalls.slice(0, 8):
					print("      small_island polys=", s["n"], " center=", s["c"])
			var root_pair_n: Dictionary = {}
			const CIRCUIT_ATTEMPTS_PER_ROOT_PAIR := 12
			for cand in cross_cands:
				var dr_c: int = _find_root_dict(dparent, int(cand["dr"]))
				var odr_c: int = _find_root_dict(dparent, int(cand["odr"]))
				if dr_c == odr_c:
					continue
				var rpair := Vector2i(mini(dr_c, odr_c), maxi(dr_c, odr_c))
				var n_try := int(root_pair_n.get(rpair, 0))
				if n_try >= CIRCUIT_ATTEMPTS_PER_ROOT_PAIR:
					continue
				root_pair_n[rpair] = n_try + 1
				circuit_welds += 1
				# No dparent union here — stitch success is decided later; keep trying up to
				# CIRCUIT_ATTEMPTS_PER_ROOT_PAIR closest vert pairs per island pair.
				if weld_enabled:
					weld_requests.append({
						"vi": int(cand["vi"]), "ovi": int(cand["ovi"]), "circuit": true,
					})
			castle_bridged += circuit_welds
			if circuit_welds > 0 or not deck_polys.is_empty():
				print("    prune: castle/cc rampart circuit welds=", circuit_welds,
					" deck_polys=", deck_polys.size(),
					" (circ_gap=", circ_gap, " circ_climb=", circ_climb,
					" deck_y=%.2f deck_band=" % deck_y, deck_band, ")")

		# Strip roofs that still made it into the kept set: interior footprint OR stacked above the
		# dominant rampart deck band. Never strip courtyard roots.
		# Per-poly median (same as circuit pass): root-mean Y misses fused bailey+rampart roots and
		# then roof-band stitch filtering rejects every elevated weld (Cc_1_00_01).
		var strip_deck_y := court_y
		var strip_band := 2.0
		var elev_ys2: Array = []
		for r2 in area_by_root.keys():
			if not touches_boundary_root.get(r2, false):
				continue
			for p2 in (polys_by_root[r2] as Array):
				var idxs2: PackedInt32Array = polys[p2]
				if idxs2.size() < 3:
					continue
				var ctr2 := Vector3.ZERO
				var inside2 := 0
				for vi2 in idxs2:
					var vv2s: Vector3 = verts[vi2]
					ctr2 += vv2s
					if roof_bbox_valid and vv2s.x >= roof_min.x and vv2s.x <= roof_max.x \
						and vv2s.z >= roof_min.y and vv2s.z <= roof_max.y:
						inside2 += 1
				ctr2 /= float(idxs2.size())
				if ctr2.y - court_y < ramp_lo or ctr2.y - court_y > ramp_hi:
					continue
				if roof_bbox_valid and float(inside2) / float(idxs2.size()) >= 0.6:
					continue
				elev_ys2.append(ctr2.y)
		if not elev_ys2.is_empty():
			elev_ys2.sort()
			strip_deck_y = float(elev_ys2[elev_ys2.size() / 2])
			var db_env2 := OS.get_environment("NAV_EXPERIMENT_CASTLE_DECK_BAND")
			strip_band = float(db_env2) if db_env2 != "" else 2.0
		var inside_frac_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_ROOF_INSIDE")
		var inside_frac := float(inside_frac_env) if inside_frac_env != "" else 0.45
		for r in area_by_root.keys():
			if not touches_boundary_root.get(r, false) or courtyard_roots.has(r):
				continue
			var n3 := 0
			var inside3 := 0
			var sy3 := 0.0
			for p in (polys_by_root[r] as Array):
				for vi in polys[p]:
					var vv3: Vector3 = verts[vi]
					sy3 += vv3.y
					n3 += 1
					if roof_bbox_valid and vv3.x >= roof_min.x and vv3.x <= roof_max.x \
						and vv3.z >= roof_min.y and vv3.z <= roof_max.y:
						inside3 += 1
			if n3 == 0:
				continue
			var my3 := sy3 / float(n3)
			if my3 - court_y < ramp_lo:
				continue
			var is_interior := roof_bbox_valid and float(inside3) / float(n3) >= inside_frac
			var is_above_deck := my3 > strip_deck_y + strip_band
			if is_interior or is_above_deck:
				touches_boundary_root[r] = false
				castle_roofs_stripped += 1

		# Poly-level fat roof strip (default ON). Component-level strip fails when roof pads are
		# mesh-/weld-connected into the rampart circuit (Cc_1_00_02: ~750sqm elevated blue). A
		# courtyard-inset test also misses building roofs that sit in the perimeter band around the
		# open bailey. Instead drop elevated FLAT polys whose local neighborhood area is fat (wide
		# roof/floor pads); thin rampart ribbons stay under the threshold. Keep anything near the
		# tile edge (exterior approaches). Disable with NAV_EXPERIMENT_CASTLE_POLY_ROOF_STRIP=0.
		var poly_drop_roof := 0
		var ramp_flat_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_POLY_FLAT_SLOPE")
		var flat_slope_max := float(ramp_flat_env) if ramp_flat_env != "" else 12.0
		var poly_elev_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_POLY_ELEV")
		var poly_elev := float(poly_elev_env) if poly_elev_env != "" else 2.0
		var fat_thr_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_FAT_AREA")
		# Fat/above-deck only in the inset roof zone. Aggressive apron fat (thr~8) removed roofs
		# but shredded the wall walk on Cc_1 — keep thr high and wall_ring_lo low.
		var fat_thr := float(fat_thr_env) if fat_thr_env != "" else 18.0
		var fat_r_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_FAT_RADIUS")
		var fat_r := float(fat_r_env) if fat_r_env != "" else 3.0
		var ring_keep_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_ROOF_RING_KEEP")
		var ring_keep := float(ring_keep_env) if ring_keep_env != "" else 8.0
		var apron_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_ROOF_APRON")
		var roof_apron := float(apron_env) if apron_env != "" else 18.0
		var wall_lo_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_WALL_RING_LO")
		# Exclude nearly everything outside the open bailey from strip candidates (wall walk).
		var wall_ring_lo := float(wall_lo_env) if wall_lo_env != "" else 0.5
		var poly_roof_on := OS.get_environment("NAV_EXPERIMENT_CASTLE_POLY_ROOF_STRIP") != "0"
		if poly_roof_on and castle_courtyard_kept > 0:
			var roof_elev_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_POLY_ROOF_ELEV")
			var roof_elev := float(roof_elev_env) if roof_elev_env != "" else maxf(poly_elev, ramp_lo)
			var apron_min := court_min - Vector2(roof_apron, roof_apron)
			var apron_max := court_max + Vector2(roof_apron, roof_apron)
			var elev_poly_roof: Array = [] # {pi, ctr, area, inside_court}
			for r in area_by_root.keys():
				if not touches_boundary_root.get(r, false):
					continue
				for p in (polys_by_root[r] as Array):
					var pi_r: int = p
					if drop_polys.has(pi_r):
						continue
					var idxs_r: PackedInt32Array = polys[pi_r]
					if idxs_r.size() < 3:
						continue
					var ar: Vector3 = verts[idxs_r[0]]
					var br: Vector3 = verts[idxs_r[1]]
					var cr: Vector3 = verts[idxs_r[2]]
					var ctr_r := (ar + br + cr) / 3.0
					var terr_r := _sample_ground_height(
						ground_grid, GROUND_GRID_CELL, Vector2(ctr_r.x, ctr_r.z), ground_fallback
					)
					var elev_terr_r := ctr_r.y - terr_r
					if elev_terr_r < roof_elev and ctr_r.y - court_y < roof_elev:
						continue
					var d_edge := minf(
						minf(ctr_r.x - tile_min.x, tile_max.x - ctr_r.x),
						minf(ctr_r.z - tile_min.y, tile_max.y - ctr_r.z)
					)
					if d_edge < ring_keep:
						continue
					if ctr_r.x < apron_min.x or ctr_r.x > apron_max.x \
							or ctr_r.z < apron_min.y or ctr_r.z > apron_max.y:
						continue
					var d_out := 0.0
					if ctr_r.x < court_min.x:
						d_out = maxf(d_out, court_min.x - ctr_r.x)
					elif ctr_r.x > court_max.x:
						d_out = maxf(d_out, ctr_r.x - court_max.x)
					if ctr_r.z < court_min.y:
						d_out = maxf(d_out, court_min.y - ctr_r.z)
					elif ctr_r.z > court_max.y:
						d_out = maxf(d_out, ctr_r.z - court_max.y)
					var inside_court := ctr_r.x >= court_min.x and ctr_r.x <= court_max.x \
						and ctr_r.z >= court_min.y and ctr_r.z <= court_max.y
					# Wall-walk ring: never a fat-strip candidate (fixes patchy ramparts).
					if not inside_court and d_out >= wall_ring_lo:
						continue
					var nrm_r := (br - ar).cross(cr - ar)
					var area_r := nrm_r.length() * 0.5
					if area_r < 0.000001:
						continue
					# Deep = inset roof zone. Court bbox on small kits reaches the wall walk, so
					# "inside_court" alone is NOT safe for fat/above-deck strips.
					var deep_r := roof_bbox_valid and ctr_r.x >= roof_min.x and ctr_r.x <= roof_max.x \
						and ctr_r.z >= roof_min.y and ctr_r.z <= roof_max.y
					elev_poly_roof.append({
						"pi": pi_r,
						"ctr": ctr_r,
						"area": area_r,
						"deep": deep_r,
					})
			var roof_cell := 1.0
			var roof_buckets: Dictionary = {}
			for ii in range(elev_poly_roof.size()):
				var ctr_b: Vector3 = elev_poly_roof[ii]["ctr"]
				var bk := Vector2i(floori(ctr_b.x / roof_cell), floori(ctr_b.z / roof_cell))
				if not roof_buckets.has(bk):
					roof_buckets[bk] = []
				(roof_buckets[bk] as Array).append(ii)
			var rad_cells_r := ceili(fat_r / roof_cell) + 1
			var r2_r := fat_r * fat_r
			var hard_elev_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_POLY_ROOF_HARD_ELEV")
			var hard_elev := float(hard_elev_env) if hard_elev_env != "" else 2.0
			castle_roof_court_y = strip_deck_y
			castle_roof_hard_elev = hard_elev
			castle_filter_roof_welds = true
			var poly_band_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_POLY_ROOF_BAND")
			var poly_above_deck := float(poly_band_env) if poly_band_env != "" else 2.0
			var fat_at_deck := OS.get_environment("NAV_EXPERIMENT_CASTLE_POLY_ROOF_FAT") != "0"
			for ii in range(elev_poly_roof.size()):
				var ctr3: Vector3 = elev_poly_roof[ii]["ctr"]
				var pi_drop: int = int(elev_poly_roof[ii]["pi"])
				var deep_c := bool(elev_poly_roof[ii]["deep"])
				# Only strip in the inset interior roof zone — never the court-edge / wall walk.
				if deep_c and ctr3.y > strip_deck_y + poly_above_deck:
					drop_polys[pi_drop] = true
					poly_drop_roof += 1
					continue
				if not fat_at_deck or not deep_c:
					continue
				var bk2 := Vector2i(floori(ctr3.x / roof_cell), floori(ctr3.z / roof_cell))
				var local_a := 0.0
				for dx in range(-rad_cells_r, rad_cells_r + 1):
					for dz in range(-rad_cells_r, rad_cells_r + 1):
						var nk := Vector2i(bk2.x + dx, bk2.y + dz)
						if not roof_buckets.has(nk):
							continue
						for jj in (roof_buckets[nk] as Array):
							var octr: Vector3 = elev_poly_roof[jj]["ctr"]
							if absf(octr.y - ctr3.y) > 1.5:
								continue
							var ddx := octr.x - ctr3.x
							var ddz := octr.z - ctr3.z
							if ddx * ddx + ddz * ddz <= r2_r:
								local_a += float(elev_poly_roof[jj]["area"])
				if local_a >= fat_thr:
					drop_polys[pi_drop] = true
					poly_drop_roof += 1

		# Fat-platform carve: wide elevated pads (tower/gatehouse tops, building decks) have high
		# local nav density; thin rampart ribbons do not. Drop flat polys on the wall ring whose
		# neighborhood area exceeds the threshold. Disable with NAV_EXPERIMENT_CASTLE_FAT_CARVE=0.
		# (Legacy opt-in; poly roof strip above covers the common roof case by default.)
		var poly_drop_fat := 0
		# Opt-in: wide tower/gatehouse pads. Default off until visually confirmed on Cc_rd03.
		if OS.get_environment("NAV_EXPERIMENT_CASTLE_FAT_CARVE") == "1" and roof_bbox_valid:
			var ring_pad := 16.0
			var elev_poly_info: Array = [] # {pi, ctr, area}
			for r in area_by_root.keys():
				if not touches_boundary_root.get(r, false):
					continue
				for p in (polys_by_root[r] as Array):
					var pi: int = p
					var idxs: PackedInt32Array = polys[pi]
					if idxs.size() < 3:
						continue
					var a: Vector3 = verts[idxs[0]]
					var b: Vector3 = verts[idxs[1]]
					var c: Vector3 = verts[idxs[2]]
					var ctr := (a + b + c) / 3.0
					if ctr.y - court_y < poly_elev:
						continue
					var on_ring := ctr.x >= court_min.x - ring_pad and ctr.x <= court_max.x + ring_pad \
						and ctr.z >= court_min.y - ring_pad and ctr.z <= court_max.y + ring_pad
					if not on_ring:
						continue
					var normal := (b - a).cross(c - a)
					var area_p := normal.length() * 0.5
					if area_p < 0.000001:
						continue
					var slope_deg := rad_to_deg(acos(clampf(absf(normal.normalized().y), 0.0, 1.0)))
					if slope_deg >= flat_slope_max:
						continue
					elev_poly_info.append({"pi": pi, "ctr": ctr, "area": area_p})
			var fat_cell := 1.0
			var fat_buckets: Dictionary = {} # Vector2i -> Array of info indices
			for ii in range(elev_poly_info.size()):
				var ctr2: Vector3 = elev_poly_info[ii]["ctr"]
				var bk := Vector2i(floori(ctr2.x / fat_cell), floori(ctr2.z / fat_cell))
				if not fat_buckets.has(bk):
					fat_buckets[bk] = []
				(fat_buckets[bk] as Array).append(ii)
			var rad_cells := ceili(fat_r / fat_cell) + 1
			var r2 := fat_r * fat_r
			for ii in range(elev_poly_info.size()):
				var ctr3: Vector3 = elev_poly_info[ii]["ctr"]
				var bk2 := Vector2i(floori(ctr3.x / fat_cell), floori(ctr3.z / fat_cell))
				var local_a := 0.0
				for dx in range(-rad_cells, rad_cells + 1):
					for dz in range(-rad_cells, rad_cells + 1):
						var nk := Vector2i(bk2.x + dx, bk2.y + dz)
						if not fat_buckets.has(nk):
							continue
						for jj in (fat_buckets[nk] as Array):
							var octr: Vector3 = elev_poly_info[jj]["ctr"]
							if absf(octr.y - ctr3.y) > 1.5:
								continue
							var ddx := octr.x - ctr3.x
							var ddz := octr.z - ctr3.z
							if ddx * ddx + ddz * ddz <= r2:
								local_a += float(elev_poly_info[jj]["area"])
				if local_a >= fat_thr:
					drop_polys[int(elev_poly_info[ii]["pi"])] = true
					poly_drop_fat += 1

		# Post-drop circuit heal: poly roof strip can delete the only index-bridge between two
		# wall-walk chunks, leaving a 3-5m position gap that the pre-drop pass never saw (they
		# were one edge-island). Re-run elevated↔elevated welds on surviving polys only.
		if c_gap > 0.0 and castle_courtyard_kept > 0 and weld_enabled \
				and OS.get_environment("NAV_EXPERIMENT_CASTLE_POST_DROP_CIRCUIT") != "0":
			var pd_polys: Array = []
			for r_pd in area_by_root.keys():
				if not touches_boundary_root.get(r_pd, false):
					continue
				for p_pd in (polys_by_root[r_pd] as Array):
					var pi_pd: int = p_pd
					if drop_polys.has(pi_pd):
						continue
					var idxs_pd: PackedInt32Array = polys[pi_pd]
					if idxs_pd.size() < 3:
						continue
					var ctr_pd := Vector3.ZERO
					for vi_pd in idxs_pd:
						ctr_pd += verts[vi_pd]
					ctr_pd /= float(idxs_pd.size())
					var elev_pd := ctr_pd.y - court_y
					if elev_pd < ramp_lo or elev_pd > ramp_hi:
						continue
					pd_polys.append(pi_pd)
			# Position-rounded edges (Godot / region-colouring), not raw indices — index bridges
			# through soon-to-be-green or micro-sliver tris hide real 4m rampart seams.
			var pd_parent: Dictionary = {}
			for pi_a in pd_polys:
				pd_parent[pi_a] = pi_a
			var pd_edge_map: Dictionary = {} # "ax,ay,az|bx,by,bz" -> poly
			for pi_b in pd_polys:
				var poly_pd: PackedInt32Array = polys[pi_b]
				var n_pd := poly_pd.size()
				for ei_pd in range(n_pd):
					var pa: Vector3 = verts[poly_pd[ei_pd]]
					var pb: Vector3 = verts[poly_pd[(ei_pd + 1) % n_pd]]
					var ka := "%d,%d,%d" % [roundi(pa.x * 1000.0), roundi(pa.y * 1000.0), roundi(pa.z * 1000.0)]
					var kb := "%d,%d,%d" % [roundi(pb.x * 1000.0), roundi(pb.y * 1000.0), roundi(pb.z * 1000.0)]
					var ek_s := ka + "|" + kb if ka <= kb else kb + "|" + ka
					if pd_edge_map.has(ek_s):
						var other_pd: int = pd_edge_map[ek_s]
						var ra_pd := _find_root_dict(pd_parent, pi_b)
						var rb_pd := _find_root_dict(pd_parent, other_pd)
						if ra_pd != rb_pd:
							pd_parent[ra_pd] = rb_pd
					else:
						pd_edge_map[ek_s] = pi_b
			var pd_grid: Dictionary = {}
			var pd_vert_root: Dictionary = {}
			var pd_cell := 1.0
			for pi_c in pd_polys:
				var dr_c := _find_root_dict(pd_parent, pi_c)
				for vi_c in polys[pi_c]:
					pd_vert_root[vi_c] = dr_c
					var v_c: Vector3 = verts[vi_c]
					var key_c := Vector2i(floori(v_c.x / pd_cell), floori(v_c.z / pd_cell))
					if not pd_grid.has(key_c):
						pd_grid[key_c] = []
					(pd_grid[key_c] as Array).append(vi_c)
			var pd_cands: Array = []
			var pd_rad := ceili(circ_gap / pd_cell) + 1
			for pi_d in pd_polys:
				var dr_d := _find_root_dict(pd_parent, pi_d)
				for vi_d in polys[pi_d]:
					var v_d: Vector3 = verts[vi_d]
					var key_d := Vector2i(floori(v_d.x / pd_cell), floori(v_d.z / pd_cell))
					for dx_d in range(-pd_rad, pd_rad + 1):
						for dz_d in range(-pd_rad, pd_rad + 1):
							var nk_d := Vector2i(key_d.x + dx_d, key_d.y + dz_d)
							if not pd_grid.has(nk_d):
								continue
							for ovi_d in (pd_grid[nk_d] as Array):
								if int(ovi_d) <= vi_d:
									continue
								var ov_d: Vector3 = verts[ovi_d]
								var d_d := Vector2(v_d.x - ov_d.x, v_d.z - ov_d.z).length()
								var dy_d := absf(v_d.y - ov_d.y)
								if d_d > circ_gap or dy_d > circ_climb:
									continue
								var odr_d := _find_root_dict(pd_parent, int(pd_vert_root[ovi_d]))
								dr_d = _find_root_dict(pd_parent, pi_d)
								if odr_d == dr_d:
									continue
								pd_cands.append({
									"d": d_d, "vi": vi_d, "ovi": int(ovi_d), "dr": dr_d, "odr": odr_d,
								})
			pd_cands.sort_custom(func(a, b): return float(a["d"]) < float(b["d"]))
			var pd_pair_n: Dictionary = {}
			var post_drop_welds := 0
			const POST_DROP_ATTEMPTS := 12
			for cand_pd in pd_cands:
				var dr_e: int = _find_root_dict(pd_parent, int(cand_pd["dr"]))
				var odr_e: int = _find_root_dict(pd_parent, int(cand_pd["odr"]))
				if dr_e == odr_e:
					continue
				var rpair_e := Vector2i(mini(dr_e, odr_e), maxi(dr_e, odr_e))
				var n_e := int(pd_pair_n.get(rpair_e, 0))
				if n_e >= POST_DROP_ATTEMPTS:
					continue
				pd_pair_n[rpair_e] = n_e + 1
				post_drop_welds += 1
				# No pd_parent union: keep trying up to POST_DROP_ATTEMPTS vert pairs so a
				# failed open-edge pick on the closest pair does not freeze the seam.
				weld_requests.append({
					"vi": int(cand_pd["vi"]), "ovi": int(cand_pd["ovi"]), "circuit": true,
				})
			if post_drop_welds > 0:
				castle_bridged += post_drop_welds
				print("    prune: castle/cc post-drop circuit welds=", post_drop_welds,
					" survivors=", pd_polys.size(), " root_pairs=", pd_pair_n.size())

		print("    prune: castle/cc lenient courtyard=", castle_courtyard_kept,
			" rampart_bridged=", castle_bridged, " roofs_skipped=", castle_roofs_skipped,
			" roofs_stripped=", castle_roofs_stripped,
			" poly_drop_roof=", poly_drop_roof,
			" poly_drop_fat=", poly_drop_fat,
			" (keep_sqm=", keep_sqm, " gap=", c_gap, " climb=", c_climb,
			" circ_gap=", circ_gap, " circ_climb=", circ_climb,
			" roof_inset=", roof_inset_eff, ")")

	var kept: Array = []
	var kept_components := 0
	var dropped_components := 0
	var dropped_area := 0.0
	for r in area_by_root.keys():
		if touches_boundary_root.get(r, false):
			var root_polys: Array = polys_by_root[r]
			var any_kept := false
			for p in root_polys:
				if lenient_castle and drop_polys.has(p):
					continue
				kept.append(p)
				any_kept = true
			if any_kept:
				kept_components += 1
			else:
				dropped_components += 1
				dropped_area += float(area_by_root[r])
		else:
			dropped_components += 1
			dropped_area += float(area_by_root[r])

	# Turn each accepted bridge into two stitch triangles so the linked components share real edges
	# and the runtime nav map connects them. Each side attaches to an unused open border edge; a
	# quad (vi,b) - (ovi,ob) is split into (vi,b,ovi) and (b,ob,ovi), sharing (vi,b) with the orphan
	# poly and (ovi,ob) with the reachable poly. Winding is irrelevant to Godot's position-based
	# edge connection, so we only guard against degenerate (reused/collinear) triangles.
	var weld_tris: Array = []
	var portal_weld_exempt: Dictionary = {} # weld_tris index -> true (skip roof-band drop)
	var used_open: Dictionary = {}
	var pick_open := func(vtx: int, avoid: int) -> int:
		if not open_nbr.has(vtx):
			return -1
		for other in (open_nbr[vtx] as Array):
			if other == avoid:
				continue
			var ek := Vector2i(mini(vtx, other), maxi(vtx, other))
			if used_open.has(ek):
				continue
			used_open[ek] = true
			return other
		return -1
	# Outdoor vs bailey lineage for weld veto. Bridged orphans sit on neither tile_edge nor
	# courtyard roots, so a vert-tag check misses circuit/bridge welds that still unite the two
	# regions (Cc_1_00_03). Track root sides with a union-find as welds are accepted.
	var seal_vi_roots: Dictionary = {} # vi -> Array[root]
	var seal_root_side: Dictionary = {} # root -> "ext"|"int"|""
	var seal_uf: Dictionary = {} # root union-find parent
	var seal_lineage_active := false
	if seal_court_valid and not seal_courtyard_roots.is_empty() and not tile_edge_roots.is_empty():
		seal_lineage_active = true
		seal_exterior_grade_vi.clear()
		seal_interior_grade_vi.clear()
		for r_se in polys_by_root.keys():
			seal_uf[r_se] = r_se
			if seal_courtyard_roots.has(r_se):
				seal_root_side[r_se] = "int"
			elif tile_edge_roots.has(r_se):
				seal_root_side[r_se] = "ext"
			else:
				seal_root_side[r_se] = ""
			for p_se in (polys_by_root[r_se] as Array):
				for vi_se in polys[p_se]:
					if not seal_vi_roots.has(vi_se):
						seal_vi_roots[vi_se] = []
					(seal_vi_roots[vi_se] as Array).append(r_se)
					if seal_courtyard_roots.has(r_se):
						seal_interior_grade_vi[vi_se] = true
					elif tile_edge_roots.has(r_se):
						seal_exterior_grade_vi[vi_se] = true

	if weld_enabled:
		var seal_find := func(r_in: Variant) -> Variant:
			var r_cur: Variant = r_in
			while seal_uf.has(r_cur) and seal_uf[r_cur] != r_cur:
				var p_cur: Variant = seal_uf[r_cur]
				if seal_uf.has(p_cur) and seal_uf[p_cur] != p_cur:
					seal_uf[r_cur] = seal_uf[p_cur]
				r_cur = seal_uf[r_cur]
			return r_cur
		var seal_side_of := func(r_in: Variant) -> String:
			var r_f: Variant = seal_find.call(r_in)
			return str(seal_root_side.get(r_f, ""))
		var seal_weld_mixes_lineage := func(vis_in: Array) -> bool:
			if not seal_lineage_active:
				return false
			var has_ext_l := false
			var has_int_l := false
			for vi_l in vis_in:
				if not seal_vi_roots.has(int(vi_l)):
					continue
				for r_l in (seal_vi_roots[int(vi_l)] as Array):
					var side_l := str(seal_side_of.call(r_l))
					if side_l == "ext":
						has_ext_l = true
					elif side_l == "int":
						has_int_l = true
					if has_ext_l and has_int_l:
						return true
			return false
		var seal_union_lineage := func(vis_in: Array) -> void:
			if not seal_lineage_active:
				return
			var roots_u: Array = []
			var side_u := ""
			for vi_u in vis_in:
				if not seal_vi_roots.has(int(vi_u)):
					continue
				for r_u in (seal_vi_roots[int(vi_u)] as Array):
					var rf_u: Variant = seal_find.call(r_u)
					if not roots_u.has(rf_u):
						roots_u.append(rf_u)
					var s_u := str(seal_root_side.get(rf_u, ""))
					if s_u == "ext" or s_u == "int":
						side_u = s_u
			if roots_u.is_empty():
				return
			var head_u: Variant = roots_u[0]
			for i_u in range(1, roots_u.size()):
				var other_u: Variant = roots_u[i_u]
				if other_u == head_u:
					continue
				seal_uf[other_u] = head_u
				var s_o := str(seal_root_side.get(other_u, ""))
				if side_u == "" and (s_o == "ext" or s_o == "int"):
					side_u = s_o
			if side_u != "":
				seal_root_side[head_u] = side_u
		var pick_nbr := func(vtx: int, avoid: int, prefer_open: bool, consume: bool) -> int:
			var lists: Array = []
			if prefer_open and open_nbr.has(vtx):
				lists.append(open_nbr[vtx])
			if any_nbr.has(vtx):
				lists.append(any_nbr[vtx])
			for lst in lists:
				for other in (lst as Array):
					if other == avoid:
						continue
					var ek2 := Vector2i(mini(vtx, int(other)), maxi(vtx, int(other)))
					if consume and used_open.has(ek2):
						continue
					if consume:
						used_open[ek2] = true
					return int(other)
			return -1
		var circuit_fallback := 0
		var circuit_fan := 0
		var circuit_fail := 0
		var court_floor_stitch := 0
		var court_floor_fail := 0
		# Process circuit welds first so rampart seams are not starved of open edges by the
		# hundreds of courtyard↔orphan bridge requests that share the same border verts.
		# Court-floor welds next (same fan stitch needs), then generic bridges.
		var ordered_wr: Array = []
		for wr0 in weld_requests:
			if bool(wr0.get("circuit", false)):
				ordered_wr.append(wr0)
		for wr_cf in weld_requests:
			if bool(wr_cf.get("court_floor", false)) and not bool(wr_cf.get("circuit", false)):
				ordered_wr.append(wr_cf)
		for wr1 in weld_requests:
			if not bool(wr1.get("circuit", false)) and not bool(wr1.get("court_floor", false)):
				ordered_wr.append(wr1)
		for wr in ordered_wr:
			var vi: int = wr["vi"]
			var ovi: int = wr["ovi"]
			if vi == ovi:
				continue
			var is_circuit := bool(wr.get("circuit", false))
			var is_court_floor := bool(wr.get("court_floor", false))
			var is_portal_forced := bool(wr.get("portal_forced", false))
			var va_d: Vector3 = verts[vi]
			var vb_d: Vector3 = verts[ovi]
			# Never stitch across a closed main-gate seam (keeps outdoor ≠ bailey).
			# Portal-forced stitches are included: outer-wall hrz21 arches sit on the field side
			# of the bailey and must not reopen the main envelope (Cc_2_hr_occ00).
			if _segment_hits_xz_aabbs(va_d, vb_d, gate_seam_aabbs, 0.5):
				weld_gate_blocked += 1
				continue
			# Outdoor↔bailey welds collapse castles into one region (Cc_1_00_02 / _03).
			# Portal-forced is not exempt: courtyard arches are bailey↔bailey (same lineage);
			# outer arches mix tile-edge with courtyard and must stay sealed.
			# Envelope check covers mega-roots where wall-walks already fused outdoor+bailey so
			# lineage tags both sides exterior (Cc_2_hr_occ00).
			if seal_weld_mixes_lineage.call([vi, ovi]) \
					or _weld_mixes_exterior_interior_vi(
						[vi, ovi], seal_exterior_grade_vi, seal_interior_grade_vi
					) \
					or (seal_court_valid and _weld_crosses_court_envelope(
						va_d, vb_d, seal_court_min, seal_court_max, seal_court_y, seal_ramp_lo
					)):
				weld_ext_int_blocked += 1
				continue
			var gap_xz := Vector2(va_d.x - vb_d.x, va_d.z - vb_d.z).length()
			# Short circuit / court-floor gaps need a neighbour-fan stitch: (vi,ovi,*) is
			# degenerate/near-degenerate and only touches islands at a point (Cc_1_00_01 /
			# Cc_1_hr_occ02 bailey hairlines).
			# Portal forced: still need a real shared border edge (vertex-only pins don't connect
			# the nav map). Prefer neighbour edges; if missing, synthesize a short border edge.
			var use_fan := (is_circuit or is_court_floor) and gap_xz < 1.25 \
				and absf(va_d.y - vb_d.y) < 0.75
			var b: int
			var ob: int
			if is_circuit or is_court_floor:
				# Never consume edges (many seams share corners); prefer any neighbour.
				b = pick_nbr.call(vi, ovi, true, false)
				ob = pick_nbr.call(ovi, vi, true, false)
				if b < 0 or ob < 0 or b == ovi or ob == vi or b == ob:
					b = pick_nbr.call(vi, ovi, false, false)
					ob = pick_nbr.call(ovi, vi, false, false)
				if (b < 0 or ob < 0 or b == ovi or ob == vi or b == ob) and is_portal_forced:
					# Spatial fallback: any near-grade vert within 1.5m of each lip (must already
					# exist on a floor poly so the stitch shares a real edge by position).
					var best_b := -1
					var best_bd := INF
					var best_ob := -1
					var best_obd := INF
					for pi_sp in range(polys.size()):
						for vi_sp in polys[pi_sp]:
							if vi_sp == vi or vi_sp == ovi:
								continue
							var vv_sp: Vector3 = verts[vi_sp]
							if absf(vv_sp.y - va_d.y) > 1.0 and absf(vv_sp.y - vb_d.y) > 1.0:
								continue
							var da_sp := Vector2(vv_sp.x - va_d.x, vv_sp.z - va_d.z).length()
							var db_sp := Vector2(vv_sp.x - vb_d.x, vv_sp.z - vb_d.z).length()
							if da_sp < 1.5 and da_sp < best_bd and da_sp > 0.05:
								best_bd = da_sp
								best_b = vi_sp
							if db_sp < 1.5 and db_sp < best_obd and db_sp > 0.05:
								best_obd = db_sp
								best_ob = vi_sp
					if best_b >= 0:
						b = best_b
					if best_ob >= 0:
						ob = best_ob
				if b < 0 or ob < 0 or b == ovi or ob == vi or b == ob:
					if is_court_floor:
						court_floor_fail += 1
					else:
						circuit_fail += 1
					continue
				if is_circuit:
					circuit_fallback += 1
			else:
				b = pick_open.call(vi, ovi)
				ob = pick_open.call(ovi, vi)
				if b < 0 or ob < 0 or b == ovi or ob == vi or b == ob:
					continue
			# Fan/border verts can sit on outdoor while vi/ovi are bailey — re-check all four.
			var stitch_vis: Array = [vi, ovi, b, ob]
			var blocked := false
			if seal_weld_mixes_lineage.call(stitch_vis) or _weld_mixes_exterior_interior_vi(
				stitch_vis, seal_exterior_grade_vi, seal_interior_grade_vi
			):
				weld_ext_int_blocked += 1
				blocked = true
			if not blocked and seal_court_valid:
				for si2 in range(stitch_vis.size()):
					for sj2 in range(si2 + 1, stitch_vis.size()):
						if _weld_crosses_court_envelope(
							verts[int(stitch_vis[si2])], verts[int(stitch_vis[sj2])],
							seal_court_min, seal_court_max, seal_court_y, seal_ramp_lo
						):
							weld_ext_int_blocked += 1
							blocked = true
							break
					if blocked:
						break
			if not blocked:
				for si2 in range(stitch_vis.size()):
					for sj2 in range(si2 + 1, stitch_vis.size()):
						if _segment_hits_xz_aabbs(
							verts[int(stitch_vis[si2])], verts[int(stitch_vis[sj2])],
							gate_seam_aabbs, 0.5
						):
							weld_gate_blocked += 1
							blocked = true
							break
					if blocked:
						break
			if blocked:
				if is_court_floor:
					court_floor_fail += 1
				elif is_circuit:
					circuit_fail += 1
				continue
			if use_fan:
				if b == ob:
					if is_court_floor:
						court_floor_fail += 1
					else:
						circuit_fail += 1
					continue
				weld_tris.append(PackedInt32Array([b, vi, ob]))
				weld_tris.append(PackedInt32Array([b, ovi, ob]))
				seal_union_lineage.call(stitch_vis)
				if is_portal_forced:
					portal_weld_exempt[weld_tris.size() - 2] = true
					portal_weld_exempt[weld_tris.size() - 1] = true
				if is_circuit:
					circuit_fan += 1
				elif is_court_floor:
					court_floor_stitch += 1
			else:
				weld_tris.append(PackedInt32Array([vi, b, ovi]))
				weld_tris.append(PackedInt32Array([b, ob, ovi]))
				seal_union_lineage.call(stitch_vis)
				if is_portal_forced:
					portal_weld_exempt[weld_tris.size() - 2] = true
					portal_weld_exempt[weld_tris.size() - 1] = true
				if is_court_floor:
					court_floor_stitch += 1
		if circuit_fallback > 0 or circuit_fan > 0 or circuit_fail > 0:
			print("    prune: circuit weld stitches=", circuit_fallback,
				" fan=", circuit_fan, " fail=", circuit_fail)
		if court_floor_stitch > 0 or court_floor_fail > 0:
			print("    prune: courtyard floor stitches=", court_floor_stitch,
				" fail=", court_floor_fail)
		if weld_gate_blocked > 0 or weld_ext_int_blocked > 0 \
				or OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
			print(
				"    prune: weld veto gate_seam=", weld_gate_blocked,
				" exterior_interior=", weld_ext_int_blocked,
				" seams=", gate_seam_aabbs.size()
			)

	var pruned := NavigationMesh.new()
	pruned.vertices = verts
	for p in kept:
		pruned.add_polygon(polys[p])
	var weld_tris_kept := 0
	var weld_tris_roof_skip := 0
	var portal_exempt_kept := 0
	for ti_w in range(weld_tris.size()):
		var t: PackedInt32Array = weld_tris[ti_w]
		var is_pex := portal_weld_exempt.has(ti_w)
		if castle_filter_roof_welds and t.size() >= 3 and not is_pex:
			var tw: Vector3 = (verts[t[0]] + verts[t[1]] + verts[t[2]]) / 3.0
			if tw.y - castle_roof_court_y >= castle_roof_hard_elev:
				weld_tris_roof_skip += 1
				continue
		pruned.add_polygon(t)
		weld_tris_kept += 1
		if is_pex:
			portal_exempt_kept += 1
	if weld_tris_roof_skip > 0 or portal_exempt_kept > 0:
		print("    prune: skipped ", weld_tris_roof_skip, " roof-band stitch tris (kept ",
			weld_tris_kept, "/", weld_tris.size(), " portal_exempt=", portal_exempt_kept, ")")
	print("    prune: kept ", kept.size(), "/", poly_count, " polys across ", kept_components,
		" reachable component(s) (", bridged_components, " bridged via <", bridge_max_dist, "m gaps, ",
		weld_tris_kept, " stitch tris); dropped ",
		dropped_components, " fully-interior orphan component(s) totaling %.1f sqm" % dropped_area)
	return pruned

const OBJ_COLOR_CELL := 0.5     # XZ grid for nav-over-object colouring
const OBJ_COLOR_Y_TOL := 1.2    # nav sits within this vertical band of the walkable object face

## XZ grid of the Y heights of walkable object faces, for colouring nav triangles that rest on them.
func _build_obj_walk_grid(walkable_faces: PackedVector3Array, cell: float) -> Dictionary:
	var grid := {}
	var i := 0
	while i + 2 < walkable_faces.size():
		var a: Vector3 = walkable_faces[i]
		var b: Vector3 = walkable_faces[i + 1]
		var c: Vector3 = walkable_faces[i + 2]
		i += 3
		var cx0 := floori(minf(a.x, minf(b.x, c.x)) / cell)
		var cx1 := floori(maxf(a.x, maxf(b.x, c.x)) / cell)
		var cz0 := floori(minf(a.z, minf(b.z, c.z)) / cell)
		var cz1 := floori(maxf(a.z, maxf(b.z, c.z)) / cell)
		if (cx1 - cx0 + 1) * (cz1 - cz0 + 1) > 100000:
			continue
		var ylo := minf(a.y, minf(b.y, c.y))
		var yhi := maxf(a.y, maxf(b.y, c.y))
		for cz in range(cz0, cz1 + 1):
			for cx in range(cx0, cx1 + 1):
				var key := Vector2i(cx, cz)
				if grid.has(key):
					var e: Vector2 = grid[key]
					grid[key] = Vector2(minf(e.x, ylo), maxf(e.y, yhi))
				else:
					grid[key] = Vector2(ylo, yhi)
	return grid

func _nav_over_object(grid: Dictionary, cell: float, centroid: Vector3) -> bool:
	if grid.is_empty():
		return false
	var key := Vector2i(floori(centroid.x / cell), floori(centroid.z / cell))
	if not grid.has(key):
		return false
	var yr: Vector2 = grid[key]
	return centroid.y >= yr.x - OBJ_COLOR_Y_TOL and centroid.y <= yr.y + OBJ_COLOR_Y_TOL

## Preview GLB only: drop object meshes whose pivot is more than PREVIEW_OBJECT_MAX_DEPTH_BELOW_GROUND
## below sampled terrain. Deep authored-Y junk bloats the side-by-side preview and isn't useful.
func _preview_skip_deep_buried_object(
	placement: Dictionary, ground_grid: Dictionary, ground_fallback: float
) -> bool:
	var origin: Vector3 = (placement["transform"] as Transform3D).origin
	var gy := _sample_ground_height(
		ground_grid, GROUND_GRID_CELL, Vector2(origin.x, origin.z), ground_fallback
	)
	return origin.y < gy - PREVIEW_OBJECT_MAX_DEPTH_BELOW_GROUND

func _export_one_glb(
	tile_key: String,
	master: String,
	gx: int,
	gz: int,
	nav: NavigationMesh,
	placements: Array,
	out_dir: String,
	walkable_faces: PackedVector3Array = PackedVector3Array(),
	ground_grid: Dictionary = {},
	ground_fallback: float = 0.0
) -> bool:
	var world_pos := TERRAIN_ORIGIN + Vector3(gx * TILE_SIZE, 0.0, gz * TILE_SIZE)
	var tile_mesh := _load_tile_mesh(master)
	if tile_mesh == null:
		push_error("Missing tile mesh for ", master)
		return false

	var root := Node3D.new()
	root.name = tile_key

	var terrain_root := Node3D.new()
	terrain_root.name = "Terrain"
	root.add_child(terrain_root)

	var ground_xform := Transform3D(_tile_mesh_basis(), world_pos)
	var frame_center := _mesh_world_center(tile_mesh, ground_xform)
	var terrain_offset := -frame_center + Vector3(-SIDE_GAP * 0.5, 0.0, 0.0)
	var merge := NavGlbMerge.new_tile_state()
	NavGlbMerge.append_mesh(
		merge["ground"],
		tile_mesh,
		NavGlbMerge.apply_translation(ground_xform, terrain_offset)
	)

	var skipped_deep := 0
	for placement in placements:
		if _preview_skip_deep_buried_object(placement, ground_grid, ground_fallback):
			skipped_deep += 1
			continue
		var object_name: String = placement["object_name"]
		var category: String = placement["category"]
		var world_xform: Transform3D = placement["transform"]
		world_xform = NavGlbMerge.apply_translation(world_xform, terrain_offset)
		var parts: Array = _load_object_parts(object_name)
		if parts.is_empty():
			continue
		var cat: String = category if category in merge["object"] else "other"
		for part in parts:
			NavGlbMerge.append_mesh(
				merge["object"][cat],
				part["mesh"],
				world_xform * part["local"]
			)
	if skipped_deep > 0:
		print("    preview: skipped ", skipped_deep, " object(s) >",
			PREVIEW_OBJECT_MAX_DEPTH_BELOW_GROUND, "m below ground on ", tile_key)

	var nav_root := Node3D.new()
	nav_root.name = "NavMesh"
	nav_root.position = Vector3(SIDE_GAP * 0.5, 0.0, 0.0)
	root.add_child(nav_root)
	# Colour nav triangles sitting over walkable object geometry blue, bare-terrain nav green.
	var obj_walk_grid := _build_obj_walk_grid(walkable_faces, OBJ_COLOR_CELL)
	var diag_dump := OS.get_environment("DIAG_DUMP")
	if diag_dump != "":
		var pr := diag_dump.split(",")
		var dx0 := float(pr[0]); var dx1 := float(pr[1]); var dz0 := float(pr[2]); var dz1 := float(pr[3])
		var nv := nav.get_vertices()
		var fnav := FileAccess.open("D:/1/dump_nav.csv", FileAccess.WRITE)
		fnav.store_line("x0,y0,z0,x1,y1,z1,x2,y2,z2,is_obj")
		for p in range(nav.get_polygon_count()):
			var poly := nav.get_polygon(p)
			for j in range(1, poly.size() - 1):
				var a: Vector3 = nv[poly[0]]; var b: Vector3 = nv[poly[j]]; var c: Vector3 = nv[poly[j + 1]]
				var ctr := (a + b + c) / 3.0
				if ctr.x >= dx0 and ctr.x <= dx1 and ctr.z >= dz0 and ctr.z <= dz1:
					var io := 1 if _nav_over_object(obj_walk_grid, OBJ_COLOR_CELL, ctr) else 0
					fnav.store_line("%f,%f,%f,%f,%f,%f,%f,%f,%f,%d" % [a.x,a.y,a.z,b.x,b.y,b.z,c.x,c.y,c.z,io])
		fnav.close()
		var fw := FileAccess.open("D:/1/dump_walk.csv", FileAccess.WRITE)
		fw.store_line("x0,y0,z0,x1,y1,z1,x2,y2,z2")
		var wi := 0
		while wi + 2 < walkable_faces.size():
			var a2: Vector3 = walkable_faces[wi]; var b2: Vector3 = walkable_faces[wi+1]; var c2: Vector3 = walkable_faces[wi+2]
			wi += 3
			var ctr2 := (a2 + b2 + c2) / 3.0
			if ctr2.x >= dx0 and ctr2.x <= dx1 and ctr2.z >= dz0 and ctr2.z <= dz1:
				fw.store_line("%f,%f,%f,%f,%f,%f,%f,%f,%f" % [a2.x,a2.y,a2.z,b2.x,b2.y,b2.z,c2.x,c2.y,c2.z])
		fw.close()
		print("DIAG_DUMP wrote nav+walk csv for region ", diag_dump)
	var is_obj := func(centroid: Vector3) -> bool:
		return _nav_over_object(obj_walk_grid, OBJ_COLOR_CELL, centroid)
	NavGlbMerge.append_nav_split(merge["nav"], merge["nav_obj"], nav, frame_center, is_obj)
	NavGlbMerge.finalize_tile_meshes(terrain_root, nav_root, merge)

	get_root().add_child(root)
	var out_path := out_dir.path_join(tile_key + ".glb")
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_scene(root, state)
	if err != OK:
		push_error("GLTF append failed err=", err)
		root.queue_free()
		return false
	err = doc.write_to_filesystem(state, out_path)
	root.queue_free()
	if err != OK:
		push_error("GLTF write failed err=", err)
		return false
	print("Exported ", out_path)
	return true

func _add_projected_aabb(source: NavigationMeshSourceGeometryData3D, aabb: AABB, global_cells: Dictionary = {}) -> void:
	var mn := aabb.position
	var mx := aabb.position + aabb.size
	var outline := PackedVector3Array([
		Vector3(mn.x, 0.0, mn.z),
		Vector3(mx.x, 0.0, mn.z),
		Vector3(mx.x, 0.0, mx.z),
		Vector3(mn.x, 0.0, mx.z),
	])
	# JSON object Y is far below tile surfaces — pad vertically so carve hits walk voxels.
	const PAD := 600.0
	var elevation := mn.y - PAD
	var height := (mx.y - mn.y) + PAD * 2.0
	source.add_projected_obstruction(outline, elevation, height, true)
	_record_obstruction_cells(global_cells, Vector2(mn.x, mn.z), Vector2(mx.x, mx.z), mn.y, mx.y)

## Rasterizes an obstructed XZ rectangle into the shared global_cells map (same grid/format as
## _add_projected_mesh_footprint_from_world_faces) so the post-bake gap-bridging pass can tell "real
## object occupies this gap" apart from "Recast tessellation seam, nothing is actually here".
func _record_obstruction_cells(global_cells: Dictionary, mn: Vector2, mx: Vector2, min_y: float, max_y: float) -> void:
	var obst_cell := _obstruction_cell_size()
	var cell_x0 := floori(mn.x / obst_cell)
	var cell_x1 := floori(mx.x / obst_cell)
	var cell_z0 := floori(mn.y / obst_cell)
	var cell_z1 := floori(mx.y / obst_cell)
	const MAX_CELLS := 200000
	var span: int = (cell_x1 - cell_x0 + 1) * (cell_z1 - cell_z0 + 1)
	if span <= 0 or span > MAX_CELLS:
		return
	for cz in range(cell_z0, cell_z1 + 1):
		for cx in range(cell_x0, cell_x1 + 1):
			var key := Vector2i(cx, cz)
			if global_cells.has(key):
				var existing: Dictionary = global_cells[key]
				existing["min_y"] = minf(existing["min_y"], min_y)
				existing["max_y"] = maxf(existing["max_y"], max_y)
			else:
				global_cells[key] = {"min_y": min_y, "max_y": max_y}

# Only tris that reach into this band above the placement origin are used for the near-ground
# carve. Arch lintels / upper tower floors / roof overhangs sit above it and must not seal an
# opening in XZ or inflate a building's footprint beyond its walls.
const NEAR_GROUND_CARVE_MAX_HEIGHT := 2.0
# Metres above the mesh's lowest vertex used as the base XZ footprint for ALWAYS_CARVE objects
# (towers may flare above this; we only stamp the ground contact silhouette).
const ALWAYS_CARVE_BASE_BAND := 1.5
# World-anchored band above sampled ground for non-rock ALWAYS_CARVE silhouettes. Tall enough to
# catch keep/wall skirts (hrz08), short enough that arch soffits (~3m+) stay out of the stamp so
# door/arch openings are not XZ-projected shut (threshold floors still walk-protect if included).
const ALWAYS_CARVE_GROUND_BAND := 2.75
# Object names whose base footprint always carves terrain underneath (near-ground band).
# Missing from Models (skipped): hrz27, tn5_house4. tn4_wall1 -> tn4_wall01.
const ALWAYS_CARVE_TERRAIN_OBJECTS := {
	"cc00": true,
	"cc01": true,
	"cc02": true,
	"cc03": true,
	"cc04": true,
	"cc05": true,
	"cc06": true,
	"cc07": true,
	"cc08": true,
	"cc09": true,
	"cc10": true,
	# cc16 gatehouse: pier land carve is via gate_seam merge (not ALWAYS_CARVE).
	# Archway curtain / gate kits (see ARCHWAY_TERRAIN_OBJECTS) — opening-aware or pier solid.
	"cc17": true,
	"cc19": true,
	"cc21": true,
	"cc22": true,
	"cc54": true,
	"cc23": true,
	"cc24": true,
	"cc25": true,
	"cc26": true,
	"cc27": true,
	"cc28": true,
	"cc29": true,
	"cc30": true,
	"cc31": true,
	"cc36": true,
	"cc37": true,
	"cc40": true,
	# cc55/cc56 are approach ramps — never terrain-carve (wipes the toe landing; weld to ground instead).
	"hrt_house3": true,
	"hrt_house4": true,
	"hrt_house5": true,
	"hrt_house6": true,
	"hrt_house7": true,
	"hrt_house8": true,
	"hrt_mageh": true,
	"hrt_meriya": true,
	"hrt_tower1": true,
	"hrt_tgate1": true,
	"hrt_wall1": true,
	"hrt_wall2": true,
	"hrt_wall3": true,
	"hrt_yard1": true,
	# Rock kits (all *rock* models except ruin_rocks1/2). Solid terrain carve.
	"hr_rock1": true,
	"hr_rock2": true,
	"hr_rock3": true,
	"hr_rock4": true,
	"hr_rock5": true,
	"hr_rock6": true,
	"hr_rock7": true,
	"hr_rock8": true,
	"hr_rock9": true,
	"hrz00": true,
	"hrz01": true,
	"hrz03": true,
	"hrz07": true,
	"hrz08": true,
	"hrz09": true,
	"hrz10": true,
	# Gate/arch wall kits — opening-aware fill keeps arch/door flood-paths walkable.
	"hrz14": true,
	"hrz16": true,
	"hrz17": true,
	"hrz19": true,
	"hrz21": true,
	"hrz22": true,
	"hrz54": true,
	"hrz23": true,
	"hrz24": true,
	"hrz25": true,
	"hrz26": true,
	"hrz28": true,
	"hrz29": true,
	"hrz30": true,
	"hrz31": true,
	"hrz36": true,
	"hrz37": true,
	"hrz40": true,
	"hrz93": true,
	"hrz94": true,
	"hrz95": true,
	"hrz96": true,
	"hrz97": true,
	"hrz98": true,
	"hrz99": true,
	"loc1_00": true,
	"loc1_05": true,
	"loc1_06": true,
	"loc2_04": true,
	"loc2_06": true,
	"loc2_08": true,
	"loc2_09": true,
	"mhouse10": true,
	"mhouse11": true,
	"mhouse12": true,
	"mhouse13": true,
	"mhouse14": true,
	"mhouse2": true,
	"mhouse20b": true,
	"mhouse20f": true,
	"mhouse4": true,
	"mhouse5": true,
	"mhouse7": true,
	"mhouse8": true,
	"mhouse9": true,
	"nh1": true,
	"nh2": true,
	"rd_fo1": true,
	"rd_fo2": true,
	"rd_house1": true,
	"rd_house2": true,
	"rd_house3": true,
	"rd_house4": true,
	"rd_house5": true,
	"rd_house6": true,
	"rd_house7": true,
	"rd_house8": true,
	"rd_meria": true,
	"rd_vil1": true,
	"rd_vil2": true,
	"rd_vil3": true,
	"rd_vil4": true,
	"rd_vil5": true,
	"rd_vil6": true,
	"rd_vil7": true,
	"rd_vil8": true,
	"rd_vil9": true,
	"rock01": true,
	"rock02": true,
	"rock03": true,
	"rock04": true,
	"rock05": true,
	"rock06": true,
	"rock10": true,
	"rock11": true,
	"rock12": true,
	"rock13": true,
	"rock20": true,
	"rock30": true,
	"s_bld21a": true,
	"s_bld21b": true,
	"s_bld22a": true,
	"s_bld22b": true,
	"s_bld22c": true,
	"s_home00": true,
	"s_home01": true,
	"s_home02": true,
	"s_home03": true,
	"s_home04": true,
	"s_home05": true,
	"s_home06": true,
	"s_home07": true,
	"s_home08": true,
	"s_home09": true,
	"s_home10": true,
	"s_home11": true,
	"s_home12": true,
	"s_home13": true,
	"s_home14": true,
	"s_home15": true,
	"s_home16": true,
	"s_home17": true,
	"s_home18": true,
	"s_home19": true,
	"s_home20": true,
	"s_home21": true,
	"s_home22": true,
	"s_home23": true,
	"s_home24": true,
	"s_home25": true,
	"s_home26": true,
	"s_home27": true,
	"s_home28": true,
	"s_home29": true,
	"s_home30": true,
	"s_home31": true,
	"s_home32": true,
	"s_home33": true,
	"s_home34": true,
	"s_home35": true,
	"s_home36": true,
	"s_home37": true,
	"s_home38": true,
	"s_home39": true,
	"s_home40": true,
	"s_home41": true,
	"s_home42": true,
	"s_home43": true,
	"s_home44": true,
	"t_house1": true,
	"t_house2": true,
	"t_house3": true,
	"t_house4": true,
	"t_house5": true,
	"t_house6": true,
	"t_house7": true,
	"tn2_d3": true,
	"tn4_wall01": true,
	"tn5_house1": true,
	"tn5_house2": true,
	"tn5_house3": true,
	"tn5_house5": true,
	"tn5_tow": true,
	"tn5_wl": true,
	"tn5_wl1": true,
	"tn5_wl2": true,
	"tn5_wl3": true,
	"tn5_wl4": true,
	"tn5_wl5": true,
	"tn5_xmen": true,
	"tn_wall": true,
	"tn_wall2": true,
	"tn_wall3": true,
	"tn_wallb": true,
	"tnwall": true,
	"tnwall2": true,
	"tnwall3": true,
}
# XZ cell size (metres) for the ground-height grid used by the below-terrain cull.
const GROUND_GRID_CELL := 1.0
# How far below a rampart walkway the wall carve stops, so the walkway survives as walkable.
const RAMPART_CARVE_CLEARANCE := 0.5
# Minimum height above local ground for a near-horizontal object surface (roof/ceiling/upper floor)
# to count as "cover" for the roofed-interior carve. Above this we treat the cell as an enclosed
# room and block the ground column beneath it; open courtyards (no overhead) stay walkable.
const BUILDING_ROOF_MIN_CLEAR := 2.0
# How high above local ground to block an enclosed building's interior column. Just enough to delete
# the ground-level nav (stops walking through the building at grade) while leaving the walkable roof
# and any elevated deck untouched, so the roof reads as one clean surface instead of a chopped mess.
const BUILDING_GROUND_BLOCK := 1.5

# World XZ cells (Vector2i at obstruction-cell resolution) -> roof Y where the enclosure carve
# FABRICATED a clean flat walkable roof cap over a building. The orphan-island prune force-keeps any
# component sitting on these cells so every sealed building shows one uniform walkable top ("walk
# over, never through"), even when the source roof mesh is pitched/fragmented and would otherwise
# bake to nothing or get pruned as an unreachable island. Accumulated across tiles in a bake run.
var _fabricated_roof_cells: Dictionary = {}
# Triangles whose face normal is within _agent_max_slope_deg() of vertical (i.e. slope from
# horizontal, matching the bake's own agent_max_slope) are fed as real walkable source geometry
# (stair treads/ramps/floors/decks); steeper triangles are walls and stay on the obstruction carve.
# cem_tower walls are thin planes; expand projected carve toward/away from center so the
# nav hole matches the visual wall thickness (gates stay at 0).
const TOWER_WALL_INFLATE := 0.45
# Inset from outer mesh AABB used to solid-fill the hollow interior (kills orphan islands).
const TOWER_INTERIOR_INSET := 0.9

func _is_tower_object(object_name: String) -> bool:
	return object_name.to_lower().contains("tower")

func _inset_aabb_xz(aabb: AABB, inset: float) -> AABB:
	var mn := aabb.position + Vector3(inset, 0.0, inset)
	var sz := aabb.size - Vector3(inset * 2.0, 0.0, inset * 2.0)
	if sz.x < 0.0 or sz.z < 0.0:
		return AABB(aabb.position, Vector3.ZERO)
	return AABB(mn, Vector3(sz.x, aabb.size.y, sz.z))

func _is_undercroft_object(object_name: String) -> bool:
	var lower := object_name.to_lower()
	return lower.contains("tower") or lower.contains("gate") or lower.contains("arch") \
		or lower.contains("_arc") or lower.begins_with("cem_arc") \
		or lower.begins_with("cem_door")

## Near-ground mesh tris → clustered projected cells, tracing the object's real footprint instead
## of a single full-object AABB (openings stay walkable, curved/irregular shapes don't over-block).
## inflate > 0 expands each tri's XZ bounds (thickens thin wall planes both inward and out).
func _add_projected_mesh_footprint(
	source: NavigationMeshSourceGeometryData3D,
	parts: Array,
	world_xform: Transform3D,
	inflate: float = 0.0,
	global_cells: Dictionary = {},
	band_top_y: float = INF
) -> int:
	# band_top_y defaults to the pivot-relative band for callers that don't override it; the
	# iceberg-aware path passes a ground-relative value so a deep-below/high-above pivot doesn't
	# push the near-ground band away from the actual terrain surface.
	var max_y := band_top_y if band_top_y != INF else world_xform.origin.y + NEAR_GROUND_CARVE_MAX_HEIGHT
	var all_faces := PackedVector3Array()
	for part in parts:
		var mesh: Mesh = part["mesh"]
		var xform: Transform3D = world_xform * part["local"]
		all_faces.append_array(_mesh_faces_world(mesh, xform))
	return _add_projected_mesh_footprint_from_world_faces(source, all_faces, max_y, inflate, global_cells)

## True 2D triangle-vs-axis-aligned-box overlap test (separating axis theorem): the 2 box axes
## plus the triangle's 3 edge normals. `inflate` grows the triangle outward (Minkowski-ish) so
## thin wall planes can still be thickened without falling back to a bounding-box footprint.
func _triangle_aabb_overlap_2d(a: Vector2, b: Vector2, c: Vector2, box_min: Vector2, box_max: Vector2, inflate: float = 0.0) -> bool:
	var box_center := (box_min + box_max) * 0.5
	var box_half := (box_max - box_min) * 0.5
	var v0 := a - box_center
	var v1 := b - box_center
	var v2 := c - box_center
	if minf(v0.x, minf(v1.x, v2.x)) > box_half.x + inflate or maxf(v0.x, maxf(v1.x, v2.x)) < -box_half.x - inflate:
		return false
	if minf(v0.y, minf(v1.y, v2.y)) > box_half.y + inflate or maxf(v0.y, maxf(v1.y, v2.y)) < -box_half.y - inflate:
		return false
	var edges := [v1 - v0, v2 - v1, v0 - v2]
	for e in edges:
		var axis := Vector2(-e.y, e.x)
		var axis_len := axis.length()
		if axis_len < 0.000001:
			continue
		var p0 := v0.dot(axis)
		var p1 := v1.dot(axis)
		var p2 := v2.dot(axis)
		var tmin := minf(p0, minf(p1, p2))
		var tmax := maxf(p0, maxf(p1, p2))
		var r := box_half.x * absf(axis.x) + box_half.y * absf(axis.y) + inflate * axis_len
		if tmin > r or tmax < -r:
			return false
	return true

## Rasterizes which actual grid cells a triangle's XZ projection touches (via SAT), instead of
## carving its full bounding box - a long thin diagonal sliver (e.g. a corner-to-corner decorative
## brace/beam) then only carves a thin trail of cells along its real path, not the huge box that
## contains it. See the Town4 s_bld23 "X-brace eats half the building" investigation.
## Per-cell max height of near-horizontal walkable surfaces, in the obstruction grid. Used to detect
## a walkway sitting on top of a wall (rampart) so the carve can stop below it. Only near-horizontal
## faces count - sloped ramp faces must not raise the cap or a ramp leaning on a wall would stop the
## wall from carving.
## Record every obstruction cell whose column is covered by object geometry (roof / ceiling / upper
## floor / pitched roof) at least BUILDING_ROOF_MIN_CLEAR above the local ground. Keeps the lowest
## such surface per cell (the effective ceiling). Feeds the tile-global enclosure carve.
func _accumulate_roofed_cells(all_faces: PackedVector3Array, ground_grid: Dictionary, ground_fallback: float, out_roofed: Dictionary) -> void:
	var obst_cell := _obstruction_cell_size()
	var i := 0
	while i + 2 < all_faces.size():
		var v0: Vector3 = all_faces[i]
		var v1: Vector3 = all_faces[i + 1]
		var v2: Vector3 = all_faces[i + 2]
		i += 3
		var nrm := (v1 - v0).cross(v2 - v0)
		if nrm.length_squared() < 0.000001:
			continue
		# "Cover" is any object triangle whose LOWEST vertex sits above head height - a flat roof, an
		# upper floor, but also a STEEP pitched roof (whose lowest edge is the eave, well above ground).
		# We deliberately do NOT filter by slope: a pitched roof encloses a room just as much as a flat
		# one, and the earlier near-horizontal-only test left pitched-roof houses transparent. Vertical
		# walls are naturally excluded because their triangles reach down to the ground (tri_lo ~= gy).
		var tri_lo := minf(v0.y, minf(v1.y, v2.y))
		var cx0 := floori(minf(v0.x, minf(v1.x, v2.x)) / obst_cell)
		var cx1 := floori(maxf(v0.x, maxf(v1.x, v2.x)) / obst_cell)
		var cz0 := floori(minf(v0.z, minf(v1.z, v2.z)) / obst_cell)
		var cz1 := floori(maxf(v0.z, maxf(v1.z, v2.z)) / obst_cell)
		if (cx1 - cx0 + 1) * (cz1 - cz0 + 1) > 200000:
			continue
		for cz in range(cz0, cz1 + 1):
			for cx in range(cx0, cx1 + 1):
				var gy := _sample_ground_height(ground_grid, GROUND_GRID_CELL, Vector2((cx + 0.5) * obst_cell, (cz + 0.5) * obst_cell), ground_fallback)
				if tri_lo < gy + BUILDING_ROOF_MIN_CLEAR:
					continue
				var key := Vector2i(cx, cz)
				if out_roofed.has(key):
					out_roofed[key] = minf(out_roofed[key], tri_lo)
				else:
					out_roofed[key] = tri_lo

## Record the lowest walkable-surface Y per cell (ramps/floors/decks/ramparts). The enclosure carve
## caps its column just below any ELEVATED entry here, so a walkable surface stacked on top of a wall
## (a rampart walkway) or over a room (an upper floor) survives while the ground beneath still blocks.
func _accumulate_walk_cells(walkable_faces: PackedVector3Array, out_walk: Dictionary) -> void:
	var obst_cell := _obstruction_cell_size()
	var i := 0
	while i + 2 < walkable_faces.size():
		var v0: Vector3 = walkable_faces[i]
		var v1: Vector3 = walkable_faces[i + 1]
		var v2: Vector3 = walkable_faces[i + 2]
		i += 3
		var tri_lo := minf(v0.y, minf(v1.y, v2.y))
		var cx0 := floori(minf(v0.x, minf(v1.x, v2.x)) / obst_cell)
		var cx1 := floori(maxf(v0.x, maxf(v1.x, v2.x)) / obst_cell)
		var cz0 := floori(minf(v0.z, minf(v1.z, v2.z)) / obst_cell)
		var cz1 := floori(maxf(v0.z, maxf(v1.z, v2.z)) / obst_cell)
		if (cx1 - cx0 + 1) * (cz1 - cz0 + 1) > 200000:
			continue
		for cz in range(cz0, cz1 + 1):
			for cx in range(cx0, cx1 + 1):
				var key := Vector2i(cx, cz)
				if out_walk.has(key):
					out_walk[key] = minf(out_walk[key], tri_lo)
				else:
					out_walk[key] = tri_lo

## Record the XZ footprint cells covered by an object's wall (steep) faces. These are the barriers
## the tile-global enclosure flood cannot pass through.
func _accumulate_wall_cells(wall_faces: PackedVector3Array, out_walls: Dictionary) -> void:
	var obst_cell := _obstruction_cell_size()
	var i := 0
	while i + 2 < wall_faces.size():
		var w0: Vector3 = wall_faces[i]
		var w1: Vector3 = wall_faces[i + 1]
		var w2: Vector3 = wall_faces[i + 2]
		i += 3
		var a2 := Vector2(w0.x, w0.z)
		var b2 := Vector2(w1.x, w1.z)
		var c2 := Vector2(w2.x, w2.z)
		var wx0 := floori(minf(a2.x, minf(b2.x, c2.x)) / obst_cell)
		var wx1 := floori(maxf(a2.x, maxf(b2.x, c2.x)) / obst_cell)
		var wz0 := floori(minf(a2.y, minf(b2.y, c2.y)) / obst_cell)
		var wz1 := floori(maxf(a2.y, maxf(b2.y, c2.y)) / obst_cell)
		if (wx1 - wx0 + 1) * (wz1 - wz0 + 1) > 200000:
			continue
		for cz in range(wz0, wz1 + 1):
			for cx in range(wx0, wx1 + 1):
				var cmn := Vector2(cx * obst_cell, cz * obst_cell)
				if _triangle_aabb_overlap_2d(a2, b2, c2, cmn, cmn + Vector2(obst_cell, obst_cell), 0.0):
					out_walls[Vector2i(cx, cz)] = true

## Tile-global enclosure carve. With the tile's full roofed-cell and wall-cell grids, morphologically
## close the walls (so door/window gaps under ~2*close_r cells seal while wider gates/archways stay
## open), flood-fill from outside every building through the remaining openings, then carve the
## roofed cells the flood never reached - the genuinely enclosed rooms. Because the flood spans the
## whole tile it follows real gates in ANY object's wall, so a courtyard/gate/covered street reached
## through a wide opening stays walkable while sealed rooms are blocked from ground up to just under
## their roof. See the Town_ph00 vs Town4 building-fill investigation.
func _carve_enclosed_interiors(
	source: NavigationMeshSourceGeometryData3D,
	roofed: Dictionary,
	walls: Dictionary,
	walk: Dictionary,
	ground_grid: Dictionary,
	ground_fallback: float,
	global_cells: Dictionary,
	out_roof_faces: PackedVector3Array
) -> int:
	var obst_cell := _obstruction_cell_size()
	var minx := INF; var maxx := -INF; var minz := INF; var maxz := -INF
	for k in roofed.keys() + walls.keys():
		var kk: Vector2i = k
		minx = minf(minx, kk.x); maxx = maxf(maxx, kk.x)
		minz = minf(minz, kk.y); maxz = maxf(maxz, kk.y)
	if minx == INF:
		return 0
	var close_env := OS.get_environment("NAV_EXPERIMENT_ROOM_CLOSE")
	var close_r := int(close_env) if close_env != "" else 3
	var pad := close_r + 2
	var x0 := int(minx) - pad; var x1 := int(maxx) + pad
	var z0 := int(minz) - pad; var z1 := int(maxz) + pad
	if (x1 - x0 + 1) * (z1 - z0 + 1) > 40000000:
		return 0

	# Morphologically close the walls: seal narrow door/window gaps, keep wide gates/archways open.
	var blocked: Dictionary = {}
	for wc in walls.keys():
		var wcell: Vector2i = wc
		for dz in range(-close_r, close_r + 1):
			for dx in range(-close_r, close_r + 1):
				blocked[Vector2i(wcell.x + dx, wcell.y + dz)] = true

	# Flood-fill through non-blocked cells, seeded from every open-sky ground cell (non-wall AND
	# non-roofed) plus the padded border. Seeding from open ground - not just the map edge - is what
	# lets a walled town work: the interior streets/courtyards are open sky and reach the covered
	# galleries/gates/passages through wide openings, so only rooms sealed on all sides (reachable
	# solely through a dilated-shut door) stay unvisited and get carved.
	var visited: Dictionary = {}
	var stack: Array = []
	for cx in range(x0, x1 + 1):
		for cz in range(z0, z1 + 1):
			var b := Vector2i(cx, cz)
			if not blocked.has(b) and not roofed.has(b):
				visited[b] = true; stack.append(b)
	while not stack.is_empty():
		var cur: Vector2i = stack.pop_back()
		for off in [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]:
			var nxt: Vector2i = cur + off
			if nxt.x < x0 or nxt.x > x1 or nxt.y < z0 or nxt.y > z1:
				continue
			if blocked.has(nxt) or visited.has(nxt):
				continue
			visited[nxt] = true
			stack.append(nxt)

	# Group the unreached roofed cells into connected pockets and only carve room-sized ones. A huge
	# enclosed pocket is almost never a genuine sealed room - it's a courtyard/gallery/ring-street the
	# flood couldn't enter (e.g. a walled town's concentric interior). Carving those would sever real
	# walkable streets, so skip anything above NAV_EXPERIMENT_ROOM_MAX_SQM. This keeps the seal on
	# kit-house rooms (Town_ph00) while sparing dense hand-authored towns like Town4.
	# SOLID mode (default): carve an enclosed building as one clean block instead of preserving its
	# interior floors, which otherwise leaves a patchy mix of holes + leftover blue floors.
	var solid_mode := OS.get_environment("NAV_EXPERIMENT_FILL_SOLID") != "0"
	var room_max_env := OS.get_environment("NAV_EXPERIMENT_ROOM_MAX_SQM")
	var room_max_sqm := float(room_max_env) if room_max_env != "" else 200.0
	var max_cells := int(room_max_sqm / (obst_cell * obst_cell))
	# Enclosed interior candidates: EVERY cell the open-sky flood couldn't reach and that isn't itself
	# a (dilated) wall cell. This is the whole wall-bounded interior of each building - roofed rooms
	# AND the non-roofed gaps between them - so we block the interior uniformly (walk over the roof,
	# never through the building) instead of leaving a patchy mix of holes, leftover floors and green
	# gaps. Excluding wall cells still separates a row of adjacent houses into one pocket PER house.
	# The open-sky flood already keeps courtyards/streets/gates walkable (they're reached), so only
	# genuinely sealed-off interiors land here. See the Town_ph00 transparent-house investigation.
	var enclosed: Dictionary = {}
	for cx in range(x0, x1 + 1):
		for cz in range(z0, z1 + 1):
			var ek := Vector2i(cx, cz)
			if not visited.has(ek) and not blocked.has(ek):
				enclosed[ek] = true
	var to_carve: Dictionary = {} # Vector2i -> carve-top height (INF => no roof anywhere in pocket)
	var comp_visited: Dictionary = {}
	var big_skipped := 0
	for start in enclosed.keys():
		if comp_visited.has(start):
			continue
		var comp: Array = []
		var cstack: Array = [start]
		comp_visited[start] = true
		var comp_roof := -INF
		while not cstack.is_empty():
			var cc: Vector2i = cstack.pop_back()
			comp.append(cc)
			if roofed.has(cc):
				comp_roof = maxf(comp_roof, float(roofed[cc]))
			for off in [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]:
				var nn: Vector2i = cc + off
				if enclosed.has(nn) and not comp_visited.has(nn):
					comp_visited[nn] = true
					cstack.append(nn)
		if comp.size() <= max_cells:
			for c in comp:
				# Carve each cell up to just below the roof: its own overhead if roofed, else the
				# pocket's roof height so non-roofed interior gaps are blocked to the same level.
				if roofed.has(c):
					to_carve[c] = float(roofed[c])
				elif comp_roof > -INF:
					to_carve[c] = comp_roof
				else:
					to_carve[c] = INF # walled pen with no overhead: just block the ground column
			# Fabricate ONE clean flat walkable roof cap for this building at a uniform height, so
			# every sealed house reads as a single walkable top instead of the pitched/fragmented (or
			# missing) source-roof mesh. Cover the interior cells PLUS the surrounding wall cells so the
			# cap reaches the outer eaves. Skip pens with no overhead at all.
			if comp_roof > -INF:
				var footprint: Dictionary = {}
				for c in comp:
					footprint[c] = true
					for dz in range(-1, 2):
						for dx in range(-1, 2):
							var nb := Vector2i(c.x + dx, c.y + dz)
							if walls.has(nb):
								footprint[nb] = true
				for fc in footprint.keys():
					var fk: Vector2i = fc
					var mnx: float = fk.x * obst_cell
					var mnz: float = fk.y * obst_cell
					var mxx: float = mnx + obst_cell
					var mxz: float = mnz + obst_cell
					out_roof_faces.append(Vector3(mnx, comp_roof, mnz))
					out_roof_faces.append(Vector3(mxx, comp_roof, mnz))
					out_roof_faces.append(Vector3(mxx, comp_roof, mxz))
					out_roof_faces.append(Vector3(mnx, comp_roof, mnz))
					out_roof_faces.append(Vector3(mxx, comp_roof, mxz))
					out_roof_faces.append(Vector3(mnx, comp_roof, mxz))
					_fabricated_roof_cells[fk] = comp_roof
		else:
			big_skipped += 1
	if OS.get_environment("DIAG_FILL") == "1":
		print("FILL_GLOBAL roofed=", roofed.size(), " walls=", walls.size(), " region=", (x1 - x0 + 1), "x", (z1 - z0 + 1),
			" enclosed=", enclosed.size(), " carve=", to_carve.size(), " big_pockets_skipped=", big_skipped, " max_cells=", max_cells)
	var dump_env := OS.get_environment("DIAG_FILL_DUMP")
	if dump_env != "":
		var dp := dump_env.split(",")
		var ddx0 := float(dp[0]); var ddx1 := float(dp[1]); var ddz0 := float(dp[2]); var ddz1 := float(dp[3])
		var fdump := FileAccess.open("D:/1/fill_cells.csv", FileAccess.WRITE)
		fdump.store_line("cx,cz,x,z,roofed,wall,blocked,visited,carve")
		var seen: Dictionary = {}
		for src in [roofed, walls, blocked, visited, enclosed]:
			for k in src.keys():
				seen[k] = true
		for k in seen.keys():
			var ck: Vector2i = k
			var wx := (ck.x + 0.5) * obst_cell; var wz := (ck.y + 0.5) * obst_cell
			if wx < ddx0 or wx > ddx1 or wz < ddz0 or wz > ddz1:
				continue
			fdump.store_line("%d,%d,%f,%f,%d,%d,%d,%d,%d" % [ck.x, ck.y, wx, wz,
				1 if roofed.has(k) else 0, 1 if walls.has(k) else 0, 1 if blocked.has(k) else 0,
				1 if visited.has(k) else 0, 1 if to_carve.has(k) else 0])
		fdump.close()
		print("FILL_DUMP wrote fill_cells.csv for ", dump_env)

	const PAD := 600.0
	var added := 0
	for key in to_carve:
		var cell_key: Vector2i = key
		var gy := _sample_ground_height(ground_grid, GROUND_GRID_CELL, Vector2((cell_key.x + 0.5) * obst_cell, (cell_key.y + 0.5) * obst_cell), ground_fallback)
		var roof_top: float = to_carve[cell_key]
		# "Walk over, never through": we only need to delete the GROUND-level nav under the building so
		# nobody walks through it at grade. We deliberately DON'T carve up to the roof - that fragments
		# the (walkable) roof surface and leaves the patchy holes-plus-slivers look. Block just the
		# ground band; the roof (and any deck several metres up) stays intact and clean. Cap below the
		# roof so a very low shed still keeps its top.
		var carve_top: float = gy + BUILDING_GROUND_BLOCK
		if roof_top != INF:
			carve_top = minf(carve_top, roof_top - RAMPART_CARVE_CLEARANCE)
		# Preserve elevated walkable surfaces stacked in this column (rampart walkway on a wall, an
		# upper floor over a room): stop the carve just below the lowest one that sits above the
		# agent's step height. Ground-level walkable (the enclosed interior floor we WANT to block)
		# is at ~ground, so it doesn't cap and still gets carved. In SOLID mode we skip this so an
		# enclosed building becomes one clean block (no leftover interior floors) - ramparts stay safe
		# regardless because they sit on wall cells, which are excluded from the enclosed set.
		if not solid_mode and walk.has(cell_key):
			var wt: float = walk[cell_key]
			if wt > gy + AGENT_MAX_CLIMB:
				carve_top = minf(carve_top, wt - RAMPART_CARVE_CLEARANCE)
		if carve_top - gy <= AGENT_MAX_CLIMB:
			continue
		var mn_x: float = cell_key.x * obst_cell
		var mn_z: float = cell_key.y * obst_cell
		var outline := PackedVector3Array([
			Vector3(mn_x, 0.0, mn_z),
			Vector3(mn_x + obst_cell, 0.0, mn_z),
			Vector3(mn_x + obst_cell, 0.0, mn_z + obst_cell),
			Vector3(mn_x, 0.0, mn_z + obst_cell),
		])
		var elevation := gy - PAD
		source.add_projected_obstruction(outline, elevation, carve_top - elevation, true)
		if not global_cells.has(cell_key):
			global_cells[cell_key] = {"min_y": gy, "max_y": carve_top}
		added += 1
	return added

func _build_walk_top_grid(walkable_faces: PackedVector3Array, obst_cell: float) -> Dictionary:
	return _build_walk_protect_grid(walkable_faces, obst_cell, true)

## Per-cell walkable Y for carve protection. use_max=true keeps the highest surface (towns/ramps).
## use_max=false keeps the lowest (castle decks under merlons — max would carve through the deck).
func _build_walk_protect_grid(walkable_faces: PackedVector3Array, obst_cell: float, use_max: bool) -> Dictionary:
	var grid := {}
	var i := 0
	while i + 2 < walkable_faces.size():
		var v0: Vector3 = walkable_faces[i]
		var v1: Vector3 = walkable_faces[i + 1]
		var v2: Vector3 = walkable_faces[i + 2]
		i += 3
		var normal := (v1 - v0).cross(v2 - v0)
		if normal.length_squared() < 0.000001:
			continue
		var slope_deg := rad_to_deg(acos(clampf(absf(normal.normalized().y), 0.0, 1.0)))
		# Protect any face Recast would consider walkable (not just near-flat rampart tops): a sloped
		# decorative embankment/ramp (e.g. s_dcr5 at ~40-50 deg) must not be carved through by the same
		# object's own steep side walls, otherwise the ramp is severed and its upper reach is pruned.
		if slope_deg > _agent_max_slope_deg():
			continue
		var tri_y := maxf(v0.y, maxf(v1.y, v2.y))
		var cx0 := floori(minf(v0.x, minf(v1.x, v2.x)) / obst_cell)
		var cx1 := floori(maxf(v0.x, maxf(v1.x, v2.x)) / obst_cell)
		var cz0 := floori(minf(v0.z, minf(v1.z, v2.z)) / obst_cell)
		var cz1 := floori(maxf(v0.z, maxf(v1.z, v2.z)) / obst_cell)
		if (cx1 - cx0 + 1) * (cz1 - cz0 + 1) > 200000:
			continue
		for cz in range(cz0, cz1 + 1):
			for cx in range(cx0, cx1 + 1):
				var key := Vector2i(cx, cz)
				if grid.has(key):
					grid[key] = maxf(grid[key], tri_y) if use_max else minf(grid[key], tri_y)
				else:
					grid[key] = tri_y
	return grid

func _add_projected_mesh_footprint_from_world_faces(
	source: NavigationMeshSourceGeometryData3D,
	faces: PackedVector3Array,
	max_y: float,
	inflate: float = 0.0,
	global_cells: Dictionary = {},
	walkable_faces: PackedVector3Array = PackedVector3Array(),
	portal_protect: Dictionary = {},
	portal_cell: float = 0.0,
	portal_skip_counter: Dictionary = {},
	portal_aabbs: Array = []
) -> int:
	var cells: Dictionary = {} # Vector2i -> {min_y: float, max_y: float}
	const MAX_CELLS_PER_TRI := 200000
	var obst_cell := _obstruction_cell_size()
	# Per-cell height of any walkable surface that sits ON TOP of the wall (e.g. a castle rampart
	# walkway on top of tn4_wall01). The carve must stop just below such a surface, otherwise the
	# wall's own footprint carve (which pads far upward to reach the walk voxels) punches straight
	# through the walkway and deletes it - leaving only the ramp walkable but not the ramparts.
	var walk_top: Dictionary = _build_walk_protect_grid(walkable_faces, obst_cell, true)
	var i := 0
	while i + 2 < faces.size():
		var v0: Vector3 = faces[i]
		var v1: Vector3 = faces[i + 1]
		var v2: Vector3 = faces[i + 2]
		i += 3
		var tri_min_y := minf(v0.y, minf(v1.y, v2.y))
		if tri_min_y > max_y:
			continue
		var tri_max_y := maxf(v0.y, maxf(v1.y, v2.y))
		var a2 := Vector2(v0.x, v0.z)
		var b2 := Vector2(v1.x, v1.z)
		var c2 := Vector2(v2.x, v2.z)
		var bbox_min := Vector2(minf(a2.x, minf(b2.x, c2.x)), minf(a2.y, minf(b2.y, c2.y))) - Vector2(inflate, inflate)
		var bbox_max := Vector2(maxf(a2.x, maxf(b2.x, c2.x)), maxf(a2.y, maxf(b2.y, c2.y))) + Vector2(inflate, inflate)
		var cell_x0 := floori(bbox_min.x / obst_cell)
		var cell_x1 := floori(bbox_max.x / obst_cell)
		var cell_z0 := floori(bbox_min.y / obst_cell)
		var cell_z1 := floori(bbox_max.y / obst_cell)
		var span: int = (cell_x1 - cell_x0 + 1) * (cell_z1 - cell_z0 + 1)
		if span <= 0 or span > MAX_CELLS_PER_TRI:
			continue
		for cz in range(cell_z0, cell_z1 + 1):
			for cx in range(cell_x0, cell_x1 + 1):
				var cell_min := Vector2(cx * obst_cell, cz * obst_cell)
				var cell_max := cell_min + Vector2(obst_cell, obst_cell)
				if not _triangle_aabb_overlap_2d(a2, b2, c2, cell_min, cell_max, inflate):
					continue
				var key := Vector2i(cx, cz)
				if cells.has(key):
					var existing: Dictionary = cells[key]
					existing["min_y"] = minf(existing["min_y"], tri_min_y)
					existing["max_y"] = maxf(existing["max_y"], tri_max_y)
				else:
					cells[key] = {"min_y": tri_min_y, "max_y": tri_max_y}

	const PAD := 600.0
	# Obstruction cells shorter than this are curbs / single steps / the low lip around a floor slab
	# that sits slightly proud of the terrain - all traversable (< agent climb). Carving them as
	# solid walls wrongly seals things: e.g. s_bld23's courtyard floor is ~0.6m above the street, so
	# its perimeter lip (and the gate threshold) would carve a continuous ring and prune the whole
	# courtyard. Below the climb height we let Recast's native voxel step-up handle it instead.
	# Defaults to the agent climb height; NAV_EXPERIMENT_MIN_WALL_H overrides (0 disables the skip).
	var min_wall_h := AGENT_MAX_CLIMB
	var mwh_env := OS.get_environment("NAV_EXPERIMENT_MIN_WALL_H")
	if mwh_env != "":
		min_wall_h = float(mwh_env)
	var added := 0
	for key in cells:
		var cell_key: Vector2i = key
		var entry: Dictionary = cells[key]
		var mn_y: float = entry["min_y"]
		var wall_top: float = entry["max_y"]
		# If a walkable surface sits well above the wall base in this cell (a rampart walkway on top),
		# stop the carve just below it so the walkway survives while the wall below still blocks.
		var carve_top := wall_top
		var capped := false
		if walk_top.has(cell_key):
			var wt: float = float(walk_top[cell_key])
			if wt > mn_y + 1.0:
				carve_top = minf(wall_top, wt - RAMPART_CARVE_CLEARANCE)
				capped = true
		var raw_h: float = maxf(carve_top - mn_y, 0.05)
		if min_wall_h > 0.0 and raw_h <= min_wall_h:
			continue
		var mn_x: float = cell_key.x * obst_cell
		var mn_z: float = cell_key.y * obst_cell
		var mx_x: float = mn_x + obst_cell
		var mx_z: float = mn_z + obst_cell
		# Skip stamps that land in an arch-gateway portal (courtyard hrz21 etc.).
		# Prefer world AABB (reliable) — protect-cell grid can miss when obst_cell ≠ portal_cell.
		var cx := (mn_x + mx_x) * 0.5
		var cz := (mn_z + mx_z) * 0.5
		var in_portal := false
		# Extra pad when entrance kits are active so agent-radius erosion from jamb stamps
		# cannot pinch the keep-grade corridor (Cc_1_00_06).
		var portal_pad := 0.15
		if not portal_aabbs.is_empty() and _point_in_arch_portal_aabbs(portal_aabbs, cx, cz, portal_pad):
			in_portal = true
		elif not portal_protect.is_empty() and portal_cell > 0.0:
			in_portal = portal_protect.has(
				Vector2i(floori(cx / portal_cell), floori(cz / portal_cell))
			)
		if in_portal:
			if portal_skip_counter.has("n"):
				portal_skip_counter["n"] = int(portal_skip_counter["n"]) + 1
			continue
		var outline := PackedVector3Array([
			Vector3(mn_x, 0.0, mn_z),
			Vector3(mx_x, 0.0, mn_z),
			Vector3(mx_x, 0.0, mx_z),
			Vector3(mn_x, 0.0, mx_z),
		])
		# Capped (rampart) cells get an exact top so they don't punch through the walkway above.
		# Uncapped cells keep the generous upward pad (harmless - nothing walkable sits above them)
		# so thin walls reliably rasterize as blocking volume.
		var elevation := mn_y - PAD
		var height := (carve_top - elevation) if capped else (raw_h + PAD * 2.0)
		source.add_projected_obstruction(outline, elevation, height, true)
		_record_obstruction_cells(global_cells, Vector2(mn_x, mn_z), Vector2(mx_x, mx_z), mn_y, carve_top)
		added += 1
	return added

func _is_always_carve_terrain_object(object_name: String) -> bool:
	return ALWAYS_CARVE_TERRAIN_OBJECTS.has(object_name.to_lower())

## Kits with modeled arch / gate openings — hole detect, portal strip/protect, preserve_openings.
## Authored list (do not invent extras). cc95 kept for Cc_1_00_05 east entrance.
const ARCHWAY_TERRAIN_OBJECTS := {
	"cc17": true,
	"cc19": true,
	"cc21": true,
	"cc22": true,
	"cc54": true,
	"cc95": true,
	"hrt_tgate1": true,
	"hrt_wall1": true,
	"hrt_wall3": true,
	"hrz14": true,
	"hrz16": true,
	"hrz17": true,
	"hrz19": true,
	"hrz21": true,
	"hrz22": true,
	"hrz54": true,
}

## Curtain / wall kits whose pier wings use solid_occupied_fill outside the portal lateral column.
const CURTAIN_ARCH_WALL_OBJECTS := {
	"cc17": true,
	"cc19": true,
	"cc21": true,
	"cc22": true,
	"cc54": true,
	"hrt_wall1": true,
	"hrt_wall3": true,
}

## Highland / outdoor boulders that must solid-carve terrain (never walkable tops).
## Arch / gate wall kits — always-carve without dilate so ~3m openings stay clear.
func _is_arch_gateway_terrain_object(object_name: String) -> bool:
	var n := object_name.to_lower()
	if ARCHWAY_TERRAIN_OBJECTS.has(n):
		return true
	# Extra entrance kits from NAV_EXPERIMENT_ENTRANCE_ARCH_KITS also need hole detect.
	return _is_castle_entrance_arch_object(object_name)

## Castle entrance arch kits — always get an arch_portal ground strip (even on the outer curtain).
## Default: cc95 (Cc_1_00_05 east). Override allowlist: NAV_EXPERIMENT_ENTRANCE_ARCH_KITS=cc22,...
## Do not default the full ARCHWAY list here — that reopens every outer mouth and regressed Cc_2_hr.
func _is_castle_entrance_arch_object(object_name: String) -> bool:
	var n := object_name.to_lower()
	if n == "cc95":
		return true
	var extra := OS.get_environment("NAV_EXPERIMENT_ENTRANCE_ARCH_KITS")
	if extra.is_empty():
		return false
	for part in extra.split(","):
		if part.strip_edges().to_lower() == n:
			return true
	return false

## Curtain wall pieces that contain an arch cut — pier-wing solid ALWAYS_CARVE
## (faces outside portal lateral column; Cc_1_00_05/06).
func _is_curtain_arch_wall_object(object_name: String) -> bool:
	return CURTAIN_ARCH_WALL_OBJECTS.has(object_name.to_lower())

## Keep tris whose XZ centroid sits outside every portal's lateral opening span so solid
## fill carves masonry piers without a bar across the arch throat.
func _filter_faces_outside_arch_lateral_column(
	faces: PackedVector3Array, portal_aabbs: Array, margin: float = 0.35
) -> PackedVector3Array:
	if faces.is_empty() or portal_aabbs.is_empty():
		return PackedVector3Array()
	var out := PackedVector3Array()
	var i := 0
	while i + 2 < faces.size():
		var a: Vector3 = faces[i]
		var b: Vector3 = faces[i + 1]
		var c: Vector3 = faces[i + 2]
		i += 3
		var cx := (a.x + b.x + c.x) * (1.0 / 3.0)
		var cz := (a.z + b.z + c.z) * (1.0 / 3.0)
		var in_column := false
		for pa_v in portal_aabbs:
			var pa: Dictionary = pa_v
			var through_z := bool(pa.get("through_z", true))
			if through_z:
				var min_x: float = float(pa["min_x"]) - margin
				var max_x: float = float(pa["max_x"]) + margin
				if cx >= min_x and cx <= max_x:
					in_column = true
					break
			else:
				var min_z: float = float(pa["min_z"]) - margin
				var max_z: float = float(pa["max_z"]) + margin
				if cz >= min_z and cz <= max_z:
					in_column = true
					break
		if in_column:
			continue
		out.append(a)
		out.append(b)
		out.append(c)
	return out

func _point_in_arch_portal_aabbs(aabbs: Array, x: float, z: float, pad: float = 0.0) -> bool:
	for a in aabbs:
		var d: Dictionary = a
		if x >= float(d["min_x"]) - pad and x <= float(d["max_x"]) + pad \
				and z >= float(d["min_z"]) - pad and z <= float(d["max_z"]) + pad:
			return true
	return false

## True for elongated wall-strip silhouettes (skip dilate — dilate bridges arches).
func _occupied_cells_are_wall_strip(occupied: Dictionary, obst_cell: float) -> bool:
	var n := occupied.size()
	if n < 6:
		return false
	var min_cx := 999999
	var max_cx := -999999
	var min_cz := 999999
	var max_cz := -999999
	for k in occupied.keys():
		var ck: Vector2i = k
		min_cx = mini(min_cx, ck.x)
		max_cx = maxi(max_cx, ck.x)
		min_cz = mini(min_cz, ck.y)
		max_cz = maxi(max_cz, ck.y)
	var sx := float(max_cx - min_cx + 1) * obst_cell
	var sz := float(max_cz - min_cz + 1) * obst_cell
	var short_s := minf(sx, sz)
	var long_s := maxf(sx, sz)
	if long_s < 4.0:
		return false
	# Thin strips ≤0.4; keeps/houses are chunkier. Diagonal walls get a loose AABB — also
	# treat sparse occupancy (mostly empty bbox) as a strip.
	var fill := float(n) * obst_cell * obst_cell / maxf(sx * sz, 0.01)
	return short_s / long_s <= 0.4 or fill <= 0.35

## Rasterize steep wall tris onto an XZ cell grid (same rules as ALWAYS_CARVE occupied).
func _rasterize_faces_xz_cells(faces: PackedVector3Array, obst_cell: float) -> Dictionary:
	var occupied: Dictionary = {}
	const MAX_CELLS_PER_TRI := 200000
	var i := 0
	while i + 2 < faces.size():
		var v0: Vector3 = faces[i]
		var v1: Vector3 = faces[i + 1]
		var v2: Vector3 = faces[i + 2]
		i += 3
		var a2 := Vector2(v0.x, v0.z)
		var b2 := Vector2(v1.x, v1.z)
		var c2 := Vector2(v2.x, v2.z)
		var bbox_min := Vector2(minf(a2.x, minf(b2.x, c2.x)), minf(a2.y, minf(b2.y, c2.y)))
		var bbox_max := Vector2(maxf(a2.x, maxf(b2.x, c2.x)), maxf(a2.y, maxf(b2.y, c2.y)))
		var cell_x0 := floori(bbox_min.x / obst_cell)
		var cell_x1 := floori(bbox_max.x / obst_cell)
		var cell_z0 := floori(bbox_min.y / obst_cell)
		var cell_z1 := floori(bbox_max.y / obst_cell)
		var span: int = (cell_x1 - cell_x0 + 1) * (cell_z1 - cell_z0 + 1)
		if span <= 0 or span > MAX_CELLS_PER_TRI:
			continue
		for cz in range(cell_z0, cell_z1 + 1):
			for cx in range(cell_x0, cell_x1 + 1):
				var cell_min := Vector2(cx * obst_cell, cz * obst_cell)
				var cell_max := cell_min + Vector2(obst_cell, obst_cell)
				if not _triangle_aabb_overlap_2d(a2, b2, c2, cell_min, cell_max, 0.0):
					continue
				occupied[Vector2i(cx, cz)] = true
	return occupied

## Arch/door holes only: empty cells in wall-thickness columns that lack solid mass.
## Do NOT mark every flood-reachable empty cell in the AABB — that clears courtyard voids beside
## sparse wall shells and can reopen the closed main gate (Cc_2_hr portal regression).
## Keep a thin through-axis window over the *widest* arch throat. Scoring by cell count alone
## preferred deep narrow recess columns; north hrz21 needs lateral span (gateway width) first.
func _arch_portal_clamp_throat(
	openings: Dictionary, through_z: bool, obst_cell: float, max_thick_m: float
) -> Dictionary:
	if openings.is_empty() or max_thick_m <= 0.0 or obst_cell <= 0.0:
		return openings
	var along_min := 999999
	var along_max := -999999
	for k in openings.keys():
		var ck: Vector2i = k
		var along := ck.y if through_z else ck.x
		along_min = mini(along_min, along)
		along_max = maxi(along_max, along)
	var span_m := float(along_max - along_min + 1) * obst_cell
	if span_m <= max_thick_m + 0.05:
		return openings
	var win := maxi(1, int(ceil(max_thick_m / obst_cell)))
	var best_lo := along_min
	var best_lat := -1
	var best_sum := -1
	for lo in range(along_min, along_max - win + 2):
		var hi := lo + win - 1
		var s := 0
		var lat_min := 999999
		var lat_max := -999999
		for k2 in openings.keys():
			var ck2: Vector2i = k2
			var along2 := ck2.y if through_z else ck2.x
			if along2 < lo or along2 > hi:
				continue
			s += 1
			var lat := ck2.x if through_z else ck2.y
			lat_min = mini(lat_min, lat)
			lat_max = maxi(lat_max, lat)
		if s <= 0:
			continue
		var lat_span := lat_max - lat_min + 1
		if lat_span > best_lat or (lat_span == best_lat and s > best_sum):
			best_lat = lat_span
			best_sum = s
			best_lo = lo
	var best_hi := best_lo + win - 1
	var out: Dictionary = {}
	for k3 in openings.keys():
		var ck3: Vector2i = k3
		var along3 := ck3.y if through_z else ck3.x
		if along3 < best_lo or along3 > best_hi:
			continue
		out[ck3] = true
	return out if not out.is_empty() else openings

## Wall through-axis from median thickness, not AABB aspect. Deep courtyard returns / wing walls
## make gateway kits taller than wide; aspect-ratio then flips the passage direction.
func _arch_wall_through_z(occupied: Dictionary) -> bool:
	if occupied.is_empty():
		return true
	var col_z: Dictionary = {} # cx -> {min,max}
	var row_x: Dictionary = {} # cz -> {min,max}
	var min_cx := 999999
	var max_cx := -999999
	var min_cz := 999999
	var max_cz := -999999
	for k in occupied.keys():
		var ck: Vector2i = k
		min_cx = mini(min_cx, ck.x)
		max_cx = maxi(max_cx, ck.x)
		min_cz = mini(min_cz, ck.y)
		max_cz = maxi(max_cz, ck.y)
		if col_z.has(ck.x):
			var czr: Dictionary = col_z[ck.x]
			czr["min"] = mini(int(czr["min"]), ck.y)
			czr["max"] = maxi(int(czr["max"]), ck.y)
		else:
			col_z[ck.x] = {"min": ck.y, "max": ck.y}
		if row_x.has(ck.y):
			var rxr: Dictionary = row_x[ck.y]
			rxr["min"] = mini(int(rxr["min"]), ck.x)
			rxr["max"] = maxi(int(rxr["max"]), ck.x)
		else:
			row_x[ck.y] = {"min": ck.x, "max": ck.x}
	var col_thick: Array = []
	for cxk in col_z.keys():
		var r: Dictionary = col_z[cxk]
		col_thick.append(int(r["max"]) - int(r["min"]) + 1)
	var row_thick: Array = []
	for czk in row_x.keys():
		var rr: Dictionary = row_x[czk]
		row_thick.append(int(rr["max"]) - int(rr["min"]) + 1)
	if col_thick.is_empty() or row_thick.is_empty():
		return (max_cz - min_cz) <= (max_cx - min_cx)
	col_thick.sort()
	row_thick.sort()
	var med_col: int = int(col_thick[col_thick.size() / 2])
	var med_row: int = int(row_thick[row_thick.size() / 2])
	# Thin median column (Z) ⇒ wall runs along X ⇒ walk through along Z.
	if med_col != med_row:
		return med_col < med_row
	return (max_cz - min_cz) <= (max_cx - min_cx)

## Cell band covering typical wall thickness on the through-axis (ignores deep recess outliers).
func _arch_wall_thickness_band(occupied: Dictionary, through_z: bool) -> Dictionary:
	var out := {"lo": 999999, "hi": -999999}
	if occupied.is_empty():
		return out
	var spans: Dictionary = {} # lateral -> {min,max} along through axis
	for k in occupied.keys():
		var ck: Vector2i = k
		var lat := ck.x if through_z else ck.y
		var along := ck.y if through_z else ck.x
		if spans.has(lat):
			var sr: Dictionary = spans[lat]
			sr["min"] = mini(int(sr["min"]), along)
			sr["max"] = maxi(int(sr["max"]), along)
		else:
			spans[lat] = {"min": along, "max": along}
	var thicknesses: Array = []
	for latk in spans.keys():
		var r: Dictionary = spans[latk]
		thicknesses.append(int(r["max"]) - int(r["min"]) + 1)
	if thicknesses.is_empty():
		return out
	thicknesses.sort()
	var med: int = int(thicknesses[thicknesses.size() / 2])
	var band_lo := 999999
	var band_hi := -999999
	for latk2 in spans.keys():
		var r2: Dictionary = spans[latk2]
		var t2: int = int(r2["max"]) - int(r2["min"]) + 1
		if t2 > med * 2 + 2:
			continue
		band_lo = mini(band_lo, int(r2["min"]))
		band_hi = maxi(band_hi, int(r2["max"]))
	if band_lo > band_hi:
		return out
	out["lo"] = band_lo
	out["hi"] = band_hi
	return out

func _arch_portal_opening_cells(
	occupied: Dictionary, obst_cell: float, through_z: bool = true
) -> Dictionary:
	if occupied.is_empty():
		return {}
	var min_cx := 999999
	var max_cx := -999999
	var min_cz := 999999
	var max_cz := -999999
	for k in occupied.keys():
		var ck: Vector2i = k
		min_cx = mini(min_cx, ck.x)
		max_cx = maxi(max_cx, ck.x)
		min_cz = mini(min_cz, ck.y)
		max_cz = maxi(max_cz, ck.y)
	var openings: Dictionary = {}
	if through_z:
		# Wall along X: per-column gaps in Z between the wall's z-band.
		var col_z: Dictionary = {} # cx -> {min,max}
		for k2 in occupied.keys():
			var c2: Vector2i = k2
			if col_z.has(c2.x):
				var czr: Dictionary = col_z[c2.x]
				czr["min"] = mini(int(czr["min"]), c2.y)
				czr["max"] = maxi(int(czr["max"]), c2.y)
			else:
				col_z[c2.x] = {"min": c2.y, "max": c2.y}
		# Typical wall thickness from columns that actually have mass.
		var band_lo := min_cz
		var band_hi := max_cz
		var thicknesses: Array = []
		for cxk in col_z.keys():
			var r: Dictionary = col_z[cxk]
			thicknesses.append(int(r["max"]) - int(r["min"]) + 1)
		if not thicknesses.is_empty():
			thicknesses.sort()
			var med: int = int(thicknesses[thicknesses.size() / 2])
			# Prefer a tight band around the median column span (ignore courtyard-scale AABB).
			band_lo = 999999
			band_hi = -999999
			for cxk2 in col_z.keys():
				var r2: Dictionary = col_z[cxk2]
				var t2: int = int(r2["max"]) - int(r2["min"]) + 1
				if t2 > med * 2 + 2:
					continue
				band_lo = mini(band_lo, int(r2["min"]))
				band_hi = maxi(band_hi, int(r2["max"]))
			if band_lo > band_hi:
				band_lo = min_cz
				band_hi = max_cz
		# Opening columns: no occupied mass, flanked by solid columns within a few metres.
		var max_open_m := 4.5
		var max_open_cells := maxi(1, int(ceil(max_open_m / obst_cell)))
		for cx3 in range(min_cx, max_cx + 1):
			if col_z.has(cx3):
				# Solid column may still have a thin inner gap (double-wall) — only treat as portal
				# when the gap is large enough to be a doorway (≥1.2m) inside the band.
				var rs: Dictionary = col_z[cx3]
				var lo: int = int(rs["min"])
				var hi: int = int(rs["max"])
				var gap_cells: Array = []
				for czg in range(maxi(lo, band_lo), mini(hi, band_hi) + 1):
					if not occupied.has(Vector2i(cx3, czg)):
						gap_cells.append(czg)
				# Contiguous gaps ≥ 1.2m inside the solid span.
				if gap_cells.is_empty():
					continue
				var run_a: int = int(gap_cells[0])
				var run_b: int = run_a
				for gi in range(1, gap_cells.size()):
					var gz: int = int(gap_cells[gi])
					if gz == run_b + 1:
						run_b = gz
					else:
						if float(run_b - run_a + 1) * obst_cell >= 1.2:
							for czp in range(run_a, run_b + 1):
								openings[Vector2i(cx3, czp)] = true
						run_a = gz
						run_b = gz
				if float(run_b - run_a + 1) * obst_cell >= 1.2:
					for czp2 in range(run_a, run_b + 1):
						openings[Vector2i(cx3, czp2)] = true
				continue
			# Empty column between solid flanks.
			var left := -999999
			var right := 999999
			for cxL in range(cx3 - 1, min_cx - 1, -1):
				if col_z.has(cxL):
					left = cxL
					break
			for cxR in range(cx3 + 1, max_cx + 1):
				if col_z.has(cxR):
					right = cxR
					break
			if left < min_cx or right > max_cx:
				continue
			if right - left - 1 > max_open_cells:
				continue
			for cz3 in range(band_lo, band_hi + 1):
				if not occupied.has(Vector2i(cx3, cz3)):
					openings[Vector2i(cx3, cz3)] = true
	else:
		# Wall along Z: per-row gaps in X (symmetric).
		var row_x: Dictionary = {} # cz -> {min,max}
		for k3 in occupied.keys():
			var c3: Vector2i = k3
			if row_x.has(c3.y):
				var rxr: Dictionary = row_x[c3.y]
				rxr["min"] = mini(int(rxr["min"]), c3.x)
				rxr["max"] = maxi(int(rxr["max"]), c3.x)
			else:
				row_x[c3.y] = {"min": c3.x, "max": c3.x}
		var band_lox := min_cx
		var band_hix := max_cx
		var thick_x: Array = []
		for czk in row_x.keys():
			var rr: Dictionary = row_x[czk]
			thick_x.append(int(rr["max"]) - int(rr["min"]) + 1)
		if not thick_x.is_empty():
			thick_x.sort()
			var medx: int = int(thick_x[thick_x.size() / 2])
			band_lox = 999999
			band_hix = -999999
			for czk2 in row_x.keys():
				var rr2: Dictionary = row_x[czk2]
				var tx: int = int(rr2["max"]) - int(rr2["min"]) + 1
				if tx > medx * 2 + 2:
					continue
				band_lox = mini(band_lox, int(rr2["min"]))
				band_hix = maxi(band_hix, int(rr2["max"]))
			if band_lox > band_hix:
				band_lox = min_cx
				band_hix = max_cx
		var max_open_m2 := 4.5
		var max_open_cells2 := maxi(1, int(ceil(max_open_m2 / obst_cell)))
		for cz4 in range(min_cz, max_cz + 1):
			if row_x.has(cz4):
				var rs2: Dictionary = row_x[cz4]
				var lo2: int = int(rs2["min"])
				var hi2: int = int(rs2["max"])
				var gap_x: Array = []
				for cxg in range(maxi(lo2, band_lox), mini(hi2, band_hix) + 1):
					if not occupied.has(Vector2i(cxg, cz4)):
						gap_x.append(cxg)
				if gap_x.is_empty():
					continue
				var run_a2: int = int(gap_x[0])
				var run_b2: int = run_a2
				for gi2 in range(1, gap_x.size()):
					var gx: int = int(gap_x[gi2])
					if gx == run_b2 + 1:
						run_b2 = gx
					else:
						if float(run_b2 - run_a2 + 1) * obst_cell >= 1.2:
							for cxp in range(run_a2, run_b2 + 1):
								openings[Vector2i(cxp, cz4)] = true
						run_a2 = gx
						run_b2 = gx
				if float(run_b2 - run_a2 + 1) * obst_cell >= 1.2:
					for cxp2 in range(run_a2, run_b2 + 1):
						openings[Vector2i(cxp2, cz4)] = true
				continue
			var up := -999999
			var down := 999999
			for czU in range(cz4 - 1, min_cz - 1, -1):
				if row_x.has(czU):
					up = czU
					break
			for czD in range(cz4 + 1, max_cz + 1):
				if row_x.has(czD):
					down = czD
					break
			if up < min_cz or down > max_cz:
				continue
			if down - up - 1 > max_open_cells2:
				continue
			for cx4 in range(band_lox, band_hix + 1):
				if not occupied.has(Vector2i(cx4, cz4)):
					openings[Vector2i(cx4, cz4)] = true
	return openings

## Grow portal cells only along the wall's through axis so a ground strip meets bailey floors
## without widening the opening sideways into solid wall mass.
func _extend_portal_through_axis(
	openings: Dictionary,
	occupied: Dictionary,
	obst_cell: float,
	pad_m: float,
	through_z: bool = true
) -> Dictionary:
	if openings.is_empty() or pad_m <= 0.0:
		return openings.duplicate()
	var pad_cells := maxi(1, int(ceil(pad_m / obst_cell)))
	# Lateral span of the opening (keep pad from spreading into pillars).
	var o_min_lat := 999999
	var o_max_lat := -999999
	for ok in openings.keys():
		var oc: Vector2i = ok
		var lat := oc.x if through_z else oc.y
		o_min_lat = mini(o_min_lat, lat)
		o_max_lat = maxi(o_max_lat, lat)
	var out: Dictionary = openings.duplicate()
	for ok2 in openings.keys():
		var base: Vector2i = ok2
		for s in range(1, pad_cells + 1):
			for sign in [-1, 1]:
				var n: Vector2i
				if through_z:
					n = Vector2i(base.x, base.y + sign * s)
					if n.x < o_min_lat or n.x > o_max_lat:
						continue
				else:
					n = Vector2i(base.x + sign * s, base.y)
					if n.y < o_min_lat or n.y > o_max_lat:
						continue
				if occupied.has(n):
					continue
				out[n] = true
	return out

func _portal_cells_from_aabb(
	min_x: float, max_x: float, min_z: float, max_z: float, obst_cell: float
) -> Dictionary:
	var out: Dictionary = {}
	if obst_cell <= 0.0 or min_x >= max_x or min_z >= max_z:
		return out
	var cx0 := floori(min_x / obst_cell)
	var cx1 := floori((max_x - 0.001) / obst_cell)
	var cz0 := floori(min_z / obst_cell)
	var cz1 := floori((max_z - 0.001) / obst_cell)
	for cz in range(cz0, cz1 + 1):
		for cx in range(cx0, cx1 + 1):
			out[Vector2i(cx, cz)] = true
	return out

func _portal_walkable_strip_aabb(
	min_x: float, max_x: float, min_z: float, max_z: float, ground_y: float
) -> PackedVector3Array:
	var out := PackedVector3Array()
	if min_x >= max_x or min_z >= max_z:
		return out
	var y := ground_y + 0.05
	out.append(Vector3(min_x, y, min_z))
	out.append(Vector3(max_x, y, min_z))
	out.append(Vector3(max_x, y, max_z))
	out.append(Vector3(min_x, y, min_z))
	out.append(Vector3(max_x, y, max_z))
	out.append(Vector3(min_x, y, max_z))
	return out

## Flat walkable quads covering portal cells so Recast generates a ground strip through the arch.
func _portal_walkable_faces_from_cells(
	cells: Dictionary, obst_cell: float, ground_y: float
) -> PackedVector3Array:
	var out := PackedVector3Array()
	var y := ground_y + 0.05
	for k in cells.keys():
		var ck: Vector2i = k
		var x0 := float(ck.x) * obst_cell
		var z0 := float(ck.y) * obst_cell
		var x1 := x0 + obst_cell
		var z1 := z0 + obst_cell
		# Two tris per cell.
		out.append(Vector3(x0, y, z0))
		out.append(Vector3(x1, y, z0))
		out.append(Vector3(x1, y, z1))
		out.append(Vector3(x0, y, z0))
		out.append(Vector3(x1, y, z1))
		out.append(Vector3(x0, y, z1))
	return out

## Single AABB quad over portal cells — continuous source through the arch throat.
func _portal_walkable_strip_from_cells(
	cells: Dictionary, obst_cell: float, ground_y: float
) -> PackedVector3Array:
	var out := PackedVector3Array()
	if cells.is_empty():
		return out
	var min_cx := 999999
	var max_cx := -999999
	var min_cz := 999999
	var max_cz := -999999
	for k in cells.keys():
		var ck: Vector2i = k
		min_cx = mini(min_cx, ck.x)
		max_cx = maxi(max_cx, ck.x)
		min_cz = mini(min_cz, ck.y)
		max_cz = maxi(max_cz, ck.y)
	var x0 := float(min_cx) * obst_cell
	var z0 := float(min_cz) * obst_cell
	var x1 := float(max_cx + 1) * obst_cell
	var z1 := float(max_cz + 1) * obst_cell
	var y := ground_y + 0.05
	out.append(Vector3(x0, y, z0))
	out.append(Vector3(x1, y, z0))
	out.append(Vector3(x1, y, z1))
	out.append(Vector3(x0, y, z0))
	out.append(Vector3(x1, y, z1))
	out.append(Vector3(x0, y, z1))
	return out

## Centre corridor through a gateway kit's wall AABB (mid ~45% of the long axis, wall-thickness band).
func _arch_portal_center_corridor_cells(
	occupied: Dictionary, obst_cell: float, through_z: bool = true
) -> Dictionary:
	if occupied.is_empty():
		return {}
	var min_cx := 999999
	var max_cx := -999999
	var min_cz := 999999
	var max_cz := -999999
	for k in occupied.keys():
		var ck: Vector2i = k
		min_cx = mini(min_cx, ck.x)
		max_cx = maxi(max_cx, ck.x)
		min_cz = mini(min_cz, ck.y)
		max_cz = maxi(max_cz, ck.y)
	var sx := max_cx - min_cx + 1
	var sz := max_cz - min_cz + 1
	var band := _arch_wall_thickness_band(occupied, through_z)
	var band_lo := int(band["lo"])
	var band_hi := int(band["hi"])
	var openings: Dictionary = {}
	if through_z:
		var lo := min_cx + int(floor(float(sx) * 0.275))
		var hi := max_cx - int(floor(float(sx) * 0.275))
		if hi < lo:
			lo = min_cx + sx / 3
			hi = max_cx - sx / 3
		var z0 := band_lo if band_lo <= band_hi else min_cz
		var z1 := band_hi if band_lo <= band_hi else max_cz
		for cx in range(lo, hi + 1):
			for cz in range(z0, z1 + 1):
				var cell := Vector2i(cx, cz)
				if not occupied.has(cell):
					openings[cell] = true
	else:
		var lo2 := min_cz + int(floor(float(sz) * 0.275))
		var hi2 := max_cz - int(floor(float(sz) * 0.275))
		if hi2 < lo2:
			lo2 = min_cz + sz / 3
			hi2 = max_cz - sz / 3
		var x0 := band_lo if band_lo <= band_hi else min_cx
		var x1 := band_hi if band_lo <= band_hi else max_cx
		for cz2 in range(lo2, hi2 + 1):
			for cx2 in range(x0, x1 + 1):
				var cell2 := Vector2i(cx2, cz2)
				if not occupied.has(cell2):
					openings[cell2] = true
	return openings

## Matches ALWAYS_CARVE rock kits; excludes ruin_rocks* (not in the allowlist).
func _is_solid_rock_terrain_object(object_name: String) -> bool:
	var n := object_name.to_lower()
	if n.begins_with("hr_rock"):
		return true
	if n.begins_with("rock") and ALWAYS_CARVE_TERRAIN_OBJECTS.has(n):
		return true
	return false

## Castle approach ramps — walkable slopes that must not receive near-ground terrain stamps.
func _is_approach_ramp_object(object_name: String) -> bool:
	var n := object_name.to_lower()
	return n == "cc55" or n == "cc56"

## Standalone bridge decks (brige* typo spelling in source data, tn3_bridge*, lbridge*, etc.).
## Multi-plank spans → bowl ribbon from pivots; single-kit arches → hump ribbon from mesh deck samples.
func _is_bridge_deck_object(object_name: String) -> bool:
	var n := object_name.to_lower()
	if n.contains("bridge") or n.contains("brige"):
		return true
	return n == "lbridge" or n.begins_with("lbridge")

## All bridge planks that belong on this tile's ribbon: local placements plus any bridge in the
## 3×3 cell neighbourhood flood-filled by proximity (so seam-crossing spans stay whole).
func _collect_bridge_planks_for_tile(gx: int, gz: int, placements: Array) -> Array:
	var planks: Array = [] # {pos, half_width, transform, object_name}
	var seen: Dictionary = {} # rounded origin key -> true
	const CHAIN_R := 25.0
	for bpl in placements:
		_try_append_bridge_plank(bpl, planks, seen)
	if planks.is_empty():
		return planks
	# Candidates from 3×3 (bridges are pivot-bucketed; span often crosses the tile seam).
	var candidates: Array = []
	for dz in range(-1, 2):
		for dx in range(-1, 2):
			for npl in _objects_by_cell.get(Vector2i(gx + dx, gz + dz), []):
				if _is_bridge_deck_object(str(npl["object_name"])):
					candidates.append(npl)
	# Flood-fill: keep absorbing candidates within CHAIN_R of any accepted plank.
	var grew := true
	while grew:
		grew = false
		for npl2 in candidates:
			var npos: Vector3 = (npl2["transform"] as Transform3D).origin
			var near := false
			for p in planks:
				var pp: Vector3 = p["pos"]
				if Vector2(npos.x, npos.z).distance_to(Vector2(pp.x, pp.z)) <= CHAIN_R:
					near = true
					break
			if near:
				var before := planks.size()
				_try_append_bridge_plank(npl2, planks, seen)
				if planks.size() > before:
					grew = true
	return planks

func _try_append_bridge_plank(placement: Dictionary, planks: Array, seen: Dictionary) -> void:
	var bname: String = placement["object_name"]
	if not _is_bridge_deck_object(bname) or _skip_obstruction(bname):
		return
	var bxform: Transform3D = placement["transform"]
	var key := "%d,%d,%d" % [
		roundi(bxform.origin.x * 10.0),
		roundi(bxform.origin.y * 10.0),
		roundi(bxform.origin.z * 10.0),
	]
	if seen.has(key):
		return
	var bparts: Array = _load_object_parts(bname)
	if bparts.is_empty():
		return
	seen[key] = true
	var baabb := _parts_world_aabb(bparts, bxform)
	planks.append({
		"pos": Vector3(bxform.origin.x, bxform.origin.y, bxform.origin.z),
		"half_width": _bridge_plank_half_width(baabb),
		"transform": bxform,
		"object_name": bname,
	})

## Narrower horizontal AABB extent ≈ deck width (longer extent is the plank span).
func _bridge_plank_half_width(aabb: AABB) -> float:
	var hx := aabb.size.x
	var hz := aabb.size.z
	var width := minf(hx, hz)
	if width < 0.4:
		width = maxf(hx, hz)
	# Clamp: rope AABBs can be huge; typical walkway is a few metres.
	width = clampf(width, 1.5, 6.0)
	return width * 0.5

## Bowl/hump walkable ribbon: multi-plank pivots (suspension sag) or mesh-sampled single-kit arches.
## When ground_grid is provided, also emit climb-stepped approaches from each end onto the
## sampled rim so agents can step on/off.
func _bridge_bowl_quad_strip(planks: Array, ground_grid: Dictionary = {}, ground_fallback: float = 0.0) -> PackedVector3Array:
	var out := PackedVector3Array()
	if planks.is_empty():
		return out
	# Cluster planks that belong to the same span (gap > 30m starts a new chain).
	var remaining: Array = planks.duplicate()
	while not remaining.is_empty():
		var chain: Array = [remaining.pop_back()]
		var grew := true
		while grew:
			grew = false
			var i := 0
			while i < remaining.size():
				var rp: Vector3 = remaining[i]["pos"]
				var near := false
				for c in chain:
					var cp: Vector3 = c["pos"]
					if Vector2(rp.x, rp.z).distance_to(Vector2(cp.x, cp.z)) <= 30.0:
						near = true
						break
				if near:
					chain.append(remaining[i])
					remaining.remove_at(i)
					grew = true
				else:
					i += 1
		out.append_array(_bridge_bowl_quad_strip_chain(chain, ground_grid, ground_fallback))
	return out

func _bridge_bowl_quad_strip_chain(
	chain: Array, ground_grid: Dictionary = {}, ground_fallback: float = 0.0
) -> PackedVector3Array:
	if chain.is_empty():
		return PackedVector3Array()
	# Peel tn3_bridge* kits out: each gets a mesh-sampled hump ribbon (pivot Y flattens arches).
	# Remaining multi-plank suspension pieces (brige*) keep the authored-pivot bowl path.
	var tn3s: Array = []
	var others: Array = []
	for c0 in chain:
		if str(c0.get("object_name", "")).begins_with("tn3_bridge"):
			tn3s.append(c0)
		else:
			others.append(c0)
	var out := PackedVector3Array()
	for tkit in tn3s:
		var mesh_stations: Array = _bridge_mesh_deck_stations(tkit)
		if mesh_stations.size() >= 2:
			mesh_stations = _bridge_sort_stations_along_span(mesh_stations)
			out.append_array(_bridge_stations_quad_strip(mesh_stations, ground_grid, ground_fallback))
		else:
			var p0: Vector3 = tkit["pos"]
			var hw0: float = tkit["half_width"]
			out.append_array(_portal_walkable_strip_aabb(
				p0.x - hw0, p0.x + hw0, p0.z - hw0, p0.z + hw0, p0.y
			))
	if others.is_empty():
		return out
	if others.size() == 1:
		# Non-tn3 singleton: try mesh sample (tn4_brige / lbridge arches), else pad.
		var ostations := _bridge_mesh_deck_stations(others[0])
		if ostations.size() >= 2:
			ostations = _bridge_sort_stations_along_span(ostations)
			out.append_array(_bridge_stations_quad_strip(ostations, ground_grid, ground_fallback))
		else:
			var op: Vector3 = others[0]["pos"]
			var ohw: float = others[0]["half_width"]
			out.append_array(_portal_walkable_strip_aabb(
				op.x - ohw, op.x + ohw, op.z - ohw, op.z + ohw, op.y
			))
		return out
	others = _bridge_sort_stations_along_span(others)
	out.append_array(_bridge_stations_quad_strip(others, ground_grid, ground_fallback))
	return out

func _bridge_sort_stations_along_span(stations: Array) -> Array:
	if stations.size() < 2:
		return stations
	var cx := 0.0
	var cz := 0.0
	for c in stations:
		var p: Vector3 = c["pos"]
		cx += p.x
		cz += p.z
	cx /= float(stations.size())
	cz /= float(stations.size())
	var var_x := 0.0
	var var_z := 0.0
	for c2 in stations:
		var p2: Vector3 = c2["pos"]
		var_x += absf(p2.x - cx)
		var_z += absf(p2.z - cz)
	var along_x := var_x >= var_z
	var sorted: Array = stations.duplicate()
	sorted.sort_custom(func(a, b):
		var pa: Vector3 = a["pos"]
		var pb: Vector3 = b["pos"]
		if along_x:
			return pa.x < pb.x
		return pa.z < pb.z
	)
	return sorted

## Deck-top stations along a single bridge mesh: up-facing near-horizontal face centroids, centerline
## band, max Y per 1 m along-span bin (arch hump / flat deck — not pivot Y).
func _bridge_mesh_deck_stations(plank: Dictionary) -> Array:
	var out: Array = [] # {pos, half_width}
	var bname := str(plank.get("object_name", ""))
	if bname.is_empty() or not plank.has("transform"):
		return out
	var xform: Transform3D = plank["transform"]
	var parts: Array = _load_object_parts(bname)
	if parts.is_empty():
		return out
	var cents: Array = [] # Vector3
	for part in parts:
		var mesh: Mesh = part["mesh"]
		var faces := _mesh_faces_world(mesh, xform * part["local"])
		var fi := 0
		while fi + 2 < faces.size():
			var a: Vector3 = faces[fi]
			var b: Vector3 = faces[fi + 1]
			var c: Vector3 = faces[fi + 2]
			fi += 3
			var nrm := (b - a).cross(c - a)
			if nrm.length_squared() < 1e-10:
				continue
			nrm = nrm.normalized()
			# Up-facing, slope from horizontal ≤ ~45° (nrm.y >= cos45).
			if nrm.y < 0.7:
				continue
			cents.append((a + b + c) / 3.0)
	if cents.size() < 3:
		return out
	var mean := Vector3.ZERO
	for p in cents:
		mean += p
	mean /= float(cents.size())
	var var_x := 0.0
	var var_z := 0.0
	for p2 in cents:
		var_x += absf(p2.x - mean.x)
		var_z += absf(p2.z - mean.z)
	var along_x := var_x >= var_z
	var lats: Array = []
	for p3 in cents:
		lats.append(p3.z if along_x else p3.x)
	lats.sort()
	var med_lat: float = float(lats[int(lats.size() / 2)])
	const LAT_BAND := 1.5
	var bins: Dictionary = {} # along_int -> max_y
	for p4 in cents:
		var lat: float = p4.z if along_x else p4.x
		if absf(lat - med_lat) > LAT_BAND:
			continue
		var along: float = p4.x if along_x else p4.z
		var bkey := int(floor(along))
		if not bins.has(bkey) or p4.y > float(bins[bkey]):
			bins[bkey] = p4.y
	var keys: Array = bins.keys()
	keys.sort()
	if keys.size() < 2:
		return out
	# Path-width for short arched kits (dirt-road bridges).
	var half_w := clampf(float(plank.get("half_width", 2.25)), 2.0, 2.5)
	for k in keys:
		var along_v := float(k) + 0.5
		var y: float = float(bins[k])
		var pos := Vector3(along_v, y, med_lat) if along_x else Vector3(med_lat, y, along_v)
		out.append({"pos": pos, "half_width": half_w})
	return out

## Climb-stepped ribbon through ordered stations + optional end approaches onto terrain rims.
func _bridge_stations_quad_strip(
	stations: Array, ground_grid: Dictionary = {}, ground_fallback: float = 0.0
) -> PackedVector3Array:
	var out := PackedVector3Array()
	if stations.size() < 2:
		return out
	var half_w := 0.0
	for s0 in stations:
		half_w = maxf(half_w, float(s0["half_width"]))
	var max_dy := minf(0.25, _agent_max_climb() * 0.85)
	const MAX_DXZ := 1.25
	for i in range(stations.size() - 1):
		var a: Vector3 = stations[i]["pos"]
		var b: Vector3 = stations[i + 1]["pos"]
		var dxz: float = Vector2(a.x, a.z).distance_to(Vector2(b.x, b.z))
		var dy: float = absf(b.y - a.y)
		var steps := maxi(1, maxi(ceili(dy / max_dy), ceili(dxz / MAX_DXZ)))
		var prev := a
		for s in range(1, steps + 1):
			var t := float(s) / float(steps)
			var cur := a.lerp(b, t)
			out.append_array(_bridge_ribbon_quad(prev, cur, half_w))
			prev = cur
	if not ground_grid.is_empty():
		var first: Vector3 = stations[0]["pos"]
		var second: Vector3 = stations[1]["pos"]
		var last: Vector3 = stations[stations.size() - 1]["pos"]
		var prev_last: Vector3 = stations[stations.size() - 2]["pos"]
		out.append_array(_bridge_end_approach(first, second, half_w, ground_grid, ground_fallback))
		out.append_array(_bridge_end_approach(last, prev_last, half_w, ground_grid, ground_fallback))
	return out

## Step from an end plank outward onto cliff terrain. Skips canyon floor (ground far below deck).
func _bridge_end_approach(
	end_plank: Vector3,
	inward_plank: Vector3,
	half_width: float,
	ground_grid: Dictionary,
	ground_fallback: float
) -> PackedVector3Array:
	var out := PackedVector3Array()
	var tang := Vector3(end_plank.x - inward_plank.x, 0.0, end_plank.z - inward_plank.z)
	if tang.length_squared() < 1e-8:
		return out
	tang = tang.normalized()
	# Probe outward for a rim: terrain within a few metres of deck Y (not the ravine floor).
	const PROBE_STEP := 1.0
	const PROBE_MAX := 14.0
	const RIM_Y_BAND := 4.5
	var rim_dist := -1.0
	var rim_y := end_plank.y
	var d := PROBE_STEP
	while d <= PROBE_MAX:
		var xz := Vector2(end_plank.x + tang.x * d, end_plank.z + tang.z * d)
		var gy := _sample_ground_height(ground_grid, GROUND_GRID_CELL, xz, ground_fallback)
		if absf(gy - end_plank.y) <= RIM_Y_BAND:
			rim_dist = d
			rim_y = gy
			# Keep scanning a bit further to land past the eroded lip.
			var d2 := d + 2.0
			while d2 <= minf(d + 6.0, PROBE_MAX):
				var xz2 := Vector2(end_plank.x + tang.x * d2, end_plank.z + tang.z * d2)
				var gy2 := _sample_ground_height(ground_grid, GROUND_GRID_CELL, xz2, ground_fallback)
				if absf(gy2 - end_plank.y) <= RIM_Y_BAND:
					rim_dist = d2
					rim_y = gy2
				else:
					break
				d2 += PROBE_STEP
			break
		d += PROBE_STEP
	# Always extend past the end plank so agent_radius erosion can't open a seam; if no rim was
	# found, stay at deck Y for a short pad (better than dropping into the canyon).
	if rim_dist < 0.0:
		rim_dist = 4.0
		rim_y = end_plank.y
	var max_dy := minf(0.25, _agent_max_climb() * 0.85)
	const MAX_DXZ := 1.0
	var dy: float = absf(rim_y - end_plank.y)
	var steps := maxi(1, maxi(ceili(dy / max_dy), ceili(rim_dist / MAX_DXZ)))
	var prev := end_plank
	for s in range(1, steps + 1):
		var t := float(s) / float(steps)
		var cur := Vector3(
			end_plank.x + tang.x * rim_dist * t,
			lerpf(end_plank.y, rim_y, t),
			end_plank.z + tang.z * rim_dist * t
		)
		out.append_array(_bridge_ribbon_quad(prev, cur, half_width))
		prev = cur
	# Small landing pad at rim height so cliff terrain and approach share footprint.
	var land := prev
	var land2 := land + tang * 1.5
	land2.y = rim_y
	out.append_array(_bridge_ribbon_quad(land, land2, half_width * 1.15))
	return out

## One slanted quad between two stations, extruded ±half_width perpendicular to the XZ tangent.
func _bridge_ribbon_quad(p0: Vector3, p1: Vector3, half_width: float) -> PackedVector3Array:
	var out := PackedVector3Array()
	var tang := Vector3(p1.x - p0.x, 0.0, p1.z - p0.z)
	if tang.length_squared() < 1e-8:
		return out
	tang = tang.normalized()
	var right := Vector3(-tang.z, 0.0, tang.x) * half_width
	# Slight longitudinal overlap so neighbouring steps share an edge in the voxelizer.
	var along := tang * 0.15
	var a := Vector3(p0.x, p0.y, p0.z) - right - along
	var b := Vector3(p0.x, p0.y, p0.z) + right - along
	var c := Vector3(p1.x, p1.y, p1.z) + right + along
	var d := Vector3(p1.x, p1.y, p1.z) - right + along
	# CCW from +Y so face normals point up (a→b→c was clockwise; Recast dropped every tri).
	out.append(a); out.append(d); out.append(c)
	out.append(a); out.append(c); out.append(b)
	return out

## Kit pieces whose near-horizontal surfaces are roofs/tops and must never become walkable nav.
## cc01-13, cc18, cc20, cc38-39, cc41-42, cc44-45, cc47-48, cc57-58, cc78-85,
## hrz00-13, hrz78-85.
func _is_never_walkable_roof_object(object_name: String) -> bool:
	var n := object_name.to_lower()
	var prefix := ""
	var num_str := ""
	if n.begins_with("hrz"):
		prefix = "hrz"
		num_str = n.substr(3)
	elif n.begins_with("cc") and not n.begins_with("cc_") and not n.begins_with("cci"):
		prefix = "cc"
		num_str = n.substr(2)
	else:
		return false
	# Allow optional trailing junk after digits (e.g. cc01a) — take leading int.
	var digits := ""
	for i in range(num_str.length()):
		var ch := num_str.substr(i, 1)
		if ch.is_valid_int():
			digits += ch
		else:
			break
	if digits.is_empty():
		return false
	var id := int(digits)
	if prefix == "hrz":
		return (id >= 0 and id <= 13) or (id >= 78 and id <= 85)
	# cc*
	if id >= 1 and id <= 13:
		return true
	if id == 18 or id == 20:
		return true
	if id == 38 or id == 39:
		return true
	if id == 41 or id == 42:
		return true
	if id == 44 or id == 45:
		return true
	if id == 47 or id == 48:
		return true
	if id == 57 or id == 58:
		return true
	if id >= 78 and id <= 85:
		return true
	return false

## Keep tris whose lowest vertex is within band_h of the mesh's global min Y (base footprint).
func _filter_faces_near_mesh_bottom(faces: PackedVector3Array, band_h: float) -> PackedVector3Array:
	if faces.is_empty():
		return faces
	var min_y := INF
	var i := 0
	while i < faces.size():
		min_y = minf(min_y, faces[i].y)
		i += 1
	if min_y == INF:
		return PackedVector3Array()
	var top := min_y + band_h
	var out := PackedVector3Array()
	i = 0
	while i + 2 < faces.size():
		var a: Vector3 = faces[i]
		var b: Vector3 = faces[i + 1]
		var c: Vector3 = faces[i + 2]
		i += 3
		if minf(a.y, minf(b.y, c.y)) <= top:
			out.append(a)
			out.append(b)
			out.append(c)
	return out

## Keep tris that meet the terrain: any vertex within [ground_y - slack, ground_y + band_h].
## Prefer this over mesh-min-Y for authored buildings whose lowest verts are sparse skirts while
## the real wall mass sits on sampled ground (hrz08).
func _filter_faces_near_ground_y(
	faces: PackedVector3Array, ground_y: float, band_h: float
) -> PackedVector3Array:
	if faces.is_empty():
		return faces
	var lo := ground_y - 1.0
	var hi := ground_y + band_h
	var out := PackedVector3Array()
	var i := 0
	while i + 2 < faces.size():
		var a: Vector3 = faces[i]
		var b: Vector3 = faces[i + 1]
		var c: Vector3 = faces[i + 2]
		i += 3
		var ymin := minf(a.y, minf(b.y, c.y))
		var ymax := maxf(a.y, maxf(b.y, c.y))
		# Reject tris entirely above the band (arch soffits / upper floors) and fully buried junk.
		if ymin > hi or ymax < lo:
			continue
		out.append(a)
		out.append(b)
		out.append(c)
	return out

## Fine-grid base-footprint carve — near-ground band only (exact top, no upward PAD into decks).
## Fills small hollow XZ cavities inside the silhouette.
## When walkable_faces are provided (end-of-pass flush), cells under ramps/decks/ramparts are capped
## or skipped so the stamp cannot punch through already-registered walkable source geometry.
## solid_fill: fill the full XZ AABB of the silhouette (solid boulders); skips hollow seal/hole fill.
## solid_occupied_fill: dilate the silhouette cells only (no global AABB) — pier wings beside an arch.
## opening_aware_fill: fill closed interiors / solid mass inside the AABB, but leave cells that
## flood-connect to the AABB exterior empty (archways, doors, outer courtyard corners).
func _carve_always_carve_base_footprint(
	source: NavigationMeshSourceGeometryData3D,
	base_faces: PackedVector3Array,
	ground_y: float,
	global_cells: Dictionary,
	walkable_faces: PackedVector3Array = PackedVector3Array(),
	solid_fill: bool = false,
	opening_aware_fill: bool = false,
	preserve_openings: bool = false,
	portal_protect: Dictionary = {},
	solid_occupied_fill: bool = false,
	portal_aabbs: Array = []
) -> int:
	var obst_cell := _always_carve_obst_cell_size()
	# Pier-wing land carve only needs to eat terrain; a full 2m column beside the entrance
	# portal strip made Recast drop the throat polys (Cc_1_00_05).
	var band_top: float = ground_y + (
		1.0 if solid_occupied_fill else NEAR_GROUND_CARVE_MAX_HEIGHT
	)
	var occupied: Dictionary = {} # Vector2i -> true
	const MAX_CELLS_PER_TRI := 200000
	var i := 0
	while i + 2 < base_faces.size():
		var v0: Vector3 = base_faces[i]
		var v1: Vector3 = base_faces[i + 1]
		var v2: Vector3 = base_faces[i + 2]
		i += 3
		var a2 := Vector2(v0.x, v0.z)
		var b2 := Vector2(v1.x, v1.z)
		var c2 := Vector2(v2.x, v2.z)
		var bbox_min := Vector2(minf(a2.x, minf(b2.x, c2.x)), minf(a2.y, minf(b2.y, c2.y)))
		var bbox_max := Vector2(maxf(a2.x, maxf(b2.x, c2.x)), maxf(a2.y, maxf(b2.y, c2.y)))
		var cell_x0 := floori(bbox_min.x / obst_cell)
		var cell_x1 := floori(bbox_max.x / obst_cell)
		var cell_z0 := floori(bbox_min.y / obst_cell)
		var cell_z1 := floori(bbox_max.y / obst_cell)
		var span: int = (cell_x1 - cell_x0 + 1) * (cell_z1 - cell_z0 + 1)
		if span <= 0 or span > MAX_CELLS_PER_TRI:
			continue
		for cz in range(cell_z0, cell_z1 + 1):
			for cx in range(cell_x0, cell_x1 + 1):
				var cell_min := Vector2(cx * obst_cell, cz * obst_cell)
				var cell_max := cell_min + Vector2(obst_cell, obst_cell)
				if not _triangle_aabb_overlap_2d(a2, b2, c2, cell_min, cell_max, 0.0):
					continue
				occupied[Vector2i(cx, cz)] = true
	if occupied.is_empty():
		return 0
	var carve_mask: Dictionary
	if solid_fill:
		# Solid boulder: fill every cell in the silhouette's XZ AABB.
		var min_cx := 999999
		var max_cx := -999999
		var min_cz := 999999
		var max_cz := -999999
		for k in occupied.keys():
			var ck: Vector2i = k
			min_cx = mini(min_cx, ck.x)
			max_cx = maxi(max_cx, ck.x)
			min_cz = mini(min_cz, ck.y)
			max_cz = maxi(max_cz, ck.y)
		carve_mask = {}
		for cz in range(min_cz, max_cz + 1):
			for cx in range(min_cx, max_cx + 1):
				carve_mask[Vector2i(cx, cz)] = true
	elif solid_occupied_fill:
		# Pier wings (Cc_1_00_05 cc22): near-ground silhouette → dilate → per-component AABB.
		# Erase only the tight 0.5 m entrance corridor (+ small moat) so masonry land carves
		# cleanly without a carpet of protect-cell walkables under the wall.
		var before_n := occupied.size()
		var seeds := _erase_arch_lateral_column_cells(
			occupied.duplicate(), portal_aabbs, obst_cell, 0.6
		)
		seeds = _dilate_occupied_cells(seeds, obst_cell, 0.3)
		carve_mask = _solid_fill_connected_component_aabbs(seeds)
		carve_mask = _erase_portal_aabb_cells(carve_mask, portal_aabbs, obst_cell, 0.0)
		carve_mask = _erase_cells_adjacent_to_portal_protect(carve_mask, portal_protect, 2)
		if OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
			print(
				"    pier_solid: occupied=", before_n,
				" carved=", carve_mask.size(),
				" portals=", portal_aabbs.size()
			)
	elif opening_aware_fill:
		# Dilate+seal closes green-under-wall cavities on chunky keeps and ordinary wall strips.
		# Arch gateway kits only: no dilate / tight seal so ~3m courtyard arches stay open
		# (preserve_openings). Skipping dilate for ALL wall strips left green under outer walls
		# (Cc_2_hr regression).
		#
		# Keep undercrofts: steep-face projection often paints the whole keep XZ solid (vault ribs /
		# internal mass). Peel to an exterior shell before dilate so the walkable floor stays open
		# (Cc_1_hr_occ02). Pillars touching open space remain (dist 0 from outside).
		var shell_depth_env := OS.get_environment("NAV_EXPERIMENT_ALWAYS_CARVE_SHELL_DEPTH")
		# Thickness of wall ring kept from a solid silhouette (smaller → more undercroft freed).
		var shell_depth := float(shell_depth_env) if shell_depth_env != "" else 1.2
		var shell_occ := occupied
		if not preserve_openings and shell_depth > 0.0:
			shell_occ = _keep_exterior_shell_cells(occupied, obst_cell, shell_depth)
		var dilate_m := 0.0
		var seal_m := 1.5
		if preserve_openings:
			dilate_m = 0.0
			seal_m = 1.5
		else:
			var dilate_env := OS.get_environment("NAV_EXPERIMENT_ALWAYS_CARVE_DILATE")
			dilate_m = float(dilate_env) if dilate_env != "" else 0.75
			var seal_env := OS.get_environment("NAV_EXPERIMENT_ALWAYS_CARVE_OPEN_SEAL")
			seal_m = float(seal_env) if seal_env != "" else 2.25
			# Large keep / courtyard shells: dilate closes the undercroft floor between piers
			# and wall returns (hrz23 1910→4968 at 0.15m cells ≈ 43 m²). Thin wall strips still
			# dilate. Threshold is area so it tracks ALWAYS_CARVE_OBST_CELL.
			var shell_area := float(shell_occ.size()) * obst_cell * obst_cell
			var max_dilate_area_env := OS.get_environment("NAV_EXPERIMENT_ALWAYS_CARVE_DILATE_MAX_AREA")
			var max_dilate_area := float(max_dilate_area_env) if max_dilate_area_env != "" else 25.0
			if shell_area > max_dilate_area:
				dilate_m = 0.0
				seal_m = minf(seal_m, 1.5)
		var thickened := _dilate_occupied_cells(shell_occ, obst_cell, dilate_m)
		var sealed := _footprint_seal_hollow_spans_max(thickened, obst_cell, seal_m)
		if preserve_openings:
			# Arch kits: openings stay in the silhouette; flood-fill only truly sealed pockets
			# behind piers (not the through-throat, which remains outside-connected).
			carve_mask = _footprint_fill_closed_except_openings(sealed, obst_cell)
		else:
			# Do not room-fill sealed interiors — that re-solidifies undercrofts after shell peel.
			carve_mask = _castle_footprint_fill_small_holes(sealed, obst_cell)
		if OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
			print(
				"    opening_aware sizes occ=", occupied.size(),
				" shell=", shell_occ.size(),
				" dilate=", thickened.size(),
				" seal=", sealed.size(),
				" mask=", carve_mask.size(),
				" preserve=", preserve_openings
			)
	else:
		# Seal open-ended hollow walls (inner/outer face strips with a gap) then fill leftover holes.
		carve_mask = _footprint_seal_hollow_spans(occupied, obst_cell)
		carve_mask = _castle_footprint_fill_small_holes(carve_mask, obst_cell)
	# Lowest walkable Y per always-carve cell — ramps/decks that already claimed this column.
	var walk_lo: Dictionary = _build_walk_protect_grid(walkable_faces, obst_cell, false)
	const PAD := 600.0
	const MIN_CARVE_H := 0.35
	var added := 0
	var portal_aabb_skips := 0
	for key_v in carve_mask.keys():
		var cell_key: Vector2i = key_v
		var mn_x: float = cell_key.x * obst_cell
		var mn_z: float = cell_key.y * obst_cell
		var mx_x: float = mn_x + obst_cell
		var mx_z: float = mn_z + obst_cell
		var cx := (mn_x + mx_x) * 0.5
		var cz := (mn_z + mx_z) * 0.5
		var portal_pad := 0.15
		if portal_protect.has(cell_key) \
				or (not portal_aabbs.is_empty() \
					and _point_in_arch_portal_aabbs(portal_aabbs, cx, cz, portal_pad)):
			portal_aabb_skips += 1
			continue
		var carve_top := band_top
		if walk_lo.has(cell_key):
			var wy: float = float(walk_lo[cell_key])
			# Walkable inside the near-ground band (ramp toe / low deck): leave the column alone so
			# terrain+ramp source can meet. Elevated walkable (rampart above walls): carve up to just
			# below it, same idea as the wall-footprint protect path.
			if wy <= ground_y + NEAR_GROUND_CARVE_MAX_HEIGHT + 0.5:
				if wy <= ground_y + MIN_CARVE_H:
					continue
				carve_top = minf(carve_top, wy - RAMPART_CARVE_CLEARANCE)
			elif wy < carve_top:
				carve_top = wy - RAMPART_CARVE_CLEARANCE
		if carve_top - ground_y < MIN_CARVE_H:
			continue
		var outline := PackedVector3Array([
			Vector3(mn_x, 0.0, mn_z),
			Vector3(mx_x, 0.0, mn_z),
			Vector3(mx_x, 0.0, mx_z),
			Vector3(mn_x, 0.0, mx_z),
		])
		var elevation := ground_y - PAD
		var height := carve_top - elevation
		source.add_projected_obstruction(outline, elevation, height, true)
		_record_obstruction_cells(global_cells, Vector2(mn_x, mn_z), Vector2(mx_x, mx_z), ground_y, carve_top)
		added += 1
	if portal_aabb_skips > 0 and OS.get_environment("DIAG_ALWAYS_CARVE") == "1":
		print("    always_carve portal_skips=", portal_aabb_skips, " carved=", added)
	return added

## Drop cells whose centers sit in any portal's lateral opening span (full through-axis).
func _erase_arch_lateral_column_cells(
	cells: Dictionary, portal_aabbs: Array, obst_cell: float, margin: float = 0.35
) -> Dictionary:
	if cells.is_empty() or portal_aabbs.is_empty() or obst_cell <= 0.0:
		return cells
	var out: Dictionary = {}
	for k in cells.keys():
		var ck: Vector2i = k
		var cx := (float(ck.x) + 0.5) * obst_cell
		var cz := (float(ck.y) + 0.5) * obst_cell
		var in_column := false
		for pa_v in portal_aabbs:
			var pa: Dictionary = pa_v
			if bool(pa.get("through_z", true)):
				if cx >= float(pa["min_x"]) - margin and cx <= float(pa["max_x"]) + margin:
					in_column = true
					break
			else:
				if cz >= float(pa["min_z"]) - margin and cz <= float(pa["max_z"]) + margin:
					in_column = true
					break
		if not in_column:
			out[ck] = true
	return out

## Drop cells within chebyshev distance `radius` of any portal_protect cell.
func _erase_cells_adjacent_to_portal_protect(
	cells: Dictionary, portal_protect: Dictionary, radius: int = 1
) -> Dictionary:
	if cells.is_empty() or portal_protect.is_empty() or radius <= 0:
		return cells
	var out: Dictionary = {}
	for k in cells.keys():
		var ck: Vector2i = k
		var near := false
		for dz in range(-radius, radius + 1):
			for dx in range(-radius, radius + 1):
				if portal_protect.has(Vector2i(ck.x + dx, ck.y + dz)):
					near = true
					break
			if near:
				break
		if not near:
			out[ck] = true
	return out

## Drop cells whose centers sit inside any portal AABB (+ margin).
func _erase_portal_aabb_cells(
	cells: Dictionary, portal_aabbs: Array, obst_cell: float, margin: float = 0.0
) -> Dictionary:
	if cells.is_empty() or portal_aabbs.is_empty() or obst_cell <= 0.0:
		return cells
	var out: Dictionary = {}
	for k in cells.keys():
		var ck: Vector2i = k
		var cx := (float(ck.x) + 0.5) * obst_cell
		var cz := (float(ck.y) + 0.5) * obst_cell
		var inside := false
		for pa_v in portal_aabbs:
			var pa: Dictionary = pa_v
			if cx >= float(pa["min_x"]) - margin and cx <= float(pa["max_x"]) + margin \
					and cz >= float(pa["min_z"]) - margin and cz <= float(pa["max_z"]) + margin:
				inside = true
				break
		if not inside:
			out[ck] = true
	return out

## AABB-fill each 4-connected component of occupied cells. Used for arch-pier wings so the
## solid mass under masonry does not bridge the empty throat between piers.
func _solid_fill_connected_component_aabbs(occupied: Dictionary) -> Dictionary:
	var out: Dictionary = {}
	if occupied.is_empty():
		return out
	var remaining: Dictionary = occupied.duplicate()
	var dirs := [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]
	while not remaining.is_empty():
		var seed_k: Vector2i = remaining.keys()[0]
		var stack: Array = [seed_k]
		remaining.erase(seed_k)
		var min_cx := seed_k.x
		var max_cx := seed_k.x
		var min_cz := seed_k.y
		var max_cz := seed_k.y
		while not stack.is_empty():
			var cur: Vector2i = stack.pop_back()
			for d in dirs:
				var n: Vector2i = cur + d
				if not remaining.has(n):
					continue
				remaining.erase(n)
				stack.append(n)
				min_cx = mini(min_cx, n.x)
				max_cx = maxi(max_cx, n.x)
				min_cz = mini(min_cz, n.y)
				max_cz = maxi(max_cz, n.y)
		for cz in range(min_cz, max_cz + 1):
			for cx in range(min_cx, max_cx + 1):
				out[Vector2i(cx, cz)] = true
	return out

## Expand occupied XZ cells by radius_m ( fortifies thin double-wall shell stamps ).
func _dilate_occupied_cells(occupied: Dictionary, obst_cell: float, radius_m: float) -> Dictionary:
	if occupied.is_empty() or radius_m <= 0.0:
		return occupied.duplicate()
	var rad := maxi(1, int(ceil(radius_m / obst_cell)))
	var out: Dictionary = occupied.duplicate()
	for k in occupied.keys():
		var ck: Vector2i = k
		for dz in range(-rad, rad + 1):
			for dx in range(-rad, rad + 1):
				if dx * dx + dz * dz > rad * rad:
					continue
				out[Vector2i(ck.x + dx, ck.y + dz)] = true
	return out

## Opening-aware solid fill: carve the silhouette AABB except empty cells that flood-connect to
## the AABB border (archways, doors, exterior pockets). Enclosed interiors stay carved.
## Count unique XZ obstruction cells covered by triangle footprints.
func _count_projected_xz_cells(faces: PackedVector3Array, obst_cell: float) -> int:
	if faces.is_empty() or obst_cell <= 0.0:
		return 0
	var occupied: Dictionary = {}
	var i := 0
	while i + 2 < faces.size():
		var v0: Vector3 = faces[i]
		var v1: Vector3 = faces[i + 1]
		var v2: Vector3 = faces[i + 2]
		i += 3
		var a2 := Vector2(v0.x, v0.z)
		var b2 := Vector2(v1.x, v1.z)
		var c2 := Vector2(v2.x, v2.z)
		var bbox_min := Vector2(minf(a2.x, minf(b2.x, c2.x)), minf(a2.y, minf(b2.y, c2.y)))
		var bbox_max := Vector2(maxf(a2.x, maxf(b2.x, c2.x)), maxf(a2.y, maxf(b2.y, c2.y)))
		var cell_x0 := floori(bbox_min.x / obst_cell)
		var cell_x1 := floori(bbox_max.x / obst_cell)
		var cell_z0 := floori(bbox_min.y / obst_cell)
		var cell_z1 := floori(bbox_max.y / obst_cell)
		if (cell_x1 - cell_x0 + 1) * (cell_z1 - cell_z0 + 1) > 200000:
			continue
		for cz in range(cell_z0, cell_z1 + 1):
			for cx in range(cell_x0, cell_x1 + 1):
				var cell_min := Vector2(cx * obst_cell, cz * obst_cell)
				var cell_max := cell_min + Vector2(obst_cell, obst_cell)
				if _triangle_aabb_overlap_2d(a2, b2, c2, cell_min, cell_max, 0.0):
					occupied[Vector2i(cx, cz)] = true
	return occupied.size()

## Keep occupied cells within shell_depth_m of exterior-empty space (AABB-border flood).
## Solid keep silhouettes become a wall ring; true wall rings and pillars are unchanged.
func _keep_exterior_shell_cells(
	occupied: Dictionary, obst_cell: float, shell_depth_m: float
) -> Dictionary:
	if occupied.is_empty() or obst_cell <= 0.0 or shell_depth_m <= 0.0:
		return occupied
	var min_cx := 999999
	var max_cx := -999999
	var min_cz := 999999
	var max_cz := -999999
	for k in occupied.keys():
		var ck: Vector2i = k
		min_cx = mini(min_cx, ck.x)
		max_cx = maxi(max_cx, ck.x)
		min_cz = mini(min_cz, ck.y)
		max_cz = maxi(max_cz, ck.y)
	min_cx -= 1
	max_cx += 1
	min_cz -= 1
	max_cz += 1
	var outside: Dictionary = {} # cell -> true
	var q: Array = []
	for cz in range(min_cz, max_cz + 1):
		for cx in [min_cx, max_cx]:
			var bk := Vector2i(cx, cz)
			if not occupied.has(bk):
				outside[bk] = true
				q.append(bk)
	for cx2 in range(min_cx, max_cx + 1):
		for cz2 in [min_cz, max_cz]:
			var bk2 := Vector2i(cx2, cz2)
			if not occupied.has(bk2) and not outside.has(bk2):
				outside[bk2] = true
				q.append(bk2)
	var qi := 0
	while qi < q.size():
		var cur: Vector2i = q[qi]
		qi += 1
		for dz in [-1, 0, 1]:
			for dx in [-1, 0, 1]:
				if dx == 0 and dz == 0:
					continue
				var n := Vector2i(cur.x + dx, cur.y + dz)
				if n.x < min_cx or n.x > max_cx or n.y < min_cz or n.y > max_cz:
					continue
				if occupied.has(n) or outside.has(n):
					continue
				outside[n] = true
				q.append(n)
	var shell_cells := maxi(1, int(ceil(shell_depth_m / obst_cell)))
	var dist: Dictionary = {} # occupied cell -> chebyshev dist from outside
	var dq: Array = []
	for ok in occupied.keys():
		var oc: Vector2i = ok
		var touches_out := false
		for dz2 in [-1, 0, 1]:
			for dx2 in [-1, 0, 1]:
				if dx2 == 0 and dz2 == 0:
					continue
				if outside.has(Vector2i(oc.x + dx2, oc.y + dz2)):
					touches_out = true
					break
			if touches_out:
				break
		if touches_out:
			dist[oc] = 0
			dq.append(oc)
	var di := 0
	while di < dq.size():
		var c2: Vector2i = dq[di]
		di += 1
		var d0: int = int(dist[c2])
		if d0 >= shell_cells:
			continue
		for dz3 in [-1, 0, 1]:
			for dx3 in [-1, 0, 1]:
				if dx3 == 0 and dz3 == 0:
					continue
				var n2 := Vector2i(c2.x + dx3, c2.y + dz3)
				if not occupied.has(n2) or dist.has(n2):
					continue
				dist[n2] = d0 + 1
				dq.append(n2)
	var shell: Dictionary = {}
	for sk in dist.keys():
		if int(dist[sk]) < shell_cells:
			shell[sk] = true
	return shell if not shell.is_empty() else occupied

func _footprint_fill_closed_except_openings(occupied: Dictionary, obst_cell: float) -> Dictionary:
	if occupied.is_empty():
		return {}
	var min_cx := 999999
	var max_cx := -999999
	var min_cz := 999999
	var max_cz := -999999
	for k in occupied.keys():
		var ck: Vector2i = k
		min_cx = mini(min_cx, ck.x)
		max_cx = maxi(max_cx, ck.x)
		min_cz = mini(min_cz, ck.y)
		max_cz = maxi(max_cz, ck.y)
	# One-cell pad so border flood can enter shallow notches/arch mouths on the hull.
	min_cx -= 1
	max_cx += 1
	min_cz -= 1
	max_cz += 1
	var outside: Dictionary = {}
	var q: Array = []
	for cz in range(min_cz, max_cz + 1):
		for cx in [min_cx, max_cx]:
			var bk := Vector2i(cx, cz)
			if not occupied.has(bk):
				outside[bk] = true
				q.append(bk)
	for cx2 in range(min_cx, max_cx + 1):
		for cz2 in [min_cz, max_cz]:
			var bk2 := Vector2i(cx2, cz2)
			if not occupied.has(bk2) and not outside.has(bk2):
				outside[bk2] = true
				q.append(bk2)
	var qi := 0
	while qi < q.size():
		var cur: Vector2i = q[qi]
		qi += 1
		for dz in [-1, 0, 1]:
			for dx in [-1, 0, 1]:
				if dx == 0 and dz == 0:
					continue
				var n := Vector2i(cur.x + dx, cur.y + dz)
				if n.x < min_cx or n.x > max_cx or n.y < min_cz or n.y > max_cz:
					continue
				if occupied.has(n) or outside.has(n):
					continue
				outside[n] = true
				q.append(n)
	var filled: Dictionary = {}
	for cz3 in range(min_cz, max_cz + 1):
		for cx3 in range(min_cx, max_cx + 1):
			var cell := Vector2i(cx3, cz3)
			if outside.has(cell):
				continue
			filled[cell] = true
	return filled

## Fill gaps between inner/outer wall face strips along each row and column when the total span
## is small (default ≤ 5m). Open-ended hollow segments (cc24/cc25) don't enclose a hole, so the
## flood-fill hole pass alone leaves the cavity walkable.
func _footprint_seal_hollow_spans(occupied: Dictionary, obst_cell: float) -> Dictionary:
	var span_env := OS.get_environment("NAV_EXPERIMENT_ALWAYS_CARVE_SEAL_SPAN")
	var max_span := float(span_env) if span_env != "" else 5.0
	return _footprint_seal_hollow_spans_max(occupied, obst_cell, max_span)

func _footprint_seal_hollow_spans_max(
	occupied: Dictionary, obst_cell: float, max_span: float
) -> Dictionary:
	var max_cells := maxi(1, int(ceil(max_span / obst_cell)))
	var sealed: Dictionary = occupied.duplicate()
	var by_row: Dictionary = {} # cz -> {min_cx, max_cx}
	var by_col: Dictionary = {} # cx -> {min_cz, max_cz}
	for k in occupied.keys():
		var ck: Vector2i = k
		if by_row.has(ck.y):
			var row: Dictionary = by_row[ck.y]
			row["min"] = mini(int(row["min"]), ck.x)
			row["max"] = maxi(int(row["max"]), ck.x)
		else:
			by_row[ck.y] = {"min": ck.x, "max": ck.x}
		if by_col.has(ck.x):
			var col: Dictionary = by_col[ck.x]
			col["min"] = mini(int(col["min"]), ck.y)
			col["max"] = maxi(int(col["max"]), ck.y)
		else:
			by_col[ck.x] = {"min": ck.y, "max": ck.y}
	for cz in by_row.keys():
		var row2: Dictionary = by_row[cz]
		var lo: int = int(row2["min"])
		var hi: int = int(row2["max"])
		if hi - lo + 1 <= max_cells:
			for cx in range(lo, hi + 1):
				sealed[Vector2i(cx, int(cz))] = true
	for cx2 in by_col.keys():
		var col2: Dictionary = by_col[cx2]
		var lo2: int = int(col2["min"])
		var hi2: int = int(col2["max"])
		if hi2 - lo2 + 1 <= max_cells:
			for cz2 in range(lo2, hi2 + 1):
				sealed[Vector2i(int(cx2), cz2)] = true
	return sealed

## Fill enclosed holes smaller than max area (default 12 m²). No dilate — avoids fattening into decks.
func _castle_footprint_fill_small_holes(occupied: Dictionary, obst_cell: float) -> Dictionary:
	var max_area_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_HOLE_MAX_AREA")
	var max_area := float(max_area_env) if max_area_env != "" else 12.0
	var max_hole_cells := maxi(1, int(ceil(max_area / (obst_cell * obst_cell))))
	var closed: Dictionary = occupied.duplicate()
	var min_cx := 999999
	var max_cx := -999999
	var min_cz := 999999
	var max_cz := -999999
	for k4 in closed.keys():
		var ck4: Vector2i = k4
		min_cx = mini(min_cx, ck4.x)
		max_cx = maxi(max_cx, ck4.x)
		min_cz = mini(min_cz, ck4.y)
		max_cz = maxi(max_cz, ck4.y)
	min_cx -= 1
	max_cx += 1
	min_cz -= 1
	max_cz += 1
	var outside: Dictionary = {}
	var q: Array = []
	for cz in range(min_cz, max_cz + 1):
		for cx in [min_cx, max_cx]:
			var bk := Vector2i(cx, cz)
			if not closed.has(bk):
				outside[bk] = true
				q.append(bk)
	for cx2 in range(min_cx, max_cx + 1):
		for cz2 in [min_cz, max_cz]:
			var bk2 := Vector2i(cx2, cz2)
			if not closed.has(bk2) and not outside.has(bk2):
				outside[bk2] = true
				q.append(bk2)
	var qi := 0
	while qi < q.size():
		var cur: Vector2i = q[qi]
		qi += 1
		for dz3 in [-1, 0, 1]:
			for dx3 in [-1, 0, 1]:
				if dx3 == 0 and dz3 == 0:
					continue
				var nk := Vector2i(cur.x + dx3, cur.y + dz3)
				if nk.x < min_cx or nk.x > max_cx or nk.y < min_cz or nk.y > max_cz:
					continue
				if closed.has(nk) or outside.has(nk):
					continue
				outside[nk] = true
				q.append(nk)
	var seen: Dictionary = {}
	for cz3 in range(min_cz, max_cz + 1):
		for cx3 in range(min_cx, max_cx + 1):
			var start := Vector2i(cx3, cz3)
			if closed.has(start) or outside.has(start) or seen.has(start):
				continue
			var comp: Array = []
			var qq: Array = [start]
			seen[start] = true
			var qi2 := 0
			while qi2 < qq.size():
				var c2: Vector2i = qq[qi2]
				qi2 += 1
				comp.append(c2)
				for dz4 in [-1, 0, 1]:
					for dx4 in [-1, 0, 1]:
						if dx4 == 0 and dz4 == 0:
							continue
						var n2 := Vector2i(c2.x + dx4, c2.y + dz4)
						if n2.x < min_cx or n2.x > max_cx or n2.y < min_cz or n2.y > max_cz:
							continue
						if closed.has(n2) or outside.has(n2) or seen.has(n2):
							continue
						seen[n2] = true
						qq.append(n2)
			if comp.size() <= max_hole_cells:
				for c3 in comp:
					closed[c3] = true
	return closed

const TREE_TRUNK_FALLBACK_RADIUS := 0.4
const TREE_TRUNK_MAX_RADIUS := 1.0

func _is_tree_object(object_name: String) -> bool:
	return object_name.to_lower().contains("tree")

func _resolve_tree_trunk_aabb(parts: Array, world_xform: Transform3D) -> AABB:
	var part_aabbs: Array = []
	for part in parts:
		var part_aabb := _part_world_aabb(part, world_xform)
		if part_aabb.size.y < 0.05:
			continue
		var xz := maxf(part_aabb.size.x, part_aabb.size.z)
		part_aabbs.append({"aabb": part_aabb, "xz": xz})

	var trunk: AABB
	if part_aabbs.is_empty():
		trunk = _make_trunk_cylinder_aabb(world_xform.origin, TREE_TRUNK_FALLBACK_RADIUS, 2.0)
	elif part_aabbs.size() == 1:
		trunk = part_aabbs[0]["aabb"]
	else:
		var min_xz: float = part_aabbs[0]["xz"]
		var max_xz: float = part_aabbs[0]["xz"]
		for entry in part_aabbs:
			min_xz = minf(min_xz, entry["xz"])
			max_xz = maxf(max_xz, entry["xz"])
		var threshold := maxf(min_xz * 1.75, max_xz * 0.45)
		var has := false
		for entry in part_aabbs:
			if entry["xz"] > threshold:
				continue
			if not has:
				trunk = entry["aabb"]
				has = true
			else:
				trunk = trunk.merge(entry["aabb"])
		if not has:
			trunk = part_aabbs[0]["aabb"]
			var best: float = part_aabbs[0]["xz"]
			for entry in part_aabbs:
				if entry["xz"] < best:
					best = entry["xz"]
					trunk = entry["aabb"]

	return _clamp_aabb_xz_around_origin(trunk, world_xform.origin, TREE_TRUNK_MAX_RADIUS)

func _make_trunk_cylinder_aabb(origin: Vector3, radius: float, height: float) -> AABB:
	return AABB(
		Vector3(origin.x - radius, origin.y, origin.z - radius),
		Vector3(radius * 2.0, height, radius * 2.0)
	)

func _clamp_aabb_xz_around_origin(aabb: AABB, origin: Vector3, max_radius: float) -> AABB:
	var half_x := minf(aabb.size.x * 0.5, max_radius)
	var half_z := minf(aabb.size.z * 0.5, max_radius)
	var y0 := aabb.position.y
	var y1 := aabb.position.y + aabb.size.y
	if aabb.size.y < 0.05:
		y0 = origin.y
		y1 = origin.y + 2.0
	return AABB(
		Vector3(origin.x - half_x, y0, origin.z - half_z),
		Vector3(half_x * 2.0, y1 - y0, half_z * 2.0)
	)

func _part_world_aabb(part: Dictionary, world_xform: Transform3D) -> AABB:
	var local_aabb: AABB = part["mesh"].get_aabb()
	var xform: Transform3D = world_xform * part["local"]
	var aabb := AABB()
	var has := false
	for i in range(8):
		var corner: Vector3 = xform * local_aabb.get_endpoint(i)
		if not has:
			aabb = AABB(corner, Vector3.ZERO)
			has = true
		else:
			aabb = aabb.expand(corner)
	return aabb

func _parts_world_aabb(parts: Array, world_xform: Transform3D) -> AABB:
	var has := false
	var aabb := AABB()
	for part in parts:
		var part_aabb := _part_world_aabb(part, world_xform)
		if not has:
			aabb = part_aabb
			has = true
		else:
			aabb = aabb.merge(part_aabb)
	return aabb

func _mesh_faces_world(mesh: Mesh, xform: Transform3D) -> PackedVector3Array:
	var out := PackedVector3Array()
	for s in range(mesh.get_surface_count()):
		var arrays := mesh.surface_get_arrays(s)
		var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var indices = arrays[Mesh.ARRAY_INDEX]
		if indices == null or indices.is_empty():
			for i in range(0, verts.size() - 2, 3):
				out.append(xform * verts[i])
				out.append(xform * verts[i + 1])
				out.append(xform * verts[i + 2])
		else:
			for i in range(0, indices.size() - 2, 3):
				out.append(xform * verts[indices[i]])
				out.append(xform * verts[indices[i + 1]])
				out.append(xform * verts[indices[i + 2]])
	return out

func _mesh_world_center(mesh: Mesh, xform: Transform3D) -> Vector3:
	var aabb := mesh.get_aabb()
	var mn := xform * aabb.position
	var mx := xform * (aabb.position + aabb.size)
	# basis can flip axes — expand properly
	var box := AABB(mn, Vector3.ZERO)
	for i in range(8):
		box = box.expand(xform * aabb.get_endpoint(i))
	return box.position + box.size * 0.5

func _sample_surface_y(samples: PackedVector3Array, xz: Vector2, fallback: float) -> float:
	if samples.is_empty():
		return fallback
	var best_d := INF
	var best_y := fallback
	for v in samples:
		var d := Vector2(v.x, v.z).distance_squared_to(xz)
		if d < best_d:
			best_d = d
			best_y = v.y
	return best_y

## Bins ground-mesh verts into an XZ grid (avg Y per cell) so terrain-height lookups during the
## below-terrain cull are ~O(1). The cull samples every object triangle vertex, so the naive
## nearest-vertex scan over the whole tile mesh would be far too slow.
func _build_ground_height_grid(ground_faces: PackedVector3Array, cell: float) -> Dictionary:
	var grid := {}
	for v in ground_faces:
		var key := Vector2i(floori(v.x / cell), floori(v.z / cell))
		if grid.has(key):
			var e: Dictionary = grid[key]
			e["s"] = float(e["s"]) + v.y
			e["n"] = int(e["n"]) + 1
		else:
			grid[key] = {"s": v.y, "n": 1}
	return grid

## Terrain height at an XZ position from the grid: the containing cell's average Y, or if empty the
## nearest non-empty cell within a small search radius, falling back to the tile's flat surface_y.
func _sample_ground_height(grid: Dictionary, cell: float, xz: Vector2, fallback: float) -> float:
	if grid.is_empty():
		return fallback
	var cx := floori(xz.x / cell)
	var cz := floori(xz.y / cell)
	var base := Vector2i(cx, cz)
	if grid.has(base):
		var e: Dictionary = grid[base]
		return float(e["s"]) / float(e["n"])
	for r in range(1, 7):
		var best_d := INF
		var best_y := fallback
		var found := false
		for dz in range(-r, r + 1):
			for dx in range(-r, r + 1):
				if absi(dx) != r and absi(dz) != r:
					continue
				var key := Vector2i(cx + dx, cz + dz)
				if not grid.has(key):
					continue
				var center := Vector2((cx + dx + 0.5) * cell, (cz + dz + 0.5) * cell)
				var d := center.distance_squared_to(xz)
				if d < best_d:
					best_d = d
					var e2: Dictionary = grid[key]
					best_y = float(e2["s"]) / float(e2["n"])
					found = true
		if found:
			return best_y
	return fallback

## Preprocess: clips a soup of world-space triangles against the terrain surface, keeping only the
## portion at/above ground (per-vertex terrain height, linearly interpolated across each triangle so
## partially-buried tris are split at the exact ground crossing). Turns "iceberg" objects into just
## their visible above-ground silhouette before the normal walkable/wall slope split runs.
func _cull_faces_below_terrain(faces: PackedVector3Array, grid: Dictionary, cell: float, fallback: float) -> PackedVector3Array:
	var out := PackedVector3Array()
	var i := 0
	while i + 2 < faces.size():
		var vs := [faces[i], faces[i + 1], faces[i + 2]]
		i += 3
		var ds := []
		for v in vs:
			var ty := _sample_ground_height(grid, cell, Vector2(v.x, v.z), fallback)
			ds.append(float(v.y) - ty)
		# Sutherland-Hodgman clip of the triangle against the half-space (y - terrain) >= 0.
		var poly := PackedVector3Array()
		for k in range(3):
			var cur: Vector3 = vs[k]
			var cd: float = ds[k]
			var nxt: Vector3 = vs[(k + 1) % 3]
			var nd: float = ds[(k + 1) % 3]
			if cd >= 0.0:
				poly.append(cur)
			if (cd >= 0.0) != (nd >= 0.0):
				var t := cd / (cd - nd)
				poly.append(cur.lerp(nxt, t))
		if poly.size() >= 3:
			for k in range(1, poly.size() - 1):
				out.append(poly[0])
				out.append(poly[k])
				out.append(poly[k + 1])
	return out

## Whether NAV_EXPERIMENT_BUILDING_FILL applies to this tile_key. Default allowlist is Town_ph00 only
## (kit-built residential houses need the fill/fabricated-roof path; hand-authored towns stay baseline).
func _building_fill_enabled_for_tile(tile_key: String) -> bool:
	if OS.get_environment("NAV_EXPERIMENT_BUILDING_FILL") != "1":
		return false
	var incl := OS.get_environment("NAV_EXPERIMENT_FILL_INCLUDE")
	if incl != "":
		for frag in incl.split(","):
			var f := frag.strip_edges()
			if f != "" and tile_key.findn(f) != -1:
				return true
		return false
	var excl := OS.get_environment("NAV_EXPERIMENT_FILL_EXCLUDE")
	if excl != "":
		for frag in excl.split(","):
			var f2 := frag.strip_edges()
			if f2 != "" and tile_key.findn(f2) != -1:
				return false
		return true
	# Locked default: only Town_ph00 (and its 2x2 cells Town_ph00_00_00 …) get fill.
	return tile_key.findn("Town_ph00") != -1

## Assign each polygon of a combined navmesh to a tile by centroid XZ (half-open [min,max) on X/Z).
## Used so a 2x2 town bake can write four production .res files that still share the continuous
## seam-aware bake / whole-block orphan prune.
func _split_navmesh_to_tile(src: NavigationMesh, tile_min: Vector2, tile_max: Vector2) -> NavigationMesh:
	var out := NavigationMesh.new()
	out.cell_size = src.cell_size
	out.cell_height = src.cell_height
	out.agent_radius = src.agent_radius
	out.agent_height = src.agent_height
	out.agent_max_climb = src.agent_max_climb
	out.agent_max_slope = src.agent_max_slope
	out.region_min_size = src.region_min_size
	out.region_merge_size = src.region_merge_size
	out.edge_max_length = src.edge_max_length
	out.edge_max_error = src.edge_max_error
	out.detail_sample_distance = src.detail_sample_distance
	out.filter_ledge_spans = src.filter_ledge_spans
	out.filter_walkable_low_height_spans = src.filter_walkable_low_height_spans
	var verts := src.get_vertices()
	out.vertices = verts
	var poly_count := src.get_polygon_count()
	var kept := 0
	for p in range(poly_count):
		var poly: PackedInt32Array = src.get_polygon(p)
		if poly.is_empty():
			continue
		var cx := 0.0
		var cz := 0.0
		for vi in poly:
			var v: Vector3 = verts[vi]
			cx += v.x
			cz += v.z
		var inv := 1.0 / float(poly.size())
		cx *= inv
		cz *= inv
		if cx < tile_min.x or cx >= tile_max.x or cz < tile_min.y or cz >= tile_max.y:
			continue
		out.add_polygon(poly)
		kept += 1
	return out

func _skip_obstruction(object_name: String) -> bool:
	var lower := object_name.to_lower()
	if OS.get_environment("NAV_EXPERIMENT_SKIP_DOORS") == "1" and lower.contains("door"):
		return true
	return lower.contains("bush") or lower.contains("grass") or lower.begins_with("fl_") \
		or lower.begins_with("flower") or lower.begins_with("kamysh") or lower.begins_with("pyram") \
		or lower.begins_with("vine") or lower in ["cam_cube", "treeput"] or lower.begins_with("tn2_fl")

## Cc_* / castle_* terrain masters: walls and decks live in the tile GLB, not as kit object placements.
func _is_castle_terrain_tile(name: String) -> bool:
	var n := name.to_lower()
	return n.begins_with("cc_") or n.begins_with("castle_")

## Castle construction kit pieces (walls/towers/keeps). Matches cc08/cc103/cci11/castle*, not cc_grass*.
func _is_castle_kit_object(object_name: String) -> bool:
	var n := object_name.to_lower()
	if n.begins_with("castle"):
		return true
	if n.begins_with("cci") and n.length() > 3 and n.substr(3, 1).is_valid_int():
		return true
	if n.begins_with("cc") and n.length() > 2 and n.substr(2, 1).is_valid_int():
		return true
	return false

static func _always_carve_obst_cell_size() -> float:
	# Slightly finer than clutter obst 0.25 for cleaner building footprints.
	var cell_env := OS.get_environment("NAV_EXPERIMENT_ALWAYS_CARVE_OBST_CELL")
	return float(cell_env) if cell_env != "" else 0.15

## Split faces by agent max slope into walkable (near-horizontal) vs wall (steep) arrays.
func _split_walkable_wall_faces(faces: PackedVector3Array) -> Dictionary:
	var walkable := PackedVector3Array()
	var wall := PackedVector3Array()
	var i := 0
	while i + 2 < faces.size():
		var a: Vector3 = faces[i]
		var b: Vector3 = faces[i + 1]
		var c: Vector3 = faces[i + 2]
		i += 3
		var normal := (b - a).cross(c - a)
		if normal.length_squared() < 0.000001:
			continue
		normal = normal.normalized()
		var slope_deg := rad_to_deg(acos(clampf(absf(normal.y), 0.0, 1.0)))
		if slope_deg <= _agent_max_slope_deg():
			walkable.append(a)
			walkable.append(b)
			walkable.append(c)
		else:
			wall.append(a)
			wall.append(b)
			wall.append(c)
	return {"walkable": walkable, "wall": wall}

## Remove walkable tris buried well below the local deck plane (hollow-wall internals). Keeps ramps
## and anything within NAV_EXPERIMENT_CASTLE_BURIED_BELOW of the neighborhood walk-top max.
## Used for castle TERRAIN meshes (single big surface); kit objects use deck-cluster filter instead.
func _filter_castle_kit_buried_walkable(walkable_faces: PackedVector3Array) -> PackedVector3Array:
	var obst_cell := _obstruction_cell_size()
	var walk_top := _build_walk_top_grid(walkable_faces, obst_cell)
	if walk_top.is_empty():
		return walkable_faces
	var r_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_WALK_TOP_RADIUS")
	# Default 12 cells @ 0.25m ≈ 3m — enough to see rampart deck from inside a thick hollow wall.
	var radius := int(r_env) if r_env != "" else 12
	var below_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_BURIED_BELOW")
	var buried_below := float(below_env) if below_env != "" else 3.0
	var ramp_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_RAMP_MIN_SLOPE")
	var ramp_min_slope := float(ramp_env) if ramp_env != "" else 12.0
	var out := PackedVector3Array()
	var i := 0
	while i + 2 < walkable_faces.size():
		var a: Vector3 = walkable_faces[i]
		var b: Vector3 = walkable_faces[i + 1]
		var c: Vector3 = walkable_faces[i + 2]
		i += 3
		var tri_y := (a.y + b.y + c.y) / 3.0
		var normal := (b - a).cross(c - a)
		if normal.length_squared() < 0.000001:
			continue
		var slope_deg := rad_to_deg(acos(clampf(absf(normal.normalized().y), 0.0, 1.0)))
		if slope_deg >= ramp_min_slope:
			out.append(a)
			out.append(b)
			out.append(c)
			continue
		var cx := floori((a.x + b.x + c.x) / 3.0 / obst_cell)
		var cz := floori((a.z + b.z + c.z) / 3.0 / obst_cell)
		var neigh_max := -INF
		for dz in range(-radius, radius + 1):
			for dx in range(-radius, radius + 1):
				var nk := Vector2i(cx + dx, cz + dz)
				if walk_top.has(nk):
					neigh_max = maxf(neigh_max, float(walk_top[nk]))
		if neigh_max == -INF or tri_y >= neigh_max - buried_below:
			out.append(a)
			out.append(b)
			out.append(c)
	return out

## Keep the main rampart/gate deck Y-cluster (+ merlons above it); drop lower internal-ledge clusters.
func _filter_castle_kit_deck_clusters(walkable_faces: PackedVector3Array) -> PackedVector3Array:
	var gap_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_DECK_CLUSTER_GAP")
	var cluster_gap := float(gap_env) if gap_env != "" else 1.25
	var band_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_DECK_CLUSTER_BAND")
	var keep_band := float(band_env) if band_env != "" else 2.5
	var ramp_env := OS.get_environment("NAV_EXPERIMENT_CASTLE_RAMP_MIN_SLOPE")
	var ramp_min_slope := float(ramp_env) if ramp_env != "" else 12.0

	var tris: Array = [] # {y, area, a,b,c, ramp}
	var i := 0
	while i + 2 < walkable_faces.size():
		var a: Vector3 = walkable_faces[i]
		var b: Vector3 = walkable_faces[i + 1]
		var c: Vector3 = walkable_faces[i + 2]
		i += 3
		var normal := (b - a).cross(c - a)
		var area := normal.length() * 0.5
		if area < 0.000001:
			continue
		var slope_deg := rad_to_deg(acos(clampf(absf(normal.normalized().y), 0.0, 1.0)))
		tris.append({
			"y": (a.y + b.y + c.y) / 3.0,
			"area": area,
			"a": a, "b": b, "c": c,
			"ramp": slope_deg >= ramp_min_slope,
		})
	if tris.is_empty():
		return walkable_faces

	# Sort by Y and cluster where consecutive faces jump by > cluster_gap.
	tris.sort_custom(func(p, q): return float(p["y"]) < float(q["y"]))
	var clusters: Array = [] # {mean_y, area, idxs}
	var cur_idxs: Array = []
	var cur_area := 0.0
	var cur_y_sum := 0.0
	for ti in range(tris.size()):
		var ty: float = tris[ti]["y"]
		if not cur_idxs.is_empty():
			var prev_y: float = tris[int(cur_idxs[cur_idxs.size() - 1])]["y"]
			if ty - prev_y > cluster_gap:
				clusters.append({
					"mean_y": cur_y_sum / float(cur_idxs.size()),
					"area": cur_area,
					"idxs": cur_idxs.duplicate(),
				})
				cur_idxs = []
				cur_area = 0.0
				cur_y_sum = 0.0
		cur_idxs.append(ti)
		cur_area += float(tris[ti]["area"])
		cur_y_sum += ty
	if not cur_idxs.is_empty():
		clusters.append({
			"mean_y": cur_y_sum / float(cur_idxs.size()),
			"area": cur_area,
			"idxs": cur_idxs.duplicate(),
		})

	# Main deck = largest-area cluster in the upper part of the mesh (not a buried shelf). Using the
	# highest cluster instead picked merlon/tower tips and erased the real rampart/gate deck below.
	var max_y := float(tris[tris.size() - 1]["y"])
	var main_area := -1.0
	var main_y := max_y
	for cl in clusters:
		var my: float = cl["mean_y"]
		if my < max_y - 8.0:
			continue
		var ar: float = cl["area"]
		if ar > main_area:
			main_area = ar
			main_y = my
	if main_area < 0.0:
		return walkable_faces

	var out := PackedVector3Array()
	for t in tris:
		if bool(t["ramp"]):
			out.append(t["a"]); out.append(t["b"]); out.append(t["c"])
			continue
		var ty2: float = t["y"]
		# Keep main deck band and anything above it (merlons / higher towers on same piece).
		if ty2 >= main_y - keep_band:
			out.append(t["a"]); out.append(t["b"]); out.append(t["c"])
	return out

## Main castle gate leaf models only (usually placed as a facing pair). Nothing else is a gate seam.
func _is_castle_gate_object(object_name: String) -> bool:
	var n := object_name.to_lower()
	return n == "castle1_gts" or n == "cc103" or n.begins_with("castle1_gts") or n.begins_with("cc103")

## Cluster gate leaves that sit next to each other and carve one through-wall obstruction slab per
## cluster. The slab is thin along the wall-opening width only in the sense of covering the paired
## leaves; it is thickened THROUGH the wall so Recast cannot leave a walkable corridor. Indoor and
## outdoor nav then become separate components across that doorway (reattach later via NavigationLink).
func _carve_castle_gate_seams(
	source: NavigationMeshSourceGeometryData3D,
	gate_leaves: Array,
	global_cells: Dictionary,
	out_aabbs: Array = []
) -> int:
	if gate_leaves.is_empty():
		return 0
	var pair_env := OS.get_environment("NAV_EXPERIMENT_GATE_PAIR_DIST")
	var pair_dist := float(pair_env) if pair_env != "" else 3.5
	var depth_env := OS.get_environment("NAV_EXPERIMENT_GATE_SEAM_DEPTH")
	# Through-wall thickness. 1.5m left a walkable corridor beside/around the leaf AABB on
	# Cc_1_00_03 (ground nav skirted the slab and kept indoor/outdoor as one region).
	var seam_depth := float(depth_env) if depth_env != "" else 3.0
	var width_pad_env := OS.get_environment("NAV_EXPERIMENT_GATE_SEAM_WIDTH_PAD")
	# Extend past the leaf pair along the opening so agents cannot slip around the slab ends.
	var width_pad := float(width_pad_env) if width_pad_env != "" else 1.75
	var height_env := OS.get_environment("NAV_EXPERIMENT_GATE_SEAM_HEIGHT")
	var seam_height := float(height_env) if height_env != "" else 3.5

	var n := gate_leaves.size()
	var parent := PackedInt32Array()
	parent.resize(n)
	for i in range(n):
		parent[i] = i
	for i in range(n):
		var ti: Transform3D = gate_leaves[i]["transform"]
		for j in range(i + 1, n):
			var tj: Transform3D = gate_leaves[j]["transform"]
			if ti.origin.distance_to(tj.origin) <= pair_dist:
				var ri := _find_root(parent, i)
				var rj := _find_root(parent, j)
				if ri != rj:
					parent[ri] = rj

	var clusters: Dictionary = {} # root -> Array[int]
	for i in range(n):
		var r := _find_root(parent, i)
		if not clusters.has(r):
			clusters[r] = []
		(clusters[r] as Array).append(i)

	var carved := 0
	const PAD := 600.0
	for root_i in clusters.keys():
		var idxs: Array = clusters[root_i]
		var union_aabb: AABB
		var have := false
		var ground_y := INF
		for ii in idxs:
			var leaf: Dictionary = gate_leaves[ii]
			var parts: Array = leaf["parts"]
			var xform: Transform3D = leaf["transform"]
			ground_y = minf(ground_y, float(leaf["ground_y"]))
			var laabb := _parts_world_aabb(parts, xform)
			if not have:
				union_aabb = laabb
				have = true
			else:
				union_aabb = union_aabb.merge(laabb)
		if not have or union_aabb.size.x < 0.05 or union_aabb.size.z < 0.05:
			continue
		# Opening width = longer XZ side of the leaf pair; seam depth = through the wall (shorter side),
		# forced to at least seam_depth so a walkable corridor cannot survive the carve.
		var mn := union_aabb.position
		var mx := union_aabb.position + union_aabb.size
		var size_x := mx.x - mn.x
		var size_z := mx.z - mn.z
		if size_x <= size_z:
			# Thin in X → thicken X (through-wall); pad Z (along opening).
			var cx := (mn.x + mx.x) * 0.5
			mn.x = cx - seam_depth * 0.5
			mx.x = cx + seam_depth * 0.5
			mn.z -= width_pad
			mx.z += width_pad
		else:
			var cz := (mn.z + mx.z) * 0.5
			mn.z = cz - seam_depth * 0.5
			mx.z = cz + seam_depth * 0.5
			mn.x -= width_pad
			mx.x += width_pad
		var top_y := maxf(mx.y, ground_y + seam_height)
		var outline := PackedVector3Array([
			Vector3(mn.x, 0.0, mn.z),
			Vector3(mx.x, 0.0, mn.z),
			Vector3(mx.x, 0.0, mx.z),
			Vector3(mn.x, 0.0, mx.z),
		])
		var elevation := ground_y - PAD
		var height := (top_y - ground_y) + PAD * 2.0
		source.add_projected_obstruction(outline, elevation, height, true)
		_record_obstruction_cells(global_cells, Vector2(mn.x, mn.z), Vector2(mx.x, mx.z), ground_y, top_y)
		out_aabbs.append({
			"min_x": mn.x, "max_x": mx.x,
			"min_z": mn.z, "max_z": mx.z,
		})
		if OS.get_environment("DIAG_GATE_SEAM") == "1":
			print("    DIAG_GATE_SEAM leaves=", idxs.size(),
				" aabb_xz=(%.2f..%.2f, %.2f..%.2f)" % [mn.x, mx.x, mn.z, mx.z],
				" ground_y=%.2f top_y=%.2f depth=%.2f pad=%.2f" % [ground_y, top_y, seam_depth, width_pad])
		carved += 1
	return carved

## True if XZ segment a→b intersects any AABB (with pad). Used to keep closed-gate seams sealed
## against post-bake weld stitches that otherwise skirt the carved doorway.
func _segment_hits_xz_aabbs(a: Vector3, b: Vector3, aabbs: Array, pad: float = 0.35) -> bool:
	if aabbs.is_empty():
		return false
	for aabb_v in aabbs:
		var d: Dictionary = aabb_v
		var min_x := float(d["min_x"]) - pad
		var max_x := float(d["max_x"]) + pad
		var min_z := float(d["min_z"]) - pad
		var max_z := float(d["max_z"]) + pad
		# Either endpoint inside, or segment crosses the box.
		if a.x >= min_x and a.x <= max_x and a.z >= min_z and a.z <= max_z:
			return true
		if b.x >= min_x and b.x <= max_x and b.z >= min_z and b.z <= max_z:
			return true
		# Sample midpoints (seams are small; 3 samples suffice).
		for t_i in range(3):
			var t := 0.25 * float(t_i + 1)
			var mx: float = a.x + (b.x - a.x) * t
			var mz: float = a.z + (b.z - a.z) * t
			if mx >= min_x and mx <= max_x and mz >= min_z and mz <= max_z:
				return true
	return false

## True if stitch endpoints mix tile-boundary near-grade verts with courtyard near-grade verts.
func _weld_mixes_exterior_interior_vi(
	vis: Array, exterior_grade: Dictionary, interior_grade: Dictionary
) -> bool:
	if exterior_grade.is_empty() or interior_grade.is_empty():
		return false
	var has_ext := false
	var has_int := false
	for vi_v in vis:
		var vi_i := int(vi_v)
		if exterior_grade.has(vi_i):
			has_ext = true
		if interior_grade.has(vi_i):
			has_int = true
		if has_ext and has_int:
			return true
	return false

## True when a near-grade stitch crosses the bailey envelope (one lip outside, one inside).
## Catches outdoor↔bailey welds on mega-roots where Recast already fused wall-walks into one
## prune component so root lineage tags both sides as "exterior" (Cc_2_hr_occ00).
func _weld_crosses_court_envelope(
	a: Vector3, b: Vector3,
	court_min: Vector2, court_max: Vector2,
	court_y: float, ramp_lo: float,
	out_pad: float = 0.5, in_inset: float = 1.0
) -> bool:
	if a.y - court_y >= ramp_lo and b.y - court_y >= ramp_lo:
		return false
	var a_in := a.x >= court_min.x + in_inset and a.x <= court_max.x - in_inset \
		and a.z >= court_min.y + in_inset and a.z <= court_max.y - in_inset
	var b_in := b.x >= court_min.x + in_inset and b.x <= court_max.x - in_inset \
		and b.z >= court_min.y + in_inset and b.z <= court_max.y - in_inset
	var a_out := a.x < court_min.x - out_pad or a.x > court_max.x + out_pad \
		or a.z < court_min.y - out_pad or a.z > court_max.y + out_pad
	var b_out := b.x < court_min.x - out_pad or b.x > court_max.x + out_pad \
		or b.z < court_min.y - out_pad or b.z > court_max.y + out_pad
	return (a_in and b_out) or (b_in and a_out)

## AABB fallback (door alcoves can lie inside the bailey box — prefer root tagging above).
func _weld_is_exterior_interior_grade(
	a: Vector3, b: Vector3,
	court_min: Vector2, court_max: Vector2,
	court_y: float, ramp_lo: float
) -> bool:
	var a_in := a.x >= court_min.x and a.x <= court_max.x and a.z >= court_min.y and a.z <= court_max.y
	var b_in := b.x >= court_min.x and b.x <= court_max.x and b.z >= court_min.y and b.z <= court_max.y
	var a_ext_grade := (not a_in) and (a.y - court_y < ramp_lo)
	var b_ext_grade := (not b_in) and (b.y - court_y < ramp_lo)
	if a_ext_grade and b_in:
		return true
	if b_ext_grade and a_in:
		return true
	if a.y - court_y < ramp_lo and b.y - court_y < ramp_lo and a_in != b_in:
		return true
	return false

func _build_cell_lookup() -> Dictionary:
	var lookup := {}
	var bytes := FileAccess.get_file_as_bytes(MAP_PATH)
	var next_occ := {}
	var idx := 0
	var offset := 0
	while offset + RECORD_SIZE <= bytes.size():
		var name_bytes := bytes.slice(offset, offset + 20)
		var name_len := 20
		for j in range(20):
			if name_bytes[j] == 0:
				name_len = j
				break
		var name_from_map := name_bytes.slice(0, name_len).get_string_from_ascii().to_lower()
		var variant1: int = bytes[offset + 20]
		var variant2: int = bytes[offset + 21]
		offset += RECORD_SIZE
		var master_name := ""
		if name_from_map.contains("fill_empt"):
			master_name = "fill_empt_00"
		elif not name_from_map.is_empty():
			master_name = ("%s_%d%d" % [name_from_map, variant1, variant2]).replace("patch", "Patch")
		if master_name.is_empty():
			idx += 1
			continue
		if master_name in SKIP_MASTERS:
			idx += 1
			continue
		if not next_occ.has(master_name):
			next_occ[master_name] = 0
		var occ: int = next_occ[master_name]
		next_occ[master_name] = occ + 1
		var gx := GRID_WIDTH - (idx % GRID_WIDTH) - 1
		var gz := idx / GRID_WIDTH
		var key := _tile_group_key(master_name, occ)
		lookup[key] = {"master": master_name, "gx": gx, "gz": gz}
		idx += 1
	return lookup

func _build_objects_by_cell() -> Dictionary:
	var by_cell := {}
	var dir := DirAccess.open(OBJECT_DATA_DIR)
	if dir == null:
		return by_cell
	dir.list_dir_begin()
	while true:
		var name := dir.get_next()
		if name == "":
			break
		if name in [".", ".."]:
			continue
		if not dir.current_is_dir() and name.ends_with(".json"):
			_index_placements_file(by_cell, OBJECT_DATA_DIR + name)
		elif dir.current_is_dir():
			var sub := DirAccess.open(OBJECT_DATA_DIR + name + "/")
			if sub == null:
				continue
			sub.list_dir_begin()
			while true:
				var n2 := sub.get_next()
				if n2 == "":
					break
				if not sub.current_is_dir() and n2.ends_with(".json"):
					_index_placements_file(by_cell, OBJECT_DATA_DIR + name + "/" + n2)
			sub.list_dir_end()
	dir.list_dir_end()
	return by_cell

func _index_placements_file(by_cell: Dictionary, path: String) -> void:
	var json := JSON.new()
	if json.parse(FileAccess.get_file_as_string(path)) != OK or typeof(json.data) != TYPE_ARRAY:
		return
	for item in json.data:
		if typeof(item) != TYPE_DICTIONARY:
			continue
		var rec: Dictionary = item
		var object_name := str(rec.get("name", rec.get("object_name", ""))).to_lower()
		if object_name.is_empty() or object_name == "empty":
			continue
		var pos := SOURCE_BASIS * Vector3(float(rec.get("x", 0.0)), float(rec.get("y", 0.0)), float(rec.get("z", 0.0)))
		if typeof(rec.get("coordinates")) == TYPE_DICTIONARY:
			var cd: Dictionary = rec["coordinates"]
			pos = SOURCE_BASIS * Vector3(float(cd.get("x", 0.0)), float(cd.get("y", 0.0)), float(cd.get("z", 0.0)))
		var rot := Vector3(float(rec.get("pitch", 0.0)), float(rec.get("yaw", 0.0)), float(rec.get("roll", 0.0)))
		if typeof(rec.get("rotation_euler")) == TYPE_DICTIONARY:
			var rd: Dictionary = rec["rotation_euler"]
			rot = Vector3(float(rd.get("pitch", 0.0)), float(rd.get("yaw", 0.0)), float(rd.get("roll", 0.0)))
		var euler := Vector3(rot.x, -rot.y, rot.z)
		var basis_godot := SOURCE_BASIS * Basis.from_euler(euler) * SOURCE_BASIS
		# Nav/ground frame: apply TerrainObjects' inherited Ry(-90°) then the GridMap-alignment
		# shift (see OBJECT_ORIGIN_SHIFT doc) so obstruction bucketing/geometry matches where the
		# object actually renders, not its raw un-rotated JSON position.
		var grid_xform := Transform3D(_objects_basis, OBJECT_ORIGIN_SHIFT) * Transform3D(basis_godot, pos)
		var cell := Vector2i(
			floori((grid_xform.origin.x - TERRAIN_ORIGIN.x) / TILE_SIZE),
			floori((grid_xform.origin.z - TERRAIN_ORIGIN.z) / TILE_SIZE)
		)
		var category := "other"
		var lower := object_name
		if lower.contains("tree") or lower.contains("bush") or lower.contains("grass"):
			category = "plant"
		elif lower.contains("rock") or lower.contains("stone"):
			category = "rock"
		if not by_cell.has(cell):
			by_cell[cell] = []
		by_cell[cell].append({
			"object_name": object_name,
			"category": category,
			"transform": grid_xform
		})

func _tile_group_key(master_name: String, occurrence: int) -> String:
	var s := master_name.to_lower().strip_edges()
	var out := PackedStringArray()
	for c in s:
		var code := c.unicode_at(0)
		var ok := (code >= 97 and code <= 122) or (code >= 48 and code <= 57) or c in ["_", "-", "."]
		out.append(c if ok else "_")
	s = "".join(out)
	if s.is_empty():
		return ""
	return s[0].to_upper() + s.substr(1) + "_%02d" % occurrence

func _load_tile_mesh(master: String) -> Mesh:
	var glb_path := TILES_DIR + master + ".glb"
	if not ResourceLoader.exists(glb_path):
		return null
	var scene: PackedScene = load(glb_path)
	var inst := scene.instantiate()
	var mesh := _find_first_mesh(inst)
	inst.free()
	return mesh.duplicate() as Mesh if mesh else null

func _tile_mesh_basis() -> Basis:
	var basis := Basis.from_euler(Vector3(0.0, deg_to_rad(90.0), 0.0))
	var reflect_z := Basis(Vector3.RIGHT, Vector3.UP, Vector3(0.0, 0.0, -1.0))
	return reflect_z * basis * reflect_z

func _find_first_mesh(node: Node) -> Mesh:
	if node is MeshInstance3D and node.mesh:
		return node.mesh
	for c in node.get_children():
		var m := _find_first_mesh(c)
		if m:
			return m
	return null

func _load_object_parts(object_name: String) -> Array:
	if _mesh_parts_cache.has(object_name):
		return _mesh_parts_cache[object_name]
	var parts: Array = []
	var scene: PackedScene = null
	for ext in ["glb", "gltf"]:
		var model_path: String = MODELS_DIR + object_name + "." + ext
		if ResourceLoader.exists(model_path):
			scene = load(model_path)
			break
	if scene == null:
		_mesh_parts_cache[object_name] = parts
		return parts
	var inst := scene.instantiate() as Node3D
	_collect_mesh_parts(inst, Transform3D.IDENTITY, parts)
	inst.free()
	_mesh_parts_cache[object_name] = parts
	return parts

func _collect_mesh_parts(node: Node, parent_xform: Transform3D, out_parts: Array) -> void:
	var xform := parent_xform
	if node is Node3D:
		xform = parent_xform * node.transform
	if node is MeshInstance3D and node.mesh:
		var sk := false
		var cur := node.get_parent()
		while cur:
			if cur is Skeleton3D:
				sk = true
				break
			cur = cur.get_parent()
		if not sk:
			out_parts.append({"mesh": node.mesh.duplicate(), "local": xform})
	for child in node.get_children():
		_collect_mesh_parts(child, xform, out_parts)
