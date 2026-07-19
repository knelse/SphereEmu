extends SceneTree

## Export object placements near a point as preview GLB(s).
## Positions use ObjectDataJson SOURCE_BASIS space (same frame as typical dump coords).
##
## Indoor base-tile identity: IndoorAreaCriteria (Tools/indoor_area_criteria.gd /
## Godot/Scripts/Terrain/IndoorAreaCriteria.cs) — Godot Y < -500 and name in
## (*_in, cci*, lb* except lbridge, rd_island*, rd_r1..5, rd_rh, room1, tn4_hotel).
## Props inside/around those shells are not name-matched.
##
## Indoor walkable on those shells (NOT the outdoor terrain bake cover/prune path):
##   0) outer-shell mark (per placement): voxelize mesh → morphological close (seal doorways)
##      → depth envelopes from +Y and ±X/±Z; faces on the closed top, or on near-vertical
##      side envelopes within SHELL_SIDE_CROWN_BAND of the top, are excluded from nav
##      (side marks use SHELL_SIDE_MAX_SLOPE so stair/ramp treads are not treated as outer skin)
##   1) force face normal upward, slope ≤ agent max (default 80°) — floors / stairs / ramps
##   2) keep if:
##      - steep stair/ramp (slope > 35°) below the roof skin (normals often face away from AABB center), or
##      - normal points toward AABB center (inward), or
##      - wing floor: weakly outward, below roof skin, and inset from AABB XZ edge, or
##      - upper floor: below roof skin, inset from AABB XZ edge, and has ceiling headroom
##   3) weld same-floor flat boundary gaps (≤ WELD_XZ_MAX) with bridge quads so stair
##      landings / doorways are geometrically continuous for nav, not only virtually linked
##   4) ALWAYS merge curated simple-room walk strips (Tools/simple_room_walk_strips.json)
##      on top of (1)–(3), regardless of whether face extraction kept any floor for that kit
##   Walkable preview colors = edge-connected components (true walk islands):
##     tris share an edge (or a tiny same-level gap) ⇒ same color; no path ⇒ different color.
##
## Usage:
##   godot --path . --headless -s Tools/export_nearby_objects_glb.gd -- \
##     --center -92.2 -1095.74 -1094.7 --radius 40 --out D:/1/out/objects.glb

const OBJECT_DATA_DIR := "res://Godot/Terrain/ObjectDataJson/"
const MODELS_DIR := "res://Godot/Models/"
## Local-space opening-to-opening strips for simple rooms (see export_simple_room_walk_strips.py).
const SIMPLE_ROOM_STRIPS_PATH := "res://Tools/simple_room_walk_strips.json"
const SOURCE_BASIS := Basis(Vector3.RIGHT, Vector3.DOWN, Vector3.FORWARD)
## Match bake_and_export_single_nav.gd: ObjectData SOURCE_BASIS → TerrainObjects/nav frame.
const OBJECT_ORIGIN_SHIFT := Vector3(4000.0, 0.0, 4000.0)
const NAV_CELL_SIZE := 0.1
const NAV_CELL_HEIGHT := 0.1
const NAV_AGENT_RADIUS := 0.25
const NAV_AGENT_HEIGHT := 1.8
const NAV_AGENT_MAX_CLIMB := 0.3
## Indoor kits use steeper stair faces than outdoor ADDFACES (70°); 80° keeps those treads.
const AGENT_MAX_SLOPE_DEFAULT := 80.0
## dot(normal, center - centroid) above this ⇒ clear interior (normal faces into the shell).
const INWARD_DOT_MIN := 0.1
## Weakly-outward faces below the roof skin are kept as wing / side-room floors.
const WING_DOT_MIN := -0.25
## Drop faces within this many metres of the placement AABB top (exterior roof).
const ROOF_SKIN_BAND := 0.9
## Upper-floor headroom: downward ceiling face must sit this far above the floor.
const CEILING_MIN_HEADROOM := 1.6
const CEILING_MAX_HEADROOM := 10.0
const CEILING_CELL := 1.0
## Wing / covered faces must sit this far inside the placement AABB on XZ (drops wall-tops).
const FOOTPRINT_INSET := 2.5
## Outer-shell mark: voxelize → close doorways → depth envelopes (+Y and 4 sides).
const SHELL_CELL := 0.5
## Dilate/erode iterations; seals gaps up to ~2 * SHELL_CLOSE_ITERS * SHELL_CELL (~4m).
const SHELL_CLOSE_ITERS := 4
## How close a face sample must sit to a closed depth envelope to count as outer shell.
const SHELL_EPS := 0.3
## Side (±X/±Z) hits only count near the closed top (avoids carving wall-edge floors / doorways).
const SHELL_SIDE_CROWN_BAND := 2.5
## Side hits: skip flat ledges (artifacts); require near the kit AABB face (not interior slabs).
## Cap below typical stair slopes (~44–74°) so ramp treads near kit edges stay walkable.
const SHELL_SIDE_MIN_SLOPE := 20.0
const SHELL_SIDE_MAX_SLOPE := 40.0
const SHELL_SIDE_AABB_BAND := 1.5
## Stairs/ramps above this slope keep without an inward-normal test (kit stairs face outward).
const STAIR_KEEP_MIN_SLOPE := 35.0
## Connectivity: shared edge/vertex (quantized), plus walkable step links.
## Same-floor holes vs stairs vs stacked decks are distinguished by (xz, dy):
##   same floor: small dy, xz ≤ FLOOR_XZ (kit doorways are often ~3m open)
##   stair/ramp: dy up to STEP_Y with horizontal offset, or tall riser (dy≥0.7) with small xz
##   stacked deck: dy~0.3 with xz≈0 → never link
const CONNECT_VERT_Q := 50.0 # 2cm bins
const CONNECT_GAP_XZ := 0.5
const CONNECT_GAP_Y := 0.15
## lb* doorway openings between shells are commonly ~3.0–3.4m with no floor tris in the void.
const CONNECT_FLOOR_XZ := 3.5
const CONNECT_FLOOR_Y := 0.18
const CONNECT_STEP_XZ_MIN := 0.18 # normal stair tread: must move horizontally
const CONNECT_STEP_XZ_MAX := 2.0
## Tall kit risers / spiral landings are often ~1.8–2.1m between kept faces.
const CONNECT_STEP_Y := 2.2
## Allow nearly-stacked stair links when dy is clearly a riser (not a 0.3m deck).
const CONNECT_TALL_RISER_Y_MIN := 0.7
## Tiny scraps rendered gray so major islands stay readable.
const CONNECT_MAJOR_MIN_TRIS := 12
## Physical weld: bridge same-floor boundary gaps with quads so nav geometry is continuous
## (not only virtual CONNECT_FLOOR_XZ links). Targets stair-landing ↔ corridor rings / doorways.
const WELD_FLOOR_Y := 0.18
## Skip tessellation micro-gaps; weld real landing↔corridor / doorway voids.
const WELD_XZ_MIN := 0.45
const WELD_XZ_MAX := 3.5
## Only weld edges from near-flat faces (landings / floors); skip ramp/stair side edges.
const WELD_MAX_FACE_SLOPE := 25.0

