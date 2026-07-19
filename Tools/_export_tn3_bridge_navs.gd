extends SceneTree
## Export nav preview GLBs for every tn3_bridge* into one timestamped folder.
## Same GLB format as export_nav_region_glb.gd. Reads Tools/_tn3_bridge_centers.txt.

const NavGlbMerge = preload("res://Tools/nav_glb_merge.gd")
const NAV_DIR := "res://Godot/Terrain/GeneratedNavMeshes/"
const CENTERS_PATH := "res://Tools/_tn3_bridge_centers.txt"
const OBJECT_ORIGIN_SHIFT := Vector3(4000.0, 0.0, 4000.0)
const RADIUS := 150.0

var _objects_basis := Basis.from_euler(Vector3(0.0, deg_to_rad(-90.0), 0.0))

func _godot_to_nav(p: Vector3) -> Vector3:
	return _objects_basis * p + OBJECT_ORIGIN_SHIFT

func _initialize() -> void:
	if not FileAccess.file_exists(CENTERS_PATH):
		push_error("Missing %s — run _list_tn3_bridges.gd first" % CENTERS_PATH)
		quit(1)
		return
	var dt := Time.get_datetime_dict_from_system()
	var stamp := "%d-%d-%d_%02d-%02d-%02d" % [
		int(dt["year"]), int(dt["month"]), int(dt["day"]),
		int(dt["hour"]), int(dt["minute"]), int(dt["second"]),
	]
	var folder := "D:/1/%s_tn3_bridge_nav" % stamp
	DirAccess.make_dir_recursive_absolute(folder)
	print("Export folder: ", folder)

	var lines := FileAccess.get_file_as_string(CENTERS_PATH).strip_edges().split("\n")
	var nav_to_out_basis := _objects_basis.inverse()
	var radius_sq := RADIUS * RADIUS
	var exported := 0
	var summary := FileAccess.open(folder + "/README.txt", FileAccess.WRITE)
	summary.store_line("tn3_bridge* nav previews (radius=%.0fm, Godot/spawner centers)" % RADIUS)
	summary.store_line("GLB origin = each bridge center. Same format as export_nav_region_glb.gd.")
	summary.store_line("")

	for line in lines:
		if str(line).is_empty():
			continue
		var parts := str(line).split("|")
		if parts.size() < 5:
			continue
		var bi := int(parts[0])
		var bname := parts[1]
		var center_godot := Vector3(float(parts[2]), float(parts[3]), float(parts[4]))
		var center_nav := _godot_to_nav(center_godot)
		var out_path := "%s/%02d_%s_%.0f_%.0f_%.0f_r%.0f.glb" % [
			folder, bi, bname, center_godot.x, center_godot.y, center_godot.z, RADIUS,
		]

		var merge: Dictionary = NavGlbMerge.new_tile_state()
		var matched := 0
		var dir := DirAccess.open(NAV_DIR)
		dir.list_dir_begin()
		var fname := dir.get_next()
		while fname != "":
			if not dir.current_is_dir() and fname.ends_with(".res"):
				var nav := load(NAV_DIR + fname) as NavigationMesh
				if nav != null and _nav_intersects_sphere(nav, center_nav, radius_sq):
					if NavGlbMerge.append_nav_xform(
						merge["nav"], nav, OBJECT_ORIGIN_SHIFT, nav_to_out_basis
					):
						matched += 1
			fname = dir.get_next()
		dir.list_dir_end()

		if matched == 0:
			push_warning("No tiles near ", center_godot)
			summary.store_line("[%d] %s MISSING tiles at %s" % [bi, bname, center_godot])
			continue

		_rebase_bucket(merge["nav"], center_godot)

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
		var ok := _write_glb(root, out_path)
		root.queue_free()
		if ok:
			exported += 1
			print("  [%d] %s tiles=%d -> %s" % [bi, bname, matched, out_path])
			summary.store_line("[%d] %s tiles=%d %s" % [bi, bname, matched, out_path.get_file()])
		else:
			push_error("Failed ", out_path)
			summary.store_line("[%d] %s WRITE FAIL" % [bi, bname])

	summary.store_line("")
	summary.store_line("Exported %d / %d" % [exported, lines.size()])
	summary.close()
	print("Exported ", exported, " / ", lines.size(), " -> ", folder)
	quit(0 if exported > 0 else 1)

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
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_scene(root, state) != OK:
		return false
	if doc.write_to_filesystem(state, path) != OK:
		return false
	return FileAccess.file_exists(path)
