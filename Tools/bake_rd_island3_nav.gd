extends SceneTree
## Hand nav for rd_island3 only (indoor cluster 131).
## 1) Flat grassy faces in hardcoded local-Y bands + under-higher-deck carve.
## 2) Nearby ObjectData props punch holes via the same ADDFACES-style path as outdoor
##    terrain bake (walkable object decks → add_faces; walls/trees → projected obstruction).
## Other indoor/outdoor bake scripts are untouched — this is rd_island3-specific.
##
## Usage:
##   godot --path . --headless -s Tools/bake_rd_island3_nav.gd

const NavGlbMerge = preload("res://Tools/nav_glb_merge.gd")

const MODEL_PATH := "res://Godot/Models/rd_island3.glb"
const MODELS_DIR := "res://Godot/Models/"
const OBJECT_DATA_DIR := "res://Godot/Terrain/ObjectDataJson/"
const NAV_RES_PATH := "res://Godot/Terrain/GeneratedIndoorNavMeshes/cluster_131.res"
const CLUSTER_ID := 131
const ISLAND_NAME := "rd_island3"

## sst_99.json placement → Godot (SOURCE_BASIS: y/z flip). Zero rotation.
const PLACEMENT_GODOT := Vector3(-3900.0625, -1100.0004, 3898.4983)

const SOURCE_BASIS := Basis(Vector3.RIGHT, Vector3.DOWN, Vector3.FORWARD)
const OBJECT_ORIGIN_SHIFT := Vector3(4000.0, 0.0, 4000.0)
## How far past the island walkable AABB to pull ObjectData props.
const OBJECT_PAD_M := 40.0
const OBST_CELL := 0.25
const RAMPART_CARVE_CLEARANCE := 0.35
const TREE_TRUNK_FALLBACK_RADIUS := 0.4
const TREE_TRUNK_MAX_RADIUS := 1.0

const MAX_SLOPE_DEG := 22.0
const BAND_PAD := 0.5
## Higher plateaus carve walkable out under their XZ footprint (cliff undersides).
## Keep dilate small so edge walkways beside cliffs are not eaten.
const UNDER_CARVE_CELL := 2.0
const UNDER_CARVE_DILATE := 1
const UNDER_CARVE_GAP_M := 4.0
## Hand bands in mesh-local Y (from flat-face clustering).
const BANDS := [
	{"name": "base", "y0": 20.1, "y1": 23.9, "color": Color(0.95, 0.25, 0.2)},
	{"name": "mid_ledge", "y0": 26.9, "y1": 27.6, "color": Color(0.95, 0.75, 0.15)},
	{"name": "mid_plateau", "y0": 32.1, "y1": 35.5, "color": Color(0.2, 0.85, 0.35)},
	{"name": "high_rim", "y0": 47.9, "y1": 50.0, "color": Color(0.25, 0.55, 0.98)},
]

const NAV_CELL_SIZE := 0.1
const NAV_CELL_HEIGHT := 0.1
const NAV_AGENT_RADIUS := 0.25
const NAV_AGENT_HEIGHT := 1.8
const NAV_AGENT_MAX_CLIMB := 0.3
const NAV_AGENT_MAX_SLOPE := 35.0
const NAV_REGION_MIN := 14.0
const NAV_REGION_MERGE := 20.0

var _objects_basis := Basis.from_euler(Vector3(0.0, deg_to_rad(-90.0), 0.0))
var _mesh_parts_cache: Dictionary = {}


