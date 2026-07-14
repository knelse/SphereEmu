extends SceneTree

const NavGlbMerge = preload("res://Tools/nav_glb_merge.gd")

# Exports side-by-side GLBs: terrain+objects (left) and navmesh (right).
# Usage:
#   godot --path . -s Tools/export_nav_preview_glbs.gd -- [--all] [--count N] [--only NAME] [--out DIR]

const NAV_DIR := "res://Godot/Terrain/GeneratedNavMeshes/"
const TILES_DIR := "res://Godot/Terrain/Tiles/"
const OBJECT_DATA_DIR := "res://Godot/Terrain/ObjectDataJson/"
const MODELS_DIR := "res://Godot/Models/"
const MAP_PATH := "res://Godot/Terrain/map.txt"
const GRID_WIDTH := 80
const RECORD_SIZE := 22
const TILE_SIZE := 100.0
const TERRAIN_ORIGIN := Vector3(-4000.0, 0.0, -4000.0)
const SOURCE_BASIS := Basis(Vector3.RIGHT, Vector3.DOWN, Vector3.FORWARD)
# Gap between terrain and navmesh centers in the exported GLB.
const SIDE_GAP := 140.0

var _out_dir := "D:/1"
var _nav_files: PackedStringArray = []
var _cell_lookup: Dictionary = {}
var _objects_by_cell: Dictionary = {}
var _mesh_parts_cache: Dictionary = {}

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var count := 20
	var render_all := false
	var only_name := ""
	var with_objects := false
	_out_dir = "D:/1"
	var i := 0
	while i < args.size():
		if args[i] == "--all":
			render_all = true
			i += 1
		elif args[i] == "--with-objects":
			with_objects = true
			i += 1
		elif args[i] == "--count" and i + 1 < args.size():
			count = int(args[i + 1])
			i += 2
		elif args[i] == "--only" and i + 1 < args.size():
			only_name = args[i + 1]
			i += 2
		elif args[i] == "--out" and i + 1 < args.size():
			_out_dir = args[i + 1]
			i += 2
		else:
			i += 1

	DirAccess.make_dir_recursive_absolute(_out_dir)
	_cell_lookup = _build_cell_lookup()
	var t0 := Time.get_ticks_msec()
	_objects_by_cell = _build_objects_by_cell()
	print("Indexed object placements in %.1fs (%d cells)" % [
		(Time.get_ticks_msec() - t0) / 1000.0,
		_objects_by_cell.size()
	])
	_nav_files = _list_nav_files()
	_nav_files.sort()
	if not only_name.is_empty():
		if _nav_files.has(only_name):
			_nav_files = PackedStringArray([only_name])
		else:
			push_error("Nav region not found: " + only_name)
			quit(1)
			return
	elif render_all:
		pass
	else:
		if with_objects:
			_nav_files = _filter_with_objects(_nav_files)
			print("Regions with objects: ", _nav_files.size())
		_nav_files = _nav_files.slice(0, mini(count, _nav_files.size()))

	print("Exporting ", _nav_files.size(), " GLBs to ", _out_dir)
	var exported := 0
	for nav_name in _nav_files:
		if _export_one(nav_name):
			exported += 1
			print("  saved ", exported, "/", _nav_files.size(), " ", _out_dir.path_join(nav_name + ".glb"))
	print("Done: ", exported, " GLB(s)")
	quit(0 if exported == _nav_files.size() else 1)

func _filter_with_objects(names: PackedStringArray) -> PackedStringArray:
	var out: PackedStringArray = []
	for nav_name in names:
		var cell: Dictionary = _cell_lookup.get(nav_name, {})
		if cell.is_empty():
			continue
		var key := Vector2i(int(cell.get("gx", 0)), int(cell.get("gz", 0)))
		if _objects_by_cell.get(key, []).size() > 0:
			out.append(nav_name)
	return out

