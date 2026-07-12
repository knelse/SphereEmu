extends SceneTree

# Headless: regenerate terrain_scene.scn's TerrainObjects subtree (multimesh visuals only) and
# bake per-tile navigation meshes to external .res files under Godot/Terrain/GeneratedNavMeshes/.
#
# The saved scene intentionally does NOT contain NavigationRegion3D nodes (or a
# TerrainNavigationBaker node): thousands of regions made the editor hang for minutes syncing
# navigation while multimesh visuals never finished loading.
#
# Object colliders are never scene nodes - TerrainObjectsFill accumulates triangle geometry in
# memory; TerrainNavigationBaker feeds that plus ground tile meshes into NavigationServer3D.
#
# Usage: godot --headless -s Tools/rebuild_terrain_objects_headless.gd --path .

const SCENE_PATH := "res://Godot/Scenes/terrain_scene.scn"
const NAV_BAKER_SCRIPT := "res://Godot/Scripts/Terrain/Fill/TerrainNavigationBaker.cs"

func _initialize() -> void:
	var packed: PackedScene = load(SCENE_PATH)
	var root := packed.instantiate()

	var objects_fill := root.get_node("TerrainObjects")
	objects_fill.set("UpdateWalkSurfaceObjectFootprintsOnRebuild", false)

	print("RebuildTerrainObjects starting...")
	var t0 := Time.get_ticks_msec()
	objects_fill.call("RebuildTerrainObjects")
	print("RebuildTerrainObjects done in %.1fs" % ((Time.get_ticks_msec() - t0) / 1000.0))

	for i in range(4):
		await process_frame

	# Ephemeral baker: write nav .res files only, do not attach regions to the scene we save.
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

	# Strip any leftover nav nodes from a previous broken save.
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

	root.free()
	quit(0)

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
