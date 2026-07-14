extends SceneTree

# Renders side-by-side previews: land tile + objects (left) and navigation mesh (right).
# Usage: godot --path . -s Tools/render_nav_previews.gd -- [--all] [--count N] [--out DIR]

const NAV_DIR := "res://Godot/Terrain/GeneratedNavMeshes/"
const TILES_DIR := "res://Godot/Terrain/Tiles/"
const OBJECT_DATA_DIR := "res://Godot/Terrain/ObjectDataJson/"
const MODELS_DIR := "res://Godot/Models/"
const MAP_PATH := "res://Godot/Terrain/map.txt"
const GRID_WIDTH := 80
const RECORD_SIZE := 22
const TILE_SIZE := 100.0
# Must match terrain GridMap.Position in terrain_scene.scn (baker uses terrain.Position, not the
# Match TerrainGridFill.TerrainWorldOrigin / SphereWorld Hyperion-centered coords (±4000).
const TERRAIN_ORIGIN := Vector3(-4000.0, 0.0, -4000.0)
# Same source→Godot mapping as TerrainObjectsFill / TerrainObjectPlacementSource.
const SOURCE_BASIS := Basis(Vector3.RIGHT, Vector3.DOWN, Vector3.FORWARD)

var _out_dir := "D:/1"
var _nav_files: PackedStringArray = []
var _cell_lookup: Dictionary = {}
var _objects_by_cell: Dictionary = {}
var _index := 0

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var count := 20
	var render_all := false
	var only_name := ""
	_out_dir = "D:/1"
	var i := 0
	while i < args.size():
		if args[i] == "--all":
			render_all = true
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
		_nav_files = _nav_files.slice(0, min(count, _nav_files.size()))

	print("Rendering ", _nav_files.size(), " previews to ", _out_dir)
	var runner := PreviewRunner.new()
	runner.tree = self
	runner.out_dir = _out_dir
	runner.nav_files = _nav_files
	runner.cell_lookup = _cell_lookup
	runner.objects_by_cell = _objects_by_cell
	get_root().add_child(runner)

