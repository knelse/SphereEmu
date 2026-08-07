extends SceneTree

# Regenerate terrain_scene.scn's TerrainObjects subtree (multimesh visuals only) and
# optionally bake per-tile navigation meshes to TerrainBake/GeneratedNavMeshes/.
#
# IMPORTANT: do NOT pass --headless when running this script. Godot's dummy headless renderer
# silently ignores MultiMesh.set_instance_transform(), so a headless rebuild saves .res files
# with instance_count set but empty transform buffers (~9 KB instead of ~1 MB per mesh batch).
#
# Usage:
#   godot -s Tools/rebuild_terrain_objects_headless.gd --path .
#   godot -s Tools/rebuild_terrain_objects_headless.gd --path . -- --objects-only

const SCENE_PATH := "res://Godot/Scenes/terrain_scene.scn"
const NAV_BAKER_SCRIPT := "res://Godot/Scripts/Terrain/Fill/TerrainNavigationBaker.cs"
const STUB_LIST_PATH := "res://Tools/_tmp/legacy_mm_paths.txt"

func _has_arg(flag: String) -> bool:
	var args := OS.get_cmdline_args()
	var user_args := OS.get_cmdline_user_args()
	return flag in args or flag in user_args

func _initialize() -> void:
	var objects_only := _has_arg("--objects-only")

	# Old MultiMesh .res files (TerrainBake copies) reference deleted Godot/Models/*.png sidecars.
	# Write dependency-free stubs at the legacy ExtResource paths so PackedScene can load; rebuild
	# then regenerates real MultiMeshes from GLBs into TerrainBake/.
	var stub_count := _ensure_legacy_multimesh_stubs()
	print("Legacy MultiMesh stubs ready: %s" % stub_count)

	var packed: PackedScene = load(SCENE_PATH)
	if packed == null:
		push_error("Failed to load %s after writing stubs" % SCENE_PATH)
		quit(1)
		return

	var root := packed.instantiate()
	if root == null:
		push_error("instantiate() returned null for %s" % SCENE_PATH)
		quit(1)
		return

	var objects_fill := root.get_node_or_null("TerrainObjects")
	if objects_fill == null:
		push_error("TerrainObjects node not found under %s" % SCENE_PATH)
		root.free()
		quit(1)
		return

	print("RebuildTerrainObjects starting...")
	var t0 := Time.get_ticks_msec()
	objects_fill.call("RebuildTerrainObjects")
	print("RebuildTerrainObjects done in %.1fs" % ((Time.get_ticks_msec() - t0) / 1000.0))

	for i in range(4):
		await process_frame

	if not objects_only:
		var script: Script = load(NAV_BAKER_SCRIPT)
		var nav_baker := Node3D.new()
		nav_baker.name = "TerrainNavigationBaker"
		nav_baker.set_script(script)
		nav_baker.set("PersistRegionsInScene", false)
		root.add_child(nav_baker)

		print("BakeTerrainNavigation starting (files only)...")
		var t1 := Time.get_ticks_msec()
		var baked = nav_baker.call("BakeTerrainNavigation")
		print("BakeTerrainNavigation done in %.1fs (%s nav mesh files)" % [(Time.get_ticks_msec() - t1) / 1000.0, baked])

		root.remove_child(nav_baker)
		nav_baker.queue_free()

		for i in range(4):
			await process_frame
	else:
		print("Skipping BakeTerrainNavigation (--objects-only)")

	var stale_baker := root.get_node_or_null("TerrainNavigationBaker")
	if stale_baker:
		root.remove_child(stale_baker)
		stale_baker.queue_free()
	for i in range(4):
		await process_frame

	_force_own(root, root)

	var new_packed := PackedScene.new()
	var err := new_packed.pack(root)
	if err != OK:
		push_error("pack() failed: %s" % err)
		root.free()
		quit(1)
		return

	err = ResourceSaver.save(new_packed, SCENE_PATH)
	if err != OK:
		push_error("save() failed: %s" % err)
		root.free()
		quit(1)
		return

	print("Saved ", SCENE_PATH)
	_print_stats(objects_fill)

	_remove_legacy_multimesh_tree()

	root.free()
	quit(0)

func _ensure_legacy_multimesh_stubs() -> int:
	var list_abs := ProjectSettings.globalize_path(STUB_LIST_PATH)
	if not FileAccess.file_exists(list_abs):
		# Already-migrated scenes have no legacy ExtResources; list may be absent/empty.
		return 0

	var file := FileAccess.open(list_abs, FileAccess.READ)
	if file == null:
		return 0

	var created := 0
	while not file.eof_reached():
		var path := file.get_line().strip_edges()
		# Skip UTF-8 BOM / blanks
		if path.is_empty() or not path.begins_with("res://"):
			continue

		var dir_path := path.get_base_dir()
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(dir_path))

		var mm := MultiMesh.new()
		var save_err := ResourceSaver.save(mm, path)
		if save_err != OK:
			push_warning("Failed to stub MultiMesh (%s): %s" % [save_err, path])
		else:
			created += 1

	file.close()
	return created

func _remove_legacy_multimesh_tree() -> void:
	var abs_dir := ProjectSettings.globalize_path("res://Godot/Terrain/GeneratedMultiMeshes")
	if DirAccess.dir_exists_absolute(abs_dir):
		_remove_dir_recursive(abs_dir)
		print("Removed legacy stub tree: ", abs_dir)

func _remove_dir_recursive(abs_path: String) -> void:
	var da := DirAccess.open(abs_path)
	if da == null:
		return
	da.list_dir_begin()
	while true:
		var name := da.get_next()
		if name == "":
			break
		if name == "." or name == "..":
			continue
		var child := abs_path.path_join(name)
		if da.current_is_dir():
			_remove_dir_recursive(child)
		else:
			DirAccess.remove_absolute(child)
	da.list_dir_end()
	DirAccess.remove_absolute(abs_path)

func _force_own(node: Node, root: Node) -> void:
	if node != root and node.owner == null:
		node.owner = root
	for child in node.get_children():
		_force_own(child, root)

func _print_stats(objects_fill: Node) -> void:
	for name in ["TerrainPlants", "TerrainRocks", "TerrainOther", "ExtraInstancedGroups"]:
		var n := objects_fill.get_node_or_null(name)
		if n:
			print(name, ": ", n.get_child_count(), " children")