var _center := Vector3(-92.2, -1095.74, -1094.7)
var _radius := 40.0
var _out_path := "D:/1/objects.glb"
var _with_walkable := true
## When set, only IndoorAreaCriteria base tiles are exported (no outdoor props/terrain clutter).
var _indoor_base_only := false
## Pre-lb* walkable: upward faces by slope only (no outer-shell carve, weld, or strips).
var _pre_lb_walkable := false
## Exact cluster membership keys from build_indoor_cluster_manifest.py (name|x|y|z|pitch|yaw|roll).
## When set, export only those placements (ignores sphere radius for inclusion).
var _members_path := ""
var _member_keys: Dictionary = {} # key -> true
var _max_slope_deg := AGENT_MAX_SLOPE_DEFAULT
var _mesh_parts_cache: Dictionary = {}
## When set, bake walkable faces to a NavigationMesh .res (outdoor nav frame) + .nav.json sidecar.
var _write_nav_res_path := ""
var _cluster_id := -1
## Skip preview object/walkable GLBs (nav-res bake still runs when requested).
var _skip_preview_glb := false
var _objects_basis := Basis.from_euler(Vector3(0.0, deg_to_rad(-90.0), 0.0))

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var i := 0
	while i < args.size():
		if args[i] == "--center" and i + 3 < args.size():
			_center = Vector3(float(args[i + 1]), float(args[i + 2]), float(args[i + 3]))
			i += 4
		elif args[i] == "--radius" and i + 1 < args.size():
			_radius = float(args[i + 1])
			i += 2
		elif args[i] == "--out" and i + 1 < args.size():
			_out_path = args[i + 1]
			i += 2
		elif args[i] == "--members" and i + 1 < args.size():
			_members_path = args[i + 1]
			i += 2
		elif args[i] == "--write-nav-res" and i + 1 < args.size():
			_write_nav_res_path = args[i + 1]
			_with_walkable = true
			i += 2
		elif args[i] == "--cluster-id" and i + 1 < args.size():
			_cluster_id = int(args[i + 1])
			i += 2
		elif args[i] == "--skip-preview-glb":
			_skip_preview_glb = true
			i += 1
		elif args[i] == "--with-walkable":
			_with_walkable = true
			i += 1
		elif args[i] == "--no-walkable":
			_with_walkable = false
			i += 1
		elif args[i] == "--indoor-base-only":
			_indoor_base_only = true
			i += 1
		elif args[i] == "--pre-lb-walkable":
			# cci / early indoor: slope-up walkable only (skip lb* shell/weld/strips).
			_pre_lb_walkable = true
			_with_walkable = true
			if absf(_max_slope_deg - AGENT_MAX_SLOPE_DEFAULT) < 0.01:
				_max_slope_deg = 70.0
			i += 1
		elif args[i] == "--max-slope" and i + 1 < args.size():
			_max_slope_deg = float(args[i + 1])
			i += 2
		else:
			i += 1

	if not _members_path.is_empty():
		if not _load_member_keys(_members_path):
			push_error("Failed to load members file: %s" % _members_path)
			quit(1)
			return

	if not _skip_preview_glb:
		DirAccess.make_dir_recursive_absolute(_out_path.get_base_dir())
	if not _write_nav_res_path.is_empty():
		DirAccess.make_dir_recursive_absolute(_write_nav_res_path.get_base_dir())

	print(
		"Indexing placements near %s r=%.1f indoor_base_only=%s pre_lb=%s members=%d nav_res=%s ..." % [
			_center, _radius, str(_indoor_base_only), str(_pre_lb_walkable), _member_keys.size(),
			_write_nav_res_path if not _write_nav_res_path.is_empty() else "-",
		]
	)
	var placements := _collect_nearby_placements()
	print("Found %d unique placements" % placements.size())
	if placements.is_empty():
		push_error("No placements matched (radius/members)")
		quit(1)
		return

	var obj_bucket := _new_bucket()
	var walk_faces := PackedVector3Array()
	var missing := 0
	var used := 0
	var name_counts: Dictionary = {}
	var walk_tris := 0
	var walk_area := 0.0
	var dropped_roof_tris := 0
	var dropped_roof_area := 0.0
	var dropped_topdown_tris := 0
	var dropped_topdown_area := 0.0
	var walk_by_name: Dictionary = {}
	var slope_hist := {"flat0_15": 0, "ramp15_35": 0, "steep35_70": 0}

	for pl in placements:
		var object_name: String = pl["object_name"]
		var parts: Array = _load_object_parts(object_name)
		if parts.is_empty():
			missing += 1
			continue
		used += 1
		name_counts[object_name] = int(name_counts.get(object_name, 0)) + 1
		var world_xform: Transform3D = pl["transform"]
		# Rebase so the cluster sits near the GLB origin for easier viewing.
		world_xform.origin -= _center

		var part_xforms: Array = []
		var aabb_initialized := false
		var world_aabb := AABB()
		for part in parts:
			var part_xform: Transform3D = world_xform * part["local"]
			part_xforms.append(part_xform)
			_append_mesh(obj_bucket, part["mesh"], part_xform)
			var part_aabb := _mesh_aabb_world(part["mesh"], part_xform)
			if not aabb_initialized:
				world_aabb = part_aabb
				aabb_initialized = true
			else:
				world_aabb = world_aabb.merge(part_aabb)

		if not _with_walkable or not aabb_initialized:
			continue
		if _is_never_walkable_object(object_name):
			continue

		var object_faces: Array = []
		for pi in range(parts.size()):
			object_faces.append(_mesh_faces_world(parts[pi]["mesh"], part_xforms[pi]))
		if _pre_lb_walkable:
			# Early indoor: keep upward faces by slope only (no shell / inward / wing).
			for faces in object_faces:
				var split_lite := _split_simple_upward_walkable(faces, _max_slope_deg)
				var wfaces_lite: PackedVector3Array = split_lite["walkable"]
				if wfaces_lite.is_empty():
					continue
				walk_faces.append_array(wfaces_lite)
				var ntris_lite: int = split_lite["tris"]
				walk_tris += ntris_lite
				walk_area += float(split_lite["area"])
				walk_by_name[object_name] = int(walk_by_name.get(object_name, 0)) + ntris_lite
				slope_hist["flat0_15"] = int(slope_hist["flat0_15"]) + int(split_lite["flat0_15"])
				slope_hist["ramp15_35"] = int(slope_hist["ramp15_35"]) + int(split_lite["ramp15_35"])
				slope_hist["steep35_70"] = int(slope_hist["steep35_70"]) + int(split_lite["steep35_70"])
		else:
			var shell_center := world_aabb.get_center()
			var shell_max_y := world_aabb.position.y + world_aabb.size.y
			var profile := _walk_profile_for(object_name)
			# Closed solid (doorways sealed) → outer envelopes from top + 4 sides.
			var shell_maps := _build_closed_outer_shell_maps(object_faces)
			var ceiling_grid := _build_ceiling_grid(
				object_faces, float(profile["max_slope"]), shell_max_y, profile
			)
			for faces in object_faces:
				var split := _split_inward_walkable(
					faces, shell_center, shell_max_y, world_aabb, ceiling_grid, profile, shell_maps
				)
				var wfaces: PackedVector3Array = split["walkable"]
				dropped_roof_tris += int(split["dropped_tris"])
				dropped_roof_area += float(split["dropped_area"])
				dropped_topdown_tris += int(split["dropped_topdown_tris"])
				dropped_topdown_area += float(split["dropped_topdown_area"])
				if wfaces.is_empty():
					continue
				walk_faces.append_array(wfaces)
				var ntris: int = split["tris"]
				walk_tris += ntris
				walk_area += float(split["area"])
				walk_by_name[object_name] = int(walk_by_name.get(object_name, 0)) + ntris
				slope_hist["flat0_15"] = int(slope_hist["flat0_15"]) + int(split["flat0_15"])
				slope_hist["ramp15_35"] = int(slope_hist["ramp15_35"]) + int(split["ramp15_35"])
				slope_hist["steep35_70"] = int(slope_hist["steep35_70"]) + int(split["steep35_70"])

	var obj_mesh: ArrayMesh = null
	if not _skip_preview_glb:
		obj_mesh = _commit_bucket(obj_bucket)
		if obj_mesh == null:
			push_error("Merged object mesh empty")
			quit(1)
			return

	var region_count := 0
	var strip_tris := 0
	var strip_area := 0.0
	var strip_placements := 0
	if _with_walkable and not _pre_lb_walkable:
		# lb* path: strips always-on even if face bake empty.
		if walk_faces.is_empty():
			push_warning("No inward walkable faces found (slope <= %.1f); still applying simple-room strips" % _max_slope_deg)
		var strip_add: Dictionary = _append_simple_room_walk_strips(placements, walk_faces)
		walk_faces = strip_add["faces"]
		strip_tris = int(strip_add["tris"])
		strip_area = float(strip_add["area"])
		strip_placements = int(strip_add["placements"])
		walk_tris += strip_tris
		walk_area += strip_area
		if strip_tris > 0:
			print(
				"  simple-room walk strips: %d tris, area=%.1f, placements=%d (always-on)" % [
					strip_tris, strip_area, strip_placements,
				]
			)
	elif _with_walkable and _pre_lb_walkable and walk_faces.is_empty():
		push_warning("No simple upward walkable faces found (slope <= %.1f)" % _max_slope_deg)

	# Build walkable connectivity once (expensive on large clusters).
	var walk_root: Node3D = null
	var weld_tris := 0
	var weld_area := 0.0
	if _with_walkable and not walk_faces.is_empty():
		if not _pre_lb_walkable:
			var welded := _weld_walkable_floor_gaps(walk_faces)
			walk_faces = welded["faces"]
			weld_tris = int(welded["tris"])
			weld_area = float(welded["area"])
			walk_tris += weld_tris
			walk_area += weld_area
			if weld_tris > 0:
				print("  welded same-floor gaps: %d tris, area=%.1f" % [weld_tris, weld_area])
		if not _write_nav_res_path.is_empty():
			if not _bake_and_write_indoor_nav(walk_faces):
				quit(1)
				return
		if not _skip_preview_glb:
			walk_root = _build_connectivity_colored_node(walk_faces)
			region_count = walk_root.get_child_count()
			var walk_path := _out_path.get_basename() + "_walkable.glb"
			if not _write_glb(walk_root, walk_path):
				walk_root.queue_free()
				quit(1)
				return
			print("Exported walkable ", walk_path)
			print(
				"  connectivity islands (edge-linked walkable): %d — same color = agent can walk between" % region_count
			)
			# _write_glb parents into the scene tree; detach before reuse.
			if walk_root.get_parent() != null:
				walk_root.get_parent().remove_child(walk_root)
	elif not _write_nav_res_path.is_empty():
		push_error("No walkable faces to bake into NavigationMesh (%s)" % _write_nav_res_path)
		quit(1)
		return

	if not _skip_preview_glb:
		var root := Node3D.new()
		root.name = "NearbyObjects"
		var mi_obj := MeshInstance3D.new()
		mi_obj.name = "Objects"
		mi_obj.mesh = obj_mesh
		mi_obj.material_override = _blue_translucent_material()
		root.add_child(mi_obj)
		if walk_root != null:
			walk_root.position = Vector3(0.0, 0.02, 0.0)
			root.add_child(walk_root)
			walk_root = null

		if not _write_glb(root, _out_path):
			root.queue_free()
			quit(1)
			return
		root.queue_free()

	var manifest_path := _out_path.get_basename() + "_manifest.txt"
	var mf: FileAccess = null
	if not _skip_preview_glb:
		mf = FileAccess.open(manifest_path, FileAccess.WRITE)
	if mf:
		mf.store_line("center=%s radius=%.1f" % [_center, _radius])
		mf.store_line("placements=%d used=%d missing_models=%d" % [placements.size(), used, missing])
		if _pre_lb_walkable:
			mf.store_line("method=pre_lb_simple_upward_slope (no shell/weld/strips)")
		else:
			mf.store_line("method=closed_outer_shell(+Y,+/-X,+/-Z) + inward_or_wing_or_covered_upper")
		mf.store_line(
			"max_slope_deg=%.1f inward_dot_min=%.2f wing_dot_min=%.2f roof_skin=%.2f headroom=[%.1f,%.1f] footprint_inset=%.2f" % [
				_max_slope_deg, INWARD_DOT_MIN, WING_DOT_MIN, ROOF_SKIN_BAND,
				CEILING_MIN_HEADROOM, CEILING_MAX_HEADROOM, FOOTPRINT_INSET,
			]
		)
		mf.store_line(
			"outer_shell cell=%.2f close=%d eps=%.2f side_slope=[%.1f,%.1f] side_aabb_band=%.1f dropped_tris=%d area=%.1f" % [
				SHELL_CELL, SHELL_CLOSE_ITERS, SHELL_EPS, SHELL_SIDE_MIN_SLOPE, SHELL_SIDE_MAX_SLOPE, SHELL_SIDE_AABB_BAND,
				dropped_topdown_tris, dropped_topdown_area,
			]
		)
		mf.store_line("walkable_tris=%d walkable_area_m2=%.1f connectivity_islands=%d" % [
			walk_tris, walk_area, region_count,
		])
		mf.store_line(
			"weld_floor_gaps xz=[%.2f,%.2f] y=%.2f max_face_slope=%.1f tris=%d area=%.1f" % [
				WELD_XZ_MIN, WELD_XZ_MAX, WELD_FLOOR_Y, WELD_MAX_FACE_SLOPE, weld_tris, weld_area,
			]
		)
		mf.store_line(
			"simple_room_strips always_on tris=%d area=%.1f placements=%d catalog=%s" % [
				strip_tris, strip_area, strip_placements, SIMPLE_ROOM_STRIPS_PATH,
			]
		)
		mf.store_line("dropped_exterior_tris=%d dropped_area_m2=%.1f" % [
			dropped_roof_tris, dropped_roof_area,
		])
		mf.store_line("slope_bins flat0-15=%d ramp15-35=%d steep35-70=%d" % [
			slope_hist["flat0_15"], slope_hist["ramp15_35"], slope_hist["steep35_70"],
		])
		mf.store_line("")
		mf.store_line("# placements by name")
		var names: Array = name_counts.keys()
		names.sort()
		for n in names:
			mf.store_line("%s\t%d" % [n, name_counts[n]])
		mf.store_line("")
		mf.store_line("# walkable tris by object name")
		var wnames: Array = walk_by_name.keys()
		wnames.sort_custom(func(a, b): return int(walk_by_name[a]) > int(walk_by_name[b]))
		for n in wnames:
			mf.store_line("%s\t%d" % [n, walk_by_name[n]])
		mf.close()

	print(
		"Indoor walkable (inset=%.2f; slope<=%.1f°): %d tris, area=%.1f, islands=%d" % [
			FOOTPRINT_INSET, _max_slope_deg, walk_tris, walk_area, region_count
		]
	)
	print(
		"  closed outer shell (+Y/sides) excluded: %d tris, area=%.1f" % [
			dropped_topdown_tris, dropped_topdown_area
		]
	)
	print(
		"  dropped as roof/exterior (not inward/wing/covered): %d tris, area=%.1f" % [
			dropped_roof_tris, dropped_roof_area
		]
	)
	if _skip_preview_glb:
		print(
			"Nav bake done (no preview GLB) objects=%d walkable_tris=%d area=%.1fm2 nav=%s" % [
				used, walk_tris, walk_area, _write_nav_res_path,
			]
		)
	else:
		print("Exported %s (%d objects, walkable_tris=%d area=%.1fm2 islands=%d dropped_ext=%.1fm2)" % [
			_out_path, used, walk_tris, walk_area, region_count, dropped_roof_area,
		])
	quit(0)


