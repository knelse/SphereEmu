@tool
extends EditorPlugin

const _TerrainBake := preload("res://Tools/terrain_bake_paths.gd")

## Editor chunk streaming in GDScript (C# addon scripts are Compile-Remove'd from SphServer
## to keep EditorPlugin out of the headless game assembly).

const TILE_SIZE := 100.0
## Slightly tighter than before to cut work near the camera.
const LOAD_RADIUS := 180.0
const QUEUE_REFRESH_SEC := 0.25
## Camera stream: keep hitch small (threaded, one in flight).
const MAX_INSTANTIATES_PER_FRAME := 2
## Load All: sync batch per frame (bulk op; hitch is acceptable).
const MAX_LOAD_ALL_PER_FRAME := 12
const CHUNKS_ROOT := "res://Godot/World/Chunks"
var GROUND_CHUNKS_ROOT : String = _TerrainBake.ground_chunks()
const GROUND_KIND := "__ground__"
const GROUND_STREAMED_NAME := "TerrainGroundStreamed"
const MAIN_SERVER_PATH := "res://Godot/Scenes/MainServer.tscn"
const SAVE_BACKUP_PATH := "user://mainserver_pre_save_backup.tscn"
const SAVE_GUARD_NAME := "__WorldChunkSaveGuard"

## kind folder -> node path under MainServer
const KIND_PARENTS := {
	"monster": "MonsterSpawners",
	"door": "Doors",
	"door_key": "DoorsWithKey",
	"npc": "NPCs",
	"alchemy": "AlchemyMaterialSpawners",
	"dungeon_entrance": "DungeonEntrances",
	"workshop": "Workshops",
	"light_crystal": "ItemsOnGround/LightСrystals",
	"castle_chest": "Castles/CastleChests",
	"castle_elixir_pillar": "Castles/CastleElixirPillars",
	"castle_tablet": "Castles/CastleTablets",
	"castle_gate": "Castles/CastleGates",
	"castle_teleport": "Castles/CastleTeleports",
	"castle_entrance": "Castles/CastleEntrances",
	"teleport": "Teleports/TeleportsRegularTargetTournament",
	"teleport_wild": "Teleports/TeleportsWild",
	"teleport_broken": "Teleports/TeleportsBroken",
	"teleport_in_dungeon": "Teleports/TeleportsInDungeon",
	"teleport_dungeon_choice_island": "Teleports/TeleportsDungeonChoiceIsland",
	"teleport_point": "TeleportPoints",
}

const LOAD_ALL_PROGRESS_EVERY := 25

var _queue_refresh_elapsed := 0.0
var _load_all_button: Button
var _write_back_button: Button
var _unload_button: Button
var _stream_toggle: CheckButton
var _stream_enabled := true
var _loaded: Dictionary = {} ## "kind/x_z" -> true
var _tracked_root: Node = null
var _block_this_save := false
var _placement_count_at_pre_save := 0

## "kind/x_z" -> true for files that exist on disk (built once).
var _catalog: Dictionary = {}
## "x_z" -> PackedStringArray of kinds that have a chunk on that tile.
var _catalog_by_tile: Dictionary = {}
## "gx_gz" -> true for ground chunk files.
var _ground_catalog: Dictionary = {}
## Pending loads: Array of {key, kind, tx, tz, dist}
var _pending: Array = []
var _pending_keys: Dictionary = {} ## key -> true while queued
## In-flight threaded PackedScene load.
var _threaded_path := ""
var _threaded_job: Dictionary = {} ## {key, kind, tx, tz}
## Load-all progress (editor "Load All World Chunks").
var _load_all_active := false
var _load_all_total := 0
var _load_all_done := 0
var _load_all_last_printed := 0
var _load_all_keys: Dictionary = {} ## key -> true for remaining LoadAll jobs
## Set when launched with: godot ... -- --load-all-world-chunks
var _cmdline_load_all := false
var _cmdline_load_all_started := false


