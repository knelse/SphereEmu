extends SceneTree

const SCENE_PATH := "res://Godot/Scenes/terrain_scene.scn"

func _initialize() -> void:
	var packed: PackedScene = load(SCENE_PATH)
	var root := packed.instantiate()
	var grid: GridMap = root.get_node("TerrainGrid/Terrain")
	var objects: Node3D = root.get_node("TerrainObjects")
	print("GridMap position:", grid.position)
	print("GridMap cells:", grid.get_used_cells().size())
	print("TerrainObjects children:", objects.get_child_count())
	var total := 0
	var sample := []
	for cat in objects.get_children():
		var cat_count := 0
		var cat_instances := 0
		for mmi in cat.get_children():
			if mmi is MultiMeshInstance3D:
				cat_count += 1
				var mm: MultiMesh = mmi.multimesh
				if mm:
					cat_instances += mm.instance_count
					if sample.size() < 3 and mm.instance_count > 0:
						sample.append({
							"name": mmi.name,
							"count": mm.instance_count,
							"xform0": mm.get_instance_transform(0),
						})
		print("  ", cat.name, " mm_nodes=", cat_count, " instances=", cat_instances)
		total += cat_instances
	print("total instances:", total)
	if sample.size() > 0:
		print("sample transform[0]:", sample[0])
	# Inspect a smaller multimesh for transform spread
	for cat in objects.get_children():
		for mmi in cat.get_children():
			if mmi is MultiMeshInstance3D and mmi.multimesh and mmi.multimesh.instance_count in range(1, 20):
				var mm: MultiMesh = mmi.multimesh
				print("small mm ", mmi.name, " count=", mm.instance_count)
				for i in mini(3, mm.instance_count):
					print("  ", i, mm.get_instance_transform(i))
				break
		break
	root.free()
	quit(0)