## Bake finalized (center-relative) walkable faces into a NavigationMesh in the outdoor nav frame.
func _bake_and_write_indoor_nav(center_relative_faces: PackedVector3Array) -> bool:
	if center_relative_faces.is_empty():
		push_error("Indoor nav bake: empty walkable faces")
		return false
	var nav_faces := _walk_faces_to_nav_frame(center_relative_faces)
	var source := NavigationMeshSourceGeometryData3D.new()
	source.add_faces(nav_faces, Transform3D.IDENTITY)
	var nav := NavigationMesh.new()
	nav.cell_size = NAV_CELL_SIZE
	nav.cell_height = NAV_CELL_HEIGHT
	nav.agent_radius = NAV_AGENT_RADIUS
	nav.agent_height = NAV_AGENT_HEIGHT
	nav.agent_max_climb = NAV_AGENT_MAX_CLIMB
	nav.agent_max_slope = _max_slope_deg
	nav.region_min_size = 4.0
	nav.region_merge_size = 20.0
	nav.edge_max_length = 12.0
	nav.edge_max_error = 1.3
	nav.detail_sample_distance = 6.0
	nav.filter_ledge_spans = false
	nav.filter_walkable_low_height_spans = true
	NavigationServer3D.bake_from_source_geometry_data(nav, source)
	if nav.get_polygon_count() == 0:
		push_error("Indoor nav bake produced empty NavigationMesh: %s" % _write_nav_res_path)
		return false
	var err := ResourceSaver.save(nav, _write_nav_res_path)
	if err != OK:
		push_error("Failed to save indoor nav %s err=%d" % [_write_nav_res_path, err])
		return false
	var aabb := _faces_aabb(nav_faces)
	var center_nav := _source_point_to_nav_frame(_center)
	var meta := {
		"id": _cluster_id,
		"path": _write_nav_res_path,
		"pre_lb": _pre_lb_walkable,
		"radius": _radius,
		"center_source": {"x": _center.x, "y": _center.y, "z": _center.z},
		"center_nav": {"x": center_nav.x, "y": center_nav.y, "z": center_nav.z},
		"aabb_nav": {
			"min": {"x": aabb.position.x, "y": aabb.position.y, "z": aabb.position.z},
			"max": {
				"x": aabb.position.x + aabb.size.x,
				"y": aabb.position.y + aabb.size.y,
				"z": aabb.position.z + aabb.size.z,
			},
		},
		"polygon_count": nav.get_polygon_count(),
		"walkable_tris": int(nav_faces.size() / 3),
	}
	var meta_path := _write_nav_res_path.get_basename() + ".nav.json"
	var mf := FileAccess.open(meta_path, FileAccess.WRITE)
	if mf == null:
		push_error("Failed to write indoor nav meta %s" % meta_path)
		return false
	mf.store_string(JSON.stringify(meta, "\t"))
	mf.close()
	print(
		"Wrote indoor nav %s polys=%d tris=%d meta=%s" % [
			_write_nav_res_path, nav.get_polygon_count(), int(nav_faces.size() / 3), meta_path,
		]
	)
	return true


func _source_point_to_nav_frame(p: Vector3) -> Vector3:
	return _objects_basis * p + OBJECT_ORIGIN_SHIFT


func _walk_faces_to_nav_frame(center_relative_faces: PackedVector3Array) -> PackedVector3Array:
	# Walk faces were built after subtracting _center; restore SOURCE_BASIS then map to nav frame.
	var out := PackedVector3Array()
	out.resize(center_relative_faces.size())
	for i in range(center_relative_faces.size()):
		out[i] = _source_point_to_nav_frame(center_relative_faces[i] + _center)
	return out


func _faces_aabb(faces: PackedVector3Array) -> AABB:
	if faces.is_empty():
		return AABB()
	var aabb := AABB(faces[0], Vector3.ZERO)
	for i in range(1, faces.size()):
		aabb = aabb.expand(faces[i])
	return aabb


func _write_glb(root: Node3D, path: String) -> bool:
	get_root().add_child(root)
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_scene(root, state)
	if err != OK:
		push_error("GLTF append failed err=%d path=%s" % [err, path])
		return false
	err = doc.write_to_filesystem(state, path)
	if err != OK:
		push_error("GLTF write failed err=%d path=%s" % [err, path])
		return false
	print("Exported ", path)
	return true


func _blue_translucent_material() -> StandardMaterial3D:
	var mat := StandardMaterial3D.new()
	# See-through indoor kit shells (cci*/lbg*/lbc*/lbm*/…).
	mat.albedo_color = Color(0.2, 0.55, 0.98, 0.28)
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.roughness = 0.85
	return mat


func _walkable_material(color: Color) -> StandardMaterial3D:
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.roughness = 0.7
	return mat


func _vert_qkey(v: Vector3) -> Vector3i:
	return Vector3i(
		roundi(v.x * CONNECT_VERT_Q),
		roundi(v.y * CONNECT_VERT_Q),
		roundi(v.z * CONNECT_VERT_Q)
	)


func _edge_qkey(a: Vector3, b: Vector3) -> String:
	var ka := _vert_qkey(a)
	var kb := _vert_qkey(b)
	if ka.x < kb.x or (ka.x == kb.x and ka.y < kb.y) or (ka.x == kb.x and ka.y == kb.y and ka.z <= kb.z):
		return "%d,%d,%d|%d,%d,%d" % [ka.x, ka.y, ka.z, kb.x, kb.y, kb.z]
	return "%d,%d,%d|%d,%d,%d" % [kb.x, kb.y, kb.z, ka.x, ka.y, ka.z]


## Always-on flat corridor strips for curated simple rooms (opening↔opening).
## Appended after regular inward-walkable extraction; does not depend on that result.
## Returns updated faces (PackedVector3Array is CoW — caller must assign back).
func _append_simple_room_walk_strips(placements: Array, walk_faces: PackedVector3Array) -> Dictionary:
	var catalog: Dictionary = _load_simple_room_strips_catalog()
	var rooms: Dictionary = catalog.get("rooms", {})
	var tris := 0
	var area := 0.0
	var used_placements := 0
	if rooms.is_empty():
		return {"faces": walk_faces, "tris": 0, "area": 0.0, "placements": 0}
	for pl in placements:
		var object_name: String = str(pl["object_name"]).to_lower()
		if not rooms.has(object_name):
			continue
		var room: Dictionary = rooms[object_name]
		var strips: Array = room.get("strips", [])
		if strips.is_empty():
			continue
		var world_xform: Transform3D = pl["transform"]
		world_xform.origin -= _center
		var added_here := 0
		for strip in strips:
			var faces_local: Array = strip.get("faces", [])
			if faces_local.size() < 6:
				# Rebuild from quad [v0,v1,v2,v3] if faces omitted.
				var quad: Array = strip.get("quad", [])
				if quad.size() < 4:
					continue
				faces_local = [quad[0], quad[1], quad[2], quad[0], quad[2], quad[3]]
			var i := 0
			while i + 2 < faces_local.size():
				var a: Vector3 = world_xform * _vec3_from_json(faces_local[i])
				var b: Vector3 = world_xform * _vec3_from_json(faces_local[i + 1])
				var c: Vector3 = world_xform * _vec3_from_json(faces_local[i + 2])
				var n := (b - a).cross(c - a)
				var tri_area := 0.5 * n.length()
				if tri_area < 1e-8:
					i += 3
					continue
				# Force upward winding for nav / connectivity.
				if n.y < 0.0:
					walk_faces.append(a)
					walk_faces.append(c)
					walk_faces.append(b)
				else:
					walk_faces.append(a)
					walk_faces.append(b)
					walk_faces.append(c)
				tris += 1
				area += tri_area
				added_here += 1
				i += 3
		if added_here > 0:
			used_placements += 1
	return {"faces": walk_faces, "tris": tris, "area": area, "placements": used_placements}


func _load_simple_room_strips_catalog() -> Dictionary:
	if not FileAccess.file_exists(SIMPLE_ROOM_STRIPS_PATH):
		push_warning("Simple-room strip catalog missing: %s" % SIMPLE_ROOM_STRIPS_PATH)
		return {}
	var f := FileAccess.open(SIMPLE_ROOM_STRIPS_PATH, FileAccess.READ)
	if f == null:
		push_warning("Failed to open simple-room strip catalog: %s" % SIMPLE_ROOM_STRIPS_PATH)
		return {}
	var raw := f.get_as_text()
	f.close()
	var parsed: Variant = JSON.parse_string(raw)
	if typeof(parsed) != TYPE_DICTIONARY:
		push_warning("Simple-room strip catalog is not a JSON object")
		return {}
	return parsed


func _vec3_from_json(v: Variant) -> Vector3:
	if v is Vector3:
		return v
	if v is Array and v.size() >= 3:
		return Vector3(float(v[0]), float(v[1]), float(v[2]))
	return Vector3.ZERO


func _uf_find(parent: PackedInt32Array, i: int) -> int:
	var root := i
	while parent[root] != root:
		root = parent[root]
	var cur := i
	while parent[cur] != root:
		var nxt: int = parent[cur]
		parent[cur] = root
		cur = nxt
	return root


func _uf_union(parent: PackedInt32Array, a: int, b: int) -> void:
	var ra := _uf_find(parent, a)
	var rb := _uf_find(parent, b)
	if ra != rb:
		parent[rb] = ra


