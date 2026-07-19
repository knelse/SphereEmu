@tool
extends EditorNode3DGizmoPlugin

const CIRCLE_SEGMENTS := 64
const SLOT_CROSS_SIZE := 1.2
const SLOT_VERTICAL_HEIGHT := 2.0
const SPAWNER_SCRIPT_PATH := "res://Godot/Scripts/Objects/HelperGizmos/AlchemyMaterialSpawner.cs"

var _spawner_script: Script


func _init() -> void:
	create_material("spawn_ok", Color(0.2, 0.95, 0.4, 0.9), false, true)
	create_material("spawn_error", Color(0.95, 0.28, 0.22, 0.95), false, true)
	create_material("slot", Color(0.02, 0.02, 0.02, 0.98), false, true)
	_spawner_script = load(SPAWNER_SCRIPT_PATH) as Script


func _has_gizmo(node: Node3D) -> bool:
	return _is_alchemy_material_spawner(node)


func _redraw(gizmo: EditorNode3DGizmo) -> void:
	var node := gizmo.get_node_3d()
	if not _is_alchemy_material_spawner(node):
		return

	gizmo.clear()

	var has_error := bool(node.get("HasBakeError"))
	var spawn_radius := float(node.get("SpawnRadiusMeters"))
	var baked_slots := _read_baked_spawn_slots(node)

	var spawn_material := get_material("spawn_error" if has_error else "spawn_ok", gizmo)
	gizmo.add_lines(_build_horizontal_circle(spawn_radius), spawn_material, false)

	var slot_material := get_material("slot", gizmo)
	for world_slot in baked_slots:
		gizmo.add_lines(_build_slot_cross(node, world_slot), slot_material, false)


func _is_alchemy_material_spawner(node: Node3D) -> bool:
	if _spawner_script == null:
		return false
	var script := node.get_script()
	return script != null and script == _spawner_script


func _read_baked_spawn_slots(node: Node3D) -> Array[Vector3]:
	var slots: Array[Vector3] = []
	if node.has_method("GetEditorBakedSpawnSlots"):
		var from_script = node.call("GetEditorBakedSpawnSlots")
		if from_script is Array:
			for item in from_script:
				if item is Vector3:
					slots.append(item)
			if not slots.is_empty():
				return slots

	var variant = node.get("BakedSpawnSlots")
	if variant is PackedVector3Array:
		for item in variant:
			slots.append(item)
		return slots
	if variant is Array:
		for item in variant:
			if item is Vector3:
				slots.append(item)
	return slots


func _build_horizontal_circle(radius: float) -> PackedVector3Array:
	var lines := PackedVector3Array()
	lines.resize(CIRCLE_SEGMENTS * 2)
	for i in CIRCLE_SEGMENTS:
		var angle0 := TAU * float(i) / float(CIRCLE_SEGMENTS)
		var angle1 := TAU * float(i + 1) / float(CIRCLE_SEGMENTS)
		lines[i * 2] = Vector3(cos(angle0) * radius, 0.0, sin(angle0) * radius)
		lines[i * 2 + 1] = Vector3(cos(angle1) * radius, 0.0, sin(angle1) * radius)
	return lines


func _build_slot_cross(spawner: Node3D, baked_slot: Vector3) -> PackedVector3Array:
	var center := spawner.to_local(baked_slot)
	var half := SLOT_CROSS_SIZE * 0.5
	var top := center + Vector3(0.0, SLOT_VERTICAL_HEIGHT, 0.0)
	return PackedVector3Array([
		center + Vector3(-half, 0.0, 0.0), center + Vector3(half, 0.0, 0.0),
		center + Vector3(0.0, 0.0, -half), center + Vector3(0.0, 0.0, half),
		center, top,
	])