func _enter_tree() -> void:
	_cmdline_load_all = _has_cmdline_flag("--load-all-world-chunks")
	if _cmdline_load_all:
		print("World Chunk Streamer: --load-all-world-chunks detected; will LoadAll when MainServer is open.")

	_stream_toggle = CheckButton.new()
	_stream_toggle.text = "Stream Chunks"
	_stream_toggle.button_pressed = true
	_stream_toggle.toggled.connect(_on_stream_toggled)
	add_control_to_container(CONTAINER_SPATIAL_EDITOR_MENU, _stream_toggle)

	_load_all_button = Button.new()
	_load_all_button.text = "Load All World Chunks"
	_load_all_button.pressed.connect(_on_load_all_pressed)
	add_control_to_container(CONTAINER_SPATIAL_EDITOR_MENU, _load_all_button)

	_write_back_button = Button.new()
	_write_back_button.text = "Write Back Selected"
	_write_back_button.tooltip_text = (
		"Save selected MainServer placements into their tile chunk scenes "
		+ "(no Load All / full Repack). Works for doors, spawners, etc."
	)
	_write_back_button.pressed.connect(_on_write_back_pressed)
	add_control_to_container(CONTAINER_SPATIAL_EDITOR_MENU, _write_back_button)

	_unload_button = Button.new()
	_unload_button.text = "Unload Streamed Chunks"
	_unload_button.pressed.connect(_on_unload_pressed)
	add_control_to_container(CONTAINER_SPATIAL_EDITOR_MENU, _unload_button)

	set_process(true)


func _has_cmdline_flag(flag: String) -> bool:
	for arg in OS.get_cmdline_user_args():
		if str(arg) == flag:
			return true
	return false


func _try_start_cmdline_load_all() -> void:
	if not _cmdline_load_all or _cmdline_load_all_started:
		return
	if _get_main_server() == null:
		return
	_cmdline_load_all_started = true
	print("World Chunk Streamer: --load-all-world-chunks — starting LoadAll on MainServer.")
	_on_load_all_pressed()


func _exit_tree() -> void:
	set_process(false)
	_cancel_threaded_load()
	_remove_save_guard()
	if _stream_toggle != null:
		remove_control_from_container(CONTAINER_SPATIAL_EDITOR_MENU, _stream_toggle)
		_stream_toggle.queue_free()
		_stream_toggle = null
	if _load_all_button != null:
		remove_control_from_container(CONTAINER_SPATIAL_EDITOR_MENU, _load_all_button)
		_load_all_button.queue_free()
		_load_all_button = null
	if _write_back_button != null:
		remove_control_from_container(CONTAINER_SPATIAL_EDITOR_MENU, _write_back_button)
		_write_back_button.queue_free()
		_write_back_button = null
	if _unload_button != null:
		remove_control_from_container(CONTAINER_SPATIAL_EDITOR_MENU, _unload_button)
		_unload_button.queue_free()
		_unload_button = null
	_clear_stream_state()
	_tracked_root = null


func _process(delta: float) -> void:
	_try_start_cmdline_load_all()

	# Drain loads every frame so hitch is spread out.
	_pump_loads()

	_queue_refresh_elapsed += delta
	if _queue_refresh_elapsed >= QUEUE_REFRESH_SEC:
		_queue_refresh_elapsed = 0.0
		if _stream_enabled:
			_refresh_pending_around_camera()
		_ensure_save_guard()


func _on_stream_toggled(pressed: bool) -> void:
	_stream_enabled = pressed
	if pressed:
		print("World Chunk Streamer: camera streaming enabled.")
		_refresh_pending_around_camera()
	else:
		_pending.clear()
		_pending_keys.clear()
		_cancel_threaded_load()
		if _load_all_active:
			_load_all_active = false
			_load_all_keys.clear()
			_update_load_all_button_text()
			print(
				"World Chunk Streamer: LoadAll cancelled at %d/%d (streaming paused)."
				% [_load_all_done, _load_all_total]
			)
		print("World Chunk Streamer: camera streaming paused.")


