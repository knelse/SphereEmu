extends SceneTree

const NavGlbMerge = preload("res://Tools/nav_glb_merge.gd")

# Exports one combined preview GLB for the map: ground tiles + object placements (left side) and
# baked outdoor NavigationMesh tiles (right side, shifted in +X). Everything (ground, objects, nav)
# is merged into as few meshes/materials as possible (5 draw calls for the whole map: 1 ground,
# 3 object categories, 1 nav) — a full-world export has ~5000 tiles and tens of thousands of object
# placements, and importing one MeshInstance3D+material per tile/object would make Godot's GLTF
# importer choke. Ground+objects use only rotations (no baked colors), so the merged meshes stay
# valid regardless of --rotate-y.
#
# Usage:
#   godot --path . --headless -s Tools/export_world_nav_glb.gd -- --out D:/1/world_nav.glb
#   godot --path . --headless -s Tools/export_world_nav_glb.gd -- --quarter top-right --out D:/1/world_nav_top_right.glb
#   godot --path . --headless -s Tools/export_world_nav_glb.gd -- --chunk 0 16 --out D:/1/world_nav_chunk_00.glb
#   godot --path . --headless -s Tools/export_world_nav_glb.gd -- --rotate-y 90 --out D:/1/world_nav_rot90.glb
#   godot --path . --headless -s Tools/export_world_nav_glb.gd -- --no-objects --out D:/1/world_nav_ground_only.glb
#   python Tools/merge_world_nav_glb.py --chunks D:/1/world_nav_chunk_*.glb --out D:/1/world_nav.glb

const NAV_DIR := "res://Godot/Terrain/GeneratedNavMeshes/"
const TILES_DIR := "res://Godot/Terrain/Tiles/"
const OBJECT_DATA_DIR := "res://Godot/Terrain/ObjectDataJson/"
const MODELS_DIR := "res://Godot/Models/"
const MAP_PATH := "res://Godot/Terrain/map.txt"
const GRID_WIDTH := 80
const RECORD_SIZE := 22
const TILE_SIZE := 100.0
# NOTE: the "Terrain" GridMap's actual saved Position in terrain_scene.scn is (0,0,0), not the
# TerrainGridFill.TerrainWorldOrigin code default of (-4000,0,-4000) — RebuildTerrainGrid was never
# re-run/saved with that value. The committed GeneratedNavMeshes/*.res files were baked against that
# live (0,0,0) origin (verified: ~99.9% of tiles sit at exactly gx*100+50, gz*100+50 in world space;
# only a handful of stale/individually-rebaked tiles sit 4000 off from that). Match that here so the
# whole map's ground/nav tiles line up into one continuous mesh instead of a mostly-shifted checkerboard.
const TERRAIN_ORIGIN := Vector3.ZERO
const SKIP_MASTERS := ["fill_empt_00"]
const SOURCE_BASIS := Basis(Vector3.RIGHT, Vector3.DOWN, Vector3.FORWARD)
const SIDE_GAP := 140.0
const TERRAIN_SCENE_PATH := "res://Godot/Scenes/terrain_scene.scn"
# Object placement JSON coordinates are stored map-centered (roughly [-4000..4000], i.e. relative
# to TerrainGridFill's code-default TerrainWorldOrigin=(-4000,0,-4000)), but the live GridMap/baked
# nav tiles actually sit corner-origin at TERRAIN_ORIGIN=ZERO (0..8000) — the two drifted apart when
# the scene's Terrain node was moved to (0,0,0) without regenerating the object JSON. Empirically
# verified: applying this shift AFTER the TerrainObjects Ry(-90) rotation below puts 100% of objects
# inside the 0..79 grid (vs 0% shifting before the rotation, since the JSON's map-center convention
# is defined in already-rotated/rendered space, not in the pre-rotation source frame).
const OBJECT_ORIGIN_SHIFT := Vector3(4000.0, 0.0, 4000.0)

