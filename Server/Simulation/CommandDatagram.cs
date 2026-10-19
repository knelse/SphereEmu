using System.Buffers.Binary;

namespace SphServer.Server.Simulation;

/// <summary>
/// Channel 1 payload: a count, then 34-byte commands
/// </summary>
public static class CommandDatagram
{
	public const byte Channel = 1;

	public const int CommandBytes = 34;

	public static bool TryDecode (byte[] payload, List<PlayerCommand> into)
	{
		if (payload is null || into is null || payload.Length < 1)
		{
			return false;
		}

		var count = payload[0];
		var needed = 1 + count * CommandBytes;
		if (payload.Length < needed)
		{
			return false;
		}

		var offset = 1;
		for (var i = 0; i < count; i++)
		{
			into.Add (ReadCommand (payload, offset));
			offset += CommandBytes;
		}

		return true;
	}

	private static PlayerCommand ReadCommand (byte[] payload, int offset)
	{
		var seq = BinaryPrimitives.ReadUInt32LittleEndian (payload.AsSpan (offset, 4));
		var wishX = BinaryPrimitives.ReadSingleLittleEndian (payload.AsSpan (offset + 4, 4));
		var wishZ = BinaryPrimitives.ReadSingleLittleEndian (payload.AsSpan (offset + 8, 4));
		var yaw = BinaryPrimitives.ReadSingleLittleEndian (payload.AsSpan (offset + 12, 4));
		var buttons = BinaryPrimitives.ReadUInt16LittleEndian (payload.AsSpan (offset + 16, 2));
		var targetHi = BinaryPrimitives.ReadUInt64LittleEndian (payload.AsSpan (offset + 18, 8));
		var targetLo = BinaryPrimitives.ReadUInt64LittleEndian (payload.AsSpan (offset + 26, 8));

		// hi then lo, each little-endian, matches Guid.ToByteArray
		var bytes = new byte[16];
		BinaryPrimitives.WriteUInt64LittleEndian (bytes.AsSpan (0, 8), targetHi);
		BinaryPrimitives.WriteUInt64LittleEndian (bytes.AsSpan (8, 8), targetLo);

		return new PlayerCommand
		{
			Seq = seq,
			WishX = wishX,
			WishZ = wishZ,
			Yaw = yaw,
			Buttons = buttons,
			TargetId = new Guid (bytes)
		};
	}
}
