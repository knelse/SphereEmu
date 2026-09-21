using System;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Client;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.Mbc;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
	private int GetMonsterLevel() => MonsterInstance?.Level ?? level;

	protected override ushort GetMoveModuleTag() => MbcModuleTag();

	private ushort MbcModuleTag()
	{
		var objectType = MonsterInstance?.MonsterDataOrigin.ObjectType ?? GameObjectType.Monster;
		return (ushort)(objectType is GameObjectType.Monster_Flying or GameObjectType.Monster_Event_Flying
			or GameObjectType.Special_Necromancer_Flyer
			? ObjectType.Monster_Flyer
			: ObjectType.Monster);
	}

	/// <summary>
	///     Don't spawn a dying mob to a client entering range after the killing blow: it would get
	///     a live spawn and never see the death. The pending despawn broadcast is harmless for it.
	/// </summary>
	protected override void ShowForClient(SphereClient client)
	{
		if (_deathStarted)
		{
			return;
		}

		if (!MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue(MonsterType, out var gameId))
		{
			SphLogger.Error($"Monster spawn: no type id for {MonsterType}. Id: {ID:X4}");
			return;
		}

		var origin = GlobalTransform.Origin;
		var entityId = client.GetLocalObjectId(ID);
		var packedLevel = Math.Max(0, GetMonsterLevel() - 1);
		var currentHp = MonsterInstance?.CurrentHp ?? 50;
		var maxHp = MonsterInstance?.MaxHp ?? 50;

		client.MaybeQueueNetworkPacketSend(BuildSpawnSnapshot(
			entityId, MbcModuleTag(),
			origin.X, -origin.Y, -origin.Z, DecodeAngleToYawRadians(Angle),
			currentHp, maxHp, gameId, packedLevel));
	}

	/// <summary>
	///     monster / monsterf region 61 SpawnSnapshot (EInit). IEEE xyz, angle8, contain_state u2,
	///     then varint current_hp, max_hp, game_id (g_0440 / SetItemGroup), packed level
	///     (i37 + i38*4000). World mobs use contain_state 0. packedLevel is 0-based
	///     (display level = max(i37,i38)+1).
	/// </summary>
	private static byte[] BuildSpawnSnapshot(ushort entityId, ushort moduleTag,
		float x, float y, float z, double angleRadians,
		int currentHp, int maxHp, int gameId, int packedLevel)
	{
		var stream = SphBitStream.GetWriteBitStream();
		stream.WriteByte(0, 1); // has_position
		stream.WriteUInt16(0, 15); // tick
		stream.WriteUInt16(entityId, 16);
		stream.WriteByte(0, 2); // process_id high
		stream.WriteUInt16((ushort)(moduleTag & 0xFFF), 12);
		stream.WriteByte(62, 7); // wire = region 61 + 1
		WriteIeeeFloat(stream, x);
		WriteIeeeFloat(stream, y);
		WriteIeeeFloat(stream, z);
		stream.WriteByte(MbcCoordEncoding.EncodeAngle(angleRadians), 8);
		stream.WriteByte(0, 2); // contain_state
		CommonPackets.WriteMbcVarint(stream, currentHp);
		CommonPackets.WriteMbcVarint(stream, maxHp);
		CommonPackets.WriteMbcVarint(stream, gameId);
		CommonPackets.WriteMbcVarint(stream, packedLevel);
		return Packet.ToByteArray(stream.GetStreamData(), 1);
	}

	private static void WriteIeeeFloat(SphWriteStream stream, float value)
	{
		var bits = BitConverter.SingleToUInt32Bits(value);
		stream.WriteUInt16((ushort)bits, 16);
		stream.WriteUInt16((ushort)(bits >> 16), 16);
	}
}