var _out_path := "D:/1/world_nav.glb"
var _skip_objects := false
var _chunk_index := -1
var _chunk_count := 1
var _quarter := ""
var _merge_meshes := true
var _region_gx_min := 0
var _region_gz_min := 0
var _region_gx_max := GRID_WIDTH - 1
var _region_gz_max := GRID_WIDTH - 1
var _cell_lookup: Dictionary = {}
var _objects_by_cell: Dictionary = {}
var _tile_mesh_cache: Dictionary = {}
var _mesh_parts_cache: Dictionary = {}
var _rotate_y_deg := 0.0
# Extra rotation on top of everything below, pivoted at world origin (0,0,0) — applied uniformly
# to ground, objects and nav so the whole map turns together as one rigid assembly.
var _world_rotate_basis := Basis.IDENTITY
# TerrainObjects is Ry(-90°) under TerrainScene; Multimesh instances inherit it.
var _objects_basis := Basis.from_euler(Vector3(0.0, deg_to_rad(-90.0), 0.0))

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var i := 0
	while i < args.size():
		if args[i] == "--no-objects":
			_skip_objects = true
			i += 1
		elif args[i] == "--chunk" and i + 2 < args.size():
			_chunk_index = int(args[i + 1])
			_chunk_count = maxi(1, int(args[i + 2]))
			i += 3
		elif args[i] == "--no-merge":
			_merge_meshes = false
			i += 1
		elif args[i] == "--quarter" and i + 1 < args.size():
			_quarter = args[i + 1].to_lower()
			i += 2
		elif args[i] == "--out" and i + 1 < args.size():
			_out_path = args[i + 1]
			i += 2
		elif args[i] == "--rotate-y" and i + 1 < args.size():
			_rotate_y_deg = float(args[i + 1])
			i += 2
		else:
			i += 1

	_objects_basis = _load_objects_basis()
	if _rotate_y_deg != 0.0:
		# Rotation pivots around the world origin (0,0,0), applied to ground/object/nav absolute
		# world-space positions before the (region-local) rebase subtraction below.
		_world_rotate_basis = Basis.from_euler(Vector3(0.0, deg_to_rad(_rotate_y_deg), 0.0))
		print("Rotating world %.1f deg around Y (world origin)" % _rotate_y_deg)

	DirAccess.make_dir_recursive_absolute(_out_path.get_base_dir())

	print("Building cell lookup...")
	_cell_lookup = _build_cell_lookup()
	if not _skip_objects:
		print("Indexing object placements...")
		var t_idx := Time.get_ticks_msec()
		_objects_by_cell = _build_objects_by_cell()
		print("Indexed %d cells with objects in %.1fs" % [
			_objects_by_cell.size(),
			(Time.get_ticks_msec() - t_idx) / 1000.0,
		])

	var keys: Array = _cell_lookup.keys()
	keys.sort()
	if not _quarter.is_empty():
		if not _apply_quarter_bounds():
			quit(1)
			return
		var filtered: Array = []
		for key in keys:
			var cell: Dictionary = _cell_lookup[key]
			var gx: int = cell["gx"]
			var gz: int = cell["gz"]
			if gx >= _region_gx_min and gx <= _region_gx_max and gz >= _region_gz_min and gz <= _region_gz_max:
				filtered.append(key)
		keys = filtered
		print("Quarter %s: gx=[%d..%d] gz=[%d..%d] -> %d cells" % [
			_quarter, _region_gx_min, _region_gx_max, _region_gz_min, _region_gz_max, keys.size(),
		])
		if _out_path.ends_with("world_nav.glb"):
			_out_path = _out_path.get_basename().get_basename() + "_%s.glb" % _quarter.replace("-", "_")

	if _chunk_index >= 0:
		var chunk_size := int(ceil(float(keys.size()) / float(_chunk_count)))
		var start_i := _chunk_index * chunk_size
		var end_i := mini(start_i + chunk_size, keys.size())
		if start_i >= keys.size():
			push_error("Chunk %d/%d is out of range (total=%d)" % [
				_chunk_index, _chunk_count, keys.size(),
			])
			quit(1)
			return
		keys = keys.slice(start_i, end_i)
		print("Chunk %d/%d: cells %d..%d (%d total)" % [
			_chunk_index, _chunk_count, start_i, end_i - 1, keys.size(),
		])

	print("Exporting world+nav GLB (%d cells, objects=%s, merge=%s) -> %s" % [
		keys.size(), "no" if _skip_objects else "yes", _merge_meshes, _out_path,
	])
	var t0 := Time.get_ticks_msec()
	var ok := _export_world(keys)
	print("Done in %.1fs: %s" % [
		(Time.get_ticks_msec() - t0) / 1000.0,
		"ok" if ok else "FAILED",
	])
	quit(0 if ok else 1)