func _on_load_all_pressed() -> void:
	var root := _get_main_server()
	if root == null:
		push_warning("World Chunk Streamer: open MainServer.tscn first.")
		return
	_reset_if_scene_changed(root)
	_ensure_catalog()
	# Queue every catalog entry (sorted later as distance 0) and let the pump drain.
	_pending.clear()
	_pending_keys.clear()
	_cancel_threaded_load()
	_load_all_keys.clear()
	for key in _catalog.keys():
		if _loaded.has(key):
			continue
		var parts: PackedStringArray = key.split("/")
		if parts.size() != 2:
			continue
		var kind := parts[0]
		var tile_parts: PackedStringArray = parts[1].split("_")
		if tile_parts.size() != 2:
			continue
		_enqueue(kind, int(tile_parts[0]), int(tile_parts[1]), 0.0)
		_load_all_keys[key] = true
	_load_all_active = not _load_all_keys.is_empty()
	_load_all_total = _load_all_keys.size()
	_load_all_done = 0
	_load_all_last_printed = 0
	_update_load_all_button_text()
	print(
		"World Chunk Streamer: LoadAll queued %d chunk(s) (loads %d/frame sync)."
		% [_load_all_total, MAX_LOAD_ALL_PER_FRAME]
	)
	if not _load_all_active:
		print("World Chunk Streamer: LoadAll already complete (nothing queued).")


func _on_write_back_pressed() -> void:
	var root := _get_main_server()
	if root == null:
		push_warning("World Chunk Streamer: open MainServer.tscn first.")
		return
	var fill := root.get_node_or_null("MonsterSpawners")
	if fill != null and fill.has_method("WriteBackSelectedToChunks"):
		fill.WriteBackSelectedToChunks()
		return
	var doors := root.get_node_or_null("Doors")
	if doors != null and doors.has_method("WriteBackSelectedToChunks"):
		doors.WriteBackSelectedToChunks()
		return
	push_warning(
		"World Chunk Streamer: WriteBackSelectedToChunks not found on MonsterSpawners/Doors."
	)


func _on_unload_pressed() -> void:
	var root := _get_main_server()
	if root == null:
		push_warning("World Chunk Streamer: open MainServer.tscn first.")
		return
	_stream_enabled = false
	if _stream_toggle != null:
		_stream_toggle.set_pressed_no_signal(false)
	_pending.clear()
	_pending_keys.clear()
	_cancel_threaded_load()
	_load_all_active = false
	_load_all_total = 0
	_load_all_done = 0
	_load_all_last_printed = 0
	_load_all_keys.clear()
	_update_load_all_button_text()
	var removed := _unload_placement_children(root)
	removed += _unload_ground_children(root)
	_loaded.clear()
	print(
		(
			"World Chunk Streamer: unloaded %d placement/ground node(s); camera streaming paused. "
			+ "Re-enable «Stream Chunks» to resume."
		)
		% removed
	)


func _refresh_pending_around_camera() -> void:
	var root := _get_main_server()
	if root == null:
		return
	_reset_if_scene_changed(root)
	_ensure_catalog()
	_ensure_ground_catalog()
	var viewport := EditorInterface.get_editor_viewport_3d(0)
	if viewport == null:
		return
	var camera := viewport.get_camera_3d()
	if camera == null:
		return
	var pos: Vector3 = camera.global_position
	var tile_radius := int(ceil(LOAD_RADIUS / TILE_SIZE))
	var cx := int(floor(pos.x / TILE_SIZE))
	var cz := int(floor(pos.z / TILE_SIZE))
	for tz in range(cz - tile_radius, cz + tile_radius + 1):
		for tx in range(cx - tile_radius, cx + tile_radius + 1):
			var tile_key := "%d_%d" % [tx, tz]
			if not _catalog_by_tile.has(tile_key):
				continue
			var center := Vector3((tx + 0.5) * TILE_SIZE, pos.y, (tz + 0.5) * TILE_SIZE)
			var dist := pos.distance_squared_to(center)
			for kind in _catalog_by_tile[tile_key]:
				_enqueue(kind, tx, tz, dist)

	_enqueue_ground_around_camera(root, camera)
	# Prefer world-object chunks over ground so dense GroundChunks don't starve doors/NPCs/etc.
	_pending.sort_custom(_pending_sort)


func _pending_sort(a: Dictionary, b: Dictionary) -> bool:
	var a_ground: bool = a.kind == GROUND_KIND
	var b_ground: bool = b.kind == GROUND_KIND
	if a_ground != b_ground:
		return not a_ground
	return a.dist < b.dist


