@tool
extends EditorProperty

## Dropdown array editor: stores GameObject IDs, displays Russian names from hint "Name:id,...".

const NONE_LABEL := "(не выбрано)"

var _property_name: StringName
var _option_labels: PackedStringArray = PackedStringArray()
var _option_ids: PackedInt32Array = PackedInt32Array()
var _id_to_index: Dictionary = {}

var _root: VBoxContainer
var _size_spin: SpinBox
var _rows: VBoxContainer
var _updating := false


func setup(property_name: StringName, hint_string: String) -> void:
	_property_name = property_name
	_parse_hint(hint_string)

	_root = VBoxContainer.new()
	_root.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var header := HBoxContainer.new()
	var size_label := Label.new()
	size_label.text = "Size:"
	header.add_child(size_label)

	_size_spin = SpinBox.new()
	_size_spin.min_value = 0
	_size_spin.max_value = 256
	_size_spin.step = 1
	_size_spin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_size_spin.value_changed.connect(_on_size_changed)
	header.add_child(_size_spin)
	_root.add_child(header)

	_rows = VBoxContainer.new()
	_rows.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_root.add_child(_rows)

	add_child(_root)
	set_bottom_editor(_root)


func _parse_hint(hint_string: String) -> void:
	_option_labels.clear()
	_option_ids.clear()
	_id_to_index.clear()

	_option_labels.append(NONE_LABEL)
	_option_ids.append(0)
	_id_to_index[0] = 0

	if hint_string.is_empty():
		return

	var parts := hint_string.split(",", false)
	for part in parts:
		var sep := part.rfind(":")
		if sep <= 0:
			continue
		var label := part.substr(0, sep).strip_edges()
		var id_str := part.substr(sep + 1).strip_edges()
		if label.is_empty() or not id_str.is_valid_int():
			continue
		var id := int(id_str)
		if _id_to_index.has(id):
			continue
		var index := _option_labels.size()
		_option_labels.append(label)
		_option_ids.append(id)
		_id_to_index[id] = index


func _update_property() -> void:
	var ids := _read_ids()
	_updating = true
	_size_spin.value = ids.size()
	_rebuild_rows(ids)
	_updating = false


func _read_ids() -> PackedInt32Array:
	var edited := get_edited_object()
	if edited == null:
		return PackedInt32Array()
	var variant = edited.get(_property_name)
	var result := PackedInt32Array()
	if variant is PackedInt32Array:
		return variant
	if variant is Array:
		for item in variant:
			result.append(int(item))
	return result


func _write_ids(ids: PackedInt32Array) -> void:
	var edited := get_edited_object()
	if edited != null and edited.has_method("SetEditorMaterialIds"):
		var as_array: Array = []
		for id in ids:
			as_array.append(id)
		edited.call("SetEditorMaterialIds", String(_property_name), as_array)
		emit_changed(_property_name, ids)
		return
	emit_changed(_property_name, ids)


func _rebuild_rows(ids: PackedInt32Array) -> void:
	for child in _rows.get_children():
		_rows.remove_child(child)
		child.queue_free()

	for i in ids.size():
		var row := HBoxContainer.new()
		row.size_flags_horizontal = Control.SIZE_EXPAND_FILL

		var index_label := Label.new()
		index_label.text = "%d:" % i
		index_label.custom_minimum_size = Vector2(28, 0)
		row.add_child(index_label)

		var option := OptionButton.new()
		option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		for label_i in _option_labels.size():
			option.add_item(_option_labels[label_i], label_i)
		var selected := int(_id_to_index.get(ids[i], 0))
		option.select(selected)
		option.item_selected.connect(_on_item_selected.bind(i, option))
		row.add_child(option)

		_rows.add_child(row)


func _on_size_changed(value: float) -> void:
	if _updating:
		return
	var new_size := int(value)
	var ids := _read_ids()
	var next := PackedInt32Array()
	next.resize(new_size)
	for i in new_size:
		next[i] = ids[i] if i < ids.size() else 0
	_write_ids(next)


func _on_item_selected(_index: int, row_index: int, option: OptionButton) -> void:
	if _updating:
		return
	var selected := option.get_selected_id()
	if selected < 0 or selected >= _option_ids.size():
		return
	var ids := _read_ids()
	if row_index < 0 or row_index >= ids.size():
		return
	ids[row_index] = _option_ids[selected]
	_write_ids(ids)
