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

	/// <summary>
	///     Largest HP the compact level-1 template (monster_level_1) can carry. Its current_hp /
	///     max_hp slots are read by the client as 7-bit values (max 127): an HP &gt; 127 spills its
	///     8th bit onto the next field's lead bit, corrupting mob_type's model selector so the
	///     client draws nothing. LIVE-CONFIRMED 2026-07-15: /mob 1110 1 160 vanishes while
	///     /mob 1291 1 96 renders. Heavier level-1 mobs must use entity_monster's wider HP slot.
	/// </summary>
	private const int MonsterLevelOneHpCap = 127;

	/// <summary>
	///     True when the compact level-1 template is safe to use — level 1 AND both HP values fit
	///     its 7-bit slot; otherwise the wide entity_monster template is used, even at level 1.
	///     GetPacketParts and ModifyPacketParts must agree, so both go through here.
	/// </summary>
	private bool UseLevelOneTemplate()
	{
		var maxHp = MonsterInstance?.MaxHp ?? 50;
		var currentHp = MonsterInstance?.CurrentHp ?? 50;
		return GetMonsterLevel() <= 1 && maxHp <= MonsterLevelOneHpCap && currentHp <= MonsterLevelOneHpCap;
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

	private static void UpdateMonsterHpFields(List<PacketPart> packetParts, int currentHp, int maxHp,
		bool useLevelOneTemplate)
	{
		if (useLevelOneTemplate)
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

	private static void UpdateMonsterLevelFields(List<PacketPart> packetParts, int level, bool useLevelOneTemplate)
	{
		if (useLevelOneTemplate)
		{
			PacketPart.UpdateValue(packetParts, "level_last_3", (level - 1) & 0b111, 3);
			return;
		}

		// A level-1 mob routed through entity_monster (because its HP exceeds the compact
		// template's 7-bit cap) writes the 29-bit level field, not monster_level_1's
		// level_last_3. EncodeMobLevelBits(1) == 0, i.e. a valid level-1 encoding.
		PacketPart.UpdateValue(packetParts, "level", EncodeMobLevelBits(level), 29);
	}

	protected override List<PacketPart> GetPacketParts()
	{
		return PacketPart.LoadDefinedWithOverride(UseLevelOneTemplate() ? "monster_level_1" : "entity_monster");
	}

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		var mobLevel = GetMonsterLevel();
		var mobCurrentHp = MonsterInstance?.CurrentHp ?? 50;
		var maxHp = MonsterInstance?.MaxHp ?? 50;
		var useLevelOne = UseLevelOneTemplate();

		UpdateMonsterHpFields(packetParts, mobCurrentHp, maxHp, useLevelOne);
		UpdateMonsterLevelFields(packetParts, mobLevel, useLevelOne);
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