func _initialize() -> void:
	var dt := Time.get_datetime_dict_from_system()
	var stamp := "%d-%d-%d_%02d-%02d-%02d" % [
		int(dt["year"]), int(dt["month"]), int(dt["day"]),
		int(dt["hour"]), int(dt["minute"]), int(dt["second"]),
	]
	var out_dir := "D:/1/%s_rd_island3_handnav" % stamp
	DirAccess.make_dir_recursive_absolute(out_dir)
	DirAccess.make_dir_recursive_absolute(NAV_RES_PATH.get_base_dir())

	print("rd_island3 hand-nav → %s" % out_dir)

	if not ResourceLoader.exists(MODEL_PATH):
		push_error("Missing model %s" % MODEL_PATH)
		quit(1)
		return

	var scene: PackedScene = load(MODEL_PATH)
	var inst := scene.instantiate() as Node3D
	if inst == null:
		push_error("Failed to instantiate %s" % MODEL_PATH)
		quit(1)
		return

	var island_root := Node3D.new()
	island_root.name = "IslandMesh"
	island_root.add_child(inst.duplicate())
	if not _write_glb(island_root, out_dir + "/island_mesh.glb"):
		quit(1)
		return
	island_root.queue_free()

	var parts: Array = []
	_collect_mesh_parts(inst, Transform3D.IDENTITY, parts)
	inst.queue_free()

	var band_faces: Array = []
	var band_counts: PackedInt32Array = PackedInt32Array()
	band_faces.resize(BANDS.size())
	band_counts.resize(BANDS.size())
	for bi in range(BANDS.size()):
		band_faces[bi] = PackedVector3Array()
		band_counts[bi] = 0

	var all_local := PackedVector3Array()
	var total_up := 0
	var total_flat := 0

	for part in parts:
		var mesh: Mesh = part["mesh"]
		var xform: Transform3D = part["xform"]
		var faces := _mesh_faces(mesh, xform)
		var i := 0
		while i + 2 < faces.size():
			var a: Vector3 = faces[i]
			var b: Vector3 = faces[i + 1]
			var c: Vector3 = faces[i + 2]
			i += 3
			var normal := (b - a).cross(c - a)
			var nlen2 := normal.length_squared()
			if nlen2 < 0.000001:
				continue
			normal /= sqrt(nlen2)
			if normal.y < 0.0:
				normal = -normal
			if normal.y <= 0.0:
				continue
			total_up += 1
			var slope_deg := rad_to_deg(acos(clampf(normal.y, 0.0, 1.0)))
			if slope_deg > MAX_SLOPE_DEG:
				continue
			total_flat += 1
			var cy := (a.y + b.y + c.y) / 3.0
			var bi := _band_index(cy)
			if bi < 0:
				continue
			var bf: PackedVector3Array = band_faces[bi]
			bf.append(a)
			bf.append(b)
			bf.append(c)
			band_faces[bi] = bf
			band_counts[bi] = int(band_counts[bi]) + 1

	print(
		"upward=%d flat<=%.0f=%d banded=%d" % [
			total_up, MAX_SLOPE_DEG, total_flat,
			int(band_counts[0]) + int(band_counts[1]) + int(band_counts[2]) + int(band_counts[3]),
		]
	)
	for bi in range(BANDS.size()):
		var bdef: Dictionary = BANDS[bi]
		print(
			"  band %s Y[%.1f,%.1f] tris=%d (pre-carve)" % [
				bdef["name"], float(bdef["y0"]) - BAND_PAD, float(bdef["y1"]) + BAND_PAD, band_counts[bi],
			]
		)

	var carved := _carve_under_higher_bands(band_faces)
	band_faces = carved["bands"]
	var carved_n: int = int(carved["dropped"])
	all_local = PackedVector3Array()
	for bi in range(BANDS.size()):
		var bf3: PackedVector3Array = band_faces[bi]
		band_counts[bi] = int(bf3.size() / 3)
		all_local.append_array(bf3)
		var bdef3: Dictionary = BANDS[bi]
		print("  band %s tris=%d (after under-carve)" % [bdef3["name"], band_counts[bi]])
	print(
		"under-carve: dropped %d tris under higher decks (cell=%.1f dilate=%d gap=%.1fm)" % [
			carved_n, UNDER_CARVE_CELL, UNDER_CARVE_DILATE, UNDER_CARVE_GAP_M,
		]
	)

	if all_local.is_empty():
		push_error("No walkable faces selected")
		quit(1)
		return

	var walk_root := Node3D.new()
	walk_root.name = "WalkableBands"
	for bi in range(BANDS.size()):
		var bf2: PackedVector3Array = band_faces[bi]
		if bf2.is_empty():
			continue
		var bdef2: Dictionary = BANDS[bi]
		var mi := MeshInstance3D.new()
		mi.name = str(bdef2["name"])
		mi.mesh = _faces_to_mesh(bf2)
		mi.material_override = _band_material(bdef2["color"])
		walk_root.add_child(mi)
	if not _write_glb(walk_root, out_dir + "/walkable_bands.glb"):
		quit(1)
		return
	walk_root.queue_free()

	var world_faces := PackedVector3Array()
	world_faces.resize(all_local.size())
	for i in range(all_local.size()):
		world_faces[i] = all_local[i] + PLACEMENT_GODOT

	var aabb_world := _faces_aabb(world_faces)
	var center_godot := aabb_world.get_center()
	var center_nav := _godot_to_nav(center_godot)

	var nav_faces := PackedVector3Array()
	nav_faces.resize(world_faces.size())
	for i in range(world_faces.size()):
		nav_faces[i] = _godot_to_nav(world_faces[i])

	var source := NavigationMeshSourceGeometryData3D.new()
	source.add_faces(nav_faces, Transform3D.IDENTITY)
	var carve_stats: Dictionary = _apply_object_carves(source, nav_faces, aabb_world)
	print(
		"object carve: placed=%d carved=%d trees=%d walls=%d walk_decks=%d skip=%d missing=%d" % [
			int(carve_stats["placed"]), int(carve_stats["carved"]), int(carve_stats["trees"]),
			int(carve_stats["walls"]), int(carve_stats["walk_decks"]),
			int(carve_stats["skipped"]), int(carve_stats["missing"]),
		]
	)

	var nav := NavigationMesh.new()
	nav.cell_size = NAV_CELL_SIZE
	nav.cell_height = NAV_CELL_HEIGHT
	nav.agent_radius = NAV_AGENT_RADIUS
	nav.agent_height = NAV_AGENT_HEIGHT
	nav.agent_max_climb = NAV_AGENT_MAX_CLIMB
	nav.agent_max_slope = NAV_AGENT_MAX_SLOPE
	nav.region_min_size = NAV_REGION_MIN
	nav.region_merge_size = NAV_REGION_MERGE
	nav.edge_max_length = 12.0
	nav.edge_max_error = 1.3
	nav.detail_sample_distance = 6.0
	nav.filter_ledge_spans = false
	nav.filter_walkable_low_height_spans = true
	NavigationServer3D.bake_from_source_geometry_data(nav, source)
	if nav.get_polygon_count() == 0:
		push_error("Recast bake produced empty NavigationMesh")
		quit(1)
		return

	var err := ResourceSaver.save(nav, NAV_RES_PATH)
	if err != OK:
		push_error("Failed to save %s err=%d" % [NAV_RES_PATH, err])
		quit(1)
		return

	var aabb_nav := _faces_aabb(nav_faces)
	var meta := {
		"id": CLUSTER_ID,
		"path": NAV_RES_PATH,
		"pre_lb": true,
		"outdoor_island": true,
		"method": "rd_island3_hand_bands_under_carve_object_punch",
		"max_slope_deg": NAV_AGENT_MAX_SLOPE,
		"select_slope_deg": MAX_SLOPE_DEG,
		"under_carve": {
			"cell": UNDER_CARVE_CELL,
			"dilate": UNDER_CARVE_DILATE,
			"gap_m": UNDER_CARVE_GAP_M,
			"dropped": carved_n,
		},
		"object_carve": carve_stats,
		"region_min_size": NAV_REGION_MIN,
		"region_merge_size": NAV_REGION_MERGE,
		"radius": 151.8,
		"center_source": {"x": center_godot.x, "y": center_godot.y, "z": center_godot.z},
		"center_nav": {"x": center_nav.x, "y": center_nav.y, "z": center_nav.z},
		"placement_godot": {
			"x": PLACEMENT_GODOT.x, "y": PLACEMENT_GODOT.y, "z": PLACEMENT_GODOT.z,
		},
		"bands": [
			{"name": "base", "y0": 20.1, "y1": 23.9},
			{"name": "mid_ledge", "y0": 26.9, "y1": 27.6},
			{"name": "mid_plateau", "y0": 32.1, "y1": 35.5},
			{"name": "high_rim", "y0": 47.9, "y1": 50.0},
		],
		"band_tris": {
			"base": int(band_counts[0]),
			"mid_ledge": int(band_counts[1]),
			"mid_plateau": int(band_counts[2]),
			"high_rim": int(band_counts[3]),
		},
		"aabb_nav": {
			"min": {"x": aabb_nav.position.x, "y": aabb_nav.position.y, "z": aabb_nav.position.z},
			"max": {
				"x": aabb_nav.position.x + aabb_nav.size.x,
				"y": aabb_nav.position.y + aabb_nav.size.y,
				"z": aabb_nav.position.z + aabb_nav.size.z,
			},
		},
		"polygon_count": nav.get_polygon_count(),
		"walkable_tris": int(all_local.size() / 3),
	}
	var meta_path := NAV_RES_PATH.get_basename() + ".nav.json"
	var mf := FileAccess.open(meta_path, FileAccess.WRITE)
	mf.store_string(JSON.stringify(meta, "\t"))
	mf.close()
	print("Wrote %s polys=%d tris=%d" % [NAV_RES_PATH, nav.get_polygon_count(), int(all_local.size() / 3)])
	_merge_indoor_nav_index(meta)

	var merge: Dictionary = NavGlbMerge.new_tile_state()
	var nav_to_godot := _objects_basis.inverse()
	NavGlbMerge.append_nav_xform(merge["nav"], nav, OBJECT_ORIGIN_SHIFT, nav_to_godot)
	var entry: Dictionary = merge["nav"]
	var verts: PackedVector3Array = entry["vertices"]
	for i in range(verts.size()):
		verts[i] = verts[i] - PLACEMENT_GODOT
	entry["vertices"] = verts

	var nav_root_node := Node3D.new()
	nav_root_node.name = "NavBaked"
	var terrain_dummy := Node3D.new()
	terrain_dummy.name = "Terrain"
	nav_root_node.add_child(terrain_dummy)
	var nav_child := Node3D.new()
	nav_child.name = "NavMesh"
	nav_root_node.add_child(nav_child)
	NavGlbMerge.finalize_tile_meshes(terrain_dummy, nav_child, merge)
	if not _write_glb(nav_root_node, out_dir + "/nav_baked.glb"):
		quit(1)
		return
	nav_root_node.queue_free()

	var summary := FileAccess.open(out_dir + "/summary.txt", FileAccess.WRITE)
	summary.store_line("rd_island3 hand-nav (bands + under-carve + object punch)")
	summary.store_line("placement_godot=%s" % PLACEMENT_GODOT)
	summary.store_line("select_slope<=%.1f pad=%.1f (normals flipped for winding)" % [MAX_SLOPE_DEG, BAND_PAD])
	summary.store_line(
		"under_carve cell=%.1f dilate=%d gap=%.1fm dropped=%d" % [
			UNDER_CARVE_CELL, UNDER_CARVE_DILATE, UNDER_CARVE_GAP_M, carved_n,
		]
	)
	summary.store_line(
		"object_carve placed=%d carved=%d trees=%d walls=%d walk_decks=%d" % [
			int(carve_stats["placed"]), int(carve_stats["carved"]), int(carve_stats["trees"]),
			int(carve_stats["walls"]), int(carve_stats["walk_decks"]),
		]
	)
	summary.store_line("walkable_tris=%d nav_polys=%d" % [int(all_local.size() / 3), nav.get_polygon_count()])
	for bi in range(BANDS.size()):
		var bd: Dictionary = BANDS[bi]
		summary.store_line(
			"  %s tris=%d Y[%.1f,%.1f]" % [
				bd["name"], band_counts[bi], float(bd["y0"]) - BAND_PAD, float(bd["y1"]) + BAND_PAD,
			]
		)
	summary.store_line("nav_res=%s" % NAV_RES_PATH)
	summary.close()

	print("Done → %s" % out_dir)
	quit(0)


