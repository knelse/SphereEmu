extends SceneTree

# Re-bakes per-tile navigation meshes to external .res files under
# Godot/Terrain/GeneratedNavMeshes/ using the REAL TerrainObjectsFill.RebuildTerrainObjects() +
# TerrainNavigationBaker.BakeTerrainNavigation() pipeline (same code the editor button runs), but
# WITHOUT resaving terrain_scene.scn — safe to run --headless.
#
# rebuild_terrain_objects_headless.gd cannot be run --headless because Godot's dummy headless
# renderer silently ignores MultiMesh.set_instance_transform(), corrupting the SAVED visual
# MultiMesh instance buffers. This script only needs RebuildTerrainObjects() to repopulate
# TerrainObjectsFill.LastBuiltObjectColliderFacesByTile in memory (built directly from JSON +
# mesh/collider data, not by reading MultiMesh instances back), then feeds that into
# BakeTerrainNavigation() and quits without ever packing/saving the scene — so it doesn't matter
# that the visual multimeshes in this throwaway in-memory instance are wrong.
#
# Usage: godot --path . --headless -s Tools/rebake_nav_only_headless.gd

const SCENE_PATH := "res://Godot/Scenes/terrain_scene.scn"
const NAV_BAKER_SCRIPT := "res://Godot/Scripts/Terrain/Fill/TerrainNavigationBaker.cs"

func _initialize() -> void:
	var packed: PackedScene = load(SCENE_PATH)
	var root := packed.instantiate()

	var objects_fill := root.get_node("TerrainObjects")
	objects_fill.set("UpdateWalkSurfaceObjectFootprintsOnRebuild", false)

	print("RebuildTerrainObjects starting (in-memory only, visuals not saved)...")
	var t0 := Time.get_ticks_msec()
	objects_fill.call("RebuildTerrainObjects")
	print("RebuildTerrainObjects done in %.1fs" % ((Time.get_ticks_msec() - t0) / 1000.0))

	var script: Script = load(NAV_BAKER_SCRIPT)
	var nav_baker := Node3D.new()
	nav_baker.name = "TerrainNavigationBaker"
	nav_baker.set_script(script)
	nav_baker.set("PersistRegionsInScene", false)
	root.add_child(nav_baker)

	print("BakeTerrainNavigation starting...")
	var t1 := Time.get_ticks_msec()
	var baked = nav_baker.call("BakeTerrainNavigation")
	print("BakeTerrainNavigation done in %.1fs (%s nav mesh files)" % [(Time.get_ticks_msec() - t1) / 1000.0, baked])

	root.free()
	quit(0)
