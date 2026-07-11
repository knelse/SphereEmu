using System.Collections.Generic;
using System.Linq;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
	// ReSharper disable InconsistentNaming
	[Export] public int NameID_1 { get; set; }
	[Export] public int NameID_2 { get; set; }
	[Export] public int NameID_3 { get; set; }
	// ReSharper restore InconsistentNaming

	private int GetMonsterLevel() => MonsterInstance?.Level ?? level;

	private static string GetMonsterPacketDefinitionName(int level)
	{
		if (level <= 1)
		{
			return "monster_level_1";
		}

		// Same field layout as monster_full_level_* / entity_monster captures.
		return "entity_monster";
	}

	private static void UpdatePartAtBit(List<PacketPart> packetParts, int bitStart, int value, int? length = null)
	{
		foreach (var part in packetParts.Where(p => p.BitPositionStart == bitStart))
		{
			var bitLength = length ?? part.BitLength;
			part.Value = BitStreamExtensions.IntToBits(value, bitLength).ToList();
			part.BitLength = bitLength;
		}
	}

	private static void EnsureMonsterFullTrailer(List<PacketPart> packetParts)
	{
		// monster_full_level_*: __undef (100) + skip_36_bits (53 bits, non-zero tail pattern).
		UpdatePartAtBit(packetParts, 226, 4, 3);

		var skipTail = packetParts.FirstOrDefault(p => p.Name == "skip_36_bits");
		if (skipTail is null)
		{
			return;
		}

		skipTail.BitLength = 53;
		skipTail.Value = BitStreamExtensions.IntToBits(66562, 53).ToList();
	}

	private static int EncodeMobLevelBits(int level)
	{
		var e = level - 1;
		var hi = (e >> 5) & 0b11111;
		var mid = (e * 125) & 0xFFF;
		var encoded = (e & 0b11111) | (mid << 5) | (hi << 17);
		if (hi != 0)
		{
			encoded += 1 << 5;
		}

		return encoded;
	}

	private static void UpdateMonsterHpFields(List<PacketPart> packetParts, int currentHp, int maxHp, bool isLevelOne)
	{
		if (isLevelOne)
		{
			PacketPart.UpdateValue(packetParts, "hp_size_t", 8, 5);
			PacketPart.UpdateValue(packetParts, "current_hp", currentHp, 8);
			PacketPart.UpdateValue(packetParts, "max_hp", maxHp, 8);
			return;
		}

		// entity_monster / monster_full_level_*: hp_size_type=01 + skip_100=100 => 0x11 (16-bit HP).
		UpdatePartAtBit(packetParts, 141, 1, 2);
		UpdatePartAtBit(packetParts, 143, 4, 3);

		PacketPart.UpdateValue(packetParts, "current_hp", currentHp, 16);
		PacketPart.UpdateValue(packetParts, "max_hp", maxHp, 16);

		foreach (var part in packetParts.Where(p => p.Name == "skip_1"))
		{
			part.Value = BitStreamExtensions.IntToBits(1, part.BitLength).ToList();
		}

		// Pre-level skip_100 is 110 in monster_full_level_* (distinct from the post-hp_size skip_100=100).
		UpdatePartAtBit(packetParts, 194, 6, 3);
	}

	private static void UpdateMonsterLevelFields(List<PacketPart> packetParts, int level)
	{
		if (level <= 1)
		{
			PacketPart.UpdateValue(packetParts, "level_last_3", (level - 1) & 0b111, 3);
			return;
		}

		PacketPart.UpdateValue(packetParts, "level", EncodeMobLevelBits(level), 29);
	}

	protected override List<PacketPart> GetPacketParts()
	{
		return PacketPart.LoadDefinedWithOverride(GetMonsterPacketDefinitionName(GetMonsterLevel()));
	}

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		var mobLevel = GetMonsterLevel();
		var mobCurrentHp = MonsterInstance?.CurrentHp ?? 50;
		var maxHp = MonsterInstance?.MaxHp ?? 50;

		UpdateMonsterHpFields(packetParts, mobCurrentHp, maxHp, mobLevel <= 1);
		UpdateMonsterLevelFields(packetParts, mobLevel);
		EnsureMonsterFullTrailer(packetParts);

		PacketPart.UpdateValue(packetParts, "action_type", (int)EntityActionType.FULL_SPAWN, 8);

		var objectType = MonsterInstance?.MonsterDataOrigin.ObjectType ?? GameObjectType.Monster;
		if (objectType is GameObjectType.Monster_Flying or GameObjectType.Monster_Event_Flying
			or GameObjectType.Special_Necromancer_Flyer)
		{
			PacketPart.UpdateValue(packetParts, "entity_type", (int)ObjectType.MonsterFlyer, 10);
		}
		else
		{
			PacketPart.UpdateValue(packetParts, "entity_type", (int)ObjectType.Monster, 10);
		}

		var mobTypeId = MonsterTypeMapping.MonsterNameToMonsterTypeMapping[MonsterType];
		PacketPart.UpdateValue(packetParts, "mob_type", mobTypeId, 14);

		if (IsNamed)
		{
		}

		return packetParts;
	}
}