func _band_index(local_y: float) -> int:
	for bi in range(BANDS.size()):
		var b: Dictionary = BANDS[bi]
		var y0: float = float(b["y0"]) - BAND_PAD
		var y1: float = float(b["y1"]) + BAND_PAD
		if local_y >= y0 and local_y <= y1:
			return bi
	return -1


func _carve_under_higher_bands(bands_in: Array) -> Dictionary:
	var inv_cell := 1.0 / UNDER_CARVE_CELL
	var band_lids: Array = []
	for bi in range(bands_in.size()):
		var lids: Dictionary = {}
		var faces: PackedVector3Array = bands_in[bi]
		var ti := 0
		while ti + 2 < faces.size():
			var cent := (faces[ti] + faces[ti + 1] + faces[ti + 2]) / 3.0
			var key := Vector2i(int(floor(cent.x * inv_cell)), int(floor(cent.z * inv_cell)))
			if lids.has(key):
				lids[key] = maxf(float(lids[key]), cent.y)
			else:
				lids[key] = cent.y
			ti += 3
		band_lids.append(lids)

	var out_bands: Array = []
	var dropped := 0
	for bi in range(bands_in.size()):
		var higher: Dictionary = {}
		for hj in range(bi + 1, bands_in.size()):
			var src: Dictionary = band_lids[hj]
			for key in src.keys():
				var h: float = float(src[key])
				if higher.has(key):
					higher[key] = maxf(float(higher[key]), h)
				else:
					higher[key] = h
		var dil: Dictionary = higher.duplicate()
		for _iter in range(UNDER_CARVE_DILATE):
			var nxt: Dictionary = dil.duplicate()
			for key in dil.keys():
				var k: Vector2i = key
				var h2: float = float(dil[k])
				for dx in range(-1, 2):
					for dz in range(-1, 2):
						var nk := Vector2i(k.x + dx, k.y + dz)
						if nxt.has(nk):
							nxt[nk] = maxf(float(nxt[nk]), h2)
						else:
							nxt[nk] = h2
			dil = nxt

		var faces2: PackedVector3Array = bands_in[bi]
		var kept := PackedVector3Array()
		var ti2 := 0
		while ti2 + 2 < faces2.size():
			var a: Vector3 = faces2[ti2]
			var b: Vector3 = faces2[ti2 + 1]
			var c: Vector3 = faces2[ti2 + 2]
			var cent2 := (a + b + c) / 3.0
			var key2 := Vector2i(int(floor(cent2.x * inv_cell)), int(floor(cent2.z * inv_cell)))
			if dil.has(key2) and cent2.y < float(dil[key2]) - UNDER_CARVE_GAP_M:
				dropped += 1
			else:
				kept.append(a)
				kept.append(b)
				kept.append(c)
			ti2 += 3
		out_bands.append(kept)
	return {"bands": out_bands, "dropped": dropped}