func _enqueue_ground_around_camera(root: Node, camera: Camera3D) -> void:
	var terrain := root.find_child("Terrain", true, false)
	if terrain == null or not terrain.has_method("local_to_map"):
		return
	var local: Vector3 = terrain.to_local(camera.global_position)
	var center: Vector3i = terrain.local_to_map(local)
	var cell_radius := int(ceil(LOAD_RADIUS / TILE_SIZE))
	for gz in range(center.z - cell_radius, center.z + cell_radius + 1):
		for gx in range(center.x - cell_radius, center.x + cell_radius + 1):
			var gkey := "%d_%d" % [gx, gz]
			if not _ground_catalog.has(gkey):
				continue
			var cell_local: Vector3 = terrain.map_to_local(Vector3i(gx, 0, gz))
			var cell_global: Vector3 = terrain.to_global(cell_local)
			var dist := camera.global_position.distance_squared_to(cell_global)
			_enqueue(GROUND_KIND, gx, gz, dist)


func _enqueue(kind: String, tile_x: int, tile_z: int, dist: float) -> void:
	var key := "%s/%d_%d" % [kind, tile_x, tile_z]
	if _loaded.has(key) or _pending_keys.has(key):
		return
	if kind == GROUND_KIND:
		if not _ground_catalog.has("%d_%d" % [tile_x, tile_z]):
			return
	elif not _catalog.has(key):
		return
	_pending.append({&"key": key, &"kind": kind, &"tx": tile_x, &"tz": tile_z, &"dist": dist})
	_pending_keys[key] = true


func _pump_loads() -> void:
	var root := _get_main_server()
	if root == null:
		return

	# Load All: sync batches — much faster than 1 threaded load/frame.
	if _load_all_active:
		_pump_load_all_sync(root)
		return

	# Finish / advance threaded request first.
	if not _threaded_path.is_empty():
		var progress: Array = []
		var status := ResourceLoader.load_threaded_get_status(_threaded_path, progress)
		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
			return
		if status == ResourceLoader.THREAD_LOAD_LOADED:
			var packed: PackedScene = ResourceLoader.load_threaded_get(_threaded_path)
			_threaded_path = ""
			var job := _threaded_job
			_threaded_job = {}
			if packed != null:
				_instantiate_into_tree(root, job.kind, job.key, packed)
			else:
				_loaded[job.key] = true
			return
		# Failed / invalid — skip this chunk.
		push_warning("World Chunk Streamer: threaded load failed for %s" % _threaded_path)
		if not _threaded_job.is_empty():
			_loaded[_threaded_job.key] = true
		_threaded_path = ""
		_threaded_job = {}
		return

	var started := 0
	while started < MAX_INSTANTIATES_PER_FRAME and not _pending.is_empty():
		var job: Dictionary = _pending.pop_front()
		_pending_keys.erase(job.key)
		if _loaded.has(job.key):
			continue
		var path: String
		if job.kind == GROUND_KIND:
			path = "%s/%d_%d.tscn" % [GROUND_CHUNKS_ROOT, job.tx, job.tz]
		else:
			path = "%s/%s/%d_%d.tscn" % [CHUNKS_ROOT, job.kind, job.tx, job.tz]
		var err := ResourceLoader.load_threaded_request(
			path, "", false, ResourceLoader.CACHE_MODE_REUSE
		)
		if err != OK:
			# Fallback to sync load if threaded request fails.
			var packed: PackedScene = load(path)
			if packed != null:
				_instantiate_into_tree(root, job.kind, job.key, packed)
			else:
				_loaded[job.key] = true
			started += 1
			continue
		_threaded_path = path
		_threaded_job = job
		# Wait until next frames for THREAD_LOAD_LOADED; don't start another yet.
		return