func _apply_quarter_bounds() -> bool:
	# Map rows are stored with gz=0 at the north edge (row-major map.txt / map.bin).
	# gx runs east with the same flip used by TerrainGridFill (gx=0 west, gx=79 east).
	var half := GRID_WIDTH / 2
	match _quarter:
		"top-right", "ne":
			_region_gx_min = half
			_region_gz_min = 0
			_region_gx_max = GRID_WIDTH - 1
			_region_gz_max = half - 1
		"top-left", "nw":
			_region_gx_min = 0
			_region_gz_min = 0
			_region_gx_max = half - 1
			_region_gz_max = half - 1
		"bottom-right", "se":
			_region_gx_min = half
			_region_gz_min = half
			_region_gx_max = GRID_WIDTH - 1
			_region_gz_max = GRID_WIDTH - 1
		"bottom-left", "sw":
			_region_gx_min = 0
			_region_gz_min = half
			_region_gx_max = half - 1
			_region_gz_max = GRID_WIDTH - 1
		_:
			push_error("Unknown quarter: ", _quarter, " (use top-right, top-left, bottom-right, bottom-left)")
			return false
	return true

func _load_objects_basis() -> Basis:
	var fallback := Basis.from_euler(Vector3(0.0, deg_to_rad(-90.0), 0.0))
	if not ResourceLoader.exists(TERRAIN_SCENE_PATH):
		return fallback
	var packed: PackedScene = load(TERRAIN_SCENE_PATH)
	var scene_root := packed.instantiate()
	var objects_node := scene_root.get_node_or_null("TerrainObjects") as Node3D
	var basis := fallback
	if objects_node != null:
		basis = objects_node.transform.basis
		print("Loaded TerrainObjects rot_deg=", objects_node.rotation_degrees)
	scene_root.free()
	return basis

func _region_rebase() -> Vector3:
	return TERRAIN_ORIGIN + Vector3(
		_region_gx_min * TILE_SIZE,
		0.0,
		_region_gz_min * TILE_SIZE
	)

func _region_width_x() -> float:
	return float(_region_gx_max - _region_gx_min + 1) * TILE_SIZE

func _export_world(keys: Array) -> bool:
	var rebase := _region_rebase()
	var region_width := _region_width_x()

	var root := Node3D.new()
	root.name = "WorldNavPreview"

	var terrain_root := Node3D.new()
	terrain_root.name = "Terrain"
	root.add_child(terrain_root)

	var nav_root := Node3D.new()
	nav_root.name = "NavMesh"
	nav_root.position = Vector3(region_width + SIDE_GAP, 0.0, 0.0)
	root.add_child(nav_root)

	# One bucket per output mesh: ground (1), object categories (3), nav (1) — 5 total regardless
	# of tile/object count, so Godot's importer only ever sees 5 meshes / 5 materials.
	var merge: Dictionary = NavGlbMerge.new_tile_state()

	var tile_count := 0
	var object_count := 0
	var nav_count := 0
	var missing_nav := 0

	for idx in range(keys.size()):
		var key: String = keys[idx]
		var cell: Dictionary = _cell_lookup[key]
		var master: String = cell["master"]
		var gx: int = cell["gx"]
		var gz: int = cell["gz"]
		var world_pos := TERRAIN_ORIGIN + Vector3(gx * TILE_SIZE, 0.0, gz * TILE_SIZE)
		# Rotate around world origin (0,0,0) first, then rebase into local export space.
		var local_origin := _world_rotate_basis * world_pos - rebase

		var tile_mesh := _load_tile_mesh(master)
		if tile_mesh != null:
			var ground_xform := Transform3D(_world_rotate_basis * _tile_mesh_basis(), local_origin)
			NavGlbMerge.append_mesh(merge["ground"], tile_mesh, ground_xform)
			tile_count += 1

		if not _skip_objects:
			object_count += _add_cell_objects(gx, gz, rebase, merge)

		var nav: NavigationMesh = null
		var nav_path := NAV_DIR + key + ".res"
		if ResourceLoader.exists(nav_path):
			nav = load(nav_path)

		if nav != null:
			# append_nav_xform computes basis*(verts - rebase_arg); pass rebase pre-rotated by
			# the inverse so the result is (world_rotate_basis*verts - rebase), i.e. rotate
			# around world origin (0,0,0) like ground/objects, then subtract the unrotated rebase.
			var nav_rebase_arg := _world_rotate_basis.inverse() * rebase
			if NavGlbMerge.append_nav_xform(merge["nav"], nav, nav_rebase_arg, _world_rotate_basis):
				nav_count += 1
			else:
				missing_nav += 1
		else:
			missing_nav += 1

		if (idx + 1) % 500 == 0 or idx + 1 == keys.size():
			print("  [%d/%d] tiles=%d objects=%d nav=%d missing_nav=%d" % [
				idx + 1, keys.size(), tile_count, object_count, nav_count, missing_nav,
			])

	NavGlbMerge.finalize_tile_meshes(terrain_root, nav_root, merge)

	get_root().add_child(root)
	var ok := _write_glb(root, _out_path)
	root.queue_free()
	if ok:
		print("Saved %s (tiles=%d objects=%d nav=%d missing_nav=%d)" % [
			_out_path, tile_count, object_count, nav_count, missing_nav,
		])
	return ok

