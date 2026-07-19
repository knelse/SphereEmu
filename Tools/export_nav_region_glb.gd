extends SceneTree
## Export a merged outdoor NavigationMesh preview GLB near a dump/JSON center.
## --center is ObjectDataJson / spawner-name space (pre-SOURCE_BASIS). Conversion matches bake:
##   nav = Ry(-90°) * (SOURCE_BASIS * center) + (4000,0,4000)
## Outdoor .res verts are in that nav/grid frame; the GLB is written back in dump/JSON space.
##
## Usage:
##   godot --path . --headless -s Tools/export_nav_region_glb.gd -- \
##     --center 1200 -173 -1400 --radius 200 --out D:/1/bridge_nav.glb

const NavGlbMerge = preload("res://Tools/nav_glb_merge.gd")
const NAV_DIR := "res://Godot/Terrain/GeneratedNavMeshes/"
const OBJECT_ORIGIN_SHIFT := Vector3(4000.0, 0.0, 4000.0)
## Same as bake_and_export_single_nav.gd: dump (x,y,z) → Godot (x,-y,-z).
const SOURCE_BASIS := Basis(Vector3.RIGHT, Vector3.DOWN, Vector3.FORWARD)

# Dump/JSON space (ObjectDataJson). Outdoor heights are typically positive here; SOURCE_BASIS
# flips Y into Godot/nav (e.g. dump +173 → nav -173). Default is the cliff bridge sample point.
var _center_source := Vector3(1200.0, 173.0, -1400.0)
var _radius := 200.0
var _out_path := ""
var _objects_basis := Basis.from_euler(Vector3(0.0, deg_to_rad(-90.0), 0.0))


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var i := 0
	while i < args.size():
		if args[i] == "--center" and i + 3 < args.size():
			_center_source = Vector3(float(args[i + 1]), float(args[i + 2]), float(args[i + 3]))
			i += 4
		elif args[i] == "--radius" and i + 1 < args.size():
			_radius = float(args[i + 1])
			i += 2
		elif args[i] == "--out" and i + 1 < args.size():
			_out_path = args[i + 1]
			i += 2
		else:
			i += 1

	if _out_path.is_empty():
		var stamp := Time.get_datetime_string_from_system().replace(":", "-").replace("T", "_")
		var folder := "D:/1/%s_bridge-nav-r%d" % [stamp, int(_radius)]
		_out_path = "%s/bridge_nav_%.0f_%.0f_%.0f_r%.0f.glb" % [
			folder, _center_source.x, _center_source.y, _center_source.z, _radius,
		]

	var center_godot := SOURCE_BASIS * _center_source
	var center_nav := _godot_to_nav(center_godot)

	DirAccess.make_dir_recursive_absolute(_out_path.get_base_dir())
	print(
		"Export nav region dump=%s godot=%s nav=%s radius=%.1f -> %s" % [
			_center_source, center_godot, center_nav, _radius, _out_path,
		]
	)

	var merge: Dictionary = NavGlbMerge.new_tile_state()
	var radius_sq := _radius * _radius
	var matched := 0
	var scanned := 0
	var dir := DirAccess.open(NAV_DIR)
	if dir == null:
		push_error("Cannot open %s" % NAV_DIR)
		quit(1)
		return

	# nav → dump/JSON: SOURCE_BASIS * Ry(+90°) * (nav - shift)
	# append_nav_xform does basis*(v - rebase); SOURCE_BASIS is an involution.
	var nav_to_dump_basis := SOURCE_BASIS * _objects_basis.inverse()

	dir.list_dir_begin()
	var fname := dir.get_next()
	while fname != "":
		if not dir.current_is_dir() and fname.ends_with(".res"):
			scanned += 1
			var path := NAV_DIR + fname
			var nav := load(path) as NavigationMesh
			if nav != null and _nav_intersects_sphere(nav, center_nav, radius_sq):
				if NavGlbMerge.append_nav_xform(
					merge["nav"], nav, OBJECT_ORIGIN_SHIFT, nav_to_dump_basis
				):
					matched += 1
					print("  + %s polys=%d" % [fname.get_basename(), nav.get_polygon_count()])
		fname = dir.get_next()
	dir.list_dir_end()

	if matched == 0:
		push_error(
			"No nav tiles intersected nav-center=%s r=%.1f (scanned %d)" % [
				center_nav, _radius, scanned,
			]
		)
		quit(2)
		return

	# Rebase exported dump/JSON verts around the requested center so the GLB sits near origin.
	_rebase_bucket(merge["nav"], _center_source)

	var root := Node3D.new()
	root.name = "NavRegionPreview"
	var nav_root := Node3D.new()
	nav_root.name = "NavMesh"
	root.add_child(nav_root)
	var terrain_root := Node3D.new()
	terrain_root.name = "Terrain"
	root.add_child(terrain_root)
	NavGlbMerge.finalize_tile_meshes(terrain_root, nav_root, merge)

	get_root().add_child(root)
	var ok := _write_glb(root, _out_path)
	root.queue_free()
	if ok:
		print("Saved %s (tiles=%d / scanned=%d)" % [_out_path, matched, scanned])
		quit(0)
	else:
		push_error("Failed to write %s" % _out_path)
		quit(3)


func _godot_to_nav(p: Vector3) -> Vector3:
	return _objects_basis * p + OBJECT_ORIGIN_SHIFT


func _nav_intersects_sphere(nav: NavigationMesh, center_nav: Vector3, radius_sq: float) -> bool:
	var verts := nav.get_vertices()
	if verts.is_empty():
		return false
	var mn := verts[0]
	var mx := verts[0]
	for v in verts:
		mn = mn.min(v)
		mx = mx.max(v)
	var closest := center_nav.clamp(mn, mx)
	return closest.distance_squared_to(center_nav) <= radius_sq


func _rebase_bucket(entry: Dictionary, center: Vector3) -> void:
	var verts: PackedVector3Array = entry["vertices"]
	for i in range(verts.size()):
		verts[i] = verts[i] - center
	entry["vertices"] = verts


func _write_glb(root: Node, path: String) -> bool:
	print("Writing GLB...")
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_scene(root, state)
	if err != OK:
		push_error("GLTF append_from_scene failed err=%s" % error_string(err))
		return false
	err = doc.write_to_filesystem(state, path)
	if err != OK:
		push_error("GLTF write_to_filesystem failed err=%s" % error_string(err))
		return false
	if not FileAccess.file_exists(path):
		push_error("GLB missing after write: %s" % path)
		return false
	print("  wrote %s (%d bytes)" % [path, FileAccess.get_file_as_bytes(path).size()])
	return true