func _pump_load_all_sync(root: Node) -> void:
	var started := 0
	while started < MAX_LOAD_ALL_PER_FRAME and not _pending.is_empty():
		var job: Dictionary = _pending.pop_front()
		_pending_keys.erase(job.key)
		if _loaded.has(job.key):
			_note_load_all_chunk_finished(job.key)
			continue
		var path: String
		if job.kind == GROUND_KIND:
			path = "%s/%d_%d.tscn" % [GROUND_CHUNKS_ROOT, job.tx, job.tz]
		else:
			path = "%s/%s/%d_%d.tscn" % [CHUNKS_ROOT, job.kind, job.tx, job.tz]
		var packed: PackedScene = load(path)
		if packed != null:
			_instantiate_into_tree(root, job.kind, job.key, packed)
		else:
			_loaded[job.key] = true
			_note_load_all_chunk_finished(job.key)
		started += 1
	# If camera streaming queued ground mid-LoadAll, leave them for the normal pump next.
	if _load_all_keys.is_empty() and _load_all_active:
		# Finished message is emitted from _note_load_all_chunk_finished.
		pass
	elif not _load_all_active and not _pending.is_empty():
		# Resume camera-style drain next frame via normal path.
		pass


func _instantiate_into_tree(root: Node, kind: String, key: String, packed: PackedScene) -> void:
	if kind == GROUND_KIND:
		var ground_parent := _get_or_create_ground_root(root)
		if ground_parent == null:
			_loaded[key] = true
			_note_load_all_chunk_finished(key)
			return
		var ground_node: Node = packed.instantiate()
		_clear_owner_recursive(ground_node)
		ground_parent.add_child(ground_node)
		# Own the full tree so viewport picking works; fold so Glb/collision stay collapsed.
		_set_owner_recursive(ground_node, root)
		_fold_tree(ground_node)
		_loaded[key] = true
		_note_load_all_chunk_finished(key)
		return

	var parent_path: String = KIND_PARENTS[kind]
	var parent := root.get_node_or_null(parent_path)
	if parent == null:
		push_warning("World Chunk Streamer: parent missing '%s'" % parent_path)
		_loaded[key] = true
		_note_load_all_chunk_finished(key)
		return
	var chunk_root: Node = packed.instantiate()
	for child in chunk_root.get_children():
		_clear_owner_recursive(child)
		_compact_duplicated_id_name(child)
		chunk_root.remove_child(child)
		parent.add_child(child)
		_set_owner_recursive(child, root)
		_fold_tree(child)
	chunk_root.queue_free()
	_loaded[key] = true
	_note_load_all_chunk_finished(key)


func _note_load_all_chunk_finished(key: String) -> void:
	if not _load_all_active or not _load_all_keys.has(key):
		return
	_load_all_keys.erase(key)
	_load_all_done += 1
	_update_load_all_button_text()
	var should_print := (
		_load_all_done == 1
		or _load_all_done - _load_all_last_printed >= LOAD_ALL_PROGRESS_EVERY
	)
	if should_print and not _load_all_keys.is_empty():
		_load_all_last_printed = _load_all_done
		print(
			"World Chunk Streamer: LoadAll progress %d/%d (%d remaining)."
			% [_load_all_done, _load_all_total, _load_all_keys.size()]
		)
	if _load_all_keys.is_empty():
		_load_all_active = false
		_update_load_all_button_text()
		print(
			"World Chunk Streamer: LoadAll finished %d/%d chunk(s)."
			% [_load_all_done, _load_all_total]
		)


func _update_load_all_button_text() -> void:
	if _load_all_button == null:
		return
	if _load_all_active and _load_all_total > 0:
		_load_all_button.text = "Load All… %d/%d" % [_load_all_done, _load_all_total]
	else:
		_load_all_button.text = "Load All World Chunks"


func _compact_duplicated_id_name(node: Node) -> void:
	if not ("ID" in node):
		return
	var id: int = int(node.get("ID"))
	if id <= 0:
		return
	var suffix := "_%d" % id
	var current := String(node.name)
	while current.ends_with(suffix):
		current = current.substr(0, current.length() - suffix.length())
	node.name = ("WO" + suffix) if current.is_empty() else (current + suffix)


func _get_or_create_ground_root(root: Node) -> Node:
	var terrain := root.find_child("Terrain", true, false)
	if terrain == null:
		return null
	var streamed := terrain.get_node_or_null(GROUND_STREAMED_NAME)
	if streamed != null:
		if streamed.owner != root:
			streamed.owner = root
		return streamed
	streamed = Node3D.new()
	streamed.name = GROUND_STREAMED_NAME
	terrain.add_child(streamed)
	streamed.owner = root
	return streamed


