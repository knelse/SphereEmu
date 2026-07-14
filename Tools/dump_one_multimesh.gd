extends SceneTree

func _initialize() -> void:
	var path := "res://Godot/Terrain/GeneratedMultiMeshes/TerrainPlants/grass_02m_MM_0.res"
	var mm: MultiMesh = load(path)
	print("loaded ", path, " instances=", mm.instance_count if mm else -1)
	if mm:
		print("transform_format=", mm.transform_format)
		for i in mini(5, mm.instance_count):
			print(i, mm.get_instance_transform(i))
	quit(0)