## Outdoor ADDFACES=2-style punch: island walkable is terrain; props carve like outdoor objects.
func _apply_object_carves(
	source: NavigationMeshSourceGeometryData3D,
	island_nav_faces: PackedVector3Array,
	aabb_world: AABB
) -> Dictionary:
	var stats := {
		"placed": 0, "carved": 0, "trees": 0, "walls": 0, "walk_decks": 0,
		"skipped": 0, "missing": 0, "pad_m": OBJECT_PAD_M,
	}
	var center := aabb_world.get_center()
	var half := aabb_world.size * 0.5
	var radius := maxf(half.x, maxf(half.y, half.z)) + OBJECT_PAD_M
	var placements := _load_nearby_placements(center, radius)
	stats["placed"] = placements.size()
	var walk_protect := _build_walk_protect_grid(island_nav_faces)
	for pl in placements:
		var object_name: String = pl["object_name"]
		if _skip_obstruction(object_name) or object_name == ISLAND_NAME or object_name.begins_with("rd_island"):
			stats["skipped"] = int(stats["skipped"]) + 1
			continue
		var parts: Array = _load_object_parts(object_name)
		if parts.is_empty():
			stats["missing"] = int(stats["missing"]) + 1
			continue
		var world_xform: Transform3D = pl["transform"]
		if _is_tree_object(object_name):
			var trunk := _resolve_tree_trunk_aabb(parts, world_xform)
			if trunk.size.y < 0.05:
				stats["skipped"] = int(stats["skipped"]) + 1
				continue
			_add_projected_aabb(source, trunk)
			stats["trees"] = int(stats["trees"]) + 1
			stats["carved"] = int(stats["carved"]) + 1
			continue
		var all_faces := PackedVector3Array()
		for part in parts:
			var mesh: Mesh = part["mesh"]
			var xform: Transform3D = world_xform * part["local"]
			all_faces.append_array(_mesh_faces(mesh, xform))
		if all_faces.is_empty():
			stats["skipped"] = int(stats["skipped"]) + 1
			continue
		var split := _split_walkable_wall_faces(all_faces)
		var walkable_faces: PackedVector3Array = split["walkable"]
		var wall_faces: PackedVector3Array = split["wall"]
		if walkable_faces.size() > 0:
			source.add_faces(walkable_faces, Transform3D.IDENTITY)
			_merge_walk_protect(walk_protect, walkable_faces)
			stats["walk_decks"] = int(stats["walk_decks"]) + 1
		if wall_faces.size() > 0:
			var added := _add_projected_wall_footprint(source, wall_faces, walk_protect)
			if added > 0:
				stats["walls"] = int(stats["walls"]) + 1
				stats["carved"] = int(stats["carved"]) + 1
			else:
				stats["skipped"] = int(stats["skipped"]) + 1
		elif walkable_faces.is_empty():
			stats["skipped"] = int(stats["skipped"]) + 1
	return stats