func _ensure_catalog() -> void:
	if not _catalog.is_empty():
		return
	var count := 0
	for kind in KIND_PARENTS.keys():
		var dir_path := "%s/%s" % [CHUNKS_ROOT, kind]
		var dir := DirAccess.open(dir_path)
		if dir == null:
			continue
		dir.list_dir_begin()
		var file_name := dir.get_next()
		while file_name != "":
			if file_name.ends_with(".tscn"):
				var stem := file_name.trim_suffix(".tscn")
				var parts := stem.split("_")
				if parts.size() == 2 and parts[0].is_valid_int() and parts[1].is_valid_int():
					var key := "%s/%s" % [kind, stem]
					_catalog[key] = true
					if not _catalog_by_tile.has(stem):
						_catalog_by_tile[stem] = PackedStringArray()
					_catalog_by_tile[stem].append(kind)
					count += 1
			file_name = dir.get_next()
		dir.list_dir_end()
	print("World Chunk Streamer: catalogued %d chunk file(s)." % count)


func _ensure_ground_catalog() -> void:
	if not _ground_catalog.is_empty():
		return
	var dir := DirAccess.open(GROUND_CHUNKS_ROOT)
	if dir == null:
		return
	var count := 0
	dir.list_dir_begin()
	var file_name := dir.get_next()
	while file_name != "":
		if file_name.ends_with(".tscn"):
			var stem := file_name.trim_suffix(".tscn")
			var parts := stem.split("_")
			if parts.size() == 2 and parts[0].is_valid_int() and parts[1].is_valid_int():
				_ground_catalog[stem] = true
				count += 1
		file_name = dir.get_next()
	dir.list_dir_end()
	print("World Chunk Streamer: catalogued %d ground chunk file(s)." % count)


func _cancel_threaded_load() -> void:
	# Godot has no cancel API; abandon the handle and ignore result next time if path reused.
	_threaded_path = ""
	_threaded_job = {}


func _clear_stream_state() -> void:
	_loaded.clear()
	_pending.clear()
	_pending_keys.clear()
	_cancel_threaded_load()
	_load_all_active = false
	_load_all_total = 0
	_load_all_done = 0
	_load_all_last_printed = 0
	_load_all_keys.clear()
	_update_load_all_button_text()


func _clear_owner_recursive(node: Node) -> void:
	node.owner = null
	for child in node.get_children():
		_clear_owner_recursive(child)


func _set_owner_recursive(node: Node, owner: Node) -> void:
	# Editor-only: full ownership so meshes/colliders are pickable. Save guard blocks
	# persisting these into MainServer.tscn.
	# WorldObject visuals set unique_name_in_owner on "Glb"; under MainServer that name
	# is scene-global and every streamed placement fights over %Glb — clear it.
	node.unique_name_in_owner = false
	node.owner = owner
	for child in node.get_children():
		_set_owner_recursive(child, owner)


func _fold_tree(node: Node) -> void:
	node.set_display_folded(true)
	for child in node.get_children():
		_fold_tree(child)


func _reset_if_scene_changed(root: Node) -> void:
	if _tracked_root == root:
		return
	_tracked_root = root
	_clear_stream_state()
	# Keep catalog across scene reloads of the same project.
	_ensure_save_guard()


func _get_main_server() -> Node:
	var root := EditorInterface.get_edited_scene_root()
	if root == null or str(root.name) != "MainServer":
		return null
	return root


func _count_placement_children(root: Node) -> int:
	var total := 0
	for parent_path in KIND_PARENTS.values():
		var parent := root.get_node_or_null(parent_path)
		if parent == null:
			continue
		total += parent.get_child_count()
	total += _count_ground_children(root)
	return total


func _count_ground_children(root: Node) -> int:
	var terrain := root.find_child("Terrain", true, false)
	if terrain == null:
		return 0
	var streamed := terrain.get_node_or_null(GROUND_STREAMED_NAME)
	if streamed == null:
		return 0
	return streamed.get_child_count()


