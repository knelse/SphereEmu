class_name IndoorAreaCriteria
extends RefCounted

## Criteria for indoor / dungeon base geometry (mirrors Godot/Scripts/Terrain/IndoorAreaCriteria.cs).
## Base tiles: deep placement Y + name patterns. Props may sit inside/around them and are not
## matched by name alone.
##
## Godot/SOURCE_BASIS Y = -ObjectDataJson.y (TerrainObjectsFill SOURCE_BASIS).

const MAX_INDOOR_PLACEMENT_Y := -500.0


static func is_indoor_depth(godot_y: float) -> bool:
	return godot_y < MAX_INDOOR_PLACEMENT_Y


static func is_indoor_base_tile_name(object_name: String) -> bool:
	var name := object_name.strip_edges().to_lower()
	if name.is_empty() or name == "empty":
		return false
	if name == "lbridge" or name.begins_with("lbridge"):
		return false
	if name.ends_with("_in"):
		return true
	if name.begins_with("cci"):
		return true
	# lb* labyrinth / dungeon shells — exclude lbridge*
	if name.begins_with("lb"):
		return true
	if name.begins_with("rd_island"):
		return true
	if name in ["rd_r1", "rd_r2", "rd_r3", "rd_r4", "rd_r5"]:
		return true
	if name in ["rd_rh", "room1", "tn4_hotel"]:
		return true
	return false


static func is_indoor_base_tile(object_name: String, godot_y: float) -> bool:
	return is_indoor_depth(godot_y) and is_indoor_base_tile_name(object_name)
