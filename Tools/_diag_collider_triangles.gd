extends SceneTree

# TEMP DIAGNOSTIC: rebuild terrain objects (no save) and report which objects contribute the most
# collider triangles, to inform how to reduce the ~51M total merged collider triangle count.

const SCENE_PATH := "res://Godot/Scenes/terrain_scene.scn"

func _initialize() -> void:
	var packed: PackedScene = load(SCENE_PATH)
	var root := packed.instantiate()

	var objects_fill := root.get_node("TerrainObjects")
	objects_fill.set("UpdateWalkSurfaceObjectFootprintsOnRebuild", false)

	print("RebuildTerrainObjects starting (diagnostic run, no save)...")
	objects_fill.call("RebuildTerrainObjects")

	var diag: Dictionary = objects_fill.get("DiagTriCountByObjectName")
	var entries := []
	var total_tris := 0
	var total_instances := 0
	for object_name in diag.keys():
		var pair = diag[object_name]
		var tris: int = pair[0]
		var instances: int = pair[1]
		entries.append([object_name, tris, instances])
		total_tris += tris
		total_instances += instances

	entries.sort_custom(func(a, b): return a[1] > b[1])

	print("=== Top 40 objects by TOTAL collider triangles (merged across all placements) ===")
	print("%-30s %14s %10s %14s" % ["object_name", "total_tris", "instances", "tris/instance"])
	for i in range(min(40, entries.size())):
		var e = entries[i]
		var per_inst := 0.0
		if e[2] > 0:
			per_inst = float(e[1]) / float(e[2])
		print("%-30s %14d %10d %14.1f" % [e[0], e[1], e[2], per_inst])

	print("")
	print("TOTAL objects with colliders: ", entries.size())
	print("TOTAL instances with colliders: ", total_instances)
	print("TOTAL collider triangles (sum): ", total_tris)

	root.free()
	quit(0)
