using System.Buffers.Binary;

namespace SphServer.Server.Simulation;

public readonly struct WorldOwner
{
	public float PosX { get; init; }
	public float PosY { get; init; }
	public float PosZ { get; init; }
	public float Yaw { get; init; }
	public float VelX { get; init; }
	public float VelY { get; init; }
	public float VelZ { get; init; }
}

public readonly struct WorldEntity
{
	public const int Bytes = 16 + 16 + 4 + 2;

	public Guid Id { get; init; }
	public float PosX { get; init; }
	public float PosY { get; init; }
	public float PosZ { get; init; }
	public float Yaw { get; init; }
	public int Hp { get; init; }
	public ushort Anim { get; init; }
}

/// <summary>
/// Channel 2 payload, Godot meters, built every tick
/// </summary>
public static class WorldDatagram
{
	public const byte Channel = 2;

	public static byte[] Write (ulong serverTick, uint ackSeq, in WorldOwner owner, IReadOnlyList<WorldEntity> entities)
	{
		var count = entities.Count;
		if (count > ushort.MaxValue)
		{
			count = ushort.MaxValue;
		}

		var bytes = new byte[8 + 4 + 7 * 4 + 2 + count * WorldEntity.Bytes];
		var span = bytes.AsSpan ();
		BinaryPrimitives.WriteUInt64LittleEndian (span, serverTick);
		BinaryPrimitives.WriteUInt32LittleEndian (span[8..], ackSeq);
		var offset = 12;
		offset = WriteFloat (span, offset, owner.PosX);
		offset = WriteFloat (span, offset, owner.PosY);
		offset = WriteFloat (span, offset, owner.PosZ);
		offset = WriteFloat (span, offset, owner.Yaw);
		offset = WriteFloat (span, offset, owner.VelX);
		offset = WriteFloat (span, offset, owner.VelY);
		offset = WriteFloat (span, offset, owner.VelZ);
		BinaryPrimitives.WriteUInt16LittleEndian (span[offset..], (ushort) count);
		offset += 2;

		for (var i = 0; i < count; i++)
		{
			var entity = entities[i];
			entity.Id.ToByteArray ().CopyTo (span[offset..]);
			offset += 16;
			offset = WriteFloat (span, offset, entity.PosX);
			offset = WriteFloat (span, offset, entity.PosY);
			offset = WriteFloat (span, offset, entity.PosZ);
			offset = WriteFloat (span, offset, entity.Yaw);
			BinaryPrimitives.WriteInt32LittleEndian (span[offset..], entity.Hp);
			offset += 4;
			BinaryPrimitives.WriteUInt16LittleEndian (span[offset..], entity.Anim);
			offset += 2;
		}

		return bytes;
	}

	private static int WriteFloat (Span<byte> span, int offset, float value)
	{
		BinaryPrimitives.WriteSingleLittleEndian (span[offset..], value);
		return offset + 4;
	}
}
