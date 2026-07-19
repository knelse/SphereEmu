@tool
extends EditorPlugin

var _gizmo_plugin: EditorNode3DGizmoPlugin


func _enter_tree() -> void:
	_gizmo_plugin = preload("res://addons/monster_spawner_gizmo/monster_spawner_gizmo_plugin.gd").new()
	add_node_3d_gizmo_plugin(_gizmo_plugin)


func _exit_tree() -> void:
	if _gizmo_plugin != null:
		remove_node_3d_gizmo_plugin(_gizmo_plugin)
		_gizmo_plugin = null
