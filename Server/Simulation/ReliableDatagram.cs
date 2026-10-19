using System.Buffers.Binary;

namespace SphServer.Server.Simulation;

/// <summary>
/// Channel 3 payload: seq, ack, command id, then the command bytes
/// </summary>
public static class ReliableDatagram
{
	public const byte Channel = 3;

	public const int HeaderBytes = 10;

	public static byte[] Encode (uint seq, uint ack, ConnectionCommand command, ReadOnlySpan<byte> payload)
	{
		var bytes = new byte[HeaderBytes + payload.Length];
		BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (0, 4), seq);
		BinaryPrimitives.WriteUInt32LittleEndian (bytes.AsSpan (4, 4), ack);
		BinaryPrimitives.WriteUInt16LittleEndian (bytes.AsSpan (8, 2), (ushort) command);
		payload.CopyTo (bytes.AsSpan (HeaderBytes));
		return bytes;
	}

	public static bool TryDecode (byte[] datagram, out uint seq, out uint ack, out ConnectionCommand command,
		out byte[] payload)
	{
		seq = 0;
		ack = 0;
		command = 0;
		payload = [];
		if (datagram is null || datagram.Length < HeaderBytes)
		{
			return false;
		}

		seq = BinaryPrimitives.ReadUInt32LittleEndian (datagram.AsSpan (0, 4));
		ack = BinaryPrimitives.ReadUInt32LittleEndian (datagram.AsSpan (4, 4));
		command = (ConnectionCommand) BinaryPrimitives.ReadUInt16LittleEndian (datagram.AsSpan (8, 2));
		payload = datagram.AsSpan (HeaderBytes).ToArray ();
		return true;
	}
}
