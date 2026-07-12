extends SceneTree

# TEMP TEST (not part of the pipeline): rebuild terrain objects (no save) + run the full
# navigation bake, writing NavigationMesh resources to a throwaway directory, to sanity-check
# timing/correctness before touching the real terrain_scene.scn. Safe to delete after use.
# Usage: godot --headless -s Tools/_test_nav_bake.gd --path .

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
	objects_fill.call("DebugPrintColliderFaceStats")

	var script: Script = load(NAV_BAKER_SCRIPT)
	var nav_baker := Node3D.new()
	nav_baker.name = "TerrainNavigationBaker"
	nav_baker.set_script(script)
	nav_baker.set("NavMeshResourcesDirectory", "res://Godot/Terrain/_TestGeneratedNavMeshes/")
	root.add_child(nav_baker)

	print("BakeTerrainNavigation starting...")
	var t1 := Time.get_ticks_msec()
	var baked = nav_baker.call("BakeTerrainNavigation")
	print("BakeTerrainNavigation done in %.1fs (%s regions)" % [(Time.get_ticks_msec() - t1) / 1000.0, baked])

	root.free()
	quit(0)
