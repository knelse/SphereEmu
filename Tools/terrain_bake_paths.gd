# Shared resolver for TerrainBake/ (sibling of Godot/, .gdignore'd).
# Usage: const Bake = preload("res://Tools/terrain_bake_paths.gd")

static func root() -> String:
	var env := OS.get_environment("TERRAIN_BAKE_PATH")
	if not env.is_empty() and DirAccess.dir_exists_absolute(env):
		return env.replace("\\", "/")
	var project := ProjectSettings.globalize_path("res://").rstrip("/\\")
	var candidate := project.path_join("TerrainBake")
	if DirAccess.dir_exists_absolute(candidate):
		return candidate.replace("\\", "/")
	var exe_dir := OS.get_executable_path().get_base_dir()
	candidate = exe_dir.path_join("TerrainBake")
	if DirAccess.dir_exists_absolute(candidate):
		return candidate.replace("\\", "/")
	return project.path_join("TerrainBake").replace("\\", "/")


static func nav_meshes() -> String:
	return root().path_join("GeneratedNavMeshes") + "/"


static func indoor_nav_meshes() -> String:
	return root().path_join("GeneratedIndoorNavMeshes") + "/"


static func multi_meshes() -> String:
	return root().path_join("GeneratedMultiMeshes") + "/"


static func ground_chunks() -> String:
	return root().path_join("GroundChunks")


static func ground_meshes() -> String:
	return root().path_join("GroundMeshes") + "/"


static func ground_shapes() -> String:
	return root().path_join("GroundShapes") + "/"