## Bridge same-floor gaps with upward quads by probing perpendicular to flat boundary edges.
## Fills stair-landing ↔ corridor rings / doorway voids with real mesh (not only virtual links).
func _weld_walkable_floor_gaps(faces: PackedVector3Array) -> Dictionary:
	var out := faces.duplicate()
	var weld_tris := 0
	var weld_area := 0.0
	var tri_count := int(faces.size() / 3)
	if tri_count == 0:
		return {"faces": out, "tris": 0, "area": 0.0}

	var edge_uses: Dictionary = {} # String -> int
	var edge_recs: Array = []
	for ti in range(tri_count):
		var v0: Vector3 = faces[ti * 3]
		var v1: Vector3 = faces[ti * 3 + 1]
		var v2: Vector3 = faces[ti * 3 + 2]
		var n := (v1 - v0).cross(v2 - v0)
		var nlen := n.length()
		if nlen < 1e-8:
			continue
		n /= nlen
		if n.y < 0.0:
			n = -n
		var slope_deg := rad_to_deg(acos(clampf(n.y, 0.0, 1.0)))
		var verts := [v0, v1, v2]
		for e in range(3):
			var a: Vector3 = verts[e]
			var b: Vector3 = verts[(e + 1) % 3]
			var ek := _edge_qkey(a, b)
			edge_uses[ek] = int(edge_uses.get(ek, 0)) + 1
			var dir := b - a
			var elen := dir.length()
			if elen < 0.05:
				continue
			dir /= elen
			edge_recs.append({
				"ek": ek,
				"a": a,
				"b": b,
				"mid": (a + b) * 0.5,
				"dir": dir,
				"len": elen,
				"slope": slope_deg,
				"centroid": (v0 + v1 + v2) / 3.0,
			})

	var boundary: Array = []
	for rec in edge_recs:
		if int(edge_uses[rec["ek"]]) != 1:
			continue
		if float(rec["slope"]) > WELD_MAX_FACE_SLOPE:
			continue
		boundary.append(rec)
	if boundary.size() < 2:
		return {"faces": out, "tris": 0, "area": 0.0}

	var cell := 1.0
	var buckets: Dictionary = {}
	for bi in range(boundary.size()):
		var mid: Vector3 = boundary[bi]["mid"]
		var key := Vector3i(
			floori(mid.x / cell),
			floori(mid.y / WELD_FLOOR_Y),
			floori(mid.z / cell)
		)
		if not buckets.has(key):
			buckets[key] = PackedInt32Array()
		var arr: PackedInt32Array = buckets[key]
		arr.append(bi)
		buckets[key] = arr

	var used: Dictionary = {} # ek -> true
	# Dense samples on long edges so offset stair-well rings still get hit.
	for bi in range(boundary.size()):
		var ra: Dictionary = boundary[bi]
		var eka: String = ra["ek"]
		if used.has(eka):
			continue
		var a0: Vector3 = ra["a"]
		var a1: Vector3 = ra["b"]
		var dira: Vector3 = ra["dir"]
		var elen: float = ra["len"]
		# Outward = away from parent face centroid in XZ.
		var perp := Vector3(-dira.z, 0.0, dira.x)
		var to_c: Vector3 = ra["centroid"] - ra["mid"]
		to_c.y = 0.0
		if perp.dot(to_c) > 0.0:
			perp = -perp
		if perp.length_squared() < 1e-10:
			continue
		perp = perp.normalized()

		var sample_n := clampi(ceili(elen / 0.35), 3, 16)
		var welded_any := false
		for si in range(sample_n):
			var t := (float(si) + 0.5) / float(sample_n)
			var origin: Vector3 = a0.lerp(a1, t)
			var hit := _weld_probe_hit(origin, perp, ra["mid"].y, eka, boundary, buckets, cell)
			if hit.is_empty():
				continue
			# Local bridge: small patch on A around the sample, paired to hit on B.
			var half_a := minf(0.2, elen * 0.45)
			var la0: Vector3 = origin - dira * half_a
			var la1: Vector3 = origin + dira * half_a
			var clamped_a := _project_segment_onto_edge(la0, la1, a0, a1)
			var b0: Vector3 = hit["b0"]
			var b1: Vector3 = hit["b1"]
			if (clamped_a[1] - clamped_a[0]).dot(b1 - b0) < 0.0:
				var tmp := b0
				b0 = b1
				b1 = tmp
			var area_added := _append_upward_bridge_quad(
				out, clamped_a[0], clamped_a[1], b0, b1
			)
			if area_added <= 0.0:
				continue
			welded_any = true
			weld_tris += 2
			weld_area += area_added
		if welded_any:
			used[eka] = true

	return {"faces": out, "tris": weld_tris, "area": weld_area}


## Cast from origin along +perp in XZ; return closest boundary hit in weld range.
func _weld_probe_hit(
	origin: Vector3,
	perp: Vector3,
	y_ref: float,
	self_ek: String,
	boundary: Array,
	buckets: Dictionary,
	cell: float
) -> Dictionary:
	var best := {}
	var best_dist := WELD_XZ_MAX + 1.0
	var probe_end := origin + perp * WELD_XZ_MAX
	var min_x := minf(origin.x, probe_end.x) - 0.5
	var max_x := maxf(origin.x, probe_end.x) + 0.5
	var min_z := minf(origin.z, probe_end.z) - 0.5
	var max_z := maxf(origin.z, probe_end.z) + 0.5
	var ix0 := floori(min_x / cell)
	var ix1 := floori(max_x / cell)
	var iz0 := floori(min_z / cell)
	var iz1 := floori(max_z / cell)
	var iy := floori(y_ref / WELD_FLOOR_Y)
	for ix in range(ix0, ix1 + 1):
		for iz in range(iz0, iz1 + 1):
			for iy_off in [-1, 0, 1]:
				var key := Vector3i(ix, iy + iy_off, iz)
				if not buckets.has(key):
					continue
				for bj in buckets[key]:
					var rb: Dictionary = boundary[bj]
					var ekb: String = rb["ek"]
					if ekb == self_ek:
						continue
					if absf(float(rb["mid"].y) - y_ref) > WELD_FLOOR_Y:
						continue
					var dirb: Vector3 = rb["dir"]
					# Prefer edges not parallel to the probe (i.e. cross the ray).
					if absf(perp.dot(dirb)) > 0.95:
						continue
					var hit := _segment_ray_hit_xz(origin, perp, rb["a"], rb["b"])
					if hit.is_empty():
						continue
					var hp: Vector3 = hit["point"]
					var dist: float = hit["dist"]
					if dist < WELD_XZ_MIN or dist > WELD_XZ_MAX or dist >= best_dist:
						continue
					# Small segment around the hit on B, clamped to B.
					var half := 0.15
					var bdir: Vector3 = dirb
					var b0: Vector3 = hp - bdir * half
					var b1: Vector3 = hp + bdir * half
					var clamped := _project_segment_onto_edge(b0, b1, rb["a"], rb["b"])
					best_dist = dist
					best = {
						"dist": dist,
						"ek": ekb,
						"b0": clamped[0],
						"b1": clamped[1],
					}
	return best


## Ray origin+t*dir (t≥0) vs segment e0-e1 in XZ. Returns {point, dist} or {}.
func _segment_ray_hit_xz(origin: Vector3, dir: Vector3, e0: Vector3, e1: Vector3) -> Dictionary:
	var ax := origin.x
	var az := origin.z
	var dx := dir.x
	var dz := dir.z
	var ex := e1.x - e0.x
	var ez := e1.z - e0.z
	var denom := dx * ez - dz * ex
	if absf(denom) < 1e-8:
		return {}
	var wx := e0.x - ax
	var wz := e0.z - az
	var t := (wx * ez - wz * ex) / denom
	var u := (wx * dz - wz * dx) / denom
	if t < WELD_XZ_MIN or t > WELD_XZ_MAX or u < 0.0 or u > 1.0:
		return {}
	var pt := Vector3(e0.x + ex * u, (e0.y + e1.y) * 0.5, e0.z + ez * u)
	return {"point": pt, "dist": t}


## Project segment p0-p1 onto edge e0-e1, clamped to the edge.
func _project_segment_onto_edge(p0: Vector3, p1: Vector3, e0: Vector3, e1: Vector3) -> Array:
	var ed := e1 - e0
	var el2 := ed.length_squared()
	if el2 < 1e-10:
		return [e0, e1]
	var t0 := clampf((p0 - e0).dot(ed) / el2, 0.0, 1.0)
	var t1 := clampf((p1 - e0).dot(ed) / el2, 0.0, 1.0)
	if absf(t1 - t0) < 0.02:
		var tm := clampf(0.5 * (t0 + t1), 0.01, 0.99)
		t0 = tm - 0.01
		t1 = tm + 0.01
	return [e0 + ed * t0, e0 + ed * t1]


## Append two tris for quad a0-a1-b1-b0 with upward normals. Returns area added (0 if degenerate).
func _append_upward_bridge_quad(
	out: PackedVector3Array, a0: Vector3, a1: Vector3, b0: Vector3, b1: Vector3
) -> float:
	var n1 := (a1 - a0).cross(b1 - a0)
	var n2 := (b1 - a0).cross(b0 - a0)
	if n1.length_squared() < 1e-10 or n2.length_squared() < 1e-10:
		return 0.0
	# Flip winding if facing down.
	if n1.y < 0.0:
		out.append(a0)
		out.append(b1)
		out.append(a1)
		out.append(a0)
		out.append(b0)
		out.append(b1)
		return 0.5 * (n1.length() + n2.length())
	out.append(a0)
	out.append(a1)
	out.append(b1)
	out.append(a0)
	out.append(b1)
	out.append(b0)
	return 0.5 * (n1.length() + n2.length())


## True if an agent could step between these surface points on the walkable mesh.
func _walkable_step_link(a: Vector3, b: Vector3) -> bool:
	var dx := a.x - b.x
	var dz := a.z - b.z
	var xz := sqrt(dx * dx + dz * dz)
	var dy := absf(a.y - b.y)
	# Same floor / classification hole / kit doorway void.
	if dy <= CONNECT_FLOOR_Y and xz <= CONNECT_FLOOR_XZ:
		return true
	# Stair / ramp tread: needs horizontal travel so stacked decks (xz≈0, dy~0.3) stay split.
	if dy <= CONNECT_STEP_Y and xz >= CONNECT_STEP_XZ_MIN and xz <= CONNECT_STEP_XZ_MAX:
		return true
	# Tall riser / spiral: landings nearly share XZ but dy is a real step, not a deck.
	if (
		dy >= CONNECT_TALL_RISER_Y_MIN
		and dy <= CONNECT_STEP_Y
		and xz <= CONNECT_STEP_XZ_MAX
	):
		return true
	return false


