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
const OBSTRUCTION_CELL := 0.25
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
const AGENT_RADIUS := 0.1
const AGENT_HEIGHT := 1.8
const AGENT_MAX_CLIMB := 1.0
const AGENT_MAX_SLOPE := 70.0

var _mesh_parts_cache: Dictionary = {}
var _objects_by_cell: Dictionary = {} # Vector2i -> Array of placements

var _cells_by_key: Dictionary = {}
var _keys: PackedStringArray = []
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

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var tile_key := ""
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
	if not tile_key.is_empty():
		_keys = PackedStringArray([tile_key])
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
	_pump_jobs()
	# Keep MainLoop alive until async bakes finish (_process).

func _process(_delta: float) -> bool:
	if _finished:
		return false
	if _next_index >= _keys.size() and _inflight == 0:
		_finished = true
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
	var elapsed := (Time.get_ticks_msec() - int(job["t0"])) / 1000.0
	print("  [%d/%d] baked %s in %.2fs polys=%d obst=%d trunks=%d undercroft=%d placements=%d" % [
		int(job["index"]) + 1,
		_keys.size(),
		tile_key,
		elapsed,
		nav.get_polygon_count(),
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
				_out_dir
			)

	if success:
		_ok += 1
	else:
		_fail += 1

	_inflight -= 1
	_pump_jobs()

func _prepare_bake(tile_key: String, master: String, gx: int, gz: int) -> Dictionary:
	var world_pos := TERRAIN_ORIGIN + Vector3(gx * TILE_SIZE, 0.0, gz * TILE_SIZE)
	var tile_mesh := _load_tile_mesh(master)
	if tile_mesh == null:
		push_error("Missing tile mesh for ", master)
		return {}

	var ground_xform := Transform3D(_tile_mesh_basis(), world_pos)
	var ground_faces := _mesh_faces_world(tile_mesh, ground_xform)

	var cell_key := Vector2i(gx, gz)
	var placements: Array = _objects_by_cell.get(cell_key, [])

	var ground_aabb := tile_mesh.get_aabb()
	var surface_y := (ground_xform * ground_aabb.position).y + (ground_xform.basis * ground_aabb.size).y * 0.5

	var source := NavigationMeshSourceGeometryData3D.new()
	source.add_faces(ground_faces, Transform3D.IDENTITY)

	var obst_count := 0
	var trunk_count := 0
	var undercroft_count := 0
	for placement in placements:
		var object_name: String = placement["object_name"]
		if _skip_obstruction(object_name):
			continue
		var parts: Array = _load_object_parts(object_name)
		if parts.is_empty():
			continue
		var world_xform: Transform3D = placement["transform"]
		world_xform.origin.y = surface_y
		if _is_tree_object(object_name):
			var trunk_aabb := _resolve_tree_trunk_aabb(parts, world_xform)
			if trunk_aabb.size.y < 0.05:
				continue
			_add_projected_aabb(source, trunk_aabb)
			trunk_count += 1
			obst_count += 1
		elif _is_undercroft_object(object_name):
			var inflate := TOWER_WALL_INFLATE if _is_tower_object(object_name) else 0.0
			var cells := _add_projected_undercroft_mesh(source, parts, world_xform, inflate)
			if cells > 0:
				undercroft_count += 1
				obst_count += cells
			if _is_tower_object(object_name):
				var outer := _parts_world_aabb(parts, world_xform)
				var inner := _inset_aabb_xz(outer, TOWER_INTERIOR_INSET)
				if inner.size.x > 0.4 and inner.size.z > 0.4 and inner.size.y > 0.05:
					_add_projected_aabb(source, inner)
					obst_count += 1
		else:
			var aabb := _parts_world_aabb(parts, world_xform)
			if aabb.size.y < 0.05:
				continue
			_add_projected_aabb(source, aabb)
			obst_count += 1

	var nav := NavigationMesh.new()
	nav.cell_size = CELL_SIZE
	nav.cell_height = CELL_HEIGHT
	nav.agent_radius = AGENT_RADIUS
	nav.agent_height = AGENT_HEIGHT
	nav.agent_max_climb = AGENT_MAX_CLIMB
	nav.agent_max_slope = AGENT_MAX_SLOPE
	nav.region_min_size = 4.0
	nav.edge_max_length = 12.0
	nav.edge_max_error = 1.3
	nav.detail_sample_distance = 6.0
	nav.filter_ledge_spans = false
	nav.filter_walkable_low_height_spans = true

	return {
		"nav": nav,
		"source": source,
		"placements": placements,
		"obst_count": obst_count,
		"trunk_count": trunk_count,
		"undercroft_count": undercroft_count,
	}

func _export_one_glb(
	tile_key: String,
	master: String,
	gx: int,
	gz: int,
	nav: NavigationMesh,
	placements: Array,
	out_dir: String
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

	var height_samples: PackedVector3Array = nav.get_vertices()
	for placement in placements:
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

	var nav_root := Node3D.new()
	nav_root.name = "NavMesh"
	nav_root.position = Vector3(SIDE_GAP * 0.5, 0.0, 0.0)
	root.add_child(nav_root)
	NavGlbMerge.append_nav(merge["nav"], nav, frame_center)
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

func _add_projected_aabb(source: NavigationMeshSourceGeometryData3D, aabb: AABB) -> void:
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

# Only tris that reach into this band above the placement origin are used for undercroft carve.
# Arch lintels / upper tower floors sit above it and must not seal the opening in XZ.
const UNDERCROFT_CARVE_MAX_HEIGHT := 2.0
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

## Near-ground mesh tris → clustered projected cells (openings stay walkable).
## inflate > 0 expands each tri's XZ bounds (thickens thin wall planes both inward and out).
func _add_projected_undercroft_mesh(
	source: NavigationMeshSourceGeometryData3D,
	parts: Array,
	world_xform: Transform3D,
	inflate: float = 0.0
) -> int:
	var max_y := world_xform.origin.y + UNDERCROFT_CARVE_MAX_HEIGHT
	var cells: Dictionary = {} # Vector2i -> {min: Vector3, max: Vector3}
	for part in parts:
		var mesh: Mesh = part["mesh"]
		var xform: Transform3D = world_xform * part["local"]
		var faces := _mesh_faces_world(mesh, xform)
		var i := 0
		while i + 2 < faces.size():
			var v0: Vector3 = faces[i]
			var v1: Vector3 = faces[i + 1]
			var v2: Vector3 = faces[i + 2]
			i += 3
			var tri_min_y := minf(v0.y, minf(v1.y, v2.y))
			if tri_min_y > max_y:
				continue
			var tri_min := Vector3(minf(v0.x, minf(v1.x, v2.x)), tri_min_y, minf(v0.z, minf(v1.z, v2.z)))
			var tri_max := Vector3(
				maxf(v0.x, maxf(v1.x, v2.x)),
				maxf(v0.y, maxf(v1.y, v2.y)),
				maxf(v0.z, maxf(v1.z, v2.z))
			)
			if inflate > 0.0:
				tri_min.x -= inflate
				tri_min.z -= inflate
				tri_max.x += inflate
				tri_max.z += inflate
			var cx := (tri_min.x + tri_max.x) * 0.5
			var cz := (tri_min.z + tri_max.z) * 0.5
			var key := Vector2i(floori(cx / OBSTRUCTION_CELL), floori(cz / OBSTRUCTION_CELL))
			if cells.has(key):
				var existing: Dictionary = cells[key]
				existing["min"] = Vector3(
					minf(existing["min"].x, tri_min.x),
					minf(existing["min"].y, tri_min.y),
					minf(existing["min"].z, tri_min.z)
				)
				existing["max"] = Vector3(
					maxf(existing["max"].x, tri_max.x),
					maxf(existing["max"].y, tri_max.y),
					maxf(existing["max"].z, tri_max.z)
				)
			else:
				cells[key] = {"min": tri_min, "max": tri_max}

	const PAD := 600.0
	var added := 0
	for key in cells:
		var mn: Vector3 = cells[key]["min"]
		var mx: Vector3 = cells[key]["max"]
		var raw_h := mx.y - mn.y
		if raw_h < 0.05:
			continue
		var outline := PackedVector3Array([
			Vector3(mn.x, 0.0, mn.z),
			Vector3(mx.x, 0.0, mn.z),
			Vector3(mx.x, 0.0, mx.z),
			Vector3(mn.x, 0.0, mx.z),
		])
		source.add_projected_obstruction(outline, mn.y - PAD, raw_h + PAD * 2.0, true)
		added += 1
	return added

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

func _skip_obstruction(object_name: String) -> bool:
	var lower := object_name.to_lower()
	return lower.contains("bush") or lower.contains("grass") or lower.begins_with("fl_") \
		or lower.begins_with("flower") or lower.begins_with("kamysh") or lower.begins_with("pyram") \
		or lower.begins_with("vine") or lower in ["cam_cube", "treeput"] or lower.begins_with("tn2_fl")

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
