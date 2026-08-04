namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Placement categories stored as chunk scenes under <c>Godot/World/Chunks/{folder}/</c>.
/// </summary>
public enum WorldContentKind : byte
{
	Monster = 1,
	Door = 2,
	DoorWithKey = 3,
	Npc = 4,
	Alchemy = 5,
	DungeonEntrance = 6,
	Workshop = 7,
	LightCrystal = 8,
	CastleChest = 9,
	CastleElixirPillar = 10,
	CastleTablet = 11,
	CastleGate = 12,
	CastleTeleport = 13,
	CastleEntrance = 14,
	Teleport = 15,
	TeleportWild = 16,
	TeleportBroken = 17,
	TeleportInDungeon = 18,
	TeleportDungeonChoiceIsland = 19,
	TeleportPoint = 20,
}

public static class WorldContentKindPaths
{
	public static string FolderName(WorldContentKind kind) => kind switch
	{
		WorldContentKind.Monster => "monster",
		WorldContentKind.Door => "door",
		WorldContentKind.DoorWithKey => "door_key",
		WorldContentKind.Npc => "npc",
		WorldContentKind.Alchemy => "alchemy",
		WorldContentKind.DungeonEntrance => "dungeon_entrance",
		WorldContentKind.Workshop => "workshop",
		WorldContentKind.LightCrystal => "light_crystal",
		WorldContentKind.CastleChest => "castle_chest",
		WorldContentKind.CastleElixirPillar => "castle_elixir_pillar",
		WorldContentKind.CastleTablet => "castle_tablet",
		WorldContentKind.CastleGate => "castle_gate",
		WorldContentKind.CastleTeleport => "castle_teleport",
		WorldContentKind.CastleEntrance => "castle_entrance",
		WorldContentKind.Teleport => "teleport",
		WorldContentKind.TeleportWild => "teleport_wild",
		WorldContentKind.TeleportBroken => "teleport_broken",
		WorldContentKind.TeleportInDungeon => "teleport_in_dungeon",
		WorldContentKind.TeleportDungeonChoiceIsland => "teleport_dungeon_choice_island",
		WorldContentKind.TeleportPoint => "teleport_point",
		_ => "unknown",
	};

	/// <summary>
	///     Node path under MainServer for the parent that owns instances of this kind.
	/// </summary>
	public static string ParentNodePath(WorldContentKind kind) => kind switch
	{
		WorldContentKind.Monster => "MonsterSpawners",
		WorldContentKind.Door => "Doors",
		WorldContentKind.DoorWithKey => "DoorsWithKey",
		WorldContentKind.Npc => "NPCs",
		WorldContentKind.Alchemy => "AlchemyMaterialSpawners",
		WorldContentKind.DungeonEntrance => "DungeonEntrances",
		WorldContentKind.Workshop => "Workshops",
		WorldContentKind.LightCrystal => "ItemsOnGround/LightСrystals",
		WorldContentKind.CastleChest => "Castles/CastleChests",
		WorldContentKind.CastleElixirPillar => "Castles/CastleElixirPillars",
		WorldContentKind.CastleTablet => "Castles/CastleTablets",
		WorldContentKind.CastleGate => "Castles/CastleGates",
		WorldContentKind.CastleTeleport => "Castles/CastleTeleports",
		WorldContentKind.CastleEntrance => "Castles/CastleEntrances",
		WorldContentKind.Teleport => "Teleports/TeleportsRegularTargetTournament",
		WorldContentKind.TeleportWild => "Teleports/TeleportsWild",
		WorldContentKind.TeleportBroken => "Teleports/TeleportsBroken",
		WorldContentKind.TeleportInDungeon => "Teleports/TeleportsInDungeon",
		WorldContentKind.TeleportDungeonChoiceIsland => "Teleports/TeleportsDungeonChoiceIsland",
		WorldContentKind.TeleportPoint => "TeleportPoints",
		_ => string.Empty,
	};

	public static WorldContentKind[] All =>
	[
		WorldContentKind.Monster,
		WorldContentKind.Door,
		WorldContentKind.DoorWithKey,
		WorldContentKind.Npc,
		WorldContentKind.Alchemy,
		WorldContentKind.DungeonEntrance,
		WorldContentKind.Workshop,
		WorldContentKind.LightCrystal,
		WorldContentKind.CastleChest,
		WorldContentKind.CastleElixirPillar,
		WorldContentKind.CastleTablet,
		WorldContentKind.CastleGate,
		WorldContentKind.CastleTeleport,
		WorldContentKind.CastleEntrance,
		WorldContentKind.Teleport,
		WorldContentKind.TeleportWild,
		WorldContentKind.TeleportBroken,
		WorldContentKind.TeleportInDungeon,
		WorldContentKind.TeleportDungeonChoiceIsland,
		WorldContentKind.TeleportPoint,
	];
}