## Edge-connected walkable islands: same color ⇒ continuous walk; different ⇒ no mesh path.
func _build_connectivity_colored_node(faces: PackedVector3Array) -> Node3D:
	var root := Node3D.new()
	root.name = "WalkableConnectivity"
	var tri_count := int(faces.size() / 3)
	if tri_count == 0:
		return root

	var parent := PackedInt32Array()
	parent.resize(tri_count)
	for i in range(tri_count):
		parent[i] = i

	# Shared edges + shared vertices (quantized) unite triangles.
	var edge_tris: Dictionary = {} # String -> PackedInt32Array
	var vert_tris: Dictionary = {} # Vector3i -> PackedInt32Array
	var edge_mids: Array = [] # {mid, ti} for gap bridging
	for ti in range(tri_count):
		var v0: Vector3 = faces[ti * 3]
		var v1: Vector3 = faces[ti * 3 + 1]
		var v2: Vector3 = faces[ti * 3 + 2]
		var verts := [v0, v1, v2]
		for e in range(3):
			var a: Vector3 = verts[e]
			var b: Vector3 = verts[(e + 1) % 3]
			var ek := _edge_qkey(a, b)
			if not edge_tris.has(ek):
				edge_tris[ek] = PackedInt32Array()
			var arr: PackedInt32Array = edge_tris[ek]
			for prev in arr:
				_uf_union(parent, ti, prev)
			arr.append(ti)
			edge_tris[ek] = arr
			edge_mids.append({"mid": (a + b) * 0.5, "ti": ti})
			var vk := _vert_qkey(a)
			if not vert_tris.has(vk):
				vert_tris[vk] = PackedInt32Array()
			var varr: PackedInt32Array = vert_tris[vk]
			for prev_v in varr:
				_uf_union(parent, ti, prev_v)
			varr.append(ti)
			vert_tris[vk] = varr

	# Bridge tiny same-floor gaps between kit placements (not stacked decks ~0.30m).
	var gap_cell := CONNECT_GAP_XZ
	var mid_buckets: Dictionary = {} # Vector3i -> PackedInt32Array of edge_mids indices
	for mi in range(edge_mids.size()):
		var mid: Vector3 = edge_mids[mi]["mid"]
		var key := Vector3i(
			floori(mid.x / gap_cell),
			floori(mid.y / CONNECT_GAP_Y),
			floori(mid.z / gap_cell)
		)
		if not mid_buckets.has(key):
			mid_buckets[key] = PackedInt32Array()
		var marr: PackedInt32Array = mid_buckets[key]
		marr.append(mi)
		mid_buckets[key] = marr
	var gap_xz2 := CONNECT_GAP_XZ * CONNECT_GAP_XZ
	var neighbor_off := [
		Vector3i(0, 0, 0),
		Vector3i(1, 0, 0), Vector3i(-1, 0, 0),
		Vector3i(0, 0, 1), Vector3i(0, 0, -1),
		Vector3i(1, 0, 1), Vector3i(1, 0, -1),
		Vector3i(-1, 0, 1), Vector3i(-1, 0, -1),
		Vector3i(0, 1, 0), Vector3i(0, -1, 0), # same-floor Y quantization jitter
	]
	for mi2 in range(edge_mids.size()):
		var mid_a: Vector3 = edge_mids[mi2]["mid"]
		var ti_a: int = edge_mids[mi2]["ti"]
		var key_a := Vector3i(
			floori(mid_a.x / gap_cell),
			floori(mid_a.y / CONNECT_GAP_Y),
			floori(mid_a.z / gap_cell)
		)
		for off in neighbor_off:
			var nk: Vector3i = key_a + off
			if not mid_buckets.has(nk):
				continue
			var others: PackedInt32Array = mid_buckets[nk]
			for mj in others:
				if mj <= mi2:
					continue
				var mid_b: Vector3 = edge_mids[mj]["mid"]
				if _walkable_step_link(mid_a, mid_b):
					_uf_union(parent, ti_a, int(edge_mids[mj]["ti"]))

	# Centroid proximity: same-floor holes + stair steps (not vertical stacked decks).
	var centroids: PackedVector3Array = PackedVector3Array()
	centroids.resize(tri_count)
	for ti_c in range(tri_count):
		centroids[ti_c] = (
			faces[ti_c * 3] + faces[ti_c * 3 + 1] + faces[ti_c * 3 + 2]
		) / 3.0
	var c_buckets: Dictionary = {}
	var c_cell := CONNECT_FLOOR_XZ
	for ti_c2 in range(tri_count):
		var c: Vector3 = centroids[ti_c2]
		var ck := Vector3i(
			floori(c.x / c_cell),
			floori(c.y / CONNECT_STEP_Y),
			floori(c.z / c_cell)
		)
		if not c_buckets.has(ck):
			c_buckets[ck] = PackedInt32Array()
		var carr: PackedInt32Array = c_buckets[ck]
		carr.append(ti_c2)
		c_buckets[ck] = carr
	var c_off := [
		Vector3i(0, 0, 0),
		Vector3i(1, 0, 0), Vector3i(-1, 0, 0),
		Vector3i(0, 0, 1), Vector3i(0, 0, -1),
		Vector3i(1, 0, 1), Vector3i(1, 0, -1),
		Vector3i(-1, 0, 1), Vector3i(-1, 0, -1),
		Vector3i(0, 1, 0), Vector3i(0, -1, 0),
		Vector3i(1, 1, 0), Vector3i(-1, 1, 0), Vector3i(0, 1, 1), Vector3i(0, 1, -1),
		Vector3i(1, -1, 0), Vector3i(-1, -1, 0), Vector3i(0, -1, 1), Vector3i(0, -1, -1),
	]
	for ti_a2 in range(tri_count):
		var ca: Vector3 = centroids[ti_a2]
		var cka := Vector3i(
			floori(ca.x / c_cell),
			floori(ca.y / CONNECT_STEP_Y),
			floori(ca.z / c_cell)
		)
		for off2 in c_off:
			var nck: Vector3i = cka + off2
			if not c_buckets.has(nck):
				continue
			for ti_b2 in c_buckets[nck]:
				if ti_b2 <= ti_a2:
					continue
				if _walkable_step_link(ca, centroids[ti_b2]):
					_uf_union(parent, ti_a2, ti_b2)

	var region_faces: Dictionary = {} # root id -> PackedVector3Array
	var region_tri_count: Dictionary = {}
	var region_centroid_sum: Dictionary = {} # root -> Vector3
	for ti2 in range(tri_count):
		var r := _uf_find(parent, ti2)
		if not region_faces.has(r):
			region_faces[r] = PackedVector3Array()
			region_tri_count[r] = 0
			region_centroid_sum[r] = Vector3.ZERO
		var rf: PackedVector3Array = region_faces[r]
		rf.append(faces[ti2 * 3])
		rf.append(faces[ti2 * 3 + 1])
		rf.append(faces[ti2 * 3 + 2])
		region_faces[r] = rf
		region_tri_count[r] = int(region_tri_count[r]) + 1
		region_centroid_sum[r] = region_centroid_sum[r] + centroids[ti2]

	# Stable order: largest islands first, then by root id.
	var rids: Array = region_faces.keys()
	rids.sort_custom(func(a, b):
		var ca2: int = region_tri_count[a]
		var cb2: int = region_tri_count[b]
		if ca2 != cb2:
			return ca2 > cb2
		return int(a) < int(b)
	)

	# Diagnose near-miss pairs among major islands (why still disjoint).
	_diagnose_disjoint_majors(
		faces, centroids, parent, rids, region_tri_count, region_centroid_sum
	)

	var out_id := 0
	var size_summary := []
	var scrap_faces := PackedVector3Array()
	var scrap_tris := 0
	var major_count := 0
	for r2 in rids:
		var ntris_r: int = region_tri_count[r2]
		if ntris_r < CONNECT_MAJOR_MIN_TRIS:
			scrap_faces.append_array(region_faces[r2])
			scrap_tris += ntris_r
			continue
		if out_id < 12:
			size_summary.append(ntris_r)
		var bucket := _new_bucket()
		_append_faces(bucket, region_faces[r2])
		var mesh := _commit_bucket(bucket)
		if mesh == null:
			continue
		var mi3 := MeshInstance3D.new()
		mi3.name = "Island_%02d_tris%d" % [out_id, ntris_r]
		mi3.mesh = mesh
		var hue := fposmod(float(out_id) * 0.61803398875, 1.0)
		mi3.material_override = _walkable_material(Color.from_hsv(hue, 0.78, 0.95, 0.7))
		root.add_child(mi3)
		out_id += 1
		major_count += 1
	if not scrap_faces.is_empty():
		var scrap_bucket := _new_bucket()
		_append_faces(scrap_bucket, scrap_faces)
		var scrap_mesh := _commit_bucket(scrap_bucket)
		if scrap_mesh != null:
			var mi_scrap := MeshInstance3D.new()
			mi_scrap.name = "Fragments_tris%d" % scrap_tris
			mi_scrap.mesh = scrap_mesh
			mi_scrap.material_override = _walkable_material(Color(0.45, 0.45, 0.45, 0.35))
			root.add_child(mi_scrap)
	print(
		"  major islands (>=%d tris): %s … total_components=%d major=%d scrap_tris=%d" % [
			CONNECT_MAJOR_MIN_TRIS, str(size_summary), rids.size(), major_count, scrap_tris,
		]
	)
	return root


## Report closest major-island pairs that did not merge, and the blocking reason.
func _diagnose_disjoint_majors(
	faces: PackedVector3Array,
	centroids: PackedVector3Array,
	parent: PackedInt32Array,
	rids: Array,
	region_tri_count: Dictionary,
	region_centroid_sum: Dictionary
) -> void:
	var majors: Array = []
	for r in rids:
		if int(region_tri_count[r]) < CONNECT_MAJOR_MIN_TRIS:
			continue
		var n: int = region_tri_count[r]
		majors.append({
			"root": r,
			"tris": n,
			"center": region_centroid_sum[r] / float(n),
		})
	if majors.size() < 2:
		return
	# All member tris per major (needed for true nearest-boundary distance).
	var members: Dictionary = {}
	for ti in range(centroids.size()):
		var rr := _uf_find(parent, ti)
		if int(region_tri_count.get(rr, 0)) < CONNECT_MAJOR_MIN_TRIS:
			continue
		if not members.has(rr):
			members[rr] = PackedInt32Array()
		var arr: PackedInt32Array = members[rr]
		arr.append(ti)
		members[rr] = arr
	var reason_counts := {
		"near_miss_same_floor": 0,
		"stair_gap": 0,
		"stacked_deck": 0,
		"doorway_gap": 0,
		"far_apart": 0,
	}
	var examples: Array = []
	# Focus on top islands — these are the rooms the user cares about.
	var top_n := mini(8, majors.size())
	print("  top major island centers:")
	for t in range(top_n):
		print(
			"    #%d tris=%d center=%s" % [
				t, majors[t]["tris"], majors[t]["center"],
			]
		)
	# Cap pairwise samples — full O(Na*Nb) on 100k+ tri islands hangs the batch for minutes.
	const DIAG_MAX_SAMPLES := 400
	for i in range(top_n):
		for j in range(i + 1, top_n):
			var ra: int = majors[i]["root"]
			var rb: int = majors[j]["root"]
			var best_xz := 1e9
			var best_dy := 1e9
			var best_a := Vector3.ZERO
			var best_b := Vector3.ZERO
			var mem_a: PackedInt32Array = members[ra]
			var mem_b: PackedInt32Array = members[rb]
			var step_a := maxi(1, int(ceil(float(mem_a.size()) / float(DIAG_MAX_SAMPLES))))
			var step_b := maxi(1, int(ceil(float(mem_b.size()) / float(DIAG_MAX_SAMPLES))))
			var ia := 0
			while ia < mem_a.size():
				var pa: Vector3 = centroids[mem_a[ia]]
				var ib := 0
				while ib < mem_b.size():
					var pb: Vector3 = centroids[mem_b[ib]]
					var dx: float = pa.x - pb.x
					var dz: float = pa.z - pb.z
					var xz: float = sqrt(dx * dx + dz * dz)
					var dy: float = absf(pa.y - pb.y)
					var score := xz + dy * 0.5
					if score < best_xz + best_dy * 0.5:
						best_xz = xz
						best_dy = dy
						best_a = pa
						best_b = pb
					ib += step_b
				ia += step_a
			var reason := "far_apart"
			if best_dy >= 0.2 and best_dy <= 0.55 and best_xz < CONNECT_STEP_XZ_MIN:
				reason = "stacked_deck"
			elif best_dy <= CONNECT_FLOOR_Y and best_xz <= CONNECT_FLOOR_XZ:
				reason = "near_miss_same_floor"
			elif best_dy <= CONNECT_FLOOR_Y and best_xz <= CONNECT_FLOOR_XZ + 1.5:
				reason = "doorway_gap"
			elif best_dy <= CONNECT_STEP_Y and best_xz <= CONNECT_STEP_XZ_MAX:
				reason = "stair_gap"
			elif best_dy <= CONNECT_STEP_Y + 0.2 and best_xz <= CONNECT_STEP_XZ_MAX + 1.0:
				reason = "stair_gap"
			else:
				reason = "far_apart"
			reason_counts[reason] = int(reason_counts[reason]) + 1
			examples.append({
				"reason": reason,
				"xz": best_xz,
				"dy": best_dy,
				"ta": majors[i]["tris"],
				"tb": majors[j]["tris"],
				"a": best_a,
				"b": best_b,
				"would_link": _walkable_step_link(best_a, best_b),
			})
	print(
		"  top-island pair gaps: same_floor_near=%d doorway_gap=%d stair_gap=%d stacked_deck=%d far=%d" % [
			reason_counts["near_miss_same_floor"],
			reason_counts["doorway_gap"],
			reason_counts["stair_gap"],
			reason_counts["stacked_deck"],
			reason_counts["far_apart"],
		]
	)
	examples.sort_custom(func(a, b): return float(a["xz"]) < float(b["xz"]))
	for k in range(mini(12, examples.size())):
		var ex: Dictionary = examples[k]
		print(
			"    %s xz=%.2f dy=%.2f tris=%d/%d would_step_link=%s" % [
				ex["reason"], ex["xz"], ex["dy"], ex["ta"], ex["tb"],
				str(ex["would_link"]),
			]
		)


