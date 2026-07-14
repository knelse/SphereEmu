class_name NavGlbMerge
extends RefCounted

static var _ground_mat: StandardMaterial3D
static var _nav_mat: StandardMaterial3D
static var _category_materials: Dictionary = {}

static func new_bucket() -> Dictionary:
	return {
		"vertices": PackedVector3Array(),
		"indices": PackedInt32Array(),
		"verts": 0,
	}

static func new_tile_state() -> Dictionary:
	return {
		"ground": new_bucket(),
		"object": {
			"plant": new_bucket(),
			"rock": new_bucket(),
			"other": new_bucket(),
		},
		"nav": new_bucket(),
	}

static func append_mesh(entry: Dictionary, mesh: Mesh, xform: Transform3D) -> void:
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
			for i in range(verts.size()):
				indices_out.append(base + i)
		base = verts_out.size()
	entry["verts"] = indices_out.size()

static func append_nav(entry: Dictionary, nav: NavigationMesh, rebase: Vector3) -> bool:
	return append_nav_xform(entry, nav, rebase, Basis.IDENTITY)

static func append_nav_xform(
	entry: Dictionary,
	nav: NavigationMesh,
	rebase: Vector3,
	basis: Basis
) -> bool:
	var verts := nav.get_vertices()
	var poly_count := nav.get_polygon_count()
	if verts.is_empty() or poly_count == 0:
		return false
	var verts_out: PackedVector3Array = entry["vertices"]
	var indices_out: PackedInt32Array = entry["indices"]
	var base := verts_out.size()
	for p in range(poly_count):
		var poly: PackedInt32Array = nav.get_polygon(p)
		if poly.size() < 3:
			continue
		for j in range(1, poly.size() - 1):
			verts_out.append(basis * (verts[poly[0]] - rebase))
			verts_out.append(basis * (verts[poly[j]] - rebase))
			verts_out.append(basis * (verts[poly[j + 1]] - rebase))
			indices_out.append(base)
			indices_out.append(base + 1)
			indices_out.append(base + 2)
			base += 3
	entry["verts"] = indices_out.size()
	return true

static func commit_bucket(entry: Dictionary) -> ArrayMesh:
	if entry.get("verts", 0) == 0:
		return null
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = entry["vertices"]
	arrays[Mesh.ARRAY_INDEX] = entry["indices"]
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh

static func add_mesh_instance(parent: Node3D, name: String, mesh: ArrayMesh, mat: Material) -> void:
	if mesh == null:
		return
	var mi := MeshInstance3D.new()
	mi.name = name
	mi.mesh = mesh
	mi.material_override = mat
	parent.add_child(mi)

static func finalize_tile_meshes(terrain_root: Node3D, nav_root: Node3D, merge: Dictionary) -> void:
	add_mesh_instance(terrain_root, "Ground", commit_bucket(merge["ground"]), ground_material())
	for category in ["plant", "rock", "other"]:
		add_mesh_instance(
			terrain_root,
			"Objects_" + category,
			commit_bucket(merge["object"][category]),
			object_material(category)
		)
	add_mesh_instance(nav_root, "Navigation", commit_bucket(merge["nav"]), nav_material())

static func ground_material() -> StandardMaterial3D:
	if _ground_mat == null:
		_ground_mat = StandardMaterial3D.new()
		_ground_mat.albedo_color = Color(0.82, 0.78, 0.70)
		_ground_mat.roughness = 0.9
	return _ground_mat

static func nav_material() -> StandardMaterial3D:
	if _nav_mat == null:
		_nav_mat = StandardMaterial3D.new()
		_nav_mat.albedo_color = Color(0.2, 0.95, 0.35)
		_nav_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	return _nav_mat

static func object_material(category: String) -> StandardMaterial3D:
	if _category_materials.has(category):
		return _category_materials[category]
	var mat := StandardMaterial3D.new()
	mat.roughness = 0.85
	match category:
		"plant":
			mat.albedo_color = Color(0.22, 0.62, 0.28)
		"rock":
			mat.albedo_color = Color(0.62, 0.6, 0.58)
		_:
			mat.albedo_color = Color(0.72, 0.32, 0.22)
	_category_materials[category] = mat
	return mat

static func apply_translation(xform: Transform3D, offset: Vector3) -> Transform3D:
	var out := xform
	out.origin += offset
	return out