class PreviewRunner extends Node:
	var tree: SceneTree
	var out_dir: String
	var nav_files: PackedStringArray
	var cell_lookup: Dictionary
	var objects_by_cell: Dictionary
	var _mesh_parts_cache: Dictionary = {}
	var index := 0
	var _pending_root: Node
	var _pending_png: String
	var _wait_frames := 0
	var _panel_size := Vector2i(768, 768)

	func _process(_delta: float) -> void:
		if _wait_frames > 0:
			_wait_frames -= 1
			if _wait_frames == 0:
				_save_pending()
			return

		if index >= nav_files.size():
			tree.quit(0)
			return

		var nav_name := nav_files[index]
		index += 1
		_begin_render(nav_name)

	func _begin_render(nav_name: String) -> void:
		var cell: Dictionary = cell_lookup.get(nav_name, {})
		var master: String = cell.get("master", "")
		var gx: int = cell.get("gx", 0)
		var gz: int = cell.get("gz", 0)
		if master.is_empty():
			push_warning("No map cell for nav region: ", nav_name)

		var world_pos := TERRAIN_ORIGIN + Vector3(gx * TILE_SIZE, 0.0, gz * TILE_SIZE)

		# Left panel: land tile mesh + objects on this cell, centered at origin in its own viewport.
		var ground_world := Node3D.new()
		var tile_mesh := _load_tile_mesh(master)
		if tile_mesh != null:
			var ground := MeshInstance3D.new()
			ground.mesh = tile_mesh
			ground.transform = Transform3D(_tile_mesh_basis(), world_pos)
			var gmat := StandardMaterial3D.new()
			gmat.albedo_color = Color(0.82, 0.78, 0.70)
			gmat.roughness = 0.9
			ground.material_override = gmat
			ground_world.add_child(ground)
		var tile_bounds := _compute_bounds(ground_world)
		var tile_center: Vector3 = tile_bounds.get("center", Vector3.ZERO)
		var frame_center := tile_center
		var frame_radius: float = tile_bounds.get("radius", TILE_SIZE * 0.5)

		var nav_path := NAV_DIR + nav_name + ".res"
		var nav: NavigationMesh = null
		if ResourceLoader.exists(nav_path):
			nav = load(nav_path)

		_add_cell_objects(ground_world, gx, gz, nav, tile_center.y)
		ground_world.position = -frame_center
		var nav_world := Node3D.new()
		if nav != null:
			var nav_mi := _navigation_mesh_instance_local(nav, frame_center)
			if nav_mi != null:
				nav_world.add_child(nav_mi)
			else:
				nav_world.add_child(_empty_nav_marker())
		else:
			nav_world.add_child(_empty_nav_marker())

		var root := Node.new()
		var left_vp := _make_viewport(
			ground_world,
			Vector3.ZERO,
			frame_radius,
			Color(0.12, 0.14, 0.18)
		)
		var right_vp := _make_viewport(
			nav_world,
			Vector3.ZERO,
			frame_radius,
			Color(0.08, 0.09, 0.11)
		)
		root.add_child(left_vp)
		root.add_child(right_vp)
		tree.get_root().add_child(root)

		_pending_root = root
		_pending_png = out_dir.path_join(nav_name + ".png")
		var object_count: int = objects_by_cell.get(Vector2i(gx, gz), []).size()
		_wait_frames = clampi(8 + object_count / 40, 8, 40)

	func _make_viewport(content: Node3D, look_at: Vector3, radius: float, bg_color: Color) -> SubViewport:
		var viewport := SubViewport.new()
		viewport.size = _panel_size
		viewport.transparent_bg = false
		viewport.own_world_3d = true
		viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS

		var env := WorldEnvironment.new()
		var e := Environment.new()
		e.background_mode = Environment.BG_COLOR
		e.background_color = bg_color
		env.environment = e
		viewport.add_child(env)

		var frame_radius: float = maxf(radius, 15.0)

		var cam := Camera3D.new()
		cam.projection = Camera3D.PROJECTION_PERSPECTIVE
		cam.fov = 48.0
		# Three-quarter view: elevated and offset in +X/+Z so depth is readable.
		var cam_offset := Vector3(frame_radius * 1.15, frame_radius * 0.95, frame_radius * 1.15)
		var cam_pos := look_at + cam_offset
		cam.position = cam_pos
		cam.look_at_from_position(cam_pos, look_at, Vector3.UP)
		cam.current = true
		viewport.add_child(cam)

		var light := DirectionalLight3D.new()
		light.rotation_degrees = Vector3(-50, -30, 0)
		light.light_energy = 1.25
		viewport.add_child(light)

		var fill := DirectionalLight3D.new()
		fill.rotation_degrees = Vector3(-25, 145, 0)
		fill.light_energy = 0.4
		viewport.add_child(fill)

		viewport.add_child(content)
		return viewport

	func _empty_nav_marker() -> MeshInstance3D:
		var empty := MeshInstance3D.new()
		var bm := BoxMesh.new()
		bm.size = Vector3(8, 1, 8)
		empty.mesh = bm
		var mat := StandardMaterial3D.new()
		mat.albedo_color = Color(1, 0.2, 0.2, 0.9)
		mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		empty.material_override = mat
		empty.position = Vector3.ZERO
		return empty

	func _save_pending() -> void:
		if _pending_root == null:
			return
		var viewports: Array[SubViewport] = []
		for child in _pending_root.get_children():
			if child is SubViewport:
				viewports.append(child)
		if viewports.size() < 2:
			push_error("Expected two viewports for side-by-side render")
		else:
			for vp in viewports:
				vp.render_target_update_mode = SubViewport.UPDATE_ONCE
			var left_img := viewports[0].get_texture().get_image()
			var right_img := viewports[1].get_texture().get_image()
			if left_img == null or right_img == null:
				push_error("Missing viewport image for ", _pending_png)
			else:
				left_img.convert(Image.FORMAT_RGBA8)
				right_img.convert(Image.FORMAT_RGBA8)
				var out_w := _panel_size.x * 2
				var out_h := _panel_size.y
				var combined := Image.create(out_w, out_h, false, Image.FORMAT_RGBA8)
				combined.fill(Color(0.05, 0.05, 0.07))
				combined.blit_rect(left_img, Rect2i(0, 0, _panel_size.x, _panel_size.y), Vector2i(0, 0))
				combined.blit_rect(right_img, Rect2i(0, 0, _panel_size.x, _panel_size.y), Vector2i(_panel_size.x, 0))
				# Divider between panels
				for y in range(out_h):
					combined.set_pixel(_panel_size.x, y, Color(0.35, 0.38, 0.42))
				var err := combined.save_png(_pending_png)
				if err != OK:
					push_error("Failed to save ", _pending_png, " err=", err)
				elif index <= 20 or index % 100 == 0 or index == nav_files.size():
					print("  saved ", index, "/", nav_files.size(), " (", out_w, "x", out_h, ") ", _pending_png)
		_pending_root.queue_free()
		_pending_root = null

	func _add_cell_objects(
		parent: Node3D,
		gx: int,
		gz: int,
		nav: NavigationMesh,
		fallback_surface_y: float
	) -> void:
		var key := Vector2i(gx, gz)
		if not objects_by_cell.has(key):
			return

		var placements: Array = objects_by_cell[key]
		var added := 0
		for placement in placements:
			var object_name: String = placement["object_name"]
			var category: String = placement["category"]
			var world_xform: Transform3D = placement["transform"]
			var parts: Array = _load_object_parts(object_name)
			if parts.is_empty():
				continue
			var mat := _object_material(category)
			for part in parts:
				var mi := MeshInstance3D.new()
				mi.mesh = part["mesh"]
				mi.transform = world_xform * part["local"]
				mi.material_override = mat
				mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
				parent.add_child(mi)
				added += 1
		if added > 0:
			pass

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

	func _object_material(category: String) -> StandardMaterial3D:
		var mat := StandardMaterial3D.new()
		mat.roughness = 0.85
		match category:
			"plant":
				mat.albedo_color = Color(0.22, 0.62, 0.28)
			"rock":
				mat.albedo_color = Color(0.62, 0.6, 0.58)
			_:
				mat.albedo_color = Color(0.72, 0.32, 0.22)
		mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		return mat

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
		_collect_mesh_parts(inst, inst, Transform3D.IDENTITY, parts)
		inst.free()
		_mesh_parts_cache[object_name] = parts
		return parts

	func _collect_mesh_parts(node: Node, root: Node3D, parent_xform: Transform3D, out_parts: Array) -> void:
		var xform := parent_xform
		if node is Node3D:
			xform = parent_xform * node.transform
		if node is MeshInstance3D and node.mesh and not _has_skeleton_ancestor(node):
			out_parts.append({
				"mesh": node.mesh.duplicate(),
				"local": xform
			})
		for child in node.get_children():
			_collect_mesh_parts(child, root, xform, out_parts)

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
		var dr := Vector3(0.0, deg_to_rad(90.0), 0.0)
		var basis := Basis.from_euler(dr)
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

	func _navigation_mesh_center(nav: NavigationMesh, fallback: Vector3) -> Vector3:
		var verts: PackedVector3Array = nav.get_vertices()
		if verts.is_empty():
			return fallback
		var mn := verts[0]
		var mx := verts[0]
		for v in verts:
			mn = mn.min(v)
			mx = mx.max(v)
		return (mn + mx) * 0.5

	func _navigation_mesh_instance_local(nav: NavigationMesh, rebase_center: Vector3) -> MeshInstance3D:
		var verts: PackedVector3Array = nav.get_vertices()
		var poly_count: int = nav.get_polygon_count()
		if verts.is_empty() or poly_count == 0:
			return null

		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		for poly_idx in range(poly_count):
			var poly_verts: PackedInt32Array = nav.get_polygon(poly_idx)
			if poly_verts.size() < 3:
				continue
			for j in range(1, poly_verts.size() - 1):
				st.add_vertex(verts[poly_verts[0]] - rebase_center)
				st.add_vertex(verts[poly_verts[j]] - rebase_center)
				st.add_vertex(verts[poly_verts[j + 1]] - rebase_center)
		var arr_mesh: ArrayMesh = st.commit()

		var mi := MeshInstance3D.new()
		mi.mesh = arr_mesh
		var mat := StandardMaterial3D.new()
		mat.albedo_color = Color(0.2, 0.95, 0.35)
		mat.cull_mode = BaseMaterial3D.CULL_DISABLED
		mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		mi.material_override = mat
		return mi

	func _navigation_mesh_instance(nav: NavigationMesh, transparent: bool) -> MeshInstance3D:
		var center := _navigation_mesh_center(nav, Vector3.ZERO)
		var mi := _navigation_mesh_instance_local(nav, center)
		if mi == null:
			return _empty_nav_marker()
		if transparent:
			var mat := mi.material_override as StandardMaterial3D
			mat.albedo_color.a = 0.55
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		return mi

	func _compute_bounds(node: Node3D) -> Dictionary:
		var box := [Vector3(INF, INF, INF), Vector3(-INF, -INF, -INF)]
		_accum_bounds(node, Transform3D.IDENTITY, box)
		var min_v: Vector3 = box[0]
		var max_v: Vector3 = box[1]
		if min_v.x == INF:
			return { "center": Vector3.ZERO, "radius": 50.0 }
		var center := (min_v + max_v) * 0.5
		var radius := maxf(maxf(max_v.x - min_v.x, max_v.y - min_v.y), max_v.z - min_v.z) * 0.55
		return { "center": center, "radius": maxf(radius, 10.0) }

	func _accum_bounds(node: Node, parent_xform: Transform3D, box: Array) -> void:
		var xform := parent_xform
		if node is Node3D:
			xform = parent_xform * node.transform
		if node is MeshInstance3D and node.mesh:
			_expand_box_with_mesh(node.mesh, xform, box)
		elif node is MultiMeshInstance3D and node.multimesh and node.multimesh.mesh:
			var mm: MultiMesh = node.multimesh
			for i in range(mm.instance_count):
				_expand_box_with_mesh(mm.mesh, xform * mm.get_instance_transform(i), box)
		for c in node.get_children():
			_accum_bounds(c, xform, box)

	func _expand_box_with_mesh(mesh: Mesh, xform: Transform3D, box: Array) -> void:
		var aabb: AABB = mesh.get_aabb()
		for corner in _aabb_corners(aabb):
			var w: Vector3 = xform * corner
			box[0].x = minf(box[0].x, w.x)
			box[0].y = minf(box[0].y, w.y)
			box[0].z = minf(box[0].z, w.z)
			box[1].x = maxf(box[1].x, w.x)
			box[1].y = maxf(box[1].y, w.y)
			box[1].z = maxf(box[1].z, w.z)

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
			_index_json_placements(by_cell, OBJECT_DATA_DIR + name, "other")
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
		_index_json_placements(by_cell, folder_path + name, "other")
	dir.list_dir_end()