func _load_nearby_placements(center_godot: Vector3, radius: float) -> Array:
	var out: Array = []
	var seen: Dictionary = {}
	var dir := DirAccess.open(OBJECT_DATA_DIR)
	if dir == null:
		return out
	dir.list_dir_begin()
	var fname := dir.get_next()
	while fname != "":
		if not dir.current_is_dir() and fname.ends_with(".json"):
			_collect_placements_file(OBJECT_DATA_DIR + fname, center_godot, radius, seen, out)
		fname = dir.get_next()
	dir.list_dir_end()
	return out


func _collect_placements_file(
	path: String, center_godot: Vector3, radius: float, seen: Dictionary, out: Array
) -> void:
	var json := JSON.new()
	if json.parse(FileAccess.get_file_as_string(path)) != OK or typeof(json.data) != TYPE_ARRAY:
		return
	var r2 := radius * radius
	for item in json.data:
		if typeof(item) != TYPE_DICTIONARY:
			continue
		var rec: Dictionary = item
		var object_name := str(rec.get("name", rec.get("object_name", ""))).strip_edges().to_lower()
		if object_name.is_empty() or object_name == "empty":
			continue
		var sx := float(rec.get("x", 0.0))
		var sy := float(rec.get("y", 0.0))
		var sz := float(rec.get("z", 0.0))
		if typeof(rec.get("coordinates")) == TYPE_DICTIONARY:
			var cd: Dictionary = rec["coordinates"]
			sx = float(cd.get("x", 0.0))
			sy = float(cd.get("y", 0.0))
			sz = float(cd.get("z", 0.0))
		var pitch := float(rec.get("pitch", 0.0))
		var yaw := float(rec.get("yaw", 0.0))
		var roll := float(rec.get("roll", 0.0))
		if typeof(rec.get("rotation_euler")) == TYPE_DICTIONARY:
			var rd: Dictionary = rec["rotation_euler"]
			pitch = float(rd.get("pitch", 0.0))
			yaw = float(rd.get("yaw", 0.0))
			roll = float(rd.get("roll", 0.0))
		var pos_godot := Vector3(sx, -sy, -sz)
		if pos_godot.distance_squared_to(center_godot) > r2:
			continue
		var key := "%s|%.3f|%.3f|%.3f|%.4f|%.4f|%.4f" % [
			object_name, pos_godot.x, pos_godot.y, pos_godot.z, pitch, yaw, roll,
		]
		if seen.has(key):
			continue
		seen[key] = true
		var euler := Vector3(pitch, -yaw, roll)
		var basis_godot := SOURCE_BASIS * Basis.from_euler(euler) * SOURCE_BASIS
		# Nav frame: same as bake_and_export_single_nav.gd object indexing.
		var nav_xform := Transform3D(_objects_basis, OBJECT_ORIGIN_SHIFT) * Transform3D(basis_godot, pos_godot)
		out.append({"object_name": object_name, "transform": nav_xform})