func _new_bucket() -> Dictionary:
	return {
		"vertices": PackedVector3Array(),
		"indices": PackedInt32Array(),
		"verts": 0,
	}


func _load_member_keys(path: String) -> bool:
	if not FileAccess.file_exists(path):
		return false
	var json := JSON.new()
	if json.parse(FileAccess.get_file_as_string(path)) != OK or typeof(json.data) != TYPE_DICTIONARY:
		return false
	var data: Dictionary = json.data
	if typeof(data.get("keys")) != TYPE_ARRAY:
		return false
	_member_keys.clear()
	for k in data["keys"]:
		_member_keys[str(k)] = true
	return not _member_keys.is_empty()


func _collect_nearby_placements() -> Array:
	var out: Array = []
	var seen: Dictionary = {}
	var dir := DirAccess.open(OBJECT_DATA_DIR)
	if dir == null:
		return out
	dir.list_dir_begin()
	while true:
		var name := dir.get_next()
		if name == "":
			break
		if name in [".", ".."]:
			continue
		# Root-level .json only. Subfolders under ObjectDataJson ignored until re-enabled.
		if not dir.current_is_dir() and name.ends_with(".json"):
			_scan_file(OBJECT_DATA_DIR + name, out, seen)
	dir.list_dir_end()
	return out


func _scan_file(path: String, out: Array, seen: Dictionary) -> void:
	var json := JSON.new()
	if json.parse(FileAccess.get_file_as_string(path)) != OK or typeof(json.data) != TYPE_ARRAY:
		return
	var use_members := not _member_keys.is_empty()
	for item in json.data:
		if typeof(item) != TYPE_DICTIONARY:
			continue
		var rec: Dictionary = item
		var object_name := str(rec.get("name", rec.get("object_name", ""))).to_lower()
		if object_name.is_empty() or object_name == "empty":
			continue
		# Build membership keys in float64 before Vector3 (float32) truncation — otherwise
		# %.3f keys diverge from the Python manifest (e.g. -1101.076 vs -1101.077) and
		# whole kits like cci03 drop out of cluster exports.
		var sx := float(rec.get("x", 0.0))
		var sy := float(rec.get("y", 0.0))
		var sz := float(rec.get("z", 0.0))
		if typeof(rec.get("coordinates")) == TYPE_DICTIONARY:
			var cd: Dictionary = rec["coordinates"]
			sx = float(cd.get("x", 0.0))
			sy = float(cd.get("y", 0.0))
			sz = float(cd.get("z", 0.0))
		var pitch := float(rec.get("pitch", 0.0))
		var yaw := float(rec.get("yaw", 0.0))
		var roll := float(rec.get("roll", 0.0))
		if typeof(rec.get("rotation_euler")) == TYPE_DICTIONARY:
			var rd: Dictionary = rec["rotation_euler"]
			pitch = float(rd.get("pitch", 0.0))
			yaw = float(rd.get("yaw", 0.0))
			roll = float(rd.get("roll", 0.0))
		# SOURCE_BASIS: (x, y, z) -> (x, -y, -z)
		var px := sx
		var py := -sy
		var pz := -sz
		var key := "%s|%.3f|%.3f|%.3f|%.4f|%.4f|%.4f" % [
			object_name, px, py, pz, pitch, yaw, roll,
		]
		var pos := Vector3(px, py, pz)
		var rot := Vector3(pitch, yaw, roll)
		if use_members:
			if not _member_keys.has(key):
				continue
		else:
			if pos.distance_to(_center) > _radius:
				continue
		if _indoor_base_only and not _is_indoor_base_tile(object_name, pos.y):
			continue
		var euler := Vector3(rot.x, -rot.y, rot.z)
		var basis_godot := SOURCE_BASIS * Basis.from_euler(euler) * SOURCE_BASIS
		var xform := Transform3D(basis_godot, pos)
		if seen.has(key):
			continue
		seen[key] = true
		out.append({"object_name": object_name, "transform": xform})


## Mirrors Tools/indoor_area_criteria.gd / IndoorAreaCriteria.cs (for headless -s).
func _is_indoor_base_tile(object_name: String, godot_y: float) -> bool:
	if godot_y >= -500.0:
		return false
	var n := object_name.strip_edges().to_lower()
	if n.is_empty() or n == "empty":
		return false
	if n == "lbridge" or n.begins_with("lbridge"):
		return false
	if n.ends_with("_in"):
		return true
	if n.begins_with("cci"):
		return true
	if n.begins_with("lb"):
		return true
	if n.begins_with("rd_island"):
		return true
	if n in ["rd_r1", "rd_r2", "rd_r3", "rd_r4", "rd_r5", "rd_rh", "room1", "tn4_hotel"]:
		return true
	return false


func _load_object_parts(object_name: String) -> Array:
	if _mesh_parts_cache.has(object_name):
		return _mesh_parts_cache[object_name]
	var parts: Array = []
	var scene: PackedScene = null
	for ext in ["glb", "gltf"]:
		var model_path: String = MODELS_DIR + object_name + "." + ext
		if ResourceLoader.exists(model_path):
			scene = load(model_path)
			break
	if scene == null:
		_mesh_parts_cache[object_name] = parts
		return parts
	var inst := scene.instantiate() as Node3D
	_collect_mesh_parts(inst, Transform3D.IDENTITY, parts)
	inst.free()
	_mesh_parts_cache[object_name] = parts
	return parts


func _collect_mesh_parts(node: Node, parent_xform: Transform3D, out_parts: Array) -> void:
	var xform := parent_xform
	if node is Node3D:
		xform = parent_xform * node.transform
	if node is MeshInstance3D and node.mesh:
		var sk := false
		var cur := node.get_parent()
		while cur:
			if cur is Skeleton3D:
				sk = true
				break
			cur = cur.get_parent()
		if not sk:
			out_parts.append({"mesh": node.mesh.duplicate(), "local": xform})
	for child in node.get_children():
		_collect_mesh_parts(child, xform, out_parts)


func _mesh_aabb_world(mesh: Mesh, xform: Transform3D) -> AABB:
	var local := mesh.get_aabb()
	var out := AABB(xform * local.get_endpoint(0), Vector3.ZERO)
	for ei in range(1, 8):
		out = out.expand(xform * local.get_endpoint(ei))
	return out


func _mesh_faces_world(mesh: Mesh, xform: Transform3D) -> PackedVector3Array:
	var out := PackedVector3Array()
	for s in range(mesh.get_surface_count()):
		out.append_array(_mesh_surface_faces_world(mesh, s, xform))
	return out


func _mesh_surface_aabb_world(mesh: Mesh, surface: int, xform: Transform3D) -> AABB:
	var arrays := mesh.surface_get_arrays(surface)
	var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	if verts.is_empty():
		return AABB()
	var out := AABB(xform * verts[0], Vector3.ZERO)
	for vi in range(1, verts.size()):
		out = out.expand(xform * verts[vi])
	return out


func _mesh_surface_faces_world(mesh: Mesh, surface: int, xform: Transform3D) -> PackedVector3Array:
	var out := PackedVector3Array()
	var arrays := mesh.surface_get_arrays(surface)
	var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var indices = arrays[Mesh.ARRAY_INDEX]
	if indices == null or indices.is_empty():
		for vi in range(0, verts.size() - 2, 3):
			out.append(xform * verts[vi])
			out.append(xform * verts[vi + 1])
			out.append(xform * verts[vi + 2])
	else:
		for ii in range(0, indices.size() - 2, 3):
			out.append(xform * verts[indices[ii]])
			out.append(xform * verts[indices[ii + 1]])
			out.append(xform * verts[indices[ii + 2]])
	return out


## Walkable keep profile (uniform indoor-clusters rules for all base-tile groups).
func _walk_profile_for(_object_name: String) -> Dictionary:
	return {
		"group": "room",
		"max_slope": _max_slope_deg,
		"inward_dot_min": INWARD_DOT_MIN,
		"wing_dot_min": WING_DOT_MIN,
		"roof_skin": ROOF_SKIN_BAND,
		"footprint_inset": FOOTPRINT_INSET,
		"require_footprint": false,
		"require_below_roof": false,
		"reject_outer_flat": false,
		"outer_flat_max_slope": 90.0,
		"reject_outer_steep": false,
		"outer_max_slope": 90.0,
		"reject_exposed_flat": false,
		"exposed_flat_max_slope": 90.0,
		"ignore_roof_skin_ceilings": false,
		"covered_needs_inward": false,
		"ceil_min": CEILING_MIN_HEADROOM,
		"ceil_max": CEILING_MAX_HEADROOM,
	}


## Build XZ grid of downward (ceiling) face Ys for one placement.
func _build_ceiling_grid(
	object_faces: Array, max_slope: float, shell_max_y: float, profile: Dictionary
) -> Dictionary:
	var grid: Dictionary = {} # Vector2i -> PackedFloat32Array
	var ignore_roof_skin: bool = bool(profile.get("ignore_roof_skin_ceilings", false))
	var roof_skin: float = float(profile["roof_skin"])
	for faces_v in object_faces:
		var faces: PackedVector3Array = faces_v
		var i := 0
		while i + 2 < faces.size():
			var a: Vector3 = faces[i]
			var b: Vector3 = faces[i + 1]
			var c: Vector3 = faces[i + 2]
			i += 3
			var normal := (b - a).cross(c - a)
			if normal.length_squared() < 0.000001:
				continue
			normal = normal.normalized()
			# Ceiling undersides face down. (Also accept inverted lids via force-down.)
			if normal.y > 0.0:
				normal = -normal
			if normal.y >= 0.0:
				continue
			var slope_deg := rad_to_deg(acos(clampf(-normal.y, 0.0, 1.0)))
			if slope_deg > max_slope:
				continue
			var centroid := (a + b + c) / 3.0
			# Outer roof lid underside must not count as interior cover for cave decks.
			if ignore_roof_skin and centroid.y >= shell_max_y - roof_skin:
				continue
			var key := Vector2i(floori(centroid.x / CEILING_CELL), floori(centroid.z / CEILING_CELL))
			if not grid.has(key):
				grid[key] = PackedFloat32Array()
			var arr: PackedFloat32Array = grid[key]
			arr.append(centroid.y)
			grid[key] = arr
	return grid


