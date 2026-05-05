@tool
extends EditorPlugin

const MAX_PICK_DISTANCE := 50000.0

var _dock: Control
var _label: Label


func _enter_tree() -> void:
	# Make sure we get forwarded 3D input even when other tools are active.
	set_input_event_forwarding_always_enabled()

	_dock = VBoxContainer.new()
	_dock.name = "MultiMesh Picker"
	_label = Label.new()
	_label.text = "MultiMeshPicker loaded. Click in 3D viewport."
	_dock.add_child(_label)
	add_control_to_bottom_panel(_dock, "MultiMesh Picker")


func _exit_tree() -> void:
	if _dock:
		remove_control_from_bottom_panel(_dock)
		_dock.queue_free()
	_dock = null
	_label = null


func _set_text(t: String) -> void:
	if _label:
		_label.text = t


func _input(event: InputEvent) -> void:
	# Debug: confirm the plugin is receiving any mouse input at all.
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var pos: Vector2 = event.position
		_set_text("MultiMeshPicker: _input click at %s" % str(pos))


func _forward_3d_gui_input(camera: Camera3D, event: InputEvent) -> int:
	# This is reliably called for GDScript editor plugins.
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var pos: Vector2 = event.position
		_set_text("MultiMeshPicker: 3D click at %s (camera='%s')" % [str(pos), camera.name])

		var hit := _try_pick(camera, pos)
		if hit.is_empty():
			_set_text("MultiMeshPicker: no hit at %s" % str(pos))
			return 0

		var selection := get_editor_interface().get_selection()
		selection.clear()
		selection.add_node(hit["node"])

		_set_text("Hit: node='%s' instance=%d sourceRecordIndex=%d" % [
			hit["node"].name,
			hit["instance"],
			hit["sourceRecordIndex"]
		])
		return 1

	return 0


func _try_pick(camera: Camera3D, mouse_pos: Vector2) -> Dictionary:
	var ray_origin := camera.project_ray_origin(mouse_pos)
	var ray_dir := camera.project_ray_normal(mouse_pos).normalized()

	var root := get_editor_interface().get_edited_scene_root()
	if root == null:
		return {}

	var mmis: Array = []
	_collect_multimesh_instances(root, mmis)

	var best_t := INF
	var best_node: MultiMeshInstance3D = null
	var best_instance := -1
	var best_source := -1

	for mmi in mmis:
		var mm: MultiMesh = mmi.multimesh
		if mm == null:
			continue
		var mesh: Mesh = mm.mesh
		if mesh == null:
			continue

		var local_aabb = mesh.get_aabb()
		var count := mm.instance_count
		for i in range(count):
			var world_xform: Transform3D = mmi.global_transform * mm.get_instance_transform(i)
			var world_aabb := _transform_aabb(local_aabb, world_xform)
			var t := _ray_aabb_t(ray_origin, ray_dir, world_aabb)
			if t < 0.0 or t > MAX_PICK_DISTANCE:
				continue
			if t >= best_t:
				continue

			best_t = t
			best_node = mmi
			best_instance = i

			var cd: Color = mm.get_instance_custom_data(i)
			var r := int(round(cd.r * 255.0))
			var g := int(round(cd.g * 255.0))
			var b := int(round(cd.b * 255.0))
			best_source = r | (g << 8) | (b << 16)

	if best_node == null:
		return {}

	return {
		"node": best_node,
		"instance": best_instance,
		"sourceRecordIndex": best_source,
	}


func _collect_multimesh_instances(n: Node, out_list: Array) -> void:
	if n is MultiMeshInstance3D:
		out_list.append(n)
	for c in n.get_children():
		if c is Node:
			_collect_multimesh_instances(c, out_list)


func _ray_aabb_t(origin: Vector3, dir: Vector3, aabb: AABB) -> float:
	var tmin := -INF
	var tmax := INF

	var r := _slab_update(origin.x, dir.x, aabb.position.x, aabb.position.x + aabb.size.x, tmin, tmax)
	if not r[0]:
		return INF
	tmin = r[1]
	tmax = r[2]

	r = _slab_update(origin.y, dir.y, aabb.position.y, aabb.position.y + aabb.size.y, tmin, tmax)
	if not r[0]:
		return INF
	tmin = r[1]
	tmax = r[2]

	r = _slab_update(origin.z, dir.z, aabb.position.z, aabb.position.z + aabb.size.z, tmin, tmax)
	if not r[0]:
		return INF
	tmin = r[1]
	tmax = r[2]

	if tmax < 0.0:
		return INF

	return tmin if tmin >= 0.0 else tmax


# Returns [ok: bool, tmin: float, tmax: float]
func _slab_update(origin: float, dir: float, minv: float, maxv: float, tmin: float, tmax: float) -> Array:
	var eps := 1e-8
	if abs(dir) < eps:
		return [origin >= minv and origin <= maxv, tmin, tmax]

	var inv := 1.0 / dir
	var t1 := (minv - origin) * inv
	var t2 := (maxv - origin) * inv
	if t1 > t2:
		var tmp := t1
		t1 = t2
		t2 = tmp

	if t1 > tmin:
		tmin = t1
	if t2 < tmax:
		tmax = t2

	return [tmin <= tmax, tmin, tmax]


func _transform_aabb(local: AABB, xform: Transform3D) -> AABB:
	var p := local.position
	var s := local.size

	var corners := [
		Vector3(p.x, p.y, p.z),
		Vector3(p.x + s.x, p.y, p.z),
		Vector3(p.x, p.y + s.y, p.z),
		Vector3(p.x, p.y, p.z + s.z),
		Vector3(p.x + s.x, p.y + s.y, p.z),
		Vector3(p.x + s.x, p.y, p.z + s.z),
		Vector3(p.x, p.y + s.y, p.z + s.z),
		Vector3(p.x + s.x, p.y + s.y, p.z + s.z),
	]

	var minv := Vector3(INF, INF, INF)
	var maxv := Vector3(-INF, -INF, -INF)
	for c in corners:
		var w: Vector3 = xform * c
		minv = Vector3(min(minv.x, w.x), min(minv.y, w.y), min(minv.z, w.z))
		maxv = Vector3(max(maxv.x, w.x), max(maxv.y, w.y), max(maxv.z, w.z))

	return AABB(minv, maxv - minv)