func _add_cell_objects(gx: int, gz: int, rebase: Vector3, merge: Dictionary) -> int:
	var key := Vector2i(gx, gz)
	if not _objects_by_cell.has(key):
		return 0
	var added := 0
	for placement in _objects_by_cell[key]:
		var object_name: String = placement["object_name"]
		var category: String = placement["category"]
		# Apply TerrainObjects' Ry(-90°) inheritance to the raw (un-rebased) placement first —
		# exactly like a MultiMeshInstance3D child inheriting its parent's rotation — then the
		# extra world rotation (also around world origin), then subtract rebase last, same as
		# ground tiles. Rebasing before rotating would rotate the rebase vector itself (wrong:
		# it's a plain map-space offset, not something objects should inherit rotated).
		var world_xform := Transform3D(_objects_basis, Vector3.ZERO) * (placement["transform"] as Transform3D)
		world_xform.origin += OBJECT_ORIGIN_SHIFT
		world_xform = Transform3D(_world_rotate_basis, Vector3.ZERO) * world_xform
		world_xform.origin -= rebase
		var parts: Array = _load_object_parts(object_name)
		if parts.is_empty():
			continue
		var cat: String = category if category in merge["object"] else "other"
		for part in parts:
			var part_xform: Transform3D = world_xform * part["local"]
			NavGlbMerge.append_mesh(merge["object"][cat], part["mesh"], part_xform)
			added += 1
	return added

func _write_glb(root: Node3D, abs_path: String) -> bool:
	print("Writing GLB (this may take a while)...")
	DirAccess.make_dir_recursive_absolute(abs_path.get_base_dir())
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_scene(root, state)
	if err != OK:
		push_error("GLTF append_from_scene failed err=", err)
		return false
	print("  append ok, writing ", abs_path, "...")
	err = doc.write_to_filesystem(state, abs_path)
	if err != OK:
		push_error("GLTF write_to_filesystem failed err=", err)
		return false
	if not FileAccess.file_exists(abs_path):
		push_error("GLB missing after write: ", abs_path)
		return false
	print("  wrote ", abs_path, " (", FileAccess.get_file_as_bytes(abs_path).size(), " bytes)")
	return true

func _load_tile_mesh(master: String) -> Mesh:
	if _tile_mesh_cache.has(master):
		return _tile_mesh_cache[master]
	if master.is_empty():
		return null
	var glb_path := TILES_DIR + master + ".glb"
	if not ResourceLoader.exists(glb_path):
		push_warning("Missing tile glb: ", glb_path)
		_tile_mesh_cache[master] = null
		return null
	var scene: PackedScene = load(glb_path)
	var inst := scene.instantiate()
	var mesh := _find_first_mesh(inst)
	inst.free()
	if mesh == null:
		_tile_mesh_cache[master] = null
		return null
	var dup := mesh.duplicate() as Mesh
	_tile_mesh_cache[master] = dup
	return dup

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
	if node is MeshInstance3D and node.mesh and not _has_skeleton_ancestor(node):
		out_parts.append({"mesh": node.mesh.duplicate(), "local": xform})
	for child in node.get_children():
		_collect_mesh_parts(child, xform, out_parts)

