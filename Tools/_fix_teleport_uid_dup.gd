extends SceneTree

## Mint a unique UID for TeleportWithTarget that does not collide with Teleport.

const TELEPORT := "res://Sphere.Game/WorldObject/Teleport.cs"
const WITH_TARGET := "res://Sphere.Game/WorldObject/TeleportWithTarget.cs"
const WITH_TARGET_SCENE := "res://Godot/Scenes/teleport_with_target.tscn"

func _initialize() -> void:
	var teleport_uid: String = _read_uid_file(TELEPORT)
	var teleport_id: int = ResourceUID.text_to_id(teleport_uid)
	if teleport_id == ResourceUID.INVALID_ID:
		push_error("Teleport.cs.uid is invalid: %s" % teleport_uid)
		quit(1)
		return

	# Ensure Teleport owns its UID.
	if ResourceUID.has_id(teleport_id):
		ResourceUID.set_id(teleport_id, TELEPORT)
	else:
		ResourceUID.add_id(teleport_id, TELEPORT)

	# Drop any old WithTarget mapping (including encoding aliases of the previous UID).
	var old_wt: String = _read_uid_file(WITH_TARGET)
	var old_id: int = ResourceUID.text_to_id(old_wt)
	if old_id != ResourceUID.INVALID_ID and ResourceUID.has_id(old_id):
		var mapped: String = ResourceUID.get_id_path(old_id)
		if mapped == WITH_TARGET or mapped.is_empty():
			ResourceUID.remove_id(old_id)

	# Also clear path_to_uid collision if ResourceLoader still points WithTarget at Teleport's UID.
	var path_uid: String = ResourceUID.path_to_uid(WITH_TARGET)
	if path_uid.begins_with("uid://"):
		var path_id: int = ResourceUID.text_to_id(path_uid)
		if path_id == teleport_id:
			# WithTarget must not share Teleport's id; keep Teleport mapping.
			pass

	var new_id: int = ResourceUID.create_id()
	while new_id == teleport_id or ResourceUID.has_id(new_id):
		new_id = ResourceUID.create_id()

	ResourceUID.add_id(new_id, WITH_TARGET)
	var new_uid: String = ResourceUID.id_to_text(new_id)

	# Round-trip guard: reject aliases that collapse to Teleport or prior id.
	if ResourceUID.text_to_id(new_uid) == teleport_id:
		push_error("Minted UID collapsed to Teleport id")
		quit(1)
		return

	_write_text(WITH_TARGET + ".uid", new_uid + "\n")
	_rewrite_scene_script_uid(WITH_TARGET_SCENE, WITH_TARGET, new_uid)

	print("fixed Teleport=%s" % teleport_uid)
	print("fixed TeleportWithTarget=%s (was %s)" % [new_uid, old_wt])
	print("has Teleport=%s path=%s" % [ResourceUID.has_id(teleport_id), ResourceUID.get_id_path(teleport_id)])
	print("has WithTarget=%s path=%s" % [ResourceUID.has_id(new_id), ResourceUID.get_id_path(new_id)])
	print("path_to_uid Teleport=%s" % ResourceUID.path_to_uid(TELEPORT))
	print("path_to_uid WithTarget=%s" % ResourceUID.path_to_uid(WITH_TARGET))
	quit()


func _read_uid_file(script_path: String) -> String:
	var f := FileAccess.open(ProjectSettings.globalize_path(script_path + ".uid"), FileAccess.READ)
	if f == null:
		return ""
	var t: String = f.get_as_text().strip_edges()
	f.close()
	return t


func _write_text(res_path: String, text: String) -> void:
	var f := FileAccess.open(ProjectSettings.globalize_path(res_path), FileAccess.WRITE)
	if f == null:
		push_error("write failed %s" % res_path)
		return
	f.store_string(text)
	f.close()


func _rewrite_scene_script_uid(scene_path: String, script_path: String, new_uid: String) -> void:
	var abs: String = ProjectSettings.globalize_path(scene_path)
	var f := FileAccess.open(abs, FileAccess.READ)
	if f == null:
		push_error("missing scene %s" % scene_path)
		return
	var text: String = f.get_as_text()
	f.close()
	var re := RegEx.new()
	re.compile('(\\[ext_resource type="Script" uid=")(uid://[a-z0-9]+)(" path="%s")' % script_path)
	var replaced: String = re.sub(text, "\\1%s\\3" % new_uid, true)
	if replaced == text:
		# fallback plain replace of path line
		var re2 := RegEx.new()
		re2.compile('uid="uid://[a-z0-9]+" path="%s"' % script_path)
		replaced = re2.sub(text, 'uid="%s" path="%s"' % [new_uid, script_path], true)
	_write_text(scene_path, replaced)