func _has_ceiling_above(centroid: Vector3, ceiling_grid: Dictionary, ceil_min: float, ceil_max: float) -> bool:
	var cx := floori(centroid.x / CEILING_CELL)
	var cz := floori(centroid.z / CEILING_CELL)
	for dx in range(-1, 2):
		for dz in range(-1, 2):
			var key := Vector2i(cx + dx, cz + dz)
			if not ceiling_grid.has(key):
				continue
			var ys: PackedFloat32Array = ceiling_grid[key]
			for cy in ys:
				if cy > centroid.y + ceil_min and cy < centroid.y + ceil_max:
					return true
	return false


func _inside_footprint(centroid: Vector3, shell_aabb: AABB, inset: float) -> bool:
	# Tiny doorway kits: don't hollow out their whole footprint.
	if shell_aabb.size.x < inset * 2.5 or shell_aabb.size.z < inset * 2.5:
		inset = min(shell_aabb.size.x, shell_aabb.size.z) * 0.15
	var min_x := shell_aabb.position.x + inset
	var max_x := shell_aabb.position.x + shell_aabb.size.x - inset
	var min_z := shell_aabb.position.z + inset
	var max_z := shell_aabb.position.z + shell_aabb.size.z - inset
	return centroid.x >= min_x and centroid.x <= max_x and centroid.z >= min_z and centroid.z <= max_z


## Voxelize kit → morphological close (seal doorways) → depth envelopes (+Y, ±X, ±Z).
func _build_closed_outer_shell_maps(object_faces: Array) -> Dictionary:
	var occ: Dictionary = {} # Vector3i -> true
	for faces_v in object_faces:
		var faces: PackedVector3Array = faces_v
		var i := 0
		while i + 2 < faces.size():
			_voxelize_triangle(occ, faces[i], faces[i + 1], faces[i + 2])
			i += 3
	if occ.is_empty():
		return {}
	var closed := _morphological_close(occ, SHELL_CLOSE_ITERS)
	return _depth_envelopes_from_voxels(closed)


func _voxelize_triangle(occ: Dictionary, a: Vector3, b: Vector3, c: Vector3) -> void:
	var cell := SHELL_CELL
	var min_x := minf(a.x, minf(b.x, c.x))
	var max_x := maxf(a.x, maxf(b.x, c.x))
	var min_y := minf(a.y, minf(b.y, c.y))
	var max_y := maxf(a.y, maxf(b.y, c.y))
	var min_z := minf(a.z, minf(b.z, c.z))
	var max_z := maxf(a.z, maxf(b.z, c.z))
	var ix0 := floori(min_x / cell)
	var ix1 := floori(max_x / cell)
	var iy0 := floori(min_y / cell)
	var iy1 := floori(max_y / cell)
	var iz0 := floori(min_z / cell)
	var iz1 := floori(max_z / cell)
	var vol := (ix1 - ix0 + 1) * (iy1 - iy0 + 1) * (iz1 - iz0 + 1)
	if vol > 8000:
		# Degenerate/huge sliver: stamp samples only.
		for p in [a, b, c, (a + b + c) / 3.0, (a + b) * 0.5, (b + c) * 0.5, (c + a) * 0.5]:
			occ[Vector3i(floori(p.x / cell), floori(p.y / cell), floori(p.z / cell))] = true
		return
	var thresh2 := (cell * 0.85) * (cell * 0.85)
	for ix in range(ix0, ix1 + 1):
		for iy in range(iy0, iy1 + 1):
			for iz in range(iz0, iz1 + 1):
				var p := Vector3(
					(float(ix) + 0.5) * cell,
					(float(iy) + 0.5) * cell,
					(float(iz) + 0.5) * cell
				)
				if _point_triangle_dist2(p, a, b, c) <= thresh2:
					occ[Vector3i(ix, iy, iz)] = true


func _point_triangle_dist2(p: Vector3, a: Vector3, b: Vector3, c: Vector3) -> float:
	# Ericson: distance from point to triangle (squared).
	var ab := b - a
	var ac := c - a
	var ap := p - a
	var d1 := ab.dot(ap)
	var d2 := ac.dot(ap)
	if d1 <= 0.0 and d2 <= 0.0:
		return ap.length_squared()
	var bp := p - b
	var d3 := ab.dot(bp)
	var d4 := ac.dot(bp)
	if d3 >= 0.0 and d4 <= d3:
		return bp.length_squared()
	var vc := d1 * d4 - d3 * d2
	if vc <= 0.0 and d1 >= 0.0 and d3 <= 0.0:
		var v := d1 / (d1 - d3)
		return (a + ab * v - p).length_squared()
	var cp := p - c
	var d5 := ab.dot(cp)
	var d6 := ac.dot(cp)
	if d6 >= 0.0 and d5 <= d6:
		return cp.length_squared()
	var vb := d5 * d2 - d1 * d6
	if vb <= 0.0 and d2 >= 0.0 and d6 <= 0.0:
		var w := d2 / (d2 - d6)
		return (a + ac * w - p).length_squared()
	var va := d3 * d6 - d5 * d4
	if va <= 0.0 and (d4 - d3) >= 0.0 and (d5 - d6) >= 0.0:
		var w2 := (d4 - d3) / ((d4 - d3) + (d5 - d6))
		return (b + (c - b) * w2 - p).length_squared()
	var denom := 1.0 / (va + vb + vc)
	var v3 := vb * denom
	var w3 := vc * denom
	return (a + ab * v3 + ac * w3 - p).length_squared()


func _morphological_close(occ: Dictionary, iters: int) -> Dictionary:
	var cur: Dictionary = occ.duplicate()
	for _i in range(iters):
		cur = _voxel_dilate(cur)
	for _j in range(iters):
		cur = _voxel_erode(cur)
	return cur


func _voxel_dilate(occ: Dictionary) -> Dictionary:
	var out: Dictionary = occ.duplicate()
	var offs := [
		Vector3i(1, 0, 0), Vector3i(-1, 0, 0),
		Vector3i(0, 1, 0), Vector3i(0, -1, 0),
		Vector3i(0, 0, 1), Vector3i(0, 0, -1),
	]
	for k in occ.keys():
		var c: Vector3i = k
		for o in offs:
			out[c + o] = true
	return out


func _voxel_erode(occ: Dictionary) -> Dictionary:
	var out: Dictionary = {}
	var offs := [
		Vector3i(0, 0, 0),
		Vector3i(1, 0, 0), Vector3i(-1, 0, 0),
		Vector3i(0, 1, 0), Vector3i(0, -1, 0),
		Vector3i(0, 0, 1), Vector3i(0, 0, -1),
	]
	for k in occ.keys():
		var c: Vector3i = k
		var ok := true
		for o in offs:
			if not occ.has(c + o):
				ok = false
				break
		if ok:
			out[c] = true
	return out


func _depth_envelopes_from_voxels(occ: Dictionary) -> Dictionary:
	var cell := SHELL_CELL
	var max_y: Dictionary = {} # Vector2i(ix,iz) -> world y
	var max_x: Dictionary = {} # Vector2i(iy,iz) -> world x
	var min_x: Dictionary = {}
	var max_z: Dictionary = {} # Vector2i(ix,iy) -> world z
	var min_z: Dictionary = {}
	for k in occ.keys():
		var c: Vector3i = k
		var wx := (float(c.x) + 0.5) * cell
		var wy := (float(c.y) + 0.5) * cell
		var wz := (float(c.z) + 0.5) * cell
		var kxz := Vector2i(c.x, c.z)
		var kyz := Vector2i(c.y, c.z)
		var kxy := Vector2i(c.x, c.y)
		if not max_y.has(kxz) or wy > float(max_y[kxz]):
			max_y[kxz] = wy
		if not max_x.has(kyz) or wx > float(max_x[kyz]):
			max_x[kyz] = wx
		if not min_x.has(kyz) or wx < float(min_x[kyz]):
			min_x[kyz] = wx
		if not max_z.has(kxy) or wz > float(max_z[kxy]):
			max_z[kxy] = wz
		if not min_z.has(kxy) or wz < float(min_z[kxy]):
			min_z[kxy] = wz
	return {
		"max_y": max_y,
		"max_x": max_x,
		"min_x": min_x,
		"max_z": max_z,
		"min_z": min_z,
	}


## True if face sits on the closed outer envelope (top or any of 4 sides).
## Side hits are restricted: crown band + steep enough + near kit AABB face
## (kills flat interior slab artifacts without touching doorway floors).
func _is_closed_outer_shell_face(
	a: Vector3,
	b: Vector3,
	c: Vector3,
	shell_maps: Dictionary,
	shell_aabb: AABB,
	slope_deg: float
) -> bool:
	if shell_maps.is_empty():
		return false
	var max_y: Dictionary = shell_maps["max_y"]
	var max_x: Dictionary = shell_maps["max_x"]
	var min_x: Dictionary = shell_maps["min_x"]
	var max_z: Dictionary = shell_maps["max_z"]
	var min_z: Dictionary = shell_maps["min_z"]
	var samples := [a, b, c, (a + b + c) / 3.0]
	var on_outer := 0
	var checked := 0
	var cell := SHELL_CELL
	var eps := SHELL_EPS
	var aabb_min := shell_aabb.position
	var aabb_max := shell_aabb.position + shell_aabb.size
	var band := SHELL_SIDE_AABB_BAND
	# Near-vertical outer skin only — do not side-mark stair/ramp treads (35–74°).
	var allow_sides := slope_deg >= SHELL_SIDE_MIN_SLOPE and slope_deg <= SHELL_SIDE_MAX_SLOPE
	for s in samples:
		var p: Vector3 = s
		var ix := floori(p.x / cell)
		var iy := floori(p.y / cell)
		var iz := floori(p.z / cell)
		var hit := false
		# +Y (top) — full sky envelope (unchanged; main outer-shell cleaner).
		var y_env := _envelope_lookup_extreme(max_y, Vector2i(ix, iz), true)
		if y_env < 1.0e19 and p.y >= y_env - eps:
			hit = true
		# ±X / ±Z: upper crown + steep + true kit perimeter (not thin interior slabs).
		var in_crown := y_env < 1.0e19 and p.y >= y_env - SHELL_SIDE_CROWN_BAND
		if not hit and allow_sides and in_crown:
			var x_hi := _envelope_lookup_extreme(max_x, Vector2i(iy, iz), true)
			var x_lo := _envelope_lookup_extreme(min_x, Vector2i(iy, iz), false)
			var z_hi := _envelope_lookup_extreme(max_z, Vector2i(ix, iy), true)
			var z_lo := _envelope_lookup_extreme(min_z, Vector2i(ix, iy), false)
			var near_pos_x := p.x >= aabb_max.x - band
			var near_neg_x := p.x <= aabb_min.x + band
			var near_pos_z := p.z >= aabb_max.z - band
			var near_neg_z := p.z <= aabb_min.z + band
			if near_pos_x and x_hi < 1.0e19 and p.x >= x_hi - eps:
				hit = true
			elif near_neg_x and x_lo > -1.0e19 and p.x <= x_lo + eps:
				hit = true
			elif near_pos_z and z_hi < 1.0e19 and p.z >= z_hi - eps:
				hit = true
			elif near_neg_z and z_lo > -1.0e19 and p.z <= z_lo + eps:
				hit = true
		checked += 1
		if hit:
			on_outer += 1
	if checked == 0:
		return false
	return on_outer * 2 >= checked