func _index_json_placements(by_cell: Dictionary, path: String, default_category: String) -> void:
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
		var pos := _source_to_godot_pos(coords)
		var euler_for_godot := Vector3(rot.x, -rot.y, rot.z)
		var basis_godot := SOURCE_BASIS * Basis.from_euler(euler_for_godot) * SOURCE_BASIS
		var world_xform := Transform3D(basis_godot, pos)
		var cell := _world_to_cell(pos)
		if not by_cell.has(cell):
			by_cell[cell] = []
		var category := default_category
		if default_category == "other":
			category = _classify_object_name(object_name)
		by_cell[cell].append({
			"object_name": object_name,
			"category": category,
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
	if rec.has("x") or rec.has("y") or rec.has("z"):
		return Vector3(float(rec.get("x", 0.0)), float(rec.get("y", 0.0)), float(rec.get("z", 0.0)))
	return Vector3.ZERO

func _record_rotation(rec: Dictionary) -> Vector3:
	if typeof(rec.get("rotation_euler")) == TYPE_DICTIONARY:
		var rd: Dictionary = rec["rotation_euler"]
		return Vector3(float(rd.get("pitch", 0.0)), float(rd.get("yaw", 0.0)), float(rd.get("roll", 0.0)))
	if rec.has("pitch") or rec.has("yaw") or rec.has("roll"):
		return Vector3(float(rec.get("pitch", 0.0)), float(rec.get("yaw", 0.0)), float(rec.get("roll", 0.0)))
	return Vector3.ZERO

func _source_to_godot_pos(source: Vector3) -> Vector3:
	return SOURCE_BASIS * source

func _source_to_godot_euler(source_pitch_yaw_roll: Vector3) -> Vector3:
	var euler_for_godot := Vector3(source_pitch_yaw_roll.x, -source_pitch_yaw_roll.y, source_pitch_yaw_roll.z)
	var basis_source := Basis.from_euler(euler_for_godot)
	var basis_godot := SOURCE_BASIS * basis_source * SOURCE_BASIS
	return basis_godot.get_euler()

func _world_to_cell(world_pos: Vector3) -> Vector2i:
	var local := world_pos - TERRAIN_ORIGIN
	return Vector2i(floori(local.x / TILE_SIZE), floori(local.z / TILE_SIZE))

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
			master_name = _patch_casing_from_map("%s_%d%d" % [name_from_map, variant1, variant2])

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
		lookup[key] = { "master": master_name, "occurrence": occ, "gx": gx, "gz": gz }
		idx += 1

	return lookup

func _patch_casing_from_map(master_name_lower: String) -> String:
	return master_name_lower.replace("patch", "Patch")

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