func _has_skeleton_ancestor(node: Node) -> bool:
	var current := node.get_parent()
	while current:
		if current is Skeleton3D:
			return true
		current = current.get_parent()
	return false

func _build_cell_lookup() -> Dictionary:
	var lookup := {}
	var bytes := FileAccess.get_file_as_bytes(MAP_PATH)
	if bytes.is_empty():
		push_error("Cannot read map: ", MAP_PATH)
		return lookup
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
		# Same mirror as TerrainGridFill.RebuildTerrainGrid: gx=0 is the west edge, gx=79 the east.
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
			_index_json_placements(by_cell, OBJECT_DATA_DIR + name)
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
					_index_json_placements(by_cell, OBJECT_DATA_DIR + name + "/" + n2)
			sub.list_dir_end()
	dir.list_dir_end()
	return by_cell

func _index_json_placements(by_cell: Dictionary, path: String) -> void:
	var json_text := FileAccess.get_file_as_string(path)
	if json_text.is_empty():
		return
	var json := JSON.new()
	if json.parse(json_text) != OK or typeof(json.data) != TYPE_ARRAY:
		return
	for item in json.data:
		if typeof(item) != TYPE_DICTIONARY:
			continue
		var rec: Dictionary = item
		var object_name := _record_object_name(rec)
		if object_name.is_empty() or object_name == "empty":
			continue
		var coords := _record_coordinates(rec)
		var rot := _record_rotation(rec)
		var pos := SOURCE_BASIS * coords
		var euler_for_godot := Vector3(rot.x, -rot.y, rot.z)
		var basis_source := Basis.from_euler(euler_for_godot)
		var basis_godot := SOURCE_BASIS * basis_source * SOURCE_BASIS
		var world_xform := Transform3D(basis_godot, pos)
		# Bucket by the TerrainObjects-rotated position (not the extra world rotation, which is
		# a uniform render-time transform applied to everything alike and never changes which
		# grid cell an object logically belongs to): rendering (_add_cell_objects) applies
		# _objects_basis to raw pos before placing objects, so the grid cell an object visually
		# ends up near is the ROTATED position's cell, not raw pos's cell.
		var rotated_pos := _objects_basis * pos + OBJECT_ORIGIN_SHIFT
		var cell := Vector2i(
			floori((rotated_pos.x - TERRAIN_ORIGIN.x) / TILE_SIZE),
			floori((rotated_pos.z - TERRAIN_ORIGIN.z) / TILE_SIZE)
		)
		if not by_cell.has(cell):
			by_cell[cell] = []
		by_cell[cell].append({
			"object_name": object_name,
			"category": _classify_object_name(object_name),
			"transform": world_xform
		})

func _record_object_name(rec: Dictionary) -> String:
	if rec.has("object_name"):
		return str(rec["object_name"]).to_lower()
	if rec.has("name"):
		return str(rec["name"]).to_lower()
	return ""

func _record_coordinates(rec: Dictionary) -> Vector3:
	if typeof(rec.get("coordinates")) == TYPE_DICTIONARY:
		var cd: Dictionary = rec["coordinates"]
		return Vector3(float(cd.get("x", 0.0)), float(cd.get("y", 0.0)), float(cd.get("z", 0.0)))
	return Vector3(float(rec.get("x", 0.0)), float(rec.get("y", 0.0)), float(rec.get("z", 0.0)))

func _record_rotation(rec: Dictionary) -> Vector3:
	if typeof(rec.get("rotation_euler")) == TYPE_DICTIONARY:
		var rd: Dictionary = rec["rotation_euler"]
		return Vector3(float(rd.get("pitch", 0.0)), float(rd.get("yaw", 0.0)), float(rd.get("roll", 0.0)))
	return Vector3(float(rec.get("pitch", 0.0)), float(rec.get("yaw", 0.0)), float(rec.get("roll", 0.0)))

func _classify_object_name(object_name: String) -> String:
	var lower := object_name.to_lower()
	if lower.contains("tree") or lower.contains("bush") or lower.contains("grass"):
		return "plant"
	if lower.contains("rock") or lower.contains("stone"):
		return "rock"
	return "other"

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
