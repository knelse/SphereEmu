@tool
extends EditorPlugin

var _gizmo_plugin: EditorNode3DGizmoPlugin
var _inspector_plugin: EditorInspectorPlugin


func _enter_tree() -> void:
	_gizmo_plugin = preload("res://addons/alchemy_material_spawner_gizmo/alchemy_material_spawner_gizmo_plugin.gd").new()
	add_node_3d_gizmo_plugin(_gizmo_plugin)

	_inspector_plugin = preload("res://addons/alchemy_material_spawner_gizmo/alchemy_material_spawner_inspector_plugin.gd").new()
	add_inspector_plugin(_inspector_plugin)


func _exit_tree() -> void:
	if _inspector_plugin != null:
		remove_inspector_plugin(_inspector_plugin)
		_inspector_plugin = null

	if _gizmo_plugin != null:
		remove_node_3d_gizmo_plugin(_gizmo_plugin)
		_gizmo_plugin = null
