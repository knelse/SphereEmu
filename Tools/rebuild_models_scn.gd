extends SceneTree

# Convert staged/current GLBs under Godot/Models into sibling .scn PackedScenes.
#
# Usage (via Tools/rebuild_models_scn.ps1):
#   godot -s Tools/rebuild_models_scn.gd --path . -- --from-models
#   godot -s Tools/rebuild_models_scn.gd --path . -- --from-models --filter Basket1

const MODELS_DIR := "res://Godot/Models/"

func _has_arg(flag: String) -> bool:
	return flag in OS.get_cmdline_args() or flag in OS.get_cmdline_user_args()


func _arg_value(flag: String) -> String:
	var args := OS.get_cmdline_user_args()
	if args.is_empty():
		args = OS.get_cmdline_args()
	for i in range(args.size()):
		if args[i] == flag and i + 1 < args.size():
			return args[i + 1]
	return ""


func _initialize() -> void:
	var filter := _arg_value("--filter").strip_edges().to_lower()
	var glbs := _collect_glbs(MODELS_DIR, filter)
	print("rebuild_models_scn: converting %s GLB(s) under %s" % [glbs.size(), MODELS_DIR])
	if glbs.is_empty():
		push_error("No GLBs found to convert (filter='%s')" % filter)
		quit(1)
		return

	var ok := 0
	var fail := 0
	for glb_path in glbs:
		var scn_path: String = glb_path.get_basename() + ".scn"
		var packed: PackedScene = load(glb_path) as PackedScene
		if packed == null:
			push_error("FAILED load %s" % glb_path)
			fail += 1
			continue
		var err := ResourceSaver.save(packed, scn_path)
		if err != OK:
			push_error("FAILED save %s (%s)" % [scn_path, err])
			fail += 1
			continue
		ok += 1
		if ok % 50 == 0:
			print("  saved %s / %s…" % [ok, glbs.size()])

	print("rebuild_models_scn: done ok=%s fail=%s" % [ok, fail])
	quit(0 if fail == 0 else 1)


func _collect_glbs(res_dir: String, filter_lower: String) -> PackedStringArray:
	var out: PackedStringArray = []
	_walk(res_dir, filter_lower, out)
	out.sort()
	return out


func _walk(res_dir: String, filter_lower: String, out: PackedStringArray) -> void:
	var da := DirAccess.open(res_dir)
	if da == null:
		return
	da.list_dir_begin()
	while true:
		var name := da.get_next()
		if name == "":
			break
		if name.begins_with("."):
			continue
		var path := res_dir.path_join(name)
		if da.current_is_dir():
			_walk(path + "/", filter_lower, out)
			continue
		if not name.to_lower().ends_with(".glb"):
			continue
		if filter_lower != "" and path.get_file().get_basename().to_lower() != filter_lower \
				and not path.to_lower().contains(filter_lower):
			continue
		out.append(path)
	da.list_dir_end()
