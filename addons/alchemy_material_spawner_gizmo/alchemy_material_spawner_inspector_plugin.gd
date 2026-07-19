@tool
extends EditorInspectorPlugin

const SPAWNER_SCRIPT_PATH := "res://Godot/Scripts/Objects/HelperGizmos/AlchemyMaterialSpawner.cs"
const ID_PROPERTIES := [
	"PlantGameObjectIds",
	"MetalGameObjectIds",
	"MineralGameObjectIds",
]

var _spawner_script: Script


func _init() -> void:
	_spawner_script = load(SPAWNER_SCRIPT_PATH) as Script


func _can_handle(object: Object) -> bool:
	if object == null or _spawner_script == null:
		return false
	var script := object.get_script()
	return script != null and script == _spawner_script


func _parse_property(
	object: Object,
	_type: Variant.Type,
	name: String,
	_hint_type: PropertyHint,
	_hint_string: String,
	_usage_flags: PropertyUsageFlags,
	_wide: bool
) -> bool:
	if name not in ID_PROPERTIES:
		return false

	var hint := ""
	if object.has_method("GetEditorMaterialPickerHint"):
		hint = str(object.call("GetEditorMaterialPickerHint", name))

	var editor := preload("res://addons/alchemy_material_spawner_gizmo/alchemy_material_id_array_editor.gd").new()
	editor.setup(StringName(name), hint)
	add_property_editor(name, editor)
	return true