## Neighborhood lookup: want_max=true → highest neighbor value; else lowest.
func _envelope_lookup_extreme(grid: Dictionary, key: Vector2i, want_max: bool) -> float:
	var best := -1.0e20 if want_max else 1.0e20
	var found := false
	for dx in range(-1, 2):
		for dy in range(-1, 2):
			var nk := Vector2i(key.x + dx, key.y + dy)
			if not grid.has(nk):
				continue
			var v := float(grid[nk])
			found = true
			if want_max:
				best = maxf(best, v)
			else:
				best = minf(best, v)
	if not found:
		return 1.0e20 if want_max else -1.0e20
	return best


## Pre-lb* / early indoor: upward faces with slope ≤ max (flip downward windings).
func _split_simple_upward_walkable(faces: PackedVector3Array, max_slope: float) -> Dictionary:
	var walkable := PackedVector3Array()
	var tris := 0
	var area := 0.0
	var flat0_15 := 0
	var ramp15_35 := 0
	var steep35_70 := 0
	var i := 0
	while i + 2 < faces.size():
		var a: Vector3 = faces[i]
		var b: Vector3 = faces[i + 1]
		var c: Vector3 = faces[i + 2]
		i += 3
		var normal := (b - a).cross(c - a)
		var nlen2 := normal.length_squared()
		if nlen2 < 0.000001:
			continue
		var nlen := sqrt(nlen2)
		normal /= nlen
		if normal.y < 0.0:
			normal = -normal
		if normal.y <= 0.0:
			continue
		var slope_deg := rad_to_deg(acos(clampf(normal.y, 0.0, 1.0)))
		if slope_deg > max_slope:
			continue
		walkable.append(a)
		walkable.append(b)
		walkable.append(c)
		tris += 1
		area += 0.5 * nlen
		if slope_deg <= 15.0:
			flat0_15 += 1
		elif slope_deg <= 35.0:
			ramp15_35 += 1
		else:
			steep35_70 += 1
	return {
		"walkable": walkable,
		"tris": tris,
		"area": area,
		"flat0_15": flat0_15,
		"ramp15_35": ramp15_35,
		"steep35_70": steep35_70,
	}


## Outer shell (closed top+sides) excluded first, then inward/wing/covered keep rules.
func _split_inward_walkable(
	faces: PackedVector3Array,
	shell_center: Vector3,
	shell_max_y: float,
	shell_aabb: AABB,
	ceiling_grid: Dictionary,
	profile: Dictionary,
	shell_maps: Dictionary
) -> Dictionary:
	var walkable := PackedVector3Array()
	var outer_shell := PackedVector3Array()
	var tris := 0
	var area := 0.0
	var dropped_tris := 0
	var dropped_area := 0.0
	var dropped_topdown_tris := 0
	var dropped_topdown_area := 0.0
	var flat0_15 := 0
	var ramp15_35 := 0
	var steep35_70 := 0
	var max_slope: float = float(profile["max_slope"])
	var inward_min: float = float(profile["inward_dot_min"])
	var wing_min: float = float(profile["wing_dot_min"])
	var roof_skin: float = float(profile["roof_skin"])
	var inset: float = float(profile["footprint_inset"])
	var require_fp: bool = bool(profile["require_footprint"])
	var reject_outer_steep: bool = bool(profile.get("reject_outer_steep", false))
	var outer_max_slope: float = float(profile.get("outer_max_slope", 90.0))
	var reject_outer_flat: bool = bool(profile.get("reject_outer_flat", false))
	var outer_flat_max_slope: float = float(profile.get("outer_flat_max_slope", 90.0))
	var reject_exposed_flat: bool = bool(profile.get("reject_exposed_flat", false))
	var exposed_flat_max_slope: float = float(profile.get("exposed_flat_max_slope", 90.0))
	var require_below_roof: bool = bool(profile.get("require_below_roof", false))
	var covered_needs_inward: bool = bool(profile["covered_needs_inward"])
	var ceil_min: float = float(profile["ceil_min"])
	var ceil_max: float = float(profile["ceil_max"])
	var i := 0
	while i + 2 < faces.size():
		var a: Vector3 = faces[i]
		var b: Vector3 = faces[i + 1]
		var c: Vector3 = faces[i + 2]
		i += 3
		var normal := (b - a).cross(c - a)
		var nlen2 := normal.length_squared()
		if nlen2 < 0.000001:
			continue
		var nlen := sqrt(nlen2)
		normal /= nlen
		# Kit floors are often wound with downward normals; treat the upward side as walkable.
		if normal.y < 0.0:
			normal = -normal
		if normal.y <= 0.0:
			continue
		var slope_deg := rad_to_deg(acos(clampf(normal.y, 0.0, 1.0)))
		if slope_deg > max_slope:
			continue
		var tri_area := 0.5 * nlen
		var centroid := (a + b + c) / 3.0
		# Closed outer envelope (top + 4 sides, doorways sealed) — never nav.
		if _is_closed_outer_shell_face(a, b, c, shell_maps, shell_aabb, slope_deg):
			outer_shell.append(a)
			outer_shell.append(b)
			outer_shell.append(c)
			dropped_topdown_tris += 1
			dropped_topdown_area += tri_area
			continue
		var to_center := shell_center - centroid
		var tlen := to_center.length()
		if tlen < 0.0001:
			dropped_tris += 1
			dropped_area += tri_area
			continue
		var inward_dot := normal.dot(to_center / tlen)
		var below_roof := centroid.y < shell_max_y - roof_skin
		var in_footprint := _inside_footprint(centroid, shell_aabb, inset)
		if reject_outer_flat and not in_footprint and slope_deg <= outer_flat_max_slope:
			dropped_tris += 1
			dropped_area += tri_area
			continue
		if reject_outer_steep and not in_footprint:
			if slope_deg > outer_max_slope or not below_roof:
				dropped_tris += 1
				dropped_area += tri_area
				continue
		var has_ceil := _has_ceiling_above(centroid, ceiling_grid, ceil_min, ceil_max)
		if (
			reject_exposed_flat
			and in_footprint
			and slope_deg <= exposed_flat_max_slope
			and not has_ceil
		):
			dropped_tris += 1
			dropped_area += tri_area
			continue
		var clear_inward := inward_dot >= inward_min
		if require_fp:
			clear_inward = clear_inward and in_footprint
		if require_below_roof and not below_roof:
			dropped_tris += 1
			dropped_area += tri_area
			continue
		# Stair/ramp treads: normals often point away from the kit AABB center, so inward/wing
		# fail even though the face is the climb between floors. Keep if under the roof skin.
		var stair_ramp := slope_deg > STAIR_KEEP_MIN_SLOPE and below_roof
		var wing_floor := inward_dot >= wing_min and below_roof and in_footprint
		var covered_upper := below_roof and in_footprint and has_ceil
		if covered_upper and covered_needs_inward and inward_dot < wing_min:
			covered_upper = false
		if not stair_ramp and not clear_inward and not wing_floor and not covered_upper:
			dropped_tris += 1
			dropped_area += tri_area
			continue
		walkable.append(a)
		walkable.append(b)
		walkable.append(c)
		tris += 1
		area += tri_area
		if slope_deg <= 15.0:
			flat0_15 += 1
		elif slope_deg <= 35.0:
			ramp15_35 += 1
		else:
			steep35_70 += 1
	return {
		"walkable": walkable,
		"outer_shell": outer_shell,
		"tris": tris,
		"area": area,
		"dropped_tris": dropped_tris,
		"dropped_area": dropped_area,
		"dropped_topdown_tris": dropped_topdown_tris,
		"dropped_topdown_area": dropped_topdown_area,
		"flat0_15": flat0_15,
		"ramp15_35": ramp15_35,
		"steep35_70": steep35_70,
	}


## Authored non-walkable roof kits (same IDs as bake_and_export_single_nav.gd).
func _is_never_walkable_object(object_name: String) -> bool:
	var n := object_name.to_lower()
	var prefix := ""
	var num_str := ""
	if n.begins_with("hrz"):
		prefix = "hrz"
		num_str = n.substr(3)
	elif n.begins_with("cc") and not n.begins_with("cc_") and not n.begins_with("cci"):
		prefix = "cc"
		num_str = n.substr(2)
	else:
		return false
	var digits := ""
	for ci in range(num_str.length()):
		var ch := num_str.substr(ci, 1)
		if ch.is_valid_int():
			digits += ch
		else:
			break
	if digits.is_empty():
		return false
	var id := int(digits)
	if prefix == "hrz":
		return (id >= 0 and id <= 13) or (id >= 78 and id <= 85)
	if id >= 1 and id <= 13:
		return true
	if id in [18, 20, 38, 39, 41, 42, 44, 45, 47, 48, 57, 58]:
		return true
	return false


func _append_faces(entry: Dictionary, faces: PackedVector3Array) -> void:
	var verts_out: PackedVector3Array = entry["vertices"]
	var indices_out: PackedInt32Array = entry["indices"]
	var base := verts_out.size()
	var i := 0
	while i + 2 < faces.size():
		verts_out.append(faces[i])
		verts_out.append(faces[i + 1])
		verts_out.append(faces[i + 2])
		indices_out.append(base)
		indices_out.append(base + 1)
		indices_out.append(base + 2)
		base += 3
		i += 3
	entry["verts"] = indices_out.size()


func _append_mesh(entry: Dictionary, mesh: Mesh, xform: Transform3D) -> void:
	if mesh == null:
		return
	var verts_out: PackedVector3Array = entry["vertices"]
	var indices_out: PackedInt32Array = entry["indices"]
	var base := verts_out.size()
	for si in range(mesh.get_surface_count()):
		var arrays := mesh.surface_get_arrays(si)
		var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		if verts.is_empty():
			continue
		var indices = arrays[Mesh.ARRAY_INDEX]
		for v in verts:
			verts_out.append(xform * v)
		if indices != null and indices.size() > 0:
			for idx in indices:
				indices_out.append(base + idx)
		else:
			for vi in range(verts.size()):
				indices_out.append(base + vi)
		base = verts_out.size()
	entry["verts"] = indices_out.size()


func _commit_bucket(entry: Dictionary) -> ArrayMesh:
	if entry.get("verts", 0) == 0:
		return null
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = entry["vertices"]
	arrays[Mesh.ARRAY_INDEX] = entry["indices"]
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh
