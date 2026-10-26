using System.Buffers.Binary;
using Godot;
using SphServer.Client;
using SphServer.Helpers;

namespace SphServer.Server.Simulation;

/// <summary>
/// Channel 3 Teleport: game-frame x, y, z and heading, the arguments of the client's MbcPlayer::Teleport
/// </summary>
public static class PlayerTeleport
{
	public const int PayloadBytes = 16;

	/// <summary>
	/// Places the body, then tells the client, whose own Teleport runs the fade and the room load
	/// </summary>
	/// <summary>
	/// SavedCoords and /tp are the game frame, the same numbers the old teleport packet carried
	/// </summary>
	public static bool Move (SphereClient client, WorldCoords coords) =>
		Move (client, new Vector3 ((float) coords.x, (float) coords.y, (float) coords.z), (float) coords.turn);

	public static bool Move (SphereClient client, Vector3 raw, float? yaw)
	{
		if (!PlayerMotion.Place (client, raw, yaw, out var placed))
		{
			return false;
		}

		var heading = PlayerMotion.FindModel (client)?.Rotation.Y ?? yaw ?? 0f;
		client.SendReliable (ConnectionCommand.Teleport, Encode (placed, heading));
		return true;
	}

	public static byte[] Encode (Vector3 raw, float heading)
	{
		var bytes = new byte[PayloadBytes];
		BinaryPrimitives.WriteSingleLittleEndian (bytes.AsSpan (0, 4), raw.X);
		// Channel 2 draws the body VisualLiftMeters up, so the client lands on the pose it then gets
		BinaryPrimitives.WriteSingleLittleEndian (bytes.AsSpan (4, 4), raw.Y - SphereClient.VisualLiftMeters);
		BinaryPrimitives.WriteSingleLittleEndian (bytes.AsSpan (8, 4), raw.Z);
		BinaryPrimitives.WriteSingleLittleEndian (bytes.AsSpan (12, 4), heading);
		return bytes;
	}
}