func _export_one(nav_name: String) -> bool:
	var cell: Dictionary = _cell_lookup.get(nav_name, {})
	var master: String = cell.get("master", "")
	var gx: int = cell.get("gx", 0)
	var gz: int = cell.get("gz", 0)
	if master.is_empty():
		push_warning("No map cell for nav region: ", nav_name)

	var world_pos := TERRAIN_ORIGIN + Vector3(gx * TILE_SIZE, 0.0, gz * TILE_SIZE)
	var root := Node3D.new()
	root.name = nav_name

	var terrain_root := Node3D.new()
	terrain_root.name = "Terrain"
	root.add_child(terrain_root)

	var tile_mesh := _load_tile_mesh(master)
	var frame_center := world_pos
	if tile_mesh != null:
		frame_center = _mesh_world_center(tile_mesh, Transform3D(_tile_mesh_basis(), world_pos))

	var nav_path := NAV_DIR + nav_name + ".res"
	var nav: NavigationMesh = null
	if ResourceLoader.exists(nav_path):
		nav = load(nav_path)

	var terrain_offset := -frame_center + Vector3(-SIDE_GAP * 0.5, 0.0, 0.0)
	var merge := NavGlbMerge.new_tile_state()
	if tile_mesh != null:
		NavGlbMerge.append_mesh(
			merge["ground"],
			tile_mesh,
			NavGlbMerge.apply_translation(Transform3D(_tile_mesh_basis(), world_pos), terrain_offset)
		)
	_add_cell_objects_to_merge(merge, gx, gz, nav, frame_center.y, terrain_offset)

	var obj_count: int = _objects_by_cell.get(Vector2i(gx, gz), []).size()

	var nav_root := Node3D.new()
	nav_root.name = "NavMesh"
	nav_root.position = Vector3(SIDE_GAP * 0.5, 0.0, 0.0)
	root.add_child(nav_root)
	if nav != null:
		if not NavGlbMerge.append_nav(merge["nav"], nav, frame_center):
			var marker := _empty_nav_marker()
			marker.name = "EmptyNav"
			nav_root.add_child(marker)
	else:
		var marker2 := _empty_nav_marker()
		marker2.name = "EmptyNav"
		nav_root.add_child(marker2)
	NavGlbMerge.finalize_tile_meshes(terrain_root, nav_root, merge)

	get_root().add_child(root)
	var out_path := _out_dir.path_join(nav_name + ".glb")
	var ok := _write_glb(root, out_path)
	root.queue_free()
	if ok:
		print("    objects=", obj_count)
	return ok

func _write_glb(root: Node3D, abs_path: String) -> bool:
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_scene(root, state)
	if err != OK:
		push_error("GLTF append_from_scene failed for ", abs_path, " err=", err)
		return false
	err = doc.write_to_filesystem(state, abs_path)
	if err != OK:
		push_error("GLTF write_to_filesystem failed for ", abs_path, " err=", err)
		return false
	return true

func _add_cell_objects_to_merge(
	merge: Dictionary,
	gx: int,
	gz: int,
	nav: NavigationMesh,
	fallback_surface_y: float,
	terrain_offset: Vector3
) -> void:
	var key := Vector2i(gx, gz)
	if not _objects_by_cell.has(key):
		return
	var height_samples: PackedVector3Array = PackedVector3Array()
	if nav != null:
		height_samples = nav.get_vertices()
	for placement in _objects_by_cell[key]:
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

func _sample_surface_y(samples: PackedVector3Array, xz: Vector2, fallback: float) -> float:
	if samples.is_empty():
		return fallback
	var best_d := INF
	var best_y := fallback
	for v in samples:
		var d: float = Vector2(v.x, v.z).distance_squared_to(xz)
		if d < best_d:
			best_d = d
			best_y = v.y
	return best_y

func _mesh_world_center(mesh: Mesh, xform: Transform3D) -> Vector3:
	var aabb := mesh.get_aabb()
	var box := AABB(xform * aabb.position, Vector3.ZERO)
	for i in range(8):
		box = box.expand(xform * aabb.get_endpoint(i))
	return box.position + box.size * 0.5

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

func _load_tile_mesh(master: String) -> Mesh:
	if master.is_empty():
		return null
	var glb_path := TILES_DIR + master + ".glb"
	if not ResourceLoader.exists(glb_path):
		push_warning("Missing tile glb: ", glb_path)
		return null
	var scene: PackedScene = load(glb_path)
	var inst := scene.instantiate()
	var mesh := _find_first_mesh(inst)
	inst.free()
	if mesh == null:
		return null
	return mesh.duplicate() as Mesh

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

func _empty_nav_marker() -> MeshInstance3D:
	var empty := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = Vector3(8, 1, 8)
	empty.mesh = bm
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(1, 0.2, 0.2)
	empty.material_override = mat
	return empty

func _compute_bounds(node: Node3D) -> Dictionary:
	var box := [Vector3(INF, INF, INF), Vector3(-INF, -INF, -INF)]
	_accum_bounds(node, Transform3D.IDENTITY, box)
	var min_v: Vector3 = box[0]
	var max_v: Vector3 = box[1]
	if min_v.x == INF:
		return {"center": Vector3.ZERO, "radius": 50.0}
	var center := (min_v + max_v) * 0.5
	var radius := maxf(maxf(max_v.x - min_v.x, max_v.y - min_v.y), max_v.z - min_v.z) * 0.55
	return {"center": center, "radius": maxf(radius, 10.0)}