func _skip_obstruction(object_name: String) -> bool:
	var lower := object_name.to_lower()
	return lower.contains("bush") or lower.contains("grass") or lower.begins_with("fl_") \
		or lower.begins_with("flower") or lower.begins_with("kamysh") or lower.begins_with("pyram") \
		or lower.begins_with("vine") or lower in ["cam_cube", "treeput"] or lower.begins_with("tn2_fl")


func _is_tree_object(object_name: String) -> bool:
	return object_name.to_lower().contains("tree")


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
	var raw: Array = []
	_collect_mesh_parts(inst, Transform3D.IDENTITY, raw)
	inst.free()
	for part in raw:
		parts.append({"mesh": part["mesh"], "local": part["xform"]})
	_mesh_parts_cache[object_name] = parts
	return parts


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
		if slope_deg <= NAV_AGENT_MAX_SLOPE:
			walkable.append(a)
			walkable.append(b)
			walkable.append(c)
		else:
			wall.append(a)
			wall.append(b)
			wall.append(c)
	return {"walkable": walkable, "wall": wall}


func _build_walk_protect_grid(walkable_faces: PackedVector3Array) -> Dictionary:
	var grid := {}
	_merge_walk_protect(grid, walkable_faces)
	return grid


func _merge_walk_protect(grid: Dictionary, walkable_faces: PackedVector3Array) -> void:
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
		if slope_deg > NAV_AGENT_MAX_SLOPE:
			continue
		var tri_y := maxf(v0.y, maxf(v1.y, v2.y))
		var cx0 := floori(minf(v0.x, minf(v1.x, v2.x)) / OBST_CELL)
		var cx1 := floori(maxf(v0.x, maxf(v1.x, v2.x)) / OBST_CELL)
		var cz0 := floori(minf(v0.z, minf(v1.z, v2.z)) / OBST_CELL)
		var cz1 := floori(maxf(v0.z, maxf(v1.z, v2.z)) / OBST_CELL)
		if (cx1 - cx0 + 1) * (cz1 - cz0 + 1) > 200000:
			continue
		for cz in range(cz0, cz1 + 1):
			for cx in range(cx0, cx1 + 1):
				var key := Vector2i(cx, cz)
				if grid.has(key):
					grid[key] = maxf(float(grid[key]), tri_y)
				else:
					grid[key] = tri_y


