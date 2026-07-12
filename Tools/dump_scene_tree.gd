extends SceneTree

# Headless check: load an imported GLB and print its node tree with types,
# to verify '-col' suffixes produce StaticBody3D/CollisionShape3D nodes.
# Usage: godot --headless -s Tools/dump_scene_tree.gd -- res://Godot/Models/bochka3.glb

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.is_empty():
		push_error("pass a res:// scene path after --")
		quit(1)
		return
	var packed: PackedScene = load(args[0])
	if packed == null:
		push_error("failed to load " + args[0])
		quit(1)
		return
	var root := packed.instantiate()
	_dump(root, 0)
	root.free()
	quit(0)

func _dump(node: Node, depth: int) -> void:
	var line := "  ".repeat(depth) + node.name + " : " + node.get_class()
	if node is CollisionShape3D and node.shape != null:
		line += " shape=" + node.shape.get_class()
	print(line)
	for child in node.get_children():
		_dump(child, depth + 1)