func _accum_bounds(node: Node, parent_xform: Transform3D, box: Array) -> void:
	var xform := parent_xform
	if node is Node3D:
		xform = parent_xform * node.transform
	if node is MeshInstance3D and node.mesh:
		var aabb: AABB = node.mesh.get_aabb()
		for corner in _aabb_corners(aabb):
			var w: Vector3 = xform * corner
			box[0].x = minf(box[0].x, w.x)
			box[0].y = minf(box[0].y, w.y)
			box[0].z = minf(box[0].z, w.z)
			box[1].x = maxf(box[1].x, w.x)
			box[1].y = maxf(box[1].y, w.y)
			box[1].z = maxf(box[1].z, w.z)
	for c in node.get_children():
		_accum_bounds(c, xform, box)

func _aabb_corners(aabb: AABB) -> Array:
	var mn := aabb.position
	var mx := aabb.position + aabb.size
	return [
		Vector3(mn.x, mn.y, mn.z), Vector3(mx.x, mn.y, mn.z),
		Vector3(mn.x, mn.y, mx.z), Vector3(mx.x, mn.y, mx.z),
		Vector3(mn.x, mx.y, mn.z), Vector3(mx.x, mx.y, mn.z),
		Vector3(mn.x, mx.y, mx.z), Vector3(mx.x, mx.y, mx.z),
	]

func _build_objects_by_cell() -> Dictionary:
	var by_cell := {}
	var dir := DirAccess.open(OBJECT_DATA_DIR)
	if dir == null:
		push_warning("Cannot open object data dir: ", OBJECT_DATA_DIR)
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
			_index_json_folder(by_cell, OBJECT_DATA_DIR + name + "/")
	dir.list_dir_end()
	return by_cell

func _index_json_folder(by_cell: Dictionary, folder_path: String) -> void:
	var dir := DirAccess.open(folder_path)
	if dir == null:
		return
	dir.list_dir_begin()
	while true:
		var name := dir.get_next()
		if name == "":
			break
		if name in [".", ".."] or dir.current_is_dir() or not name.ends_with(".json"):
			continue
		_index_json_placements(by_cell, folder_path + name)
	dir.list_dir_end()

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
		var cell := Vector2i(
			floori((pos.x - TERRAIN_ORIGIN.x) / TILE_SIZE),
			floori((pos.z - TERRAIN_ORIGIN.z) / TILE_SIZE)
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

func _list_nav_files() -> PackedStringArray:
	var files: PackedStringArray = []
	var dir := DirAccess.open(NAV_DIR)
	if dir == null:
		push_error("Cannot open " + NAV_DIR)
		return files
	dir.list_dir_begin()
	while true:
		var f := dir.get_next()
		if f == "":
			break
		if f.ends_with(".res"):
			files.append(f.get_basename())
	dir.list_dir_end()
	return files

func _build_cell_lookup() -> Dictionary:
	var lookup := {}
	var bytes := FileAccess.get_file_as_bytes(MAP_PATH)
	if bytes.is_empty():
		push_error("Cannot read map: " + MAP_PATH)
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
		if not next_occ.has(master_name):
			next_occ[master_name] = 0
		var occ: int = next_occ[master_name]
		next_occ[master_name] = occ + 1
		var gx := GRID_WIDTH - (idx % GRID_WIDTH) - 1
		var gz := idx / GRID_WIDTH
		var key := _tile_group_key(master_name, occ)
		lookup[key] = {"master": master_name, "occurrence": occ, "gx": gx, "gz": gz}
		idx += 1
	return lookup

func _sanitize_godot_node_name(name: String) -> String:
	var s := name.to_lower().strip_edges()
	if s.is_empty():
		return "object"
	var out := PackedStringArray()
	for c in s:
		var code := c.unicode_at(0)
		var ok := (code >= 97 and code <= 122) or (code >= 48 and code <= 57) or c in ["_", "-", "."]
		out.append(c if ok else "_")
	s = "".join(out)
	return s if not s.is_empty() else "object"

func _tile_group_key(master_name: String, occurrence: int) -> String:
	var s := _sanitize_godot_node_name(master_name)
	if s.is_empty():
		return ""
	return s[0].to_upper() + s.substr(1) + "_%02d" % occurrence