func _add_projected_aabb(source: NavigationMeshSourceGeometryData3D, aabb: AABB) -> void:
	var mn := aabb.position
	var mx := aabb.position + aabb.size
	var outline := PackedVector3Array([
		Vector3(mn.x, 0.0, mn.z),
		Vector3(mx.x, 0.0, mn.z),
		Vector3(mx.x, 0.0, mx.z),
		Vector3(mn.x, 0.0, mx.z),
	])
	const PAD := 600.0
	var elevation := mn.y - PAD
	var height := (mx.y - mn.y) + PAD * 2.0
	source.add_projected_obstruction(outline, elevation, height, true)


func _add_projected_wall_footprint(
	source: NavigationMeshSourceGeometryData3D,
	faces: PackedVector3Array,
	walk_top: Dictionary
) -> int:
	var cells: Dictionary = {}
	var i := 0
	while i + 2 < faces.size():
		var v0: Vector3 = faces[i]
		var v1: Vector3 = faces[i + 1]
		var v2: Vector3 = faces[i + 2]
		i += 3
		var tri_min_y := minf(v0.y, minf(v1.y, v2.y))
		var tri_max_y := maxf(v0.y, maxf(v1.y, v2.y))
		var a2 := Vector2(v0.x, v0.z)
		var b2 := Vector2(v1.x, v1.z)
		var c2 := Vector2(v2.x, v2.z)
		var bbox_min := Vector2(minf(a2.x, minf(b2.x, c2.x)), minf(a2.y, minf(b2.y, c2.y)))
		var bbox_max := Vector2(maxf(a2.x, maxf(b2.x, c2.x)), maxf(a2.y, maxf(b2.y, c2.y)))
		var cell_x0 := floori(bbox_min.x / OBST_CELL)
		var cell_x1 := floori(bbox_max.x / OBST_CELL)
		var cell_z0 := floori(bbox_min.y / OBST_CELL)
		var cell_z1 := floori(bbox_max.y / OBST_CELL)
		var span: int = (cell_x1 - cell_x0 + 1) * (cell_z1 - cell_z0 + 1)
		if span <= 0 or span > 200000:
			continue
		for cz in range(cell_z0, cell_z1 + 1):
			for cx in range(cell_x0, cell_x1 + 1):
				var cell_min := Vector2(cx * OBST_CELL, cz * OBST_CELL)
				var cell_max := cell_min + Vector2(OBST_CELL, OBST_CELL)
				if not _triangle_aabb_overlap_2d(a2, b2, c2, cell_min, cell_max, 0.0):
					continue
				var key := Vector2i(cx, cz)
				if cells.has(key):
					var existing: Dictionary = cells[key]
					existing["min_y"] = minf(existing["min_y"], tri_min_y)
					existing["max_y"] = maxf(existing["max_y"], tri_max_y)
				else:
					cells[key] = {"min_y": tri_min_y, "max_y": tri_max_y}
	const PAD := 600.0
	var added := 0
	for key in cells:
		var cell_key: Vector2i = key
		var entry: Dictionary = cells[key]
		var mn_y: float = entry["min_y"]
		var wall_top: float = entry["max_y"]
		var carve_top := wall_top
		var capped := false
		if walk_top.has(cell_key):
			var wt: float = float(walk_top[cell_key])
			if wt > mn_y + 1.0:
				carve_top = minf(wall_top, wt - RAMPART_CARVE_CLEARANCE)
				capped = true
		var raw_h: float = maxf(carve_top - mn_y, 0.05)
		if raw_h <= NAV_AGENT_MAX_CLIMB:
			continue
		var mn_x: float = cell_key.x * OBST_CELL
		var mn_z: float = cell_key.y * OBST_CELL
		var mx_x: float = mn_x + OBST_CELL
		var mx_z: float = mn_z + OBST_CELL
		var outline := PackedVector3Array([
			Vector3(mn_x, 0.0, mn_z),
			Vector3(mx_x, 0.0, mn_z),
			Vector3(mx_x, 0.0, mx_z),
			Vector3(mn_x, 0.0, mx_z),
		])
		var elevation := mn_y - PAD
		var height := (carve_top - elevation) if capped else (raw_h + PAD * 2.0)
		source.add_projected_obstruction(outline, elevation, height, true)
		added += 1
	return added


