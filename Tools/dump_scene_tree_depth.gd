extends SceneTree

# Headless: print a scene's node tree up to a depth limit.
# Usage: godot --headless -s Tools/dump_scene_tree_depth.gd -- res://path.scn [max_depth]

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.is_empty():
		push_error("pass a res:// scene path after --")
		quit(1)
		return
	var max_depth := 3
	if args.size() > 1:
		max_depth = int(args[1])
	var packed: PackedScene = load(args[0])
	if packed == null:
		push_error("failed to load " + args[0])
		quit(1)
		return
	var root := packed.instantiate()
	_dump(root, 0, max_depth)
	root.free()
	quit(0)

func _dump(node: Node, depth: int, max_depth: int) -> void:
	var extra := ""
	if node.get_script() != null:
		extra = " script=" + str(node.get_script().resource_path.get_file())
	print("  ".repeat(depth) + node.name + " : " + node.get_class() + extra + " children=" + str(node.get_child_count()))
	if depth >= max_depth:
		return
	for child in node.get_children():
		_dump(child, depth + 1, max_depth)