func _unload_placement_children(root: Node) -> int:
	var removed := 0
	for parent_path in KIND_PARENTS.values():
		var parent := root.get_node_or_null(parent_path)
		if parent == null:
			continue
		var children := parent.get_children()
		for child in children:
			parent.remove_child(child)
			child.free()
			removed += 1
	return removed


func _unload_ground_children(root: Node) -> int:
	var terrain := root.find_child("Terrain", true, false)
	if terrain == null:
		return 0
	var streamed := terrain.get_node_or_null(GROUND_STREAMED_NAME)
	if streamed == null:
		return 0
	var removed := streamed.get_child_count()
	for child in streamed.get_children():
		streamed.remove_child(child)
		child.free()
	return removed


func _ensure_save_guard() -> void:
	var root := _get_main_server()
	if root == null:
		return
	if root.get_node_or_null(SAVE_GUARD_NAME) != null:
		return
	var guard := Node.new()
	guard.name = SAVE_GUARD_NAME
	guard.set_script(preload("res://addons/world_chunk_streamer/main_server_save_guard.gd"))
	guard.set("plugin", self)
	root.add_child(guard)
	guard.owner = null


func _remove_save_guard() -> void:
	var root := _get_main_server()
	if root == null:
		return
	var guard := root.get_node_or_null(SAVE_GUARD_NAME)
	if guard != null:
		guard.queue_free()


func handle_editor_pre_save() -> void:
	var root := _get_main_server()
	if root == null:
		_block_this_save = false
		return

	_placement_count_at_pre_save = _count_placement_children(root)
	_block_this_save = _placement_count_at_pre_save > 0
	if not _block_this_save:
		return

	var abs_main := ProjectSettings.globalize_path(MAIN_SERVER_PATH)
	var abs_backup := ProjectSettings.globalize_path(SAVE_BACKUP_PATH)
	var err := DirAccess.copy_absolute(abs_main, abs_backup)
	if err != OK:
		push_error(
			"World Chunk Streamer: could not backup MainServer before blocked save (%s)." % error_string(err)
		)

	for parent_path in KIND_PARENTS.values():
		var parent := root.get_node_or_null(parent_path)
		if parent == null:
			continue
		for child in parent.get_children():
			_clear_owner_recursive(child)

	push_error(
		(
			"World Chunk Streamer: refusing to keep a MainServer save while %d streamed placement "
			+ "node(s) are loaded. File will be reverted — click 'Unload Streamed Chunks' first, "
			+ "then save. Use «Write Back Selected» (or Repack / split_world_chunks.ps1) to persist "
			+ "placement edits."
		)
		% _placement_count_at_pre_save
	)


func handle_editor_post_save() -> void:
	if not _block_this_save:
		return
	_block_this_save = false

	var abs_main := ProjectSettings.globalize_path(MAIN_SERVER_PATH)
	var abs_backup := ProjectSettings.globalize_path(SAVE_BACKUP_PATH)
	if not FileAccess.file_exists(abs_backup):
		push_error("World Chunk Streamer: blocked save but backup missing; unload chunks and fix manually.")
		return

	var err := DirAccess.copy_absolute(abs_backup, abs_main)
	if err != OK:
		push_error(
			"World Chunk Streamer: failed to restore MainServer from backup (%s)." % error_string(err)
		)
		return

	EditorInterface.get_resource_filesystem().update_file(MAIN_SERVER_PATH)
	EditorInterface.mark_scene_as_unsaved()
	_show_blocked_save_dialog(_placement_count_at_pre_save)


func _show_blocked_save_dialog(count: int) -> void:
	var dialog := AcceptDialog.new()
	dialog.title = "MainServer save blocked"
	dialog.dialog_text = (
		"Save was reverted because %d streamed world object(s) are still loaded under MainServer.\n\n"
		+ "Click «Unload Streamed Chunks» (this also pauses streaming), then save again.\n"
		+ "Re-enable «Stream Chunks» when you want camera loading again.\n"
		+ "To persist placement edits, use «Write Back Selected» (or Repack / "
		+ "Tools/split_world_chunks.ps1) instead of Save Scene."
	) % count
	dialog.confirmed.connect(dialog.queue_free)
	dialog.canceled.connect(dialog.queue_free)
	EditorInterface.get_base_control().add_child(dialog)
	dialog.popup_centered()