func _triangle_aabb_overlap_2d(
	a: Vector2, b: Vector2, c: Vector2, box_min: Vector2, box_max: Vector2, inflate: float
) -> bool:
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
		return _make_trunk_cylinder_aabb(world_xform.origin, TREE_TRUNK_FALLBACK_RADIUS, 2.0)
	if part_aabbs.size() == 1:
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


func _godot_to_nav(p: Vector3) -> Vector3:
	return _objects_basis * p + OBJECT_ORIGIN_SHIFT


func _collect_mesh_parts(node: Node, parent_xform: Transform3D, out: Array) -> void:
	var xform := parent_xform
	if node is Node3D:
		xform = parent_xform * (node as Node3D).transform
	if node is MeshInstance3D:
		var mi := node as MeshInstance3D
		if mi.mesh != null:
			out.append({"mesh": mi.mesh, "xform": xform})
	for child in node.get_children():
		_collect_mesh_parts(child, xform, out)


func _mesh_faces(mesh: Mesh, xform: Transform3D) -> PackedVector3Array:
	var out := PackedVector3Array()
	for si in range(mesh.get_surface_count()):
		var arrays := mesh.surface_get_arrays(si)
		var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		if verts.is_empty():
			continue
		var indices = arrays[Mesh.ARRAY_INDEX]
		if indices != null and indices.size() > 0:
			var i := 0
			while i + 2 < indices.size():
				out.append(xform * verts[indices[i]])
				out.append(xform * verts[indices[i + 1]])
				out.append(xform * verts[indices[i + 2]])
				i += 3
		else:
			var j := 0
			while j + 2 < verts.size():
				out.append(xform * verts[j])
				out.append(xform * verts[j + 1])
				out.append(xform * verts[j + 2])
				j += 3
	return out


func _faces_to_mesh(faces: PackedVector3Array) -> ArrayMesh:
	var indices := PackedInt32Array()
	indices.resize(faces.size())
	for i in range(faces.size()):
		indices[i] = i
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = faces
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _band_material(color: Color) -> StandardMaterial3D:
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.roughness = 0.85
	return mat


func _faces_aabb(faces: PackedVector3Array) -> AABB:
	if faces.is_empty():
		return AABB()
	var aabb := AABB(faces[0], Vector3.ZERO)
	for i in range(1, faces.size()):
		aabb = aabb.expand(faces[i])
	return aabb


func _write_glb(root: Node3D, path: String) -> bool:
	get_root().add_child(root)
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_scene(root, state)
	if err != OK:
		push_error("GLTF append failed err=%d path=%s" % [err, path])
		return false
	err = doc.write_to_filesystem(state, path)
	if err != OK:
		push_error("GLTF write failed err=%d path=%s" % [err, path])
		return false
	print("Exported %s (%d bytes)" % [path, FileAccess.get_file_as_bytes(path).size()])
	return true


func _merge_indoor_nav_index(entry: Dictionary) -> void:
	var dir_path := NAV_RES_PATH.get_base_dir()
	var sidecars: Array = []
	var dir := DirAccess.open(dir_path)
	if dir == null:
		return
	dir.list_dir_begin()
	var fname := dir.get_next()
	while fname != "":
		if not dir.current_is_dir() and fname.ends_with(".nav.json"):
			var p := dir_path.path_join(fname)
			var json := JSON.new()
			if json.parse(FileAccess.get_file_as_string(p)) == OK and typeof(json.data) == TYPE_DICTIONARY:
				var d: Dictionary = json.data
				if int(d.get("id", -1)) == CLUSTER_ID:
					sidecars.append(entry)
				else:
					sidecars.append(d)
		fname = dir.get_next()
	dir.list_dir_end()
	var found := false
	for s in sidecars:
		if int(s.get("id", -1)) == CLUSTER_ID:
			found = true
			break
	if not found:
		sidecars.append(entry)
	sidecars.sort_custom(func(a, b): return int(a.get("id", 0)) < int(b.get("id", 0)))
	var index := {
		"version": 1,
		"count": sidecars.size(),
		"generated": Time.get_datetime_string_from_system(true),
		"clusters": sidecars,
	}
	var f := FileAccess.open(dir_path.path_join("index.json"), FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(index, "\t"))
		f.close()
		print("Updated indoor nav index (%d clusters)" % sidecars.size())
